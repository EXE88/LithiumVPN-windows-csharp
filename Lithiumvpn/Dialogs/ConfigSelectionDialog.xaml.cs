using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Lithiumvpn.Dialogs
{
    public sealed partial class ConfigSelectionDialog : ContentDialog
    {
        // ─── مدل داده ───────────────────────────────────────────────
        public class ConfigInfo
        {
            public string PurchaseId { get; init; } = "";
            public string DataVolume { get; init; } = "";
            public DateOnly ExpiryDate { get; init; }
            public int DaysLeft { get; init; }
            public string Tag { get; init; } = "";
            public string FlagEmoji { get; init; } = "";
            public string Country { get; init; } = "";
            public string CountryCode { get; init; } = "";
            // ConfigName replaces previous 'City' field. Not shown in modal cards; only used in header after selection.
            public string ConfigName { get; init; } = "";
            public int PingMs { get; init; }
            public bool IsAvailable { get; init; } = true;

            public string ExpiryPersian
            {
                get
                {
                    var pc = new PersianCalendar();
                    var dt = ExpiryDate.ToDateTime(TimeOnly.MinValue);
                    int y = pc.GetYear(dt);
                    int m = pc.GetMonth(dt);
                    int d = pc.GetDayOfMonth(dt);
                    return $"{y}/{m:D2}/{d:D2}";
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
                    new() { PurchaseId="P1", Tag="P1_NL", FlagEmoji="🇳🇱", Country="Netherlands", CountryCode="NL", ConfigName="cn_nl_a1", PingMs=24, DaysLeft=47,  ExpiryDate=new DateOnly(2025,6,30)  },
                    new() { PurchaseId="P1", Tag="P1_DE", FlagEmoji="🇩🇪", Country="Germany",     CountryCode="DE", ConfigName="cn_de_f1", PingMs=38, DaysLeft=47,  ExpiryDate=new DateOnly(2025,6,30)  },
                    new() { PurchaseId="P1", Tag="P1_PL", FlagEmoji="🇵🇱", Country="Poland",      CountryCode="PL", ConfigName="cn_pl_w1", PingMs=51, DaysLeft=47,  ExpiryDate=new DateOnly(2025,6,30)  },
                }
            ),
            (
                "Purchase #2", "100 GB",
                new List<ConfigInfo>
                {
                    new() { PurchaseId="P2", Tag="P2_NL", FlagEmoji="🇳🇱", Country="Netherlands", CountryCode="NL", ConfigName="cn_nl_b1", PingMs=27, DaysLeft=93,  ExpiryDate=new DateOnly(2025,8,15)  },
                    new() { PurchaseId="P2", Tag="P2_DE", FlagEmoji="🇩🇪", Country="Germany",     CountryCode="DE", ConfigName="cn_de_f2", PingMs=41, DaysLeft=93,  ExpiryDate=new DateOnly(2025,8,15)  },
                    new() { PurchaseId="P2", Tag="P2_PL", FlagEmoji="🇵🇱", Country="Poland",      CountryCode="PL", ConfigName="cn_pl_w2", PingMs=0,  DaysLeft=93,  ExpiryDate=new DateOnly(2025,8,15)  },
                }
            ),
            (
                "Purchase #3", "200 GB",
                new List<ConfigInfo>
                {
                    new() { PurchaseId="P3", Tag="P3_NL", FlagEmoji="🇳🇱", Country="Netherlands", CountryCode="NL", ConfigName="cn_nl_c1", PingMs=22, DaysLeft=169, ExpiryDate=new DateOnly(2025,12,1)  },
                    new() { PurchaseId="P3", Tag="P3_DE", FlagEmoji="🇩🇪", Country="Germany",     CountryCode="DE", ConfigName="cn_de_f3", PingMs=35, DaysLeft=169, ExpiryDate=new DateOnly(2025,12,1)  },
                    new() { PurchaseId="P3", Tag="P3_PL", FlagEmoji="🇵🇱", Country="Poland",      CountryCode="PL", ConfigName="cn_pl_w3", PingMs=48, DaysLeft=169, ExpiryDate=new DateOnly(2025,12,1)  },
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

        // ─── ساخت UI از داده ─────────────────────────────────────────
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            foreach (var (label, volume, configs) in _purchases)
                PurchasesPanel.Children.Add(BuildPurchaseSection(label, volume, configs));
        }

        // ✅ helper برای خوندن ThemeResource از App level
        private static Brush GetThemeBrush(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out var val) && val is Brush b)
                return b;
            // fallback شفاف
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        private StackPanel BuildPurchaseSection(string label, string volume, List<ConfigInfo> configs)
        {
            string expiryPersian = configs.FirstOrDefault()?.ExpiryPersian ?? "";

            // ── هدر purchase ──
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
                    // ✅ Foreground inherit میشه از parent — نیازی به ThemeResource نیست
                }
            };

            var infoText = new TextBlock
            {
                // show purchase volume, a separator dot, then an "Expiry" label followed by the Persian expiry date
                Text = $"{volume}  ·  Expiry {expiryPersian}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6, // mimic secondary text
            };

            var divider = new Rectangle
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.15,
                Fill = GetThemeBrush("TextFillColorSecondaryBrush"),
            };

            Grid.SetColumn(labelBorder, 0);
            Grid.SetColumn(infoText, 1);
            Grid.SetColumn(divider, 2);

            headerGrid.Children.Add(labelBorder);
            headerGrid.Children.Add(infoText);
            headerGrid.Children.Add(divider);

            // ── ردیف کارت‌ها ──
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
            var content = new StackPanel { Spacing = 3 };

            content.Children.Add(new TextBlock { Text = cfg.FlagEmoji, FontSize = 24, Margin = new Thickness(0, 0, 0, 2) });
            // ✅ Foreground رو ست نمیکنیم — از ContentDialog inherit میشه و با تم تغییر میکنه
            content.Children.Add(new TextBlock { Text = cfg.Country, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            // Note: do NOT show config name in the modal cards per UX requirement; city replaced by ConfigName in model

            // Do not show ping in modal cards per UX — only show availability state
            if (!cfg.IsAvailable)
                content.Children.Add(new TextBlock { Text = "Unavailable", FontSize = 11, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 57, 53)) });

            var btn = new Button
            {
                Template = (ControlTemplate)Resources["ConfigCardTemplate"],
                Content = content,
                // Tag should contain the config metadata including the purchase volume
                Tag = new ConfigInfo
                {
                    PurchaseId = cfg.PurchaseId,
                    DataVolume = volume,
                    ExpiryDate = cfg.ExpiryDate,
                    DaysLeft = cfg.DaysLeft,
                    Tag = cfg.Tag,
                    FlagEmoji = cfg.FlagEmoji,
                    Country = cfg.Country,
                    CountryCode = cfg.CountryCode,
                    ConfigName = cfg.ConfigName,
                    PingMs = cfg.PingMs,
                    IsAvailable = cfg.IsAvailable
                },
                IsEnabled = cfg.IsAvailable,
                UseSystemFocusVisuals = false,
                Opacity = cfg.IsAvailable ? 1.0 : 0.45,
            };

            if (cfg.IsAvailable)
                btn.Click += ConfigCard_Click;

            return btn;
        }

        // ─── رویداد انتخاب ───────────────────────────────────────────
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