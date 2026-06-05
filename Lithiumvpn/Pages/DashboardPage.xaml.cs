using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using System;
using System.Text.RegularExpressions;

namespace Lithiumvpn.Pages
{
    public sealed partial class DashboardPage : Page
    {
        private enum ConnectionState { Disconnected, Connecting, Connected }
        private ConnectionState currentState = ConnectionState.Disconnected;
        private DispatcherTimer connectionTimer;

        public DashboardPage()
        {
            this.InitializeComponent();

            // Keep page instance cached so UI state (connection button visual state) persists across navigation
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;

            connectionTimer = new DispatcherTimer();
            connectionTimer.Interval = TimeSpan.FromSeconds(2.5);
            connectionTimer.Tick += ConnectionTimer_Tick;

            UpdateVisualState();

            // Set default config on startup (Germany) so header shows flag and config name immediately
            try
            {
                var defaultCfg = new Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo
                {
                    PurchaseId = "P1",
                    DataVolume = "50 GB",
                    ExpiryDate = new DateOnly(2025, 6, 30),
                    DaysLeft = 47,
                    Tag = "P1_DE",
                    FlagEmoji = "🇩🇪",
                    Country = "Germany",
                    CountryCode = "DE",
                    ConfigName = "cn_de_f1",
                    PingMs = 38,
                    IsAvailable = true
                };

                ApplySelectedConfig(defaultCfg);
            }
            catch { }
        }

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
            // ── هدر: config selection bar ──────────────────────────
            // Try to load SVG flag from Assets/Flags/{countrycode}.svg, fallback to emoji
            ConfigFlag.Text = cfg.FlagEmoji;   // set emoji as fallback
            try
            {
                var code = (cfg.CountryCode ?? "").ToLower();
                if (!string.IsNullOrEmpty(code))
                {
                    var uri = new Uri($"ms-appx:///Assets/Flags/{code}.svg");
                    var svg = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(uri);
                    var brush = new ImageBrush { ImageSource = svg, Stretch = Stretch.UniformToFill };
                    ConfigFlagEllipse.Fill = brush;
                    ConfigFlagEllipse.Visibility = Visibility.Visible;
                    ConfigFlag.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ConfigFlagEllipse.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    ConfigFlagEllipse.Visibility = Visibility.Collapsed;
                    ConfigFlag.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                ConfigFlagEllipse.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                ConfigFlagEllipse.Visibility = Visibility.Collapsed;
                ConfigFlag.Visibility = Visibility.Visible;
            }
            ConfigCountry.Text = $"{cfg.Country} ({cfg.CountryCode})";
            // show config name in header (replaces previous city field)
            try { ConfigName.Text = cfg.ConfigName; } catch { }

            // ── Fix 5: فقط عدد رو بده نه متن کامل ──────────────────
            // کارت Ping — فقط عدد (واحد "ms" توی XAML جداست)
            PingValue.Text = cfg.PingMs.ToString();

            // کارت Expiry — روزهای باقیمانده (نه تاریخ)
            ExpiryValue.Text = cfg.DaysLeft.ToString();

            // کارت GB Left — حجم دیتا
            // ✅ Fix 5: DataValue یه TextBlock کوچیک داخل ProgressRing هست
            // نیازی به تغییر اعداد نیست چون بعداً با API واقعی میشه
            // فقط DataValue رو آپدیت کن
            // Show only numeric portion (e.g., "200" instead of "200 GB")
            try
            {
                var m = Regex.Match(cfg.DataVolume ?? string.Empty, "[\\d\\.]+");
                DataValue.Text = m.Success ? m.Value : cfg.DataVolume;
            }
            catch
            {
                DataValue.Text = cfg.DataVolume;
            }
        }
    }
}