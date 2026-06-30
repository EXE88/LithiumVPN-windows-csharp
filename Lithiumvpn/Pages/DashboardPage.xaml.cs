using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Text.RegularExpressions;
using Windows.UI;
using Lithiumvpn.Localization;

namespace Lithiumvpn.Pages
{
    public sealed partial class DashboardPage : Page
    {
        private bool _tourEventsAttached = false;

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        private void StartTour()
        {
            if (!_tourEventsAttached)
            {
                _tourEventsAttached = true;
                TourTip1.ActionButtonClick += (s, e) => { TourTip1.IsOpen = false; TourTip2.IsOpen = true; };
                TourTip1.CloseButtonClick  += (s, e) => TourTip1.IsOpen = false;
                TourTip2.ActionButtonClick += (s, e) => { TourTip2.IsOpen = false; TourTip3.IsOpen = true; };
                TourTip2.CloseButtonClick  += (s, e) => TourTip2.IsOpen = false;
                TourTip3.CloseButtonClick  += (s, e) => TourTip3.IsOpen = false;
            }
            TourTip1.IsOpen = true;
        }

        private enum ConnectionState { Disconnected, Connecting, Connected }
        private ConnectionState currentState = ConnectionState.Disconnected;
        private DispatcherTimer connectionTimer;

        // Remember the active config so we can re-render its labels when the language changes.
        private Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo? _selectedConfig;

        public DashboardPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

            connectionTimer = new DispatcherTimer();
            connectionTimer.Interval = TimeSpan.FromSeconds(2.5);
            connectionTimer.Tick += ConnectionTimer_Tick;

            UpdateVisualState();

            // ✅ تغییر ۱: حالت اولیه — هیچ config انتخاب نشده
            SetEmptyState();

            // Re-apply localized labels when the language is switched at runtime.
            // The page is cached for the app lifetime, so the subscription persists with it.
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_selectedConfig is { } cfg)
                ApplySelectedConfig(cfg);
            else
                SetEmptyState();
        }

        // Map an English country name to its localized form.
        private static string LocalizeCountry(string country) => country switch
        {
            "Netherlands" => LocalizationManager.Instance.Get("Country_Netherlands"),
            "Germany" => LocalizationManager.Instance.Get("Country_Germany"),
            "Poland" => LocalizationManager.Instance.Get("Country_Poland"),
            _ => country
        };

        // ─── حالت خالی اولیه ──────────────────────────────────────────
        private void SetEmptyState()
        {
            // header
            ConfigFlagEllipse.Visibility = Visibility.Collapsed;
            ConfigFlag.Text = "";
            ConfigFlag.Visibility = Visibility.Visible;
            ConfigCountry.Text = LocalizationManager.Instance.Get("Dash_ChooseConfig");
            ConfigName.Text = LocalizationManager.Instance.Get("Dash_NoConfigSelected");

            // data cards — همه صفر
            PingValue.Text = "—";
            ExpiryValue.Text = "—";
            DataValue.Text = "0";

            // ProgressRing صفر
            AnimateProgressRing(0);
        }

        // ─── Connection logic ──────────────────────────────────────────
        private void MainConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentState == ConnectionState.Disconnected)
            {
                currentState = ConnectionState.Connecting;
                connectionTimer.Start();
                UpdateVisualState();
            }
            else if (currentState == ConnectionState.Connected)
            {
                currentState = ConnectionState.Disconnected;
                UpdateVisualState();
            }
        }

        private void ConnectionTimer_Tick(object? sender, object e)
        {
            connectionTimer.Stop();
            currentState = ConnectionState.Connected;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            string stateName = currentState switch
            {
                ConnectionState.Disconnected => "Disconnected",
                ConnectionState.Connecting => "Connecting",
                ConnectionState.Connected => "Connected",
                _ => "Disconnected"
            };
            VisualStateManager.GoToState(MainConnectButton, stateName, true);
        }

        // ─── Config selector ───────────────────────────────────────────
        private async void ConfigSelector_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Lithiumvpn.Dialogs.ConfigSelectionDialog
            {
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();

            if (dialog.SelectedConfig is { } cfg)
                ApplySelectedConfig(cfg);
        }

        private void ApplySelectedConfig(Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo cfg)
        {
            _selectedConfig = cfg;

            // ── هدر ──────────────────────────────────────────────────
            ConfigFlag.Text = cfg.FlagEmoji;
            try
            {
                var code = (cfg.CountryCode ?? "").ToLower();
                if (!string.IsNullOrEmpty(code))
                {
                    var uri = new Uri($"ms-appx:///Assets/Flags/{code}.svg");
                    var svg = new SvgImageSource(uri);
                    ConfigFlagEllipse.Fill = new ImageBrush { ImageSource = svg, Stretch = Stretch.UniformToFill };
                    ConfigFlagEllipse.Visibility = Visibility.Visible;
                    ConfigFlag.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ConfigFlagEllipse.Visibility = Visibility.Collapsed;
                    ConfigFlag.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                ConfigFlagEllipse.Visibility = Visibility.Collapsed;
                ConfigFlag.Visibility = Visibility.Visible;
            }

            ConfigCountry.Text = $"{LocalizeCountry(cfg.Country)} ({cfg.CountryCode})";
            ConfigName.Text = cfg.ConfigName;

            // ── data cards ────────────────────────────────────────────
            PingValue.Text = cfg.PingMs > 0 ? cfg.PingMs.ToString() : "—";
            ExpiryValue.Text = cfg.DaysLeft.ToString();

            // ── ✅ تغییر ۲: محاسبه درصد GB left و ProgressRing هوشمند ──
            // DataVolume = حجم کل purchase (مثلاً "100 GB")
            // GbLeft = حجم باقی‌مانده این config (مثلاً "6.75")
            // فعلاً GbLeft رو از cfg.GbLeft میگیریم

            double totalGb = ParseGb(cfg.DataVolume);
            double leftGb = cfg.GbLeft;
            double percent = totalGb > 0 ? Math.Clamp((leftGb / totalGb) * 100.0, 0, 100) : 0;

            // نمایش عدد
            DataValue.Text = leftGb.ToString("0.##");

            // رنگ بر اساس درصد: سبز ← زرد ← نارنجی ← قرمز
            DataProgressRing.Foreground = new SolidColorBrush(PercentToColor(percent));

            // انیمیشن نرم ProgressRing
            AnimateProgressRing(percent);
        }

        // ─── انیمیشن نرم ProgressRing ─────────────────────────────────
        private DispatcherTimer? _ringTimer;
        private double _ringTarget;
        private double _ringCurrent;
        private const double RingStep = 1.5; // سرعت انیمیشن — هر tick چند درصد

        private void AnimateProgressRing(double targetValue)
        {
            _ringTimer?.Stop();

            _ringTarget = Math.Clamp(targetValue, 0, 100);
            // ✅ به جای _currentRingValue از DataProgressRing.Value استفاده کن
            _ringCurrent = DataProgressRing.Value;

            if (Math.Abs(_ringCurrent - _ringTarget) < 0.5)
            {
                DataProgressRing.Value = _ringTarget;
                return;
            }

            _ringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _ringTimer.Tick += RingTimer_Tick;
            _ringTimer.Start();
        }

        private void RingTimer_Tick(object? sender, object e)
        {
            double diff = _ringTarget - _ringCurrent;

            // Easing: هر tick کسری از فاصله باقیمانده رو طی میکنه
            double step = diff * 0.12;

            // اگه خیلی نزدیک شدیم، مستقیم برو به target
            if (Math.Abs(diff) < 0.3)
            {
                _ringCurrent = _ringTarget;
                DataProgressRing.Value = _ringTarget;
                _ringTimer?.Stop();
                return;
            }

            _ringCurrent += step;
            DataProgressRing.Value = _ringCurrent;

            // رنگ رو هم همزمان آپدیت کن
            DataProgressRing.Foreground = new SolidColorBrush(PercentToColor(_ringCurrent));
        }

        // ─── Helper: تبدیل درصد به رنگ ────────────────────────────────
        private static Color PercentToColor(double percent)
        {
            // 100% = سبز (#00C853)
            // 50%  = زرد  (#FFD600)
            // 20%  = نارنجی (#FF6D00)
            // 0%   = قرمز (#D50000)
            if (percent >= 60)
            {
                // سبز به زرد
                double t = (percent - 60) / 40.0;
                return InterpolateColor(
                    Color.FromArgb(255, 255, 214, 0),   // زرد
                    Color.FromArgb(255, 0, 200, 83),    // سبز
                    t);
            }
            else if (percent >= 25)
            {
                // زرد به نارنجی
                double t = (percent - 25) / 35.0;
                return InterpolateColor(
                    Color.FromArgb(255, 255, 109, 0),   // نارنجی
                    Color.FromArgb(255, 255, 214, 0),   // زرد
                    t);
            }
            else
            {
                // نارنجی به قرمز
                double t = percent / 25.0;
                return InterpolateColor(
                    Color.FromArgb(255, 213, 0, 0),     // قرمز
                    Color.FromArgb(255, 255, 109, 0),   // نارنجی
                    t);
            }
        }

        private static Color InterpolateColor(Color from, Color to, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                255,
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t));
        }

        private static double ParseGb(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var m = Regex.Match(text, @"[\d\.]+");
            return m.Success && double.TryParse(m.Value, out var v) ? v : 0;
        }
    }
}