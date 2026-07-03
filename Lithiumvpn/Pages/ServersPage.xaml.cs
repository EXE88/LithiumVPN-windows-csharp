using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;

namespace Lithiumvpn.Pages
{
    public sealed partial class ServersPage : Page
    {
        private static readonly Random _rng = new();

        private Expander? _firstExpander;
        private Border? _firstConfigCard;

        public ServersPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            // Dynamic cards resolve brushes against ActualTheme; rebuild on switch.
            this.ActualThemeChanged += (s, e) => RenderPurchases();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RenderPurchases();
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        // ─── Build summary + purchase expanders from live data ─────────
        private void RenderPurchases()
        {
            PurchasesContainer.Children.Clear();
            _firstExpander = null;
            _firstConfigCard = null;

            var purchases = AppState.Instance.Purchases;

            // Summary stats
            StatPurchases.Text = purchases.Count.ToString();
            StatActive.Text = purchases.Count(p => (p.Configs ?? new()).Any(c => c.DaysLeft > 0)).ToString();
            StatLocations.Text = purchases
                .SelectMany(p => p.Configs ?? new List<ConfigDto>())
                .Select(c => (c.Country ?? "").ToUpperInvariant())
                .Where(c => c.Length > 0)
                .Distinct().Count().ToString();

            if (purchases.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                return;
            }
            EmptyState.Visibility = Visibility.Collapsed;

            int index = 1;
            foreach (var purchase in purchases)
            {
                var expander = BuildPurchaseExpander(index++, purchase);
                PurchasesContainer.Children.Add(expander.Expander);
                if (expander.InfoTip is not null)
                    PurchasesContainer.Children.Add(expander.InfoTip);

                _firstExpander ??= expander.Expander;
            }

            TourTip2.Target = _firstExpander;
            TourTip3.Target = _firstConfigCard;
        }

        private (Expander Expander, TeachingTip? InfoTip) BuildPurchaseExpander(int index, PurchaseDto purchase)
        {
            var loc = LocalizationManager.Instance;
            var configs = purchase.Configs ?? new List<ConfigDto>();

            // ── Info tip (data / time left) ──
            int planUsage = PlanUsageFor(purchase.Plan);
            double leftData = configs.Sum(c => c.GbLeftValue);
            double totalData = planUsage > 0 ? planUsage * configs.Count : leftData;
            int planMonths = PlanTimeFor(purchase.Plan);
            int totalDays = planMonths > 0 ? planMonths * 30 : 0;
            int daysLeft = configs.Count > 0 ? configs.Max(c => c.DaysLeft) : 0;

            var infoBtn = new Button { Style = (Style)Resources["InfoButtonStyle"] };
            ToolTipService.SetToolTip(infoBtn, loc.Get("Servers_PlanDetails"));
            infoBtn.Content = new FontIcon
            {
                Glyph = "",   // info
                FontSize = 13,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
            };

            var infoTip = BuildInfoTip(index, infoBtn, leftData, totalData, daysLeft, totalDays);
            infoBtn.Click += (s, e) => infoTip.IsOpen = true;

            // ── Header ──
            var headerGrid = new Grid { Padding = new Thickness(0, 10, 0, 10), ColumnSpacing = 10 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pIcon = new FontIcon
            {
                Glyph = "",
                FontSize = 18,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pIcon, 0);

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var tag = new Border
            {
                Style = (Style)Resources["PurchaseTagStyle"],
                Child = new TextBlock
                {
                    Text = string.Format(loc.Get("Dialog_Purchase"), index),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            headerStack.Children.Add(tag);
            headerStack.Children.Add(infoBtn);
            Grid.SetColumn(headerStack, 1);

            headerGrid.Children.Add(pIcon);
            headerGrid.Children.Add(headerStack);

            // ── Content: vertical line + config cards ──
            var contentGrid = new Grid { ColumnSpacing = 10 };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var line = new Rectangle
            {
                Width = 2,
                RadiusX = 1,
                RadiusY = 1,
                Fill = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                Opacity = 0.35,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(3, 2, 0, 2)
            };
            Grid.SetColumn(line, 0);

            var cardsStack = new StackPanel { Spacing = 8 };
            Grid.SetColumn(cardsStack, 1);
            foreach (var cfg in configs)
            {
                var card = BuildConfigCard(cfg);
                cardsStack.Children.Add(card);
                _firstConfigCard ??= card;
            }

            contentGrid.Children.Add(line);
            contentGrid.Children.Add(cardsStack);

            var expander = new Expander
            {
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush"),
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Header = headerGrid,
                Content = contentGrid
            };

            // Lightweight styling: the default expander header/content brushes are
            // translucent and let the particle backdrop bleed through — pin them to
            // the same card brush the Help page cards use.
            var cardBg = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush");
            expander.Resources["ExpanderHeaderBackground"] = cardBg;
            expander.Resources["ExpanderContentBackground"] = cardBg;
            expander.Resources["ExpanderContentBorderBrush"] =
                ThemeRes.Brush(this, "CardStrokeColorDefaultBrush");

            return (expander, infoTip);
        }

        private TeachingTip BuildInfoTip(int index, FrameworkElement target,
            double leftData, double totalData, int daysLeft, int totalDays)
        {
            var loc = LocalizationManager.Instance;

            double dataPct = totalData > 0 ? Math.Clamp(leftData / totalData * 100, 0, 100) : 0;
            double timePct = totalDays > 0 ? Math.Clamp((double)daysLeft / totalDays * 100, 0, 100) : 0;

            var panel = new StackPanel { Width = 260, Spacing = 16, Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(BuildInfoRow("", loc.Get("Servers_DataLeft"),
                $"{leftData:0.##} / {totalData:0.##} GB", dataPct));
            panel.Children.Add(BuildInfoRow("", loc.Get("Servers_TimeLeft"),
                $"{daysLeft} / {totalDays} " + loc.Get("Account_DaysSuffix"), timePct));
            panel.Children.Add(new TextBlock
            {
                Text = loc.Get("Servers_SharedQuota"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });

            return new TeachingTip
            {
                Target = target,
                Title = string.Format(loc.Get("Servers_PurchaseDetails"), index),
                CloseButtonContent = loc.Get("Common_GotIt"),
                IsOpen = false,
                Content = panel
            };
        }

        private StackPanel BuildInfoRow(string glyph, string label, string tagText, double percent)
        {
            var stack = new StackPanel { Spacing = 6 };

            var grid = new Grid();
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
            });
            left.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });

            var tagBorder = new Border
            {
                Style = (Style)Resources["PurchaseTagStyle"],
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = tagText,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };

            grid.Children.Add(left);
            grid.Children.Add(tagBorder);

            var bar = new ProgressBar
            {
                Value = percent,
                Maximum = 100,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                Background = ThemeRes.Brush(this, "ControlAltFillColorQuarternaryBrush")
            };

            stack.Children.Add(grid);
            stack.Children.Add(bar);
            return stack;
        }

        private Border BuildConfigCard(ConfigDto cfg)
        {
            var loc = LocalizationManager.Instance;
            string code = (cfg.Country ?? "").ToUpperInvariant();

            var card = new Border
            {
                Style = (Style)Resources["ConfigCardStyle"],
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };
            card.PointerEntered += ConfigCard_PointerEntered;
            card.PointerExited += ConfigCard_PointerExited;

            var outer = new Grid { RowSpacing = 10 };
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: flag + name + ping tag
            var row0 = new Grid { ColumnSpacing = 10 };
            row0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row0.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var flag = BuildFlagCircle(code);
            Grid.SetColumn(flag, 0);

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            nameStack.Children.Add(new TextBlock
            {
                Text = LocalizeCountry(CodeToCountryName(code)),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = cfg.Name ?? "",
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });
            Grid.SetColumn(nameStack, 1);

            var pingTagText = new TextBlock
            {
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            };
            var pingTag = new Border
            {
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 4, 9, 4),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(),
                Child = pingTagText
            };
            Grid.SetColumn(pingTag, 2);

            row0.Children.Add(flag);
            row0.Children.Add(nameStack);
            row0.Children.Add(pingTag);
            Grid.SetRow(row0, 0);

            // Row 1: Ping + Connect
            var row1 = new Grid { ColumnSpacing = 8 };
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pingBtn = new Button
            {
                Tag = pingTag,
                Background = ThemeRes.Brush(this, "SubtleFillColorSecondaryBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6)
            };
            ToolTipService.SetToolTip(pingBtn, loc.Get("Common_TestPing"));
            pingBtn.Click += Ping_Click;
            var pingContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            pingContent.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 13,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            pingContent.Children.Add(new TextBlock
            {
                Text = loc.Get("Common_Ping"),
                FontSize = 12,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            pingBtn.Content = pingContent;
            Grid.SetColumn(pingBtn, 0);

            var connectBtn = new Button
            {
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            var connContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            connContent.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
            connContent.Children.Add(new TextBlock { Text = loc.Get("Common_Connect"), FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
            connectBtn.Content = connContent;
            Grid.SetColumn(connectBtn, 1);

            row1.Children.Add(pingBtn);
            row1.Children.Add(connectBtn);
            Grid.SetRow(row1, 1);

            outer.Children.Add(row0);
            outer.Children.Add(row1);
            card.Child = outer;
            return card;
        }

        // Circular SVG flag with a subtle background, falling back to an empty circle.
        private static Grid BuildFlagCircle(string code)
        {
            var container = new Grid { Width = 40, Height = 40 };
            container.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)) });
            try
            {
                var lower = code.ToLowerInvariant();
                if (!string.IsNullOrEmpty(lower))
                {
                    var svg = new SvgImageSource(new Uri($"ms-appx:///Assets/Flags/{lower}.svg"));
                    container.Children.Add(new Ellipse
                    {
                        Fill = new ImageBrush { ImageSource = svg, Stretch = Stretch.UniformToFill }
                    });
                }
            }
            catch { }
            container.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
            return container;
        }

        private static string CodeToCountryName(string code) => code.ToUpperInvariant() switch
        {
            "DE" => "Germany",
            "NL" => "Netherlands",
            "PL" => "Poland",
            _ => code
        };

        private static string LocalizeCountry(string country) => country switch
        {
            "Netherlands" => LocalizationManager.Instance.Get("Country_Netherlands"),
            "Germany" => LocalizationManager.Instance.Get("Country_Germany"),
            "Poland" => LocalizationManager.Instance.Get("Country_Poland"),
            _ => country
        };

        private static int PlanUsageFor(string? planName)
        {
            if (string.IsNullOrEmpty(planName)) return 0;
            foreach (var p in AppState.Instance.Plans)
                if (string.Equals(p.PlanName, planName, StringComparison.OrdinalIgnoreCase))
                    return p.Usage;
            return 0;
        }

        private static int PlanTimeFor(string? planName)
        {
            if (string.IsNullOrEmpty(planName)) return 0;
            foreach (var p in AppState.Instance.Plans)
                if (string.Equals(p.PlanName, planName, StringComparison.OrdinalIgnoreCase))
                    return p.Time;
            return 0;
        }

        // ─── hover animation ────────────────────────────────────────────
        private void ConfigCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                AnimateScale(card, 1.02);
                card.Background = ThemeRes.Brush(this, "SubtleFillColorSecondaryBrush");
            }
        }

        private void ConfigCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card)
            {
                AnimateScale(card, 1.0);
                card.Background = ThemeRes.Brush(this, "CardBackgroundFillColorSecondaryBrush");
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

        // ─── Ping button: random value shown in the tag (ping stays fake) ─
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

            PulseButton(btn);

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

        private static Color PingColor(int ping)
        {
            if (ping <= 40) return Color.FromArgb(255, 0, 200, 83);
            if (ping <= 80) return Color.FromArgb(255, 124, 179, 66);
            if (ping <= 120) return Color.FromArgb(255, 255, 179, 0);
            return Color.FromArgb(255, 229, 57, 53);
        }

        // ─── تور راهنما ─────────────────────────────────────────────────
        private bool _tourAttached = false;
        private void StartTour()
        {
            if (_firstExpander is null) return;
            if (!_tourAttached)
            {
                _tourAttached = true;
                TourTip1.ActionButtonClick += (s, e) => { TourTip1.IsOpen = false; TourTip2.IsOpen = true; };
                TourTip1.CloseButtonClick  += (s, e) => TourTip1.IsOpen = false;
                TourTip2.ActionButtonClick += (s, e) =>
                {
                    TourTip2.IsOpen = false;
                    if (_firstExpander is not null) _firstExpander.IsExpanded = true;
                    TourTip3.IsOpen = true;
                };
                TourTip2.CloseButtonClick  += (s, e) => TourTip2.IsOpen = false;
                TourTip3.CloseButtonClick  += (s, e) => TourTip3.IsOpen = false;
            }
            TourTip1.IsOpen = true;
        }
    }
}
