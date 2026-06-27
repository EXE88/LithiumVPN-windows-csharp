using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.UI.Xaml.Shapes;

namespace Lithiumvpn.Pages
{
    public sealed partial class LoginPage : Page
    {
        private enum ToastType { Success, Info, Warning, Error }
        // ── Default credentials (demo only) ──────────────────
        private const string DefaultUsername = "lithium";
        private const string DefaultPassword = "Lithium@2025";
        private const string DefaultOtp      = "123456";

        public event Action? LoginCompleted;

        private string _pendingUsername = "";
        private DispatcherTimer? _countdownTimer;
        private int _countdownSeconds = 4;

        public LoginPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OtpBack_Click(object sender, RoutedEventArgs e)
        {
            // navigate back to step 1
            GoToStep(Step2Panel, Step1Panel, stepIndex: 0);
        }

        private async void OtpResend_Click(object sender, RoutedEventArgs e)
        {
            // In a real app you'd re-request the verification code here.
            // Show an in-app toast notification instead of a modal dialog.
            ShowToast("A new verification code has been sent to your email.");
        }

        private async void ShowToast(string message, ToastType type = ToastType.Success)
        {
            // Create visual toast container
            var toast = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 35, 0, 0)
            };

            // choose colors by type
            var bgColor = Microsoft.UI.ColorHelper.FromArgb(255, 230, 250, 230);
            var iconColor = Microsoft.UI.ColorHelper.FromArgb(255, 76, 175, 80);
            switch (type)
            {
                case ToastType.Info:
                    bgColor = Microsoft.UI.ColorHelper.FromArgb(255, 230, 240, 255);
                    iconColor = Microsoft.UI.ColorHelper.FromArgb(255, 33, 150, 243);
                    break;
                case ToastType.Warning:
                    bgColor = Microsoft.UI.ColorHelper.FromArgb(255, 255, 249, 230);
                    iconColor = Microsoft.UI.ColorHelper.FromArgb(255, 255, 193, 7);
                    break;
                case ToastType.Error:
                    bgColor = Microsoft.UI.ColorHelper.FromArgb(255, 255, 235, 238);
                    iconColor = Microsoft.UI.ColorHelper.FromArgb(255, 244, 67, 54);
                    break;
            }

            toast.Background = new SolidColorBrush(bgColor);
            toast.CornerRadius = new CornerRadius(8);
            toast.Padding = new Thickness(12, 8, 12, 8);
            toast.Opacity = 0;
            toast.HorizontalAlignment = HorizontalAlignment.Center;
            toast.VerticalAlignment = VerticalAlignment.Top;
            toast.Margin = new Thickness(0, 35, 0, 0);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            // simple icon (circle)
            var icon = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(iconColor),
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = message,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380
            };

            var closeBtn = new Button
            {
                Content = "×",
                Background = null,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            panel.Children.Add(icon);
            panel.Children.Add(text);
            panel.Children.Add(closeBtn);
            toast.Child = panel;

            // place toast into page root
            RootGrid.Children.Add(toast);

            // entrance animation
            var tt = new TranslateTransform { Y = -12 };
            toast.RenderTransform = tt;

            var sbIn = new Storyboard();
            var opIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(240) };
            Storyboard.SetTarget(opIn, toast);
            Storyboard.SetTargetProperty(opIn, "Opacity");

            var txIn = new DoubleAnimation { From = -12, To = 0, Duration = TimeSpan.FromMilliseconds(240) };
            Storyboard.SetTarget(txIn, tt);
            Storyboard.SetTargetProperty(txIn, "Y");

            sbIn.Children.Add(opIn);
            sbIn.Children.Add(txIn);
            sbIn.Begin();

            // removal helpers
            var removeToast = new Action(async () =>
            {
                var sbOut = new Storyboard();
                var opOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
                Storyboard.SetTarget(opOut, toast);
                Storyboard.SetTargetProperty(opOut, "Opacity");

                var txOut = new DoubleAnimation { To = -12, Duration = TimeSpan.FromMilliseconds(200) };
                Storyboard.SetTarget(txOut, tt);
                Storyboard.SetTargetProperty(txOut, "Y");

                sbOut.Children.Add(opOut);
                sbOut.Children.Add(txOut);
                sbOut.Begin();

                await Task.Delay(220);
                RootGrid.Children.Remove(toast);
            });

            // allow manual close
            closeBtn.Click += (_, _) => removeToast();

            // auto-dismiss after delay
            await Task.Delay(3500);
            removeToast();
        }

        private bool _initialized;
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;
            UpdateLogo();
            // ensure PinBox shows revealed characters if supported
            TrySetPinBoxRevealVisible();
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

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            if (LoginUsername.Text.Trim() == DefaultUsername &&
                LoginPassword.Password    == DefaultPassword)
            {
                LoginCompleted?.Invoke();
            }
            else
            {
                ShowToast("Invalid username or password.", ToastType.Error);
                AnimateFieldError(LoginBtn);
            }
        }

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

        private void RegNext_Click(object sender, RoutedEventArgs e)
        {
            string user    = RegUsername.Text.Trim();
            string email   = RegEmail.Text.Trim();
            string pass    = RegPassword.Password;
            string confirm = RegConfirmPassword.Password;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(email) ||
                string.IsNullOrEmpty(pass))
            {
                ShowToast("Please fill in all fields.", ToastType.Error);
                return;
            }
            if (!email.Contains('@'))
            {
                ShowToast("Invalid email address.", ToastType.Error);
                return;
            }
            if (pass != confirm)
            {
                ShowToast("Passwords do not match.", ToastType.Error);
                return;
            }
            // proceed
            _pendingUsername      = user;
            OtpEmailHint.Text     = $"We sent a 6-digit code to {MaskEmail(email)}";

            GoToStep(Step1Panel, Step2Panel, stepIndex: 1);
        }

        private void RegVerify_Click(object sender, RoutedEventArgs e)
        {
            string entered = (OtpPinBox.Password ?? "").Replace(" ", "");

            if (entered == DefaultOtp)
            {
                // show success state on pinbox
                TryShowPinBoxSuccess();

                // small delay so the success state is visible before proceeding
                var _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(450);
                    GoToStep(Step2Panel, Step3Panel, stepIndex: 2);
                    ShowWelcome();
                });
            }
            else
            {
                // show error as toast and animate pinbox
                ShowToast($"Invalid code. (hint: {DefaultOtp})", ToastType.Error);
                TryShowPinBoxError();
            }
        }

        private bool TryShowPinBoxSuccess()
        {
            if (OtpPinBox is null) return false;

            // Try common API names from DevWinUI PinBox
            var names = new[] { "ShowSuccess", "PlaySuccess", "SetSuccess", "ShowCompleted" };
            if (TryInvokePinBoxMethods(names)) return true;

            // Fallback: pulse animation
            try
            {
                var st = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
                OtpPinBox.RenderTransform = st;
                OtpPinBox.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

                var sb = new Storyboard();
                var a1 = new DoubleAnimation { From = 1, To = 1.06, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var a2 = new DoubleAnimation { From = 1.06, To = 1, BeginTime = TimeSpan.FromMilliseconds(140), Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

                Storyboard.SetTarget(a1, st);
                Storyboard.SetTargetProperty(a1, "ScaleX");
                Storyboard.SetTarget(a2, st);
                Storyboard.SetTargetProperty(a2, "ScaleX");

                var b1 = new DoubleAnimation { From = 1, To = 1.06, Duration = TimeSpan.FromMilliseconds(140), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var b2 = new DoubleAnimation { From = 1.06, To = 1, BeginTime = TimeSpan.FromMilliseconds(140), Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                Storyboard.SetTarget(b1, st);
                Storyboard.SetTargetProperty(b1, "ScaleY");
                Storyboard.SetTarget(b2, st);
                Storyboard.SetTargetProperty(b2, "ScaleY");

                sb.Children.Add(a1);
                sb.Children.Add(a2);
                sb.Children.Add(b1);
                sb.Children.Add(b2);
                sb.Begin();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // field-level helpers removed — register uses Step1Error textbox again

        private async void AnimateFieldError(Control field)
        {
            try
            {
                var tt = new TranslateTransform { X = 0 };
                field.RenderTransform = tt;
                field.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

                var sb = new Storyboard();
                var d1 = new DoubleAnimationUsingKeyFrames();
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = -8, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 8, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = -4, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 4, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)) });
                Storyboard.SetTarget(d1, tt);
                Storyboard.SetTargetProperty(d1, "X");
                sb.Children.Add(d1);
                sb.Begin();
                await Task.Delay(360);
            }
            catch { }
        }

        private void RegPassword_GotFocus(object sender, RoutedEventArgs e)
        {
            PasswordRulesBox.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            var op = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180) };
            Storyboard.SetTarget(op, PasswordRulesBox);
            Storyboard.SetTargetProperty(op, "Opacity");
            sb.Children.Add(op);
            sb.Begin();
        }

        private async void RegPassword_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Delay(200);
            var sb = new Storyboard();
            var op = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160) };
            Storyboard.SetTarget(op, PasswordRulesBox);
            Storyboard.SetTargetProperty(op, "Opacity");
            sb.Children.Add(op);
            sb.Begin();
            await Task.Delay(160);
            PasswordRulesBox.Visibility = Visibility.Collapsed;
        }

        private void RegPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            var pass = RegPassword.Password ?? "";
            bool lenOk = pass.Length >= 8;
            bool digitOk = System.Text.RegularExpressions.Regex.IsMatch(pass, "\\d");
            RuleLenIcon.Text = lenOk ? "✔" : "○";
            RuleLenIcon.Foreground = new SolidColorBrush(lenOk ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Gray);
            RuleDigitIcon.Text = digitOk ? "✔" : "○";
            RuleDigitIcon.Foreground = new SolidColorBrush(digitOk ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Gray);
            // no border manipulation here — Step1Error will display validation messages
        }

        private bool TrySetPinBoxRevealVisible()
        {
            if (OtpPinBox is null) return false;

            var type = OtpPinBox.GetType();

            // 1) Try PasswordRevealMode enum property (common pattern)
            var p = type.GetProperty("PasswordRevealMode", BindingFlags.Public | BindingFlags.Instance);
            if (p is not null && p.CanWrite)
            {
                try
                {
                    var enumType = p.PropertyType;
                    var val = Enum.Parse(enumType, "Visible");
                    p.SetValue(OtpPinBox, val);
                    return true;
                }
                catch { }
            }

            // 2) Try alternative property names
            foreach (var name in new[] { "RevealMode", "PasswordReveal", "IsPasswordRevealEnabled", "ShowReveal", "EnableReveal" })
            {
                var pi = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (pi is not null && pi.CanWrite)
                {
                    try
                    {
                        if (pi.PropertyType == typeof(bool))
                        {
                            pi.SetValue(OtpPinBox, true);
                            return true;
                        }
                        if (pi.PropertyType.IsEnum)
                        {
                            var enumVal = Enum.Parse(pi.PropertyType, "Visible");
                            pi.SetValue(OtpPinBox, enumVal);
                            return true;
                        }
                    }
                    catch { }
                }
            }

            // 3) Try to set PasswordChar to null/zero
            var pc = type.GetProperty("PasswordChar", BindingFlags.Public | BindingFlags.Instance);
            if (pc is not null && pc.CanWrite)
            {
                try
                {
                    if (pc.PropertyType == typeof(char)) pc.SetValue(OtpPinBox, '\0');
                    return true;
                }
                catch { }
            }

            // 4) Try UseSystemPasswordChar or IsPassword boolean toggles
            foreach (var name in new[] { "UseSystemPasswordChar", "IsPassword", "IsPasswordBox", "IsPasswordMasked" })
            {
                var pi2 = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (pi2 is not null && pi2.CanWrite && pi2.PropertyType == typeof(bool))
                {
                    try
                    {
                        pi2.SetValue(OtpPinBox, false);
                        return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        private bool TryShowPinBoxError()
        {
            if (OtpPinBox is null) return false;

            var names = new[] { "ShowError", "PlayError", "SetError", "Shake" };
            if (TryInvokePinBoxMethods(names)) return true;

            // Fallback: shake animation
            try
            {
                var tt = new TranslateTransform { X = 0 };
                OtpPinBox.RenderTransform = tt;

                var sb = new Storyboard();
                var d1 = new DoubleAnimationUsingKeyFrames();
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = -10, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 10, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = -6, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 6, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)) });
                d1.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)) });

                Storyboard.SetTarget(d1, tt);
                Storyboard.SetTargetProperty(d1, "X");
                sb.Children.Add(d1);
                sb.Begin();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryInvokePinBoxMethods(string[] names)
        {
            var type = OtpPinBox.GetType();
            foreach (var n in names)
            {
                try
                {
                    var mi = type.GetMethod(n, BindingFlags.Public | BindingFlags.Instance);
                    if (mi is not null)
                    {
                        mi.Invoke(OtpPinBox, null);
                        return true;
                    }
                    // also try property setter boolean e.g. IsError = true
                    var pi = type.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    if (pi is not null && pi.CanWrite && pi.PropertyType == typeof(bool))
                    {
                        pi.SetValue(OtpPinBox, true);
                        return true;
                    }
                }
                catch
                {
                    // ignore and try next
                }
            }

            return false;
        }

        private void ShowWelcome()
        {
            WelcomeTitle.Text        = $"Welcome, {_pendingUsername}!";
            WelcomeAvatar.DisplayName = _pendingUsername;

            ConfettiLeft.FireBasic();
            ConfettiRight.FireBasic();

            _countdownSeconds = 5;
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
