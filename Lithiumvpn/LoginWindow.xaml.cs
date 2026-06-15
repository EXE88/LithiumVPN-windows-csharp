using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using System;

namespace Lithiumvpn
{
    public sealed partial class LoginWindow : Window
    {
        // ── Default credentials (demo only) ──────────────────
        private const string DefaultUsername = "lithium";
        private const string DefaultPassword = "Lithium@2025";
        private const string DefaultOtp      = "123456";

        public event Action? LoginCompleted;

        private string _pendingUsername = "";
        private DispatcherTimer? _countdownTimer;
        private int _countdownSeconds = 4;

        public LoginWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var wid  = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var aw   = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wid);
                if (aw?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                { p.IsResizable = false; p.IsMaximizable = false; }
            }
            catch { }

            this.Activated += OnActivated;
        }

        private bool _initialized;
        private void OnActivated(object sender, WindowActivatedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;
            UpdateLogo();
            if (App.GetThemeService is not null)
                App.GetThemeService.ThemeChanged += (_, _) => UpdateLogo();
        }

        private void UpdateLogo()
        {
            if (AppLogoLogin is null || App.GetThemeService is null) return;
            AppLogoLogin.Source = new SvgImageSource(new Uri(
                App.GetThemeService.IsDark
                    ? "ms-appx:///Assets/LogoWhite.svg"
                    : "ms-appx:///Assets/LogoBlack.svg"));
        }

        // ══════════════════════════════════════════════════════
        //  LOGIN
        // ══════════════════════════════════════════════════════
        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LoginUsername.Text.Trim() == DefaultUsername &&
                LoginPassword.Password    == DefaultPassword)
            {
                LoginCompleted?.Invoke();
            }
            else
            {
                ShowError(LoginError, "Invalid username or password.");
            }
        }

        // ══════════════════════════════════════════════════════
        //  PANEL SWITCH   Login ↔ Register
        // ══════════════════════════════════════════════════════
        private void SwitchToRegister_Click(object sender, RoutedEventArgs e)
            => AnimateCardSwitch(LoginCard, RegisterCard,
                                 LoginCardTranslate, RegisterCardTranslate, fromX: 40);

        private void SwitchToLogin_Click(object sender, RoutedEventArgs e)
            => AnimateCardSwitch(RegisterCard, LoginCard,
                                 RegisterCardTranslate, LoginCardTranslate, fromX: -40);

        private void AnimateCardSwitch(FrameworkElement outEl, FrameworkElement inEl,
                                       TranslateTransform outTx, TranslateTransform inTx,
                                       double fromX)
        {
            var sbOut = new Storyboard();
            var opOut = new DoubleAnimation
            {
                To = 0, Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(opOut, outEl);
            Storyboard.SetTargetProperty(opOut, "Opacity");
            sbOut.Children.Add(opOut);

            sbOut.Completed += (_, _) =>
            {
                outEl.Visibility = Visibility.Collapsed;
                inEl.Opacity     = 0;
                inTx.X           = fromX;
                inEl.Visibility  = Visibility.Visible;

                var sbIn = new Storyboard();

                var opIn = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(240),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(opIn, inEl);
                Storyboard.SetTargetProperty(opIn, "Opacity");

                var txIn = new DoubleAnimation
                {
                    From = fromX, To = 0, Duration = TimeSpan.FromMilliseconds(240),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(txIn, inTx);
                Storyboard.SetTargetProperty(txIn, "X");

                sbIn.Children.Add(opIn);
                sbIn.Children.Add(txIn);
                sbIn.Begin();
            };
            sbOut.Begin();
        }

        // ══════════════════════════════════════════════════════
        //  REGISTER — Step 1
        // ══════════════════════════════════════════════════════
        private void RegNext_Click(object sender, RoutedEventArgs e)
        {
            string user    = RegUsername.Text.Trim();
            string email   = RegEmail.Text.Trim();
            string pass    = RegPassword.Password;
            string confirm = RegConfirmPassword.Password;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(email) ||
                string.IsNullOrEmpty(pass))
            {
                ShowError(Step1Error, "Please fill in all fields.");
                return;
            }
            if (!email.Contains('@'))
            {
                ShowError(Step1Error, "Invalid email address.");
                return;
            }
            if (pass != confirm)
            {
                ShowError(Step1Error, "Passwords do not match.");
                return;
            }

            Step1Error.Visibility = Visibility.Collapsed;
            _pendingUsername      = user;
            OtpEmailHint.Text     = $"We sent a 6-digit code to {MaskEmail(email)}";

            GoToStep(Step1Panel, Step2Panel, stepIndex: 1);
        }

        // ══════════════════════════════════════════════════════
        //  REGISTER — Step 2 (OTP)
        // ══════════════════════════════════════════════════════
        private void RegVerify_Click(object sender, RoutedEventArgs e)
        {
            // اگه PinBox property دیگه‌ای داره (مثل Pin یا Text) اینجا تغییر بده
            string entered = (OtpPinBox.Password ?? "").Replace(" ", "");

            if (entered == DefaultOtp)
            {
                Step2Error.Visibility = Visibility.Collapsed;
                GoToStep(Step2Panel, Step3Panel, stepIndex: 2);
                ShowWelcome();
            }
            else
            {
                ShowError(Step2Error, $"Invalid code. (hint: {DefaultOtp})");
            }
        }

        // ══════════════════════════════════════════════════════
        //  REGISTER — Step 3 (Welcome)
        // ══════════════════════════════════════════════════════
        private void ShowWelcome()
        {
            WelcomeTitle.Text        = $"Welcome, {_pendingUsername}!";
            WelcomeAvatar.DisplayName = _pendingUsername;

            // ConfettiCannon — اگه IsActive نداشت Start() یا Trigger() امتحان کن
            ConfettiLeft.FireBasic();
            ConfettiRight.FireBasic();

            _countdownSeconds = 4;
            CountdownText.Text = $"Opening app in {_countdownSeconds}s...";

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) =>
            {
                _countdownSeconds--;
                CountdownText.Text = $"Opening app in {_countdownSeconds}s...";
                if (_countdownSeconds <= 0)
                {
                    _countdownTimer!.Stop();
                    LoginCompleted?.Invoke();
                }
            };
            _countdownTimer.Start();
        }

        // ══════════════════════════════════════════════════════
        //  Step transition animation
        // ══════════════════════════════════════════════════════
        private void GoToStep(UIElement from, UIElement to, int stepIndex)
        {
            var sbOut = new Storyboard();
            var opOut = new DoubleAnimation
            {
                To = 0, Duration = TimeSpan.FromMilliseconds(150)
            };
            Storyboard.SetTarget(opOut, from);
            Storyboard.SetTargetProperty(opOut, "Opacity");
            sbOut.Children.Add(opOut);

            sbOut.Completed += (_, _) =>
            {
                ((FrameworkElement)from).Visibility = Visibility.Collapsed;
                ((FrameworkElement)to).Opacity      = 0;
                ((FrameworkElement)to).Visibility   = Visibility.Visible;

                RegisterStepBar.StepIndex = stepIndex;

                var sbIn = new Storyboard();
                var opIn = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(opIn, to);
                Storyboard.SetTargetProperty(opIn, "Opacity");
                sbIn.Children.Add(opIn);
                sbIn.Begin();
            };
            sbOut.Begin();
        }

        // ── Helpers ──────────────────────────────────────────
        private static void ShowError(TextBlock tb, string msg)
        {
            tb.Text = msg;
            tb.Visibility = Visibility.Visible;
        }

        private static string MaskEmail(string email)
        {
            int at = email.IndexOf('@');
            if (at <= 1) return email;
            return email[0] + "***" + email[at..];
        }
    }
}