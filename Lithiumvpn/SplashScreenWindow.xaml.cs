using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;

namespace Lithiumvpn
{
    public sealed partial class SplashScreenWindow : Window
    {
        // کلمه‌هایی که باید بین‌شون morph بشه
        private readonly string[] _morphWords = { "LithiumVPN", "Secure", "Fast", "LithiumVPN" };
        private int _morphIndex = 0;

        // تایمر morph
        private DispatcherTimer? _morphTimer;

        // callback که بعد از ۳ ثانیه صدا زده میشه
        public event Action? SplashCompleted;

        public SplashScreenWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

            // ✅ اندازه و center از App.xaml.cs تنظیم میشه — اینجا فقط presenter
            // ✅ غیرفعال کردن resize/maximize/minimize از طریق OverlappedPresenter
            try
            {
                var presenter = Microsoft.UI.Windowing.OverlappedPresenter.Create();
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                this.AppWindow.SetPresenter(presenter);
            }
            catch { }

            this.Activated += OnActivated;
        }

        private bool _started = false;

        private void OnActivated(object sender, WindowActivatedEventArgs e)
        {
            if (_started) return;
            _started = true;

            // لوگو بر اساس تم
            SetLogo();

            // شروع انیمیشن‌های ورود
            _ = StartAsync();
        }

        private void SetLogo()
        {
            // تم سیستم رو از RootGrid بخون
            bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
            var logoPath = isDark
                ? "ms-appx:///Assets/LogoWhite.svg"
                : "ms-appx:///Assets/LogoBlack.svg";

            SplashLogo.Source = new SvgImageSource(new Uri(logoPath));
        }

        private async Task StartAsync()
        {
            // ── مرحله ۱: لوگو fade in + scale ──────────────────────
            await FadeInLogo();

            // ── مرحله ۲: text و progressbar fade in ─────────────────
            await FadeInTextAndBar();

            // ── مرحله ۳: شروع morph بین کلمات ──────────────────────
            StartMorphTimer();

            // ── مرحله ۴: ۳ ثانیه صبر ────────────────────────────────
            await Task.Delay(3000);

            // ── مرحله ۵: fade out کل splash ─────────────────────────
            await FadeOutAll();

            // ── مرحله ۶: اعلام تموم شدن ─────────────────────────────
            SplashCompleted?.Invoke();
        }

        // ─── انیمیشن fade in لوگو ────────────────────────────────────
        private Task FadeInLogo()
        {
            var tcs = new TaskCompletionSource();

            var sb = new Storyboard();

            // scale 0.8 → 1.0
            var scaleX = new DoubleAnimation { From = 0.8, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
            var scaleY = new DoubleAnimation { From = 0.8, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
            scaleX.EasingFunction = scaleY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };

            Storyboard.SetTarget(scaleX, LogoScale);
            Storyboard.SetTarget(scaleY, LogoScale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");
            Storyboard.SetTargetProperty(scaleY, "ScaleY");

            // opacity 0 → 1
            var opacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(500)) };
            opacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(opacity, SplashLogo);
            Storyboard.SetTargetProperty(opacity, "Opacity");

            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Children.Add(opacity);

            sb.Completed += (s, e) => tcs.TrySetResult();
            sb.Begin();

            return tcs.Task;
        }

        // ─── fade in text و progressbar ──────────────────────────────
        private Task FadeInTextAndBar()
        {
            var tcs = new TaskCompletionSource();

            var sb = new Storyboard();

            // text opacity
            var textOpacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            textOpacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(textOpacity, MorphText);
            Storyboard.SetTargetProperty(textOpacity, "Opacity");

            // text translateY 10 → 0
            var textY = new DoubleAnimation { From = 10, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            textY.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(textY, MorphTranslate);
            Storyboard.SetTargetProperty(textY, "Y");

            // bar opacity
            var barOpacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            barOpacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            Storyboard.SetTarget(barOpacity, LoadingBar);
            Storyboard.SetTargetProperty(barOpacity, "Opacity");

            sb.Children.Add(textOpacity);
            sb.Children.Add(textY);
            sb.Children.Add(barOpacity);

            sb.Completed += (s, e) => tcs.TrySetResult();
            sb.Begin();

            return tcs.Task;
        }

        // ─── morph timer ──────────────────────────────────────────────
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

            // fade out
            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(fadeOut, MorphText);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            // slide up موقع خروج
            var slideOut = new DoubleAnimation { From = 0, To = -8, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            slideOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            Storyboard.SetTarget(slideOut, MorphTranslate);
            Storyboard.SetTargetProperty(slideOut, "Y");

            sb.Children.Add(fadeOut);
            sb.Children.Add(slideOut);

            sb.Completed += (s, e) =>
            {
                // تغییر متن
                MorphText.Text = newText;

                // fade in از پایین
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

        // ─── fade out همه چیز ─────────────────────────────────────────
        private Task FadeOutAll()
        {
            _morphTimer?.Stop();

            var tcs = new TaskCompletionSource();

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
            sb.Completed += (s, e) => tcs.TrySetResult();
            sb.Begin();

            return tcs.Task;
        }
    }
}