using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;
using Lithiumvpn.Services.Xray;

namespace Lithiumvpn.Pages
{
    public sealed partial class DashboardPage : Page
    {
        private bool _tourEventsAttached = false;

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // ServersPage "Connect" hands us the chosen config through the nav parameter.
            if (e.Parameter is Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo navCfg)
            {
                ApplySelectedConfig(navCfg);
                if (!string.IsNullOrWhiteSpace(navCfg.ConfigCode))
                    _ = ConnectAsync(navCfg.ConfigCode);
            }

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

        // Remember the active config so we can re-render its labels when the language changes.
        private Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo? _selectedConfig;

        private readonly ConnectionService _vpn = ConnectionService.Instance;
        private readonly DispatcherTimer _liveTimer;   // traffic + latency while connected
        private int _liveTick;

        public DashboardPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

            // While connected: refresh traffic counters every tick, latency every 5th tick.
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _liveTimer.Tick += LiveTimer_Tick;

            _vpn.StateChanged += OnVpnStateChanged;
            ApplyVpnState(_vpn.State);

            // ✅ تغییر ۱: حالت اولیه — هیچ config انتخاب نشده
            SetEmptyState();

            // Re-apply localized labels when the language is switched at runtime.
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;

            // Live backend data: when the cached status refreshes, update the selected
            // config's figures (data left / days left) without needing a reselect.
            AppState.Instance.Changed += OnAppStateChanged;
        }

        private void OnAppStateChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_selectedConfig is { } cfg && RefreshSelectedFromState(cfg) is { } updated)
                {
                    _selectedConfig = updated;
                    ApplySelectedConfig(updated, keepPing: true);
                }
            });
        }

        /// <summary>Rebuilds the selected config's view model from the latest AppState data.</summary>
        private static Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo? RefreshSelectedFromState(
            Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo current)
        {
            foreach (var purchase in AppState.Instance.Purchases)
            {
                int totalGb = PlanUsageFor(purchase.Plan);
                foreach (var c in purchase.Configs ?? new())
                {
                    if (string.Equals(c.ConfigCode, current.ConfigCode, StringComparison.Ordinal))
                        return new Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo
                        {
                            PurchaseId = purchase.PurchaseId.ToString(),
                            DataVolume = totalGb > 0 ? $"{totalGb} GB" : current.DataVolume,
                            GbLeft = c.GbLeftValue,
                            DaysLeft = c.DaysLeft,
                            ExpiryDate = DateOnly.FromDateTime(DateTime.Now.AddDays(c.DaysLeft)),
                            Tag = current.Tag,
                            FlagEmoji = current.FlagEmoji,
                            Country = current.Country,
                            CountryCode = current.CountryCode,
                            ConfigName = c.Name ?? current.ConfigName,
                            ConfigCode = c.ConfigCode ?? current.ConfigCode,
                            PingMs = current.PingMs,
                            IsAvailable = current.IsAvailable,
                        };
                }
            }
            return null;
        }

        private static int PlanUsageFor(string? planName)
        {
            if (string.IsNullOrEmpty(planName)) return 0;
            foreach (var p in AppState.Instance.Plans)
                if (string.Equals(p.PlanName, planName, StringComparison.OrdinalIgnoreCase))
                    return p.Usage;
            return 0;
        }

        private void OnLanguageChanged()
        {
            if (_selectedConfig is { } cfg)
                ApplySelectedConfig(cfg, keepPing: true);
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
            ConfigFlagEllipse.Visibility = Visibility.Collapsed;
            ConfigFlag.Text = "";
            ConfigFlag.Visibility = Visibility.Visible;
            ConfigCountry.Text = LocalizationManager.Instance.Get("Dash_ChooseConfig");
            ConfigName.Text = LocalizationManager.Instance.Get("Dash_NoConfigSelected");

            ApplyStatsLayout(isLocal: false);

            PingValue.Text = "—";
            ExpiryValue.Text = "—";
            DataValue.Text = "0";
            UploadValue.Text = "—";
            DownloadValue.Text = "—";

            AnimateProgressRing(0);
        }

        // ─── Stats layout: full (backend) vs. ping-only (personal config) ─────
        private void ApplyStatsLayout(bool isLocal)
        {
            if (isLocal)
            {
                FadeCollapse(DataCard);
                FadeCollapse(ExpiryCard);
                FadeCollapse(TrafficRow);
                // Center the lone Ping card across all three columns.
                Grid.SetColumn(PingCard, 0);
                Grid.SetColumnSpan(PingCard, 3);
                PingCard.Margin = new Thickness(0, 0, 0, 5);
            }
            else
            {
                Grid.SetColumn(PingCard, 2);
                Grid.SetColumnSpan(PingCard, 1);
                PingCard.Margin = new Thickness(5, 0, 0, 5);
                FadeShow(DataCard);
                FadeShow(ExpiryCard);
                FadeShow(TrafficRow);
            }
        }

        private static void FadeCollapse(FrameworkElement el)
        {
            if (el.Visibility == Visibility.Collapsed) return;
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fade, el);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (_, _) => el.Visibility = Visibility.Collapsed;
            sb.Begin();
        }

        private static void FadeShow(FrameworkElement el)
        {
            if (el.Visibility == Visibility.Visible && el.Opacity >= 1) return;
            el.Opacity = 0;
            el.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, el);
            Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Begin();
        }

        // ─── Connection logic (real Xray tunnel) ───────────────────────
        private async void MainConnectButton_Click(object sender, RoutedEventArgs e)
        {
            switch (_vpn.State)
            {
                case VpnState.Disconnected:
                    if (_selectedConfig is not { } cfg || string.IsNullOrWhiteSpace(cfg.ConfigCode))
                    {
                        ShowInfo(LocalizationManager.Instance.Get("Dash_SelectConfigFirst"),
                            InfoBarSeverity.Warning);
                        return;
                    }
                    await ConnectAsync(cfg.ConfigCode);
                    break;

                case VpnState.Connected:
                    await _vpn.DisconnectAsync();
                    break;

                // Connecting: ignore clicks while the attempt is in flight.
            }
        }

        private async Task ConnectAsync(string configCode)
        {
            ConnectionInfoBar.IsOpen = false;
            try
            {
                await _vpn.ConnectAsync(configCode);
            }
            catch (Exception ex)
            {
                ShowInfo(string.Format(
                    LocalizationManager.Instance.Get("Dash_ConnectFailed"), FirstLine(ex.Message)),
                    InfoBarSeverity.Error);
            }
        }

        private static string FirstLine(string text)
        {
            var nl = text.IndexOf('\n');
            return (nl > 0 ? text[..nl] : text).Trim();
        }

        private void OnVpnStateChanged(VpnState state)
        {
            DispatcherQueue.TryEnqueue(() => ApplyVpnState(state));
        }

        private void ApplyVpnState(VpnState state)
        {
            string stateName = state switch
            {
                VpnState.Connecting => "Connecting",
                VpnState.Connected => "Connected",
                _ => "Disconnected"
            };
            VisualStateManager.GoToState(MainConnectButton, stateName, true);

            if (state == VpnState.Connected)
            {
                _liveTick = 0;
                UploadValue.Text = "0";
                DownloadValue.Text = "0";
                _liveTimer.Start();
                _ = RefreshLatencyAsync();
                _ = RefreshTrafficAsync();
            }
            else
            {
                _liveTimer.Stop();
                if (state == VpnState.Disconnected)
                {
                    UploadValue.Text = "—";
                    DownloadValue.Text = "—";
                }
            }
        }

        private void LiveTimer_Tick(object? sender, object e)
        {
            _ = RefreshTrafficAsync();
            if (_liveTick++ % 5 == 0)
                _ = RefreshLatencyAsync();
        }

        /// <summary>Cumulative tunnel traffic → the upload/download cards (MB).</summary>
        private async Task RefreshTrafficAsync()
        {
            var traffic = await _vpn.GetTrafficAsync();
            if (traffic is not { } t) return;
            UploadValue.Text = FormatBytes(t.Uplink);
            DownloadValue.Text = FormatBytes(t.Downlink);
        }

        private static string FormatBytes(long bytes)
        {
            double mb = bytes / 1_048_576.0;
            return mb >= 1000 ? (mb / 1024.0).ToString("0.00") : mb.ToString("0.00");
        }

        /// <summary>Latency through the live tunnel (only meaningful while connected).</summary>
        private async Task RefreshLatencyAsync()
        {
            if (_vpn.State != VpnState.Connected) return;
            int ping = await NetworkTestService.ProxiedLatencyAsync(_vpn.HttpPort);
            if (_vpn.State == VpnState.Connected)
                PingValue.Text = ping > 0 ? ping.ToString() : "—";
        }

        private void ShowInfo(string message, InfoBarSeverity severity)
        {
            ConnectionInfoBar.Severity = severity;
            ConnectionInfoBar.Message = message;
            ConnectionInfoBar.IsOpen = true;
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
            {
                bool changed = _selectedConfig?.ConfigCode != cfg.ConfigCode;
                ApplySelectedConfig(cfg);

                // Switching configs while connected → reconnect through the new server.
                if (changed && _vpn.State == VpnState.Connected)
                    await ConnectAsync(cfg.ConfigCode);
            }
        }

        private void ApplySelectedConfig(
            Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo cfg, bool keepPing = false)
        {
            _selectedConfig = cfg;

            // ── Personal / imported config: no backend quota, so the GB-left, expiry
            //    and upload/download cards fade away and only Ping remains. ──
            if (cfg.IsLocal)
            {
                ConfigFlagEllipse.Visibility = Visibility.Collapsed;
                ConfigFlag.Visibility = Visibility.Visible;
                ConfigFlag.Text = "\U0001F464";   // 👤 personal marker
                ConfigCountry.Text = cfg.ConfigName;
                ConfigName.Text = LocalizationManager.Instance.Get("Dialog_LocalConfig");

                ApplyStatsLayout(isLocal: true);

                if (!keepPing && _vpn.State != VpnState.Connected)
                    _ = PingSelectedAsync(cfg.ConfigCode);
                return;
            }

            // Backend config → make sure the full stats layout is restored.
            ApplyStatsLayout(isLocal: false);

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
            ExpiryValue.Text = cfg.DaysLeft.ToString();

            double totalGb = ParseGb(cfg.DataVolume);
            double leftGb = cfg.GbLeft;
            double percent = totalGb > 0 ? Math.Clamp((leftGb / totalGb) * 100.0, 0, 100) : 0;

            DataValue.Text = leftGb.ToString("0.##");
            DataProgressRing.Foreground = new SolidColorBrush(PercentToColor(percent));
            AnimateProgressRing(percent);

            // Auto-ping the freshly selected config (single-flight — supersedes any
            // in-progress ping from the servers page or a previous selection).
            // While connected, the live tunnel already drives the ping card.
            if (!keepPing && _vpn.State != VpnState.Connected)
                _ = PingSelectedAsync(cfg.ConfigCode);
        }

        private async Task PingSelectedAsync(string configCode)
        {
            if (string.IsNullOrWhiteSpace(configCode)) return;
            PingValue.Text = "…";
            int? ping = await PingService.Instance.PingConfigAsync(configCode);

            // null = superseded by a newer ping; leave the card for that one to fill.
            if (ping is null) return;
            // Ignore a late result if the user has since selected a different config.
            if (_selectedConfig?.ConfigCode != configCode) return;
            if (_vpn.State == VpnState.Connected) return;

            PingValue.Text = ping.Value >= 0 ? ping.Value.ToString() : "-1";
        }

        // ─── انیمیشن نرم ProgressRing ─────────────────────────────────
        private DispatcherTimer? _ringTimer;
        private double _ringTarget;
        private double _ringCurrent;

        private void AnimateProgressRing(double targetValue)
        {
            _ringTimer?.Stop();

            _ringTarget = Math.Clamp(targetValue, 0, 100);
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
            double step = diff * 0.12;

            if (Math.Abs(diff) < 0.3)
            {
                _ringCurrent = _ringTarget;
                DataProgressRing.Value = _ringTarget;
                _ringTimer?.Stop();
                return;
            }

            _ringCurrent += step;
            DataProgressRing.Value = _ringCurrent;
            DataProgressRing.Foreground = new SolidColorBrush(PercentToColor(_ringCurrent));
        }

        // ─── Helper: تبدیل درصد به رنگ ────────────────────────────────
        private static Color PercentToColor(double percent)
        {
            if (percent >= 60)
            {
                double t = (percent - 60) / 40.0;
                return InterpolateColor(
                    Color.FromArgb(255, 255, 214, 0),
                    Color.FromArgb(255, 0, 200, 83),
                    t);
            }
            else if (percent >= 25)
            {
                double t = (percent - 25) / 35.0;
                return InterpolateColor(
                    Color.FromArgb(255, 255, 109, 0),
                    Color.FromArgb(255, 255, 214, 0),
                    t);
            }
            else
            {
                double t = percent / 25.0;
                return InterpolateColor(
                    Color.FromArgb(255, 213, 0, 0),
                    Color.FromArgb(255, 255, 109, 0),
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
