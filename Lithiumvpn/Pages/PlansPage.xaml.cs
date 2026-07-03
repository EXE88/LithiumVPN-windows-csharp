using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Globalization;
using System.Linq;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;

namespace Lithiumvpn.Pages
{
    public sealed partial class PlansPage : Page
    {
        public PlansPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            // Dynamic cards resolve brushes against ActualTheme; rebuild on switch.
            this.ActualThemeChanged += (s, e) => RenderPlans();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RenderBalance();
            RenderPlans();
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        private void RenderBalance()
        {
            var coins = AppState.Instance.Status?.CoinCount ?? 0;
            HeroBalanceText.Text = coins.ToString("N0", CultureInfo.InvariantCulture);
        }

        // ─── Build the plan cards from the /plans/ dataset ─────────────
        private void RenderPlans()
        {
            PlansContainer.Children.Clear();

            var plans = AppState.Instance.Plans;
            if (plans is null || plans.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                return;
            }
            EmptyState.Visibility = Visibility.Collapsed;

            // Highlight the most expensive plan as the "best" one.
            int maxPrice = plans.Max(p => p.Price);
            bool highlightUsed = false;
            Border? firstCard = null, highlightCard = null;

            foreach (var plan in plans)
            {
                bool highlight = !highlightUsed && plan.Price == maxPrice && plans.Count > 1;
                if (highlight) highlightUsed = true;

                var card = BuildPlanCard(plan, highlight);
                PlansContainer.Children.Add(card);

                firstCard ??= card;
                if (highlight) highlightCard = card;
            }

            // Retarget the tour tips onto the generated cards.
            TourTip2.Target = firstCard;
            TourTip3.Target = highlightCard ?? firstCard;
        }

        private Border BuildPlanCard(PlanDto plan, bool highlight)
        {
            var loc = LocalizationManager.Instance;

            var card = new Border
            {
                Background = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush"),
                BorderBrush = highlight
                    ? ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
                    : ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(highlight ? 1.5 : 1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 16, 18, 16),
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };
            card.PointerEntered += PlanCard_PointerEntered;
            card.PointerExited += PlanCard_PointerExited;

            var root = new StackPanel { Spacing = 14 };

            // ── Header: icon + name + badge ──
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            var iconBox = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = highlight
                    ? ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
                    : ThemeRes.Brush(this, "SubtleFillColorSecondaryBrush"),
                Child = new FontIcon
                {
                    Glyph = highlight ? "" : "",
                    FontSize = 17,
                    Foreground = highlight
                        ? new SolidColorBrush(Microsoft.UI.Colors.White)
                        : ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            titleStack.Children.Add(new TextBlock
            {
                Text = LocalizePlanName(plan.PlanName),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = string.Format(loc.Get("Plans_CardSubtitle"), plan.Usage, plan.Time),
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });
            headerLeft.Children.Add(iconBox);
            headerLeft.Children.Add(titleStack);
            Grid.SetColumn(headerLeft, 0);
            headerGrid.Children.Add(headerLeft);

            if (highlight)
            {
                var badge = new Border
                {
                    Background = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(9, 4, 9, 4),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = loc.Get("Plans_MostPopular"),
                        FontSize = 10,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                    }
                };
                Grid.SetColumn(badge, 1);
                headerGrid.Children.Add(badge);
            }
            root.Children.Add(headerGrid);

            // ── Price ──
            var priceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom };
            priceRow.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 18,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            priceRow.Children.Add(new TextBlock
            {
                Text = plan.Price.ToString("N0", CultureInfo.InvariantCulture),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            priceRow.Children.Add(new TextBlock
            {
                Text = loc.Get("Plans_CoinsUnit"),
                FontSize = 13,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 5)
            });
            priceRow.Children.Add(new TextBlock
            {
                Text = string.Format(loc.Get("Plans_PricePerMonths"), plan.Time),
                FontSize = 12,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 5)
            });
            root.Children.Add(priceRow);

            root.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1,
                Fill = ThemeRes.Brush(this, "DividerStrokeColorDefaultBrush")
            });

            // ── Features ──
            var features = new StackPanel { Spacing = 9 };
            features.Children.Add(FeatureRow("", string.Format(loc.Get("Plans_FeatureData"), plan.Usage)));
            features.Children.Add(FeatureRow("", string.Format(loc.Get("Plans_FeatureValidityMonths"), plan.Time)));
            features.Children.Add(FeatureRow("", string.Format(loc.Get("Plans_FeatureLocations"), plan.LocationCount)));
            features.Children.Add(FeatureRow("", string.Format(loc.Get("Plans_FeatureDevices"), plan.NumberOfUsers)));
            root.Children.Add(features);

            // ── Buy button ──
            var buyBtn = new Button
            {
                Tag = plan,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            buyBtn.Click += Buy_Click;

            var buyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            buyContent.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 14,
                Foreground = highlight ? new SolidColorBrush(Microsoft.UI.Colors.White)
                                       : ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            buyContent.Children.Add(new TextBlock
            {
                Text = string.Format(loc.Get("Plans_BuyWithCoins"), plan.Price.ToString("N0", CultureInfo.InvariantCulture)),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = highlight ? new SolidColorBrush(Microsoft.UI.Colors.White)
                                       : ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            buyBtn.Content = buyContent;

            if (highlight)
            {
                buyBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            }
            else
            {
                buyBtn.Background = ThemeRes.Brush(this, "SubtleFillColorSecondaryBrush");
                buyBtn.BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush");
                buyBtn.BorderThickness = new Thickness(1);
            }
            root.Children.Add(buyBtn);

            card.Child = root;
            return card;
        }

        private StackPanel FeatureRow(string glyph, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            return row;
        }

        private static string LocalizePlanName(string? name) => name switch
        {
            "Basic" => LocalizationManager.Instance.Get("Plans_Basic"),
            "Pro" => LocalizationManager.Instance.Get("Plans_Pro"),
            "Ultimate" => LocalizationManager.Instance.Get("Plans_Ultimate"),
            null or "" => "—",
            _ => name
        };

        // ─── hover animation ────────────────────────────────────────────
        private void PlanCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card) AnimateScale(card, 1.015);
        }

        private void PlanCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border card) AnimateScale(card, 1.0);
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

        // ─── Buy a plan (real /plans/buy/) ─────────────────────────────
        private async void Buy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PlanDto plan) return;

            var loc = LocalizationManager.Instance;
            string planName = LocalizePlanName(plan.PlanName);
            int balance = AppState.Instance.Status?.CoinCount ?? 0;

            if (plan.Price > balance)
            {
                await new ContentDialog
                {
                    Title = loc.Get("Plans_NotEnoughTitle"),
                    Content = string.Format(loc.Get("Plans_NotEnoughBody"),
                                            planName, plan.Price.ToString("N0"), balance.ToString("N0")),
                    CloseButtonText = loc.Get("Common_OK"),
                    DefaultButton = ContentDialogButton.Close,
                    FlowDirection = loc.FlowDirection,
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
                return;
            }

            var confirm = new ContentDialog
            {
                Title = string.Format(loc.Get("Plans_BuyTitle"), planName),
                Content = string.Format(loc.Get("Plans_BuyBody"),
                                        plan.Price.ToString("N0"), balance.ToString("N0")),
                PrimaryButtonText = loc.Get("Plans_BuyConfirm"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            btn.IsEnabled = false;
            var result = await ApiService.BuyPlanAsync(plan.Id);

            if (!result.IsSuccess)
            {
                btn.IsEnabled = true;
                await new ContentDialog
                {
                    Title = loc.Get("Plans_NotEnoughTitle"),
                    Content = result.Message ?? loc.Get("Login_GenericError"),
                    CloseButtonText = loc.Get("Common_OK"),
                    DefaultButton = ContentDialogButton.Close,
                    FlowDirection = loc.FlowDirection,
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
                return;
            }

            // Refresh coins + purchases so this page and the others reflect the buy.
            await AppState.Instance.RefreshStatusAsync();
            btn.IsEnabled = true;
            RenderBalance();

            await new ContentDialog
            {
                Title = loc.Get("Plans_BuyConfirm"),
                Content = string.Format(loc.Get("Plans_PurchasedToast"), planName),
                CloseButtonText = loc.Get("Common_OK"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }

        // ─── تور راهنما ─────────────────────────────────────────────────
        private bool _tourAttached = false;
        private void StartTour()
        {
            if (PlansContainer.Children.Count == 0) return;
            if (!_tourAttached)
            {
                _tourAttached = true;
                TourTip1.ActionButtonClick += (s, e) => { TourTip1.IsOpen = false; TourTip2.IsOpen = true; };
                TourTip1.CloseButtonClick  += (s, e) => TourTip1.IsOpen = false;
                TourTip2.ActionButtonClick += (s, e) => { TourTip2.IsOpen = false; TourTip3.IsOpen = true; };
                TourTip2.CloseButtonClick  += (s, e) => TourTip2.IsOpen = false;
                TourTip3.CloseButtonClick  += (s, e) => TourTip3.IsOpen = false;
            }
            TourTip1.IsOpen = true;
        }
    }
}
