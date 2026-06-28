using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.UI;

namespace Lithiumvpn.Pages
{
    public sealed partial class ServersPage : Page
    {
        private static readonly Random _rng = new();

        public ServersPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;
        }

        // ─── انیمیشن hover روی config card ──────────────────────────────
        private void ConfigCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                AnimateScale(card, 1.02);
                card.Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            }
        }

        private void ConfigCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                AnimateScale(card, 1.0);
                card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
            }
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

        // ─── دکمه Ping: عدد رندوم را به‌صورت تگ به config card اضافه می‌کند ─
        private void Ping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not Border tag) return;

            int ping = _rng.Next(18, 140);

            if (tag.Child is TextBlock tb)
                tb.Text = $"{ping} ms";

            tag.Background = new SolidColorBrush(PingColor(ping));

            bool firstTime = tag.Visibility == Visibility.Collapsed;
            tag.Visibility = Visibility.Visible;

            // پالس کوچک روی دکمه
            PulseButton(btn);

            // ورود نرم تگ (بار اول scale-in، دفعات بعد یک پالس کوچک)
            if (tag.RenderTransform is ScaleTransform st)
            {
                var sb = new Storyboard();
                foreach (var prop in new[] { "ScaleX", "ScaleY" })
                {
                    var anim = new DoubleAnimationUsingKeyFrames();
                    anim.KeyFrames.Add(new EasingDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                        Value = firstTime ? 0.6 : 0.85
                    });
                    anim.KeyFrames.Add(new EasingDoubleKeyFrame
                    {
                        KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
                        Value = 1,
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
                    });
                    Storyboard.SetTarget(anim, st);
                    Storyboard.SetTargetProperty(anim, prop);
                    sb.Children.Add(anim);
                }
                sb.Begin();
            }
        }

        private static void PulseButton(Button btn)
        {
            btn.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            if (btn.RenderTransform is not ScaleTransform st)
            {
                st = new ScaleTransform();
                btn.RenderTransform = st;
            }

            var sb = new Storyboard();
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var anim = new DoubleAnimationUsingKeyFrames();
                anim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)),
                    Value = 0.92,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                    Value = 1,
                    EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 }
                });
                Storyboard.SetTarget(anim, st);
                Storyboard.SetTargetProperty(anim, prop);
                sb.Children.Add(anim);
            }
            sb.Begin();
        }

        // رنگ تگ پینگ بر اساس کیفیت (هماهنگ با پالت برنامه)
        private static Color PingColor(int ping)
        {
            if (ping <= 40) return Color.FromArgb(255, 0, 200, 83);    // سبز
            if (ping <= 80) return Color.FromArgb(255, 124, 179, 66);  // سبز ملایم
            if (ping <= 120) return Color.FromArgb(255, 255, 179, 0);  // کهربایی
            return Color.FromArgb(255, 229, 57, 53);                   // قرمز
        }

        // ─── دکمه info روی purchase card: TeachingTip را باز می‌کند ──────
        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TeachingTip tip)
                tip.IsOpen = true;
        }

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
                TourTip2.ActionButtonClick += (s, e) =>
                {
                    TourTip2.IsOpen = false;
                    Purchase1Expander.IsExpanded = true;   // باز کردن تا config cardها دیده شوند
                    TourTip3.IsOpen = true;
                };
                TourTip2.CloseButtonClick  += (s, e) => TourTip2.IsOpen = false;
                TourTip3.CloseButtonClick  += (s, e) => TourTip3.IsOpen = false;
            }
            TourTip1.IsOpen = true;
        }
    }
}
