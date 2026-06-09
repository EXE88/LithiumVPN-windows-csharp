using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace Lithiumvpn.Dialogs
{
    public sealed partial class ConfigSelectionDialog : ContentDialog
    {
        // ─── مدل داده ───────────────────────────────────────────────
        public class ConfigInfo
        {
            public string PurchaseId { get; init; } = "";
            public string DataVolume { get; init; } = ""; // حجم کل purchase مثلاً "100 GB"
            public double GbLeft { get; init; }       // ✅ حجم باقی‌مانده این config
            public DateOnly ExpiryDate { get; init; }
            public int DaysLeft { get; init; }
            public string Tag { get; init; } = "";
            public string FlagEmoji { get; init; } = "";
            public string Country { get; init; } = "";
            public string CountryCode { get; init; } = "";
            public string ConfigName { get; init; } = "";
            public int PingMs { get; init; }
            public bool IsAvailable { get; init; } = true;

            public string ExpiryPersian
            {
                get
                {
                    var pc = new PersianCalendar();
                    var dt = ExpiryDate.ToDateTime(TimeOnly.MinValue);
                    return $"{pc.GetYear(dt)}/{pc.GetMonth(dt):D2}/{pc.GetDayOfMonth(dt):D2}";
                }
            }
        }

        // ─── داده‌های پیش‌فرض ────────────────────────────────────────
        private readonly List<(string Label, string Volume, List<ConfigInfo> Configs)> _purchases = new()
        {
            (
                "Purchase #1", "50 GB",
                new List<ConfigInfo>
                {
                    new() { PurchaseId="P1", Tag="P1_NL", FlagEmoji="🇳🇱", Country="Netherlands", CountryCode="NL", ConfigName="cn_nl_a1", PingMs=24, GbLeft=32.5, DaysLeft=47,  ExpiryDate=new DateOnly(2025,6,30)  },
                    new() { PurchaseId="P1", Tag="P1_DE", FlagEmoji="🇩🇪", Country="Germany",     CountryCode="DE", ConfigName="cn_de_f1", PingMs=38, GbLeft=6.75, DaysLeft=47,  ExpiryDate=new DateOnly(2025,6,30)  },
                    new() { PurchaseId="P1", Tag="P1_PL", FlagEmoji="🇵🇱", Country="Poland",      CountryCode="PL", ConfigName="cn_pl_w1", PingMs=51, GbLeft=44.0, DaysLeft=47,  ExpiryDate=new DateOnly(2025,6,30)  },
                }
            ),
            (
                "Purchase #2", "100 GB",
                new List<ConfigInfo>
                {
                    new() { PurchaseId="P2", Tag="P2_NL", FlagEmoji="🇳🇱", Country="Netherlands", CountryCode="NL", ConfigName="cn_nl_b1", PingMs=27, GbLeft=78.0, DaysLeft=93,  ExpiryDate=new DateOnly(2025,8,15)  },
                    new() { PurchaseId="P2", Tag="P2_DE", FlagEmoji="🇩🇪", Country="Germany",     CountryCode="DE", ConfigName="cn_de_f2", PingMs=41, GbLeft=15.3, DaysLeft=93,  ExpiryDate=new DateOnly(2025,8,15)  },
                    new() { PurchaseId="P2", Tag="P2_PL", FlagEmoji="🇵🇱", Country="Poland",      CountryCode="PL", ConfigName="cn_pl_w2", PingMs=0,  GbLeft=0,    DaysLeft=93,  ExpiryDate=new DateOnly(2025,8,15)  },
                }
            ),
            (
                "Purchase #3", "200 GB",
                new List<ConfigInfo>
                {
                    new() { PurchaseId="P3", Tag="P3_NL", FlagEmoji="🇳🇱", Country="Netherlands", CountryCode="NL", ConfigName="cn_nl_c1", PingMs=22, GbLeft=185.0, DaysLeft=169, ExpiryDate=new DateOnly(2025,12,1)  },
                    new() { PurchaseId="P3", Tag="P3_DE", FlagEmoji="🇩🇪", Country="Germany",     CountryCode="DE", ConfigName="cn_de_f3", PingMs=35, GbLeft=120.5, DaysLeft=169, ExpiryDate=new DateOnly(2025,12,1)  },
                    new() { PurchaseId="P3", Tag="P3_PL", FlagEmoji="🇵🇱", Country="Poland",      CountryCode="PL", ConfigName="cn_pl_w3", PingMs=48, GbLeft=92.0,  DaysLeft=169, ExpiryDate=new DateOnly(2025,12,1)  },
                }
            ),
        };

        public ConfigInfo? SelectedConfig { get; private set; }
        private Button? _selectedButton;

        public ConfigSelectionDialog()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            foreach (var (label, volume, configs) in _purchases)
                PurchasesPanel.Children.Add(BuildPurchaseSection(label, volume, configs));
        }

        private StackPanel BuildPurchaseSection(string label, string volume, List<ConfigInfo> configs)
        {
            string expiryPersian = configs.FirstOrDefault()?.ExpiryPersian ?? "";

            var headerGrid = new Grid { ColumnSpacing = 8 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBorder = new Border
            {
                Style = (Style)Resources["PurchaseLabelStyle"],
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                }
            };

            var infoText = new TextBlock
            {
                Text = $"{volume}  ·  Expiry {expiryPersian}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6,
            };

            var divider = new Rectangle
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.15,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            };

            Grid.SetColumn(labelBorder, 0);
            Grid.SetColumn(infoText, 1);
            Grid.SetColumn(divider, 2);
            headerGrid.Children.Add(labelBorder);
            headerGrid.Children.Add(infoText);
            headerGrid.Children.Add(divider);

            var cardsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var cfg in configs)
                cardsPanel.Children.Add(BuildConfigCard(cfg, volume));

            var section = new StackPanel { Spacing = 10 };
            section.Children.Add(headerGrid);
            section.Children.Add(cardsPanel);
            return section;
        }

        private Button BuildConfigCard(ConfigInfo cfg, string volume)
        {
            // ✅ تغییر ۳: SVG پرچم دایره‌ای — مثل config selection bar
            var flagContainer = BuildFlagCircle(cfg.CountryCode, cfg.FlagEmoji);

            var countryText = new TextBlock
            {
                Text = cfg.Country,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0),
            };

            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(flagContainer);
            content.Children.Add(countryText);

            if (!cfg.IsAvailable)
                content.Children.Add(new TextBlock
                {
                    Text = "Unavailable",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 57, 53))
                });

            var tagInfo = new ConfigInfo
            {
                PurchaseId = cfg.PurchaseId,
                DataVolume = volume,
                GbLeft = cfg.GbLeft,
                ExpiryDate = cfg.ExpiryDate,
                DaysLeft = cfg.DaysLeft,
                Tag = cfg.Tag,
                FlagEmoji = cfg.FlagEmoji,
                Country = cfg.Country,
                CountryCode = cfg.CountryCode,
                ConfigName = cfg.ConfigName,
                PingMs = cfg.PingMs,
                IsAvailable = cfg.IsAvailable,
            };

            var btn = new Button
            {
                Template = (ControlTemplate)Resources["ConfigCardTemplate"],
                Content = content,
                Tag = tagInfo,
                IsEnabled = cfg.IsAvailable,
                UseSystemFocusVisuals = false,
                Opacity = cfg.IsAvailable ? 1.0 : 0.45,
            };

            if (cfg.IsAvailable)
                btn.Click += ConfigCard_Click;

            return btn;
        }

        // ✅ دایره پرچم SVG — همان الگوی config selection bar
        private static Grid BuildFlagCircle(string countryCode, string fallbackEmoji)
        {
            var container = new Grid { Width = 40, Height = 40 };

            // دایره پس‌زمینه
            var bg = new Ellipse
            {
                Width = 40,
                Height = 40,
                Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
            };
            container.Children.Add(bg);

            // تلاش برای بارگذاری SVG
            bool svgLoaded = false;
            try
            {
                var code = countryCode.ToLower();
                if (!string.IsNullOrEmpty(code))
                {
                    var uri = new Uri($"ms-appx:///Assets/Flags/{code}.svg");
                    var svg = new SvgImageSource(uri);
                    var imgEllipse = new Ellipse
                    {
                        Width = 40,
                        Height = 40,
                        Fill = new ImageBrush { ImageSource = svg, Stretch = Stretch.UniformToFill },
                    };
                    container.Children.Add(imgEllipse);
                    svgLoaded = true;
                }
            }
            catch { }

            // fallback emoji اگه SVG نبود
            if (!svgLoaded)
            {
                container.Children.Add(new TextBlock
                {
                    Text = fallbackEmoji,
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            return container;
        }

        private void ConfigCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedBtn) return;
            if (clickedBtn.Tag is not ConfigInfo cfg) return;

            if (_selectedButton != null)
                VisualStateManager.GoToState(_selectedButton, "Unselected", true);

            VisualStateManager.GoToState(clickedBtn, "Selected", true);
            _selectedButton = clickedBtn;
            SelectedConfig = cfg;

            _ = Task.Delay(180).ContinueWith(_ =>
                DispatcherQueue.TryEnqueue(() => this.Hide()));
        }
    }
}