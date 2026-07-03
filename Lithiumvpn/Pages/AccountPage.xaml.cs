using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;

namespace Lithiumvpn.Pages
{
    public sealed partial class AccountPage : Page
    {
        public AccountPage()
        {
            this.InitializeComponent();

            // Dynamic cards resolve brushes against ActualTheme; rebuild on switch.
            this.ActualThemeChanged += (s, e) => RenderSubscriptions();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            PopulateProfile();
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        private void PopulateProfile()
        {
            var status = AppState.Instance.Status;
            if (status is not null)
            {
                var name = string.IsNullOrWhiteSpace(status.Username) ? "—" : status.Username!;
                ProfileName.Text = name;
                ProfileAvatar.DisplayName = name;
                ProfileEmail.Text = string.IsNullOrWhiteSpace(status.Email) ? "" : status.Email!;
            }

            RenderSubscriptions();
        }

        private void RenderSubscriptions()
        {
            SubscriptionsContainer.Children.Clear();

            var purchases = AppState.Instance.Purchases;
            if (purchases.Count == 0)
            {
                NoSubsCard.Visibility = Visibility.Visible;
                return;
            }
            NoSubsCard.Visibility = Visibility.Collapsed;

            Border? first = null;
            foreach (var purchase in purchases)
            {
                var card = BuildSubscriptionCard(purchase);
                SubscriptionsContainer.Children.Add(card);
                first ??= card;
            }

            // Tour tip 2 points at the first subscription card.
            TourTip2.Target = first;
        }

        private Border BuildSubscriptionCard(PurchaseDto purchase)
        {
            var loc = LocalizationManager.Instance;
            var configs = purchase.Configs ?? new List<ConfigDto>();

            int planUsage = PlanUsageFor(purchase.Plan);
            double leftData = configs.Sum(c => c.GbLeftValue);
            double totalData = planUsage > 0 ? planUsage * configs.Count : leftData;
            int daysLeft = configs.Count > 0 ? configs.Max(c => c.DaysLeft) : 0;
            int locations = configs.Select(c => (c.Country ?? "").ToUpperInvariant())
                                    .Where(c => c.Length > 0).Distinct().Count();
            double usedPct = totalData > 0 ? Math.Clamp((totalData - leftData) / totalData * 100, 0, 100) : 0;

            var card = new Border
            {
                Background = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var root = new StackPanel { Spacing = 12 };

            // Header: plan badge + days-left chip
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Background = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = purchase.Plan ?? "—",
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            Grid.SetColumn(badge, 0);

            var daysChip = new Border
            {
                Background = ThemeRes.Brush(this, "SubtleFillColorSecondaryBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3)
            };
            var chipStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            chipStack.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            chipStack.Children.Add(new TextBlock
            {
                Text = $"{daysLeft} {loc.Get("Account_DaysLeftSuffix")}",
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            daysChip.Child = chipStack;
            Grid.SetColumn(daysChip, 2);

            header.Children.Add(badge);
            header.Children.Add(daysChip);
            root.Children.Add(header);

            root.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1,
                Fill = ThemeRes.Brush(this, "DividerStrokeColorDefaultBrush")
            });

            // Stats: data left / expiry / locations
            var stats = new Grid();
            for (int i = 0; i < 3; i++)
                stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stats.Children.Add(StatColumn(0, "", $"{leftData:0.##} GB", loc.Get("Account_DataLeft")));
            stats.Children.Add(StatColumn(1, "", $"{daysLeft} {loc.Get("Account_DaysSuffix")}", loc.Get("Account_Expiry")));
            stats.Children.Add(StatColumn(2, "", locations.ToString(), loc.Get("Account_Locations")));
            root.Children.Add(stats);

            // Data usage bar
            var usageStack = new StackPanel { Spacing = 4 };
            var usageHeader = new Grid();
            usageHeader.Children.Add(new TextBlock
            {
                Text = loc.Get("Account_DataUsage"),
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });
            usageHeader.Children.Add(new TextBlock
            {
                Text = $"{usedPct:0}%",
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });
            usageStack.Children.Add(usageHeader);
            usageStack.Children.Add(new ProgressBar
            {
                Value = usedPct,
                Maximum = 100,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                Background = ThemeRes.Brush(this, "ControlAltFillColorQuarternaryBrush")
            });
            root.Children.Add(usageStack);

            card.Child = root;
            return card;
        }

        private StackPanel StatColumn(int column, string glyph, string value, string label)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
            Grid.SetColumn(panel, column);
            panel.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });
            return panel;
        }

        private static int PlanUsageFor(string? planName)
        {
            if (string.IsNullOrEmpty(planName)) return 0;
            foreach (var p in AppState.Instance.Plans)
                if (string.Equals(p.PlanName, planName, StringComparison.OrdinalIgnoreCase))
                    return p.Usage;
            return 0;
        }

        private bool _tourAttached = false;
        private void StartTour()
        {
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

        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;
            var dialog = new ContentDialog
            {
                Title = loc.Get("Account_SignOutDialogTitle"),
                Content = loc.Get("Account_SignOutDialogBody"),
                PrimaryButtonText = loc.Get("Account_SignOut"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                // Invalidate the refresh token server-side, drop cached data, then
                // return to the login page.
                await Services.ApiService.LogoutAsync();
                Services.AppState.Instance.Clear();
                this.Frame?.Navigate(typeof(LoginPage));
            }
        }

        private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;
            await new ContentDialog
            {
                Title = loc.Get("Account_DeleteDialogTitle"),
                Content = loc.Get("Account_DeleteDialogBody"),
                PrimaryButtonText = loc.Get("Account_DeleteForever"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }
    }
}
