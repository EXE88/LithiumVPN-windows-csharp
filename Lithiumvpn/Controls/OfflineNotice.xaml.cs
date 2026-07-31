using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;

namespace Lithiumvpn.Controls
{
    /// <summary>
    /// Reusable placeholder shown in place of backend-only content while the app is
    /// offline: an icon, a message, and a "Reconnect" button (optionally an "Add from
    /// clipboard" button for the Configs page). It runs the reconnect attempt itself
    /// and reports the result so each host page only decides what to do next.
    /// </summary>
    public sealed partial class OfflineNotice : UserControl
    {
        public OfflineNotice()
        {
            this.InitializeComponent();
            LocalizationManager.Instance.LanguageChanged += ApplyBody;
            this.Loaded += (_, _) => ApplyBody();
            this.Unloaded += (_, _) => LocalizationManager.Instance.LanguageChanged -= ApplyBody;
        }

        /// <summary>Localization key for the body line; lets each page word it appropriately.</summary>
        public string BodyKey { get; set; } = "Offline_Body";

        /// <summary>Show the extra "Add from clipboard" button (Configs page).</summary>
        public bool ShowImportButton
        {
            get => ImportButton.Visibility == Visibility.Visible;
            set => ImportButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Raised when the reconnect succeeded and the account data is loaded.</summary>
        public event EventHandler? WentOnline;

        /// <summary>Raised when the backend is reachable but the user must sign in.</summary>
        public event EventHandler? NeedLogin;

        /// <summary>Raised when the user clicks the optional "Add from clipboard" button.</summary>
        public event EventHandler? ImportRequested;

        private void ApplyBody()
        {
            BodyBlock.Text = LocalizationManager.Instance.Get(BodyKey);
        }

        private void Import_Click(object sender, RoutedEventArgs e) =>
            ImportRequested?.Invoke(this, EventArgs.Empty);

        private async void Reconnect_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            var outcome = await ConnectivityService.Instance.TryReconnectAsync();
            SetBusy(false);

            switch (outcome)
            {
                case ConnectivityService.ReconnectOutcome.WentOnline:
                    WentOnline?.Invoke(this, EventArgs.Empty);
                    break;
                case ConnectivityService.ReconnectOutcome.NeedLogin:
                    NeedLogin?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    // Still offline — nudge the button so the user sees it retried.
                    Shake(ReconnectButton);
                    break;
            }
        }

        private void SetBusy(bool busy)
        {
            ReconnectButton.IsEnabled = !busy;
            ImportButton.IsEnabled = !busy;
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ReconnectIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        }

        private static void Shake(FrameworkElement element)
        {
            if (element.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
            {
                element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                element.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
            }

            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
            double[] frames = { 0, -6, 6, -4, 4, 0 };
            for (int i = 0; i < frames.Length; i++)
            {
                anim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
                {
                    KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(
                        TimeSpan.FromMilliseconds(60 * i)),
                    Value = frames[i]
                });
            }
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, element.RenderTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "X");
            sb.Children.Add(anim);
            sb.Begin();
        }
    }
}
