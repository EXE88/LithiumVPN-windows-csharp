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
using Lithiumvpn.Localization;

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
            public string ConfigCode { get; init; } = "";
            public int PingMs { get; init; }
            public bool IsAvailable { get; init; } = true;

            /// <summary>True for user-imported ("personal") configs — no flag, no plan quota.</summary>
            public bool IsLocal { get; init; }
            public string LocalId { get; init; } = "";

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

        public ConfigInfo? SelectedConfig { get; private set; }
        private Button? _selectedButton;

        public ConfigSelectionDialog()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // The user's own imported configs come first — they always work, even offline.
            var locals = Services.LocalConfigStore.Instance.Configs;
            if (locals.Count > 0)
                PurchasesPanel.Children.Add(BuildLocalSection(locals));

            // Then the backend data (each purchase = one plan, each config = one
            // server/location inside that purchase).
            int index = 1;
            foreach (var (volume, configs) in BuildPurchasesFromState())
                PurchasesPanel.Children.Add(BuildPurchaseSection(index++, volume, configs));
        }

        // ─── Imported / local configs section ──────────────────────────
        private StackPanel BuildLocalSection(IReadOnlyList<Services.LocalConfig> locals)
        {
            var loc = LocalizationManager.Instance;

            var headerGrid = new Grid { ColumnSpacing = 8 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBorder = new Border
            {
                Style = (Style)Resources["PurchaseLabelStyle"],
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock
                        {
                            Text = loc.Get("Dialog_ImportedConfigs"),
                            FontSize = 12,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
            var divider = new Rectangle
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.15,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            };
            Grid.SetColumn(labelBorder, 0);
            Grid.SetColumn(divider, 1);
            headerGrid.Children.Add(labelBorder);
            headerGrid.Children.Add(divider);

            // Tiles wrapped 3-per-row so any number of imported configs fits the dialog.
            var rows = new StackPanel { Spacing = 8 };
            StackPanel? currentRow = null;
            int i = 0;
            foreach (var lc in locals)
            {
                if (i % 3 == 0)
                {
                    currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    rows.Children.Add(currentRow);
                }
                currentRow!.Children.Add(BuildLocalCard(lc));
                i++;
            }

            var section = new StackPanel { Spacing = 10 };
            section.Children.Add(headerGrid);
            section.Children.Add(rows);
            return section;
        }

        private Button BuildLocalCard(Services.LocalConfig lc)
        {
            var loc = LocalizationManager.Instance;

            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(BuildLocalIconCircle());
            content.Children.Add(new TextBlock
            {
                Text = lc.Name,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 96,
            });
            content.Children.Add(new TextBlock
            {
                Text = loc.Get("Dialog_LocalConfig"),
                FontSize = 10,
                Opacity = 0.6,
            });

            var info = new ConfigInfo
            {
                IsLocal = true,
                LocalId = lc.Id,
                ConfigName = lc.Name,
                ConfigCode = lc.Link,
                Country = "",
                CountryCode = "",
                IsAvailable = true,
            };

            var btn = new Button
            {
                Template = (ControlTemplate)Resources["ConfigCardTemplate"],
                Content = content,
                Tag = info,
                UseSystemFocusVisuals = false,
            };
            btn.Click += ConfigCard_Click;
            return btn;
        }

        // Circular badge with a "person" glyph, marking a personal/imported config.
        private static Grid BuildLocalIconCircle()
        {
            var container = new Grid { Width = 40, Height = 40 };
            container.Children.Add(new Ellipse
            {
                Width = 40,
                Height = 40,
                Fill = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            });
            container.Children.Add(new FontIcon
            {
                Glyph = "",   // contact / person
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return container;
        }

        /// <summary>Maps the cached account purchases into the dialog's view model.</summary>
        private static List<(string Volume, List<ConfigInfo> Configs)> BuildPurchasesFromState()
        {
            var result = new List<(string, List<ConfigInfo>)>();

            foreach (var purchase in Services.AppState.Instance.Purchases)
            {
                // Total data for this purchase comes from the matching plan's usage (GB).
                int totalGb = PlanUsageFor(purchase.Plan);
                string volume = totalGb > 0 ? $"{totalGb} GB" : "";

                var configs = new List<ConfigInfo>();
                foreach (var cfg in purchase.Configs ?? new List<Services.ConfigDto>())
                {
                    string code = cfg.Country ?? "";
                    string country = CodeToCountryName(code);
                    configs.Add(new ConfigInfo
                    {
                        PurchaseId  = purchase.PurchaseId.ToString(),
                        DataVolume  = volume,                       // total, for the ring %
                        GbLeft      = cfg.GbLeftValue,
                        DaysLeft    = cfg.DaysLeft,
                        ExpiryDate  = DateOnly.FromDateTime(DateTime.Now.AddDays(cfg.DaysLeft)),
                        Tag         = $"{purchase.PurchaseId}_{cfg.Name}",
                        FlagEmoji   = "",
                        Country     = country,
                        CountryCode = code,
                        ConfigName  = cfg.Name ?? "",
                        ConfigCode  = cfg.ConfigCode ?? "",
                        PingMs      = 0,          // ping stays measured on demand (fake)
                        IsAvailable = true,
                    });
                }

                if (configs.Count > 0)
                    result.Add((volume, configs));
            }

            return result;
        }

        private static int PlanUsageFor(string? planName)
        {
            if (string.IsNullOrEmpty(planName)) return 0;
            foreach (var p in Services.AppState.Instance.Plans)
                if (string.Equals(p.PlanName, planName, StringComparison.OrdinalIgnoreCase))
                    return p.Usage;
            return 0;
        }

        // Map an ISO-3166 alpha-2 code to an English country name the app can localize.
        private static string CodeToCountryName(string code) => code.ToUpperInvariant() switch
        {
            "DE" => "Germany",
            "NL" => "Netherlands",
            "PL" => "Poland",
            _ => code
        };

        private StackPanel BuildPurchaseSection(int index, string volume, List<ConfigInfo> configs)
        {
            var loc = LocalizationManager.Instance;
            string label = string.Format(loc.Get("Dialog_Purchase"), index);
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
                Text = $"{volume}  ·  {loc.Get("Dialog_ExpiryPrefix")} {expiryPersian}",
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
                Text = LocalizeCountry(cfg.Country),
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
                    Text = LocalizationManager.Instance.Get("Dialog_Unavailable"),
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
                ConfigCode = cfg.ConfigCode,
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

        private static string LocalizeCountry(string country) => country switch
        {
            "Netherlands" => LocalizationManager.Instance.Get("Country_Netherlands"),
            "Germany" => LocalizationManager.Instance.Get("Country_Germany"),
            "Poland" => LocalizationManager.Instance.Get("Country_Poland"),
            _ => country
        };

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