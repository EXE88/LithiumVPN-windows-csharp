using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Globalization;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;

namespace Lithiumvpn.Pages
{
    public sealed partial class NotificationsPage : Page
    {
        public NotificationsPage()
        {
            InitializeComponent();

            // Dynamic cards resolve brushes against ActualTheme; rebuild on switch.
            this.ActualThemeChanged += (s, e) => Render();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Render();

            // Mark everything as read on the server; failures are non-fatal.
            try { await ApiService.MarkEventsCheckedAsync(); } catch { }
        }

        private void Render()
        {
            NotificationsList.Children.Clear();

            var events = AppState.Instance.Events;
            if (events is null || events.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;
            foreach (var ev in events)
                NotificationsList.Children.Add(BuildCard(ev));
        }

        private Border BuildCard(EventDto ev)
        {
            var card = new Border
            {
                Background = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 12, 15, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                Glyph = "",
                FontSize = 20,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 14, 0)
            };
            Grid.SetColumn(icon, 0);

            var content = new StackPanel { Spacing = 3 };
            Grid.SetColumn(content, 1);

            content.Children.Add(new TextBlock
            {
                Text = ev.Topic ?? "",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });

            if (!string.IsNullOrWhiteSpace(ev.Description))
                content.Children.Add(new TextBlock
                {
                    Text = ev.Description,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
                });

            var when = FormatDate(ev.CreatedAt);
            if (!string.IsNullOrEmpty(when))
                content.Children.Add(new TextBlock
                {
                    Text = when,
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0),
                    Foreground = ThemeRes.Brush(this, "TextFillColorTertiaryBrush")
                });

            grid.Children.Add(icon);
            grid.Children.Add(content);
            card.Child = grid;
            return card;
        }

        private static string FormatDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.LocalDateTime.ToString("yyyy/MM/dd  HH:mm", CultureInfo.InvariantCulture)
                : raw;
        }
    }
}
