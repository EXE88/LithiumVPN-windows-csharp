using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;

namespace Lithiumvpn.Pages
{
    public sealed partial class SplashPage : Page
    {
        private readonly string[] _morphWords = { "LithiumVPN", "Secure", "Fast", "LithiumVPN" };
        private int _morphIndex = 0;
        private DispatcherTimer? _morphTimer;

        public event Action? SplashCompleted;

        /// <summary>
        /// When a saved session was validated and its data prefetched on the splash,
        /// the app skips the login page and goes straight to the dashboard.
        /// </summary>
        public bool GoToDashboard { get; private set; }

        public SplashPage()
        {
            this.InitializeComponent();
            this.Loaded += SplashPage_Loaded;
        }

        private bool _started = false;
        private void SplashPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;
            SetLogo();
            _ = StartAsync();
        }

        private void SetLogo()
        {
            bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
            var logoPath = isDark
                ? "ms-appx:///Assets/LogoWhite.svg"
                : "ms-appx:///Assets/LogoBlack.svg";

            SplashLogo.Source = new SvgImageSource(new Uri(logoPath));
        }

        private async Task StartAsync()
        {
            await FadeInLogo();
            await FadeInTextAndBar();
            StartMorphTimer();
            await ValidateAndProceedAsync();
        }

        /// <summary>
        /// The app must never be held hostage by an unreachable backend. The splash
        /// probes the server with a short (3s) cap:
        ///  • reachable + valid session → prefetch data and go straight to the dashboard;
        ///  • reachable, no/expired session → fall through to the login page;
        ///  • unreachable (or the prefetch fails) → open the app OFFLINE on the dashboard,
        ///    where backend-only pages show a "reconnect" placeholder.
        /// </summary>
        private async Task ValidateAndProceedAsync()
        {
            // Keep the branding visible for a short minimum even if the backend is instant.
            var minDisplay = Task.Delay(1400);

            bool reachable = await Services.ApiService.CheckConnectivityAsync(TimeSpan.FromSeconds(3));

            if (reachable && Services.TokenStore.Instance.HasSession)
            {
                var outcome = await Services.AppState.Instance.LoadAllAsync();
                if (outcome == Services.AppState.LoadOutcome.Success)
                    GoToDashboard = true;          // valid session → skip the login page
                else if (outcome == Services.AppState.LoadOutcome.NetworkError)
                    GoToDashboard = true;          // backend faltered mid-load → open offline
                // AuthFailed: the client already cleared the dead session → fall to login.
            }
            else if (!reachable)
            {
                // Backend down: enter the app offline rather than blocking on retry.
                GoToDashboard = true;
            }
            // reachable && no session → GoToDashboard stays false → login page.

            await minDisplay;
            await FadeOutAll();
            SplashCompleted?.Invoke();
        }

        private async Task ShowErrorAsync(string bodyKey)
        {
            _morphTimer?.Stop();
            ErrorBody.Text = Localization.LocalizationManager.Instance.Get(bodyKey);

            // Fade the loading content (logo included) out, then the error panel in.
            var tcs = new TaskCompletionSource<object?>();
            var sbOut = new Storyboard();
            foreach (var target in new UIElement[] { SplashLogo, MorphText, LoadingBar })
            {
                var fade = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
                Storyboard.SetTarget(fade, target);
                Storyboard.SetTargetProperty(fade, "Opacity");
                sbOut.Children.Add(fade);
            }
            sbOut.Completed += (s, e) => tcs.TrySetResult(null);
            sbOut.Begin();
            await tcs.Task;

            SplashLogo.Visibility = Visibility.Collapsed;
            LoadingBar.Visibility = Visibility.Collapsed;
            MorphText.Visibility = Visibility.Collapsed;
            ErrorPanel.Opacity = 0;
            ErrorPanel.Visibility = Visibility.Visible;

            var sbIn = new Storyboard();
            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(280)) };
            Storyboard.SetTarget(fadeIn, ErrorPanel);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sbIn.Children.Add(fadeIn);
            sbIn.Begin();
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            // Reset back to the loading state and re-run validation.
            RetryButton.IsEnabled = false;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SplashLogo.Visibility = Visibility.Visible;
            SplashLogo.Opacity = 1;
            MorphText.Visibility = Visibility.Visible;
            MorphText.Opacity = 1;
            LoadingBar.Visibility = Visibility.Visible;
            LoadingBar.Opacity = 1;
            _morphIndex = 0;
            MorphText.Text = _morphWords[0];
            StartMorphTimer();
            RetryButton.IsEnabled = true;
            await ValidateAndProceedAsync();
        }

        private Task FadeInLogo()
        {
            var tcs = new TaskCompletionSource<object?>();

            var sb = new Storyboard();

            var scaleX = new DoubleAnimation { From = 0.8, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
            var scaleY = new DoubleAnimation { From = 0.8, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
            scaleX.EasingFunction = scaleY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };

            Storyboard.SetTarget(scaleX, LogoScale);
            Storyboard.SetTarget(scaleY, LogoScale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");
            Storyboard.SetTargetProperty(scaleY, "ScaleY");

            var opacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
            opacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(opacity, SplashLogo);
            Storyboard.SetTargetProperty(opacity, "Opacity");

            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Children.Add(opacity);

            sb.Completed += (s, e) => tcs.TrySetResult(null);
            sb.Begin();

            return tcs.Task;
        }

        private Task FadeInTextAndBar()
        {
            var tcs = new TaskCompletionSource<object?>();

            var sb = new Storyboard();

            var textOpacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            textOpacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(textOpacity, MorphText);
            Storyboard.SetTargetProperty(textOpacity, "Opacity");

            var textY = new DoubleAnimation { From = 10, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            textY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(textY, MorphTranslate);
            Storyboard.SetTargetProperty(textY, "Y");

            var barOpacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            barOpacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(barOpacity, LoadingBar);
            Storyboard.SetTargetProperty(barOpacity, "Opacity");

            sb.Children.Add(textOpacity);
            sb.Children.Add(textY);
            sb.Children.Add(barOpacity);

            sb.Completed += (s, e) => tcs.TrySetResult(null);
            sb.Begin();

            return tcs.Task;
        }

        private void StartMorphTimer()
        {
            _morphTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _morphTimer.Tick += MorphTimer_Tick;
            _morphTimer.Start();
        }

        private void MorphTimer_Tick(object? sender, object e)
        {
            _morphIndex = (_morphIndex + 1) % _morphWords.Length;
            AnimateMorphText(_morphWords[_morphIndex]);
        }

        private void AnimateMorphText(string newText)
        {
            var sb = new Storyboard();

            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(fadeOut, MorphText);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            var slideOut = new DoubleAnimation { From = 0, To = -8, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            slideOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            Storyboard.SetTarget(slideOut, MorphTranslate);
            Storyboard.SetTargetProperty(slideOut, "Y");

            sb.Children.Add(fadeOut);
            sb.Children.Add(slideOut);

            sb.Completed += (s, e) =>
            {
                MorphText.Text = newText;

                var sb2 = new Storyboard();

                var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(250)) };
                fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                Storyboard.SetTarget(fadeIn, MorphText);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");

                var slideIn = new DoubleAnimation { From = 8, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(250)) };
                slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                Storyboard.SetTarget(slideIn, MorphTranslate);
                Storyboard.SetTargetProperty(slideIn, "Y");

                sb2.Children.Add(fadeIn);
                sb2.Children.Add(slideIn);
                sb2.Begin();
            };

            sb.Begin();
        }

        private Task FadeOutAll()
        {
            _morphTimer?.Stop();

            var tcs = new TaskCompletionSource<object?>();

            var sb = new Storyboard();

            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, RootGrid);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            sb.Children.Add(fadeOut);
            sb.Completed += (s, e) => tcs.TrySetResult(null);
            sb.Begin();

            return tcs.Task;
        }
    }
}
