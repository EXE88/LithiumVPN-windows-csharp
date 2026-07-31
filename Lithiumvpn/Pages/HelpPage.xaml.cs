using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;

namespace Lithiumvpn.Pages
{
    public sealed partial class HelpPage : Page
    {
        private int _openTicketId = -1;

        public HelpPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            // Dynamic cards resolve brushes against ActualTheme; rebuild on switch.
            this.ActualThemeChanged += (s, e) =>
            {
                RenderTickets();
                if (ChatOverlay.Visibility == Visibility.Visible && _openTicketId >= 0)
                    _ = LoadMessagesAsync();
            };

            ConnectivityService.Instance.StateChanged += _ => DispatcherQueue.TryEnqueue(ApplyConnectivity);

            OfflinePanel.WentOnline += async (_, _) =>
            {
                ApplyConnectivity();
                if (await AppState.Instance.RefreshTicketsAsync())
                    RenderTickets();
            };
            OfflinePanel.NeedLogin += (_, _) => Frame?.Navigate(typeof(LoginPage));
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyConnectivity();
            if (!ConnectivityService.Instance.IsOnline) return;

            RenderTickets();
            // Pull the latest list in the background.
            if (await AppState.Instance.RefreshTicketsAsync())
                RenderTickets();
        }

        private void ApplyConnectivity()
        {
            bool online = ConnectivityService.Instance.IsOnline;
            SupportSection.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
            OfflinePanel.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
            if (online) RenderTickets();
        }

        private void StartTour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var tag = btn.Tag?.ToString();
            System.Type? pageType = tag switch
            {
                "Dashboard" => typeof(DashboardPage),
                "Configs"   => typeof(ServersPage),
                "Account"   => typeof(AccountPage),
                "Coins"     => typeof(CoinsPage),
                "Plans"     => typeof(PlansPage),
                _           => null
            };

            if (pageType is not null)
                TourManager.RequestTour(pageType);
        }

        // ─────────────────────────────────────────────────────────────
        //  Ticket list
        // ─────────────────────────────────────────────────────────────
        private void RenderTickets()
        {
            TicketsList.Children.Clear();

            var tickets = AppState.Instance.Tickets;
            if (tickets is null || tickets.Count == 0)
            {
                TicketsEmpty.Visibility = Visibility.Visible;
                return;
            }
            TicketsEmpty.Visibility = Visibility.Collapsed;

            foreach (var ticket in tickets)
                TicketsList.Children.Add(BuildTicketCard(ticket));
        }

        private Border BuildTicketCard(TicketDto ticket)
        {
            var card = new Border
            {
                Background = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Tag = ticket
            };
            card.PointerPressed += (s, e) => OpenChat(ticket);

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = "",
                FontSize = 18,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            info.Children.Add(new TextBlock
            {
                Text = ticket.Subject ?? "—",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            info.Children.Add(new TextBlock
            {
                Text = $"#{ticket.Id} · {FormatDate(ticket.CreatedAt)}",
                FontSize = 11,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
            });
            Grid.SetColumn(info, 1);

            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (ticket.Unread > 0)
            {
                right.Children.Add(new Border
                {
                    Background = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                    CornerRadius = new CornerRadius(10),
                    MinWidth = 20,
                    Height = 20,
                    Padding = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = ticket.Unread.ToString(),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }
            right.Children.Add(new FontIcon
            {
                Glyph = "",   // chevron
                FontSize = 12,
                Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(right, 2);

            grid.Children.Add(icon);
            grid.Children.Add(info);
            grid.Children.Add(right);
            card.Child = grid;
            return card;
        }

        // ─────────────────────────────────────────────────────────────
        //  New ticket
        // ─────────────────────────────────────────────────────────────
        private async void NewTicket_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;

            var input = new TextBox
            {
                PlaceholderText = loc.Get("Help_TicketSubjectPlaceholder"),
                AcceptsReturn = false,
                MaxLength = 120
            };
            var dialog = new ContentDialog
            {
                Title = loc.Get("Help_NewTicket"),
                Content = input,
                PrimaryButtonText = loc.Get("Help_Create"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            string subject = input.Text.Trim();
            if (string.IsNullOrEmpty(subject)) return;

            var result = await ApiService.CreateTicketAsync(subject);
            if (!result.IsSuccess || result.Data is null)
            {
                await ShowErrorAsync(result.Message ?? loc.Get("Help_ActionFailed"));
                return;
            }

            await AppState.Instance.RefreshTicketsAsync();
            RenderTickets();

            // Open the freshly created ticket.
            OpenChat(new TicketDto
            {
                Id = result.Data.Id,
                Subject = result.Data.Subject ?? subject,
                CreatedAt = result.Data.CreatedAt
            });
        }

        // ─────────────────────────────────────────────────────────────
        //  Chat
        // ─────────────────────────────────────────────────────────────
        private async void OpenChat(TicketDto ticket)
        {
            _openTicketId = ticket.Id;
            ChatSubject.Text = ticket.Subject ?? $"#{ticket.Id}";
            ChatMeta.Text = $"#{ticket.Id} · {FormatDate(ticket.CreatedAt)}";
            MessagesList.Children.Clear();
            MessageInput.Text = "";
            ShowChatOverlay();

            await LoadMessagesAsync();

            // Mark the conversation as read (non-fatal).
            try { await ApiService.MarkTicketSeenAsync(ticket.Id); } catch { }
        }

        private async Task LoadMessagesAsync()
        {
            var loc = LocalizationManager.Instance;
            var result = await ApiService.GetTicketMessagesAsync(_openTicketId);
            if (!result.IsSuccess || result.Data is null)
            {
                await ShowErrorAsync(result.Message ?? loc.Get("Help_ActionFailed"));
                return;
            }

            MessagesList.Children.Clear();
            var messages = result.Data.Messages;
            if (messages is not null)
                foreach (var msg in messages)
                    MessagesList.Children.Add(BuildBubble(msg));

            // Scroll to the newest message.
            ChatScroll.UpdateLayout();
            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, true);
        }

        private Border BuildBubble(TicketMessageDto msg)
        {
            bool mine = msg.IsClient;
            bool dark = ActualTheme == ElementTheme.Dark;

            // Admin bubbles use solid, theme-matched colors — the translucent card
            // brushes disappear against the chat background in both themes.
            var adminBg = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3C)
                : Windows.UI.Color.FromArgb(0xFF, 0xE9, 0xE9, 0xEB));
            var adminFg = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF7)
                : Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1E));
            var adminTime = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(0x99, 0xF2, 0xF2, 0xF7)
                : Windows.UI.Color.FromArgb(0x99, 0x1C, 0x1C, 0x1E));

            var bubble = new Border
            {
                Background = mine
                    ? ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
                    : adminBg,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8),
                MaxWidth = 340,
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };

            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(new TextBlock
            {
                Text = msg.Message ?? "",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = mine
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : adminFg
            });
            stack.Children.Add(new TextBlock
            {
                Text = FormatTime(msg.CreatedAt),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = mine
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF))
                    : adminTime
            });
            bubble.Child = stack;
            return bubble;
        }

        private async void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            string text = MessageInput.Text.Trim();
            if (string.IsNullOrEmpty(text) || _openTicketId < 0) return;

            SendButton.IsEnabled = false;
            var result = await ApiService.SendTicketMessageAsync(_openTicketId, text);
            SendButton.IsEnabled = true;

            if (!result.IsSuccess)
            {
                await ShowErrorAsync(result.Message ?? LocalizationManager.Instance.Get("Help_ActionFailed"));
                return;
            }

            MessageInput.Text = "";
            await LoadMessagesAsync();
        }

        // ─── Overlay open/close animation ─────────────────────────────
        private void ShowChatOverlay()
        {
            if (ChatOverlay.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                ChatOverlay.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();

            ChatOverlay.Opacity = 0;
            ChatOverlay.Visibility = Visibility.Visible;

            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            };

            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = ease
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, ChatOverlay);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");

            var slide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 36, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                EasingFunction = ease
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, ChatOverlay.RenderTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "Y");

            sb.Children.Add(fade);
            sb.Children.Add(slide);
            sb.Begin();
        }

        private Task HideChatOverlayAsync()
        {
            var tcs = new TaskCompletionSource<object?>();
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn
            };

            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 1, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = ease
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, ChatOverlay);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");

            if (ChatOverlay.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform)
            {
                var slide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0, To = 24,
                    Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                    EasingFunction = ease
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, ChatOverlay.RenderTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "Y");
                sb.Children.Add(slide);
            }

            sb.Children.Add(fade);
            sb.Completed += (s, e) =>
            {
                ChatOverlay.Visibility = Visibility.Collapsed;
                ChatOverlay.Opacity = 1;
                tcs.TrySetResult(null);
            };
            sb.Begin();
            return tcs.Task;
        }

        private async void ChatBack_Click(object sender, RoutedEventArgs e)
        {
            await HideChatOverlayAsync();
            _openTicketId = -1;

            // Refresh the list so the unread badge reflects the seen state.
            if (await AppState.Instance.RefreshTicketsAsync())
                RenderTickets();
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────
        private async Task ShowErrorAsync(string message)
        {
            var loc = LocalizationManager.Instance;
            await new ContentDialog
            {
                Title = loc.Get("Help_ActionFailed"),
                Content = message,
                CloseButtonText = loc.Get("Common_OK"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }

        private static string FormatDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.LocalDateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                : raw;
        }

        private static string FormatTime(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.LocalDateTime.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture)
                : raw;
        }
    }
}
