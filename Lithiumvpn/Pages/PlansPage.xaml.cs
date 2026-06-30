using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Globalization;
using Lithiumvpn.Localization;

namespace Lithiumvpn.Pages
{
    public sealed partial class PlansPage : Page
    {
        private int _balance = 2450;

        public PlansPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;
        }

        // ─── انیمیشن hover روی plan card ────────────────────────────────
        private void PlanCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card) AnimateScale(card, 1.015);
        }

        private void PlanCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card) AnimateScale(card, 1.0);
        }

        private static void AnimateScale(Border target, double to)
        {
            if (target.RenderTransform is not ScaleTransform st) return;

            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var anim = new DoubleAnimation
                {
                    To = to,
                    Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                    EnableDependentAnimation = true,
                    EasingFunction = ease
                };
                Storyboard.SetTarget(anim, st);
                Storyboard.SetTargetProperty(anim, prop);
                sb.Children.Add(anim);
            }
            sb.Begin();
        }

        // ─── خرید پلن ───────────────────────────────────────────────────
        private async void Buy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;

            var parts = tag.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int price)) return;
            string planName = parts[0];

            var loc = LocalizationManager.Instance;
            string localizedPlan = LocalizePlanName(planName);

            // موجودی کافی نیست
            if (price > _balance)
            {
                await new ContentDialog
                {
                    Title = loc.Get("Plans_NotEnoughTitle"),
                    Content = string.Format(loc.Get("Plans_NotEnoughBody"),
                                            localizedPlan, price.ToString("N0"), _balance.ToString("N0")),
                    CloseButtonText = loc.Get("Common_OK"),
                    DefaultButton = ContentDialogButton.Close,
                    FlowDirection = loc.FlowDirection,
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
                return;
            }

            // تأیید خرید
            var confirm = new ContentDialog
            {
                Title = string.Format(loc.Get("Plans_BuyTitle"), localizedPlan),
                Content = string.Format(loc.Get("Plans_BuyBody"),
                                        price.ToString("N0"), _balance.ToString("N0")),
                PrimaryButtonText = loc.Get("Plans_BuyConfirm"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            _balance -= price;
            HeroBalanceText.Text = _balance.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string LocalizePlanName(string englishName) => englishName switch
        {
            "Basic" => LocalizationManager.Instance.Get("Plans_Basic"),
            "Pro" => LocalizationManager.Instance.Get("Plans_Pro"),
            "Ultimate" => LocalizationManager.Instance.Get("Plans_Ultimate"),
            _ => englishName
        };

        // ─── تور راهنما ─────────────────────────────────────────────────
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        private bool _tourAttached = false;
        private void StartTour()
        {
            if (!_tourAttached)
            {
                _tourAttached = true;
                TourTip1.ActionButtonClick += (s, e) => { TourTip1.IsOpen = false; TourTip2.IsOpen = true; };
                TourTip1.CloseButtonClick  += (s, e) => TourTip1.IsOpen = false;
                TourTip2.ActionButtonClick += (s, e) => { TourTip2.IsOpen = false; TourTip3.IsOpen = true; };
                TourTip2.CloseButtonClick  += (s, e) => TourTip2.IsOpen = false;
                TourTip3.CloseButtonClick  += (s, e) => TourTip3.IsOpen = false;
            }
            TourTip1.IsOpen = true;
        }
    }
}
