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
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Lithiumvpn.Localization;
using Lithiumvpn.Services;
using Lithiumvpn.Services.Xray;

namespace Lithiumvpn.Pages
{
    public sealed partial class ServersPage : Page
    {
        private Expander? _firstExpander;
        private Border? _firstConfigCard;

        /// <summary>One config card's live visuals, so a group ping can fill its tag.</summary>
        private sealed class ConfigCardVisual
        {
            public required Border Card { get; init; }
            public required Border PingTag { get; init; }
            public required string Link { get; init; }
        }

        // ─── Group ("ping all") state ───────────────────────────────────
        // Only one batch may run at a time; starting another supersedes it, and the
        // page cancels it when navigating away so no probe core outlives the view.
        private CancellationTokenSource? _batchCts;
        private bool _renderPending;

        /// <summary>
        /// Every "ping all" button currently on screen. They are disabled together
        /// while a batch runs so a second group can't be started mid-flight (which
        /// would strand the first group's tags on "Pinging…").
        /// </summary>
        private readonly List<Button> _pingAllButtons = new();

        private bool IsBatchRunning => _batchCts is not null;

        private void SetPingAllButtonsEnabled(bool enabled)
        {
            foreach (var b in _pingAllButtons)
                b.IsEnabled = enabled && b.Tag is int count && count > 0;
        }

        private void CancelBatchPing()
        {
            try { _batchCts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        public ServersPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            // Dynamic cards resolve brushes against ActualTheme; rebuild on switch.
            this.ActualThemeChanged += (s, e) => RenderAll();

            // Live backend data: re-render whenever the cached account data changes.
            AppState.Instance.Changed += () => DispatcherQueue.TryEnqueue(RenderAll);

            // Imported configs + connectivity drive what this page shows.
            LocalConfigStore.Instance.Changed += () => DispatcherQueue.TryEnqueue(RenderAll);
            ConnectivityService.Instance.StateChanged += _ => DispatcherQueue.TryEnqueue(RenderAll);

            OfflinePanel.ImportRequested += async (_, _) => await ImportFromClipboardAsync();
            OfflinePanel.WentOnline += (_, _) => RenderAll();
            OfflinePanel.NeedLogin += (_, _) => Frame?.Navigate(typeof(LoginPage));
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RenderAll();
            if (TourManager.IsTourPending)
            {
                TourManager.IsTourPending = false;
                DispatcherQueue.TryEnqueue(StartTour);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // Never let a group ping keep probing (and spawning cores) for a page
            // the user has left.
            CancelBatchPing();
            base.OnNavigatedFrom(e);
        }

        /// <summary>
        /// Single entry point that decides — from connectivity + imported configs —
        /// which sections to show, then (re)builds each visible section.
        /// </summary>
        private void RenderAll()
        {
            // A group ping is filling tags on the cards currently on screen; rebuilding
            // now would orphan them (the 20s heartbeat alone would kill every batch).
            // Defer until the batch finishes instead.
            if (IsBatchRunning)
            {
                _renderPending = true;
                return;
            }
            _renderPending = false;

            // Both sections are rebuilt below — drop the previous generation's buttons
            // so the registry can't grow unbounded or hold detached elements.
            _pingAllButtons.Clear();

            bool online = ConnectivityService.Instance.IsOnline;
            bool hasLocal = LocalConfigStore.Instance.HasAny;

            RenderImported();

            if (online)
            {
                ContentScroll.Visibility = Visibility.Visible;
                OfflinePanel.Visibility = Visibility.Collapsed;

                HeroCard.Visibility = Visibility.Visible;
                BackendHeader.Visibility = Visibility.Visible;
                PurchasesContainer.Visibility = Visibility.Visible;
                ImportedSection.Visibility = Visibility.Visible;
                RenderPurchases();
            }
            else
            {
                // Offline: no backend summary/purchases at all.
                HeroCard.Visibility = Visibility.Collapsed;
                BackendHeader.Visibility = Visibility.Collapsed;
                PurchasesContainer.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Collapsed;

                if (hasLocal)
                {
                    ContentScroll.Visibility = Visibility.Visible;
                    OfflinePanel.Visibility = Visibility.Collapsed;
                    ImportedSection.Visibility = Visibility.Visible;
                }
                else
                {
                    // Nothing to show → the reconnect / add placeholder.
                    ContentScroll.Visibility = Visibility.Collapsed;
                    ImportedSection.Visibility = Visibility.Collapsed;
                    OfflinePanel.Visibility = Visibility.Visible;
                }
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

        // ─── Imported / personal configs ────────────────────────────────
        /// <summary>
        /// Builds one expander per group: the manually pasted configs first, then one
        /// per subscription. Subscriptions are never merged — each keeps its own
        /// dropdown, its own "update" action and its own configs.
        /// </summary>
        private void RenderImported()
        {
            ImportedContainer.Children.Clear();

            var store = LocalConfigStore.Instance;
            var loc = LocalizationManager.Instance;

            var manual = store.ManualConfigs;
            var subs = store.Subscriptions;

            ImportedEmpty.Visibility = store.HasAny ? Visibility.Collapsed : Visibility.Visible;

            // ── Single (manually pasted) configs ──
            if (manual.Count > 0)
            {
                ImportedContainer.Children.Add(BuildGroupExpander(
                    glyph: "",                      // contact
                    title: loc.Get("Configs_SingleTitle"),
                    meta: null,
                    configs: manual,
                    subscription: null));
            }

            // ── One expander per subscription ──
            foreach (var sub in subs)
            {
                var configs = store.ConfigsFor(sub.Id);
                ImportedContainer.Children.Add(BuildGroupExpander(
                    glyph: "",                      // cloud / subscription
                    title: sub.Name,
                    meta: FormatUpdatedAt(sub.UpdatedAt),
                    configs: configs,
                    subscription: sub));
            }
        }

        private static string? FormatUpdatedAt(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                return null;
            return string.Format(
                LocalizationManager.Instance.Get("Configs_UpdatedAt"),
                dt.LocalDateTime.ToString("yyyy/MM/dd HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// One group dropdown: header (icon, title, count, meta, actions) + config cards.
        /// <paramref name="subscription"/> is null for the manual group, which has no
        /// update/rename/delete-group actions.
        /// </summary>
        private Expander BuildGroupExpander(
            string glyph,
            string title,
            string? meta,
            IReadOnlyList<LocalConfig> configs,
            LocalSubscription? subscription)
        {
            var loc = LocalizationManager.Instance;

            // ── Cards + the ping targets they expose ──
            var cardsStack = new StackPanel { Spacing = 8, Padding = new Thickness(0, 4, 0, 4) };
            var targets = new List<ConfigCardVisual>();
            foreach (var cfg in configs)
            {
                var visual = BuildLocalConfigCard(cfg);
                cardsStack.Children.Add(visual.Card);
                targets.Add(visual);
            }

            if (configs.Count == 0)
            {
                cardsStack.Children.Add(new TextBlock
                {
                    Text = loc.Get("Configs_GroupEmpty"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 4, 2, 4),
                    Foreground = ThemeRes.Brush(this, "TextFillColorTertiaryBrush")
                });
            }

            // ── Header ──
            // The content column is only ~400px wide (the nav rail is a fixed 230 of
            // the 635px window), so a subscription's three labelled actions get their
            // own row rather than squeezing the name down to an ellipsis.
            bool actionsOnOwnRow = subscription is not null;

            var headerGrid = new Grid
            {
                ColumnSpacing = 8,
                RowSpacing = 8,
                Padding = new Thickness(0, 8, 0, 8)
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (actionsOnOwnRow)
                headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            titleStack.Children.Add(new TextBlock
            {
                Text = $"{title}  ({configs.Count})",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            if (!string.IsNullOrEmpty(meta))
                titleStack.Children.Add(new TextBlock
                {
                    Text = meta,
                    FontSize = 11,
                    Foreground = ThemeRes.Brush(this, "TextFillColorSecondaryBrush")
                });
            Grid.SetColumn(titleStack, 1);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            // "Ping all" — available on every group.
            actions.Children.Add(BuildPingAllButton(targets));

            if (subscription is not null)
            {
                // "Update" — re-fetch this subscription (directly, never via the proxy).
                actions.Children.Add(BuildUpdateSubButton(subscription));

                // Overflow: rename / delete the whole subscription.
                var menuBtn = new Button
                {
                    Style = (Style)Resources["InfoButtonStyle"],
                    Content = new FontIcon
                    {
                        Glyph = "",   // more
                        FontSize = 13,
                        Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
                    }
                };
                ToolTipService.SetToolTip(menuBtn, loc.Get("Configs_MoreActions"));

                var flyout = BuildThemedMenuFlyout();
                var renameItem = BuildMenuItem("Configs_Rename", "\uE70F");
                renameItem.Click += (s, e) => _ = RenameSubscriptionAsync(subscription);
                var deleteItem = BuildMenuItem("Configs_Delete", "\uE74D", destructive: true);
                deleteItem.Click += (s, e) => _ = DeleteSubscriptionAsync(subscription);
                flyout.Items.Add(renameItem);
                flyout.Items.Add(deleteItem);
                menuBtn.Flyout = flyout;
                actions.Children.Add(menuBtn);
            }

            if (actionsOnOwnRow)
            {
                // Full-width second row, starting under the icon (flips with RTL).
                Grid.SetRow(actions, 1);
                Grid.SetColumn(actions, 0);
                Grid.SetColumnSpan(actions, 3);
                actions.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                Grid.SetColumn(actions, 2);
            }

            headerGrid.Children.Add(icon);
            headerGrid.Children.Add(titleStack);
            headerGrid.Children.Add(actions);

            var expander = new Expander
            {
                // Collapsed by default, like the purchase expanders — the page should
                // open as a compact list of groups, not a wall of cards.
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Header = headerGrid,
                Content = cardsStack
            };

            // Match the purchase expanders: pin the translucent default brushes to the
            // card brush so the particle backdrop doesn't bleed through.
            var cardBg = ThemeRes.Brush(this, "CardBackgroundFillColorDefaultBrush");
            expander.Resources["ExpanderHeaderBackground"] = cardBg;
            expander.Resources["ExpanderContentBackground"] = cardBg;
            expander.Resources["ExpanderContentBorderBrush"] =
                ThemeRes.Brush(this, "CardStrokeColorDefaultBrush");

            return expander;
        }

        private ConfigCardVisual BuildLocalConfigCard(LocalConfig lc)
        {
            var loc = LocalizationManager.Instance;

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

            // Row 0: personal icon + name + (ping tag + options menu)
            var row0 = new Grid { ColumnSpacing = 10 };
            row0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row0.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row0.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = BuildLocalIconCircle();
            Grid.SetColumn(icon, 0);

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            nameStack.Children.Add(new TextBlock
            {
                Text = lc.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = loc.Get("Dialog_LocalConfig"),
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

            var menuBtn = new Button
            {
                Style = (Style)Resources["InfoButtonStyle"],
                Content = new FontIcon
                {
                    Glyph = "",   // more
                    FontSize = 13,
                    Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
                }
            };
            ToolTipService.SetToolTip(menuBtn, loc.Get("Configs_MoreActions"));

            var flyout = BuildThemedMenuFlyout();
            var renameItem = BuildMenuItem("Configs_Rename", "\uE70F");
            renameItem.Click += (s, e) => _ = RenameLocalAsync(lc);
            var deleteItem = BuildMenuItem("Configs_Delete", "\uE74D", destructive: true);
            deleteItem.Click += (s, e) => _ = DeleteLocalAsync(lc);
            flyout.Items.Add(renameItem);
            flyout.Items.Add(deleteItem);
            menuBtn.Flyout = flyout;

            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            rightStack.Children.Add(pingTag);
            rightStack.Children.Add(menuBtn);
            Grid.SetColumn(rightStack, 2);

            row0.Children.Add(icon);
            row0.Children.Add(nameStack);
            row0.Children.Add(rightStack);
            Grid.SetRow(row0, 0);

            // Row 1: Ping + Connect (same handlers as backend cards)
            var row1 = new Grid { ColumnSpacing = 8 };
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pingBtn = new Button
            {
                Tag = (pingTag, lc.Link),
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
                Glyph = "",
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
            connContent.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
            connContent.Children.Add(new TextBlock { Text = loc.Get("Common_Connect"), FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
            connectBtn.Content = connContent;
            connectBtn.Tag = BuildLocalConfigInfo(lc);
            connectBtn.Click += Connect_Click;
            Grid.SetColumn(connectBtn, 1);

            row1.Children.Add(pingBtn);
            row1.Children.Add(connectBtn);
            Grid.SetRow(row1, 1);

            outer.Children.Add(row0);
            outer.Children.Add(row1);
            card.Child = outer;

            return new ConfigCardVisual { Card = card, PingTag = pingTag, Link = lc.Link };
        }

        // ─── Themed menus + labelled action buttons ─────────────────────

        /// <summary>
        /// A MenuFlyout whose popup is pinned to the window's actual theme.
        /// Flyouts render in a separate popup tree that resolves {ThemeResource}
        /// against the OS theme rather than the per-window theme DevWinUI applies
        /// (the same reason code-behind must use <see cref="ThemeRes"/>), which
        /// otherwise leaves item text invisible — e.g. white-on-light.
        /// </summary>
        private MenuFlyout BuildThemedMenuFlyout()
        {
            var flyout = new MenuFlyout();
            var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(
                FrameworkElement.RequestedThemeProperty, this.ActualTheme));
            flyout.MenuFlyoutPresenterStyle = presenterStyle;
            return flyout;
        }

        /// <summary>Menu row with an explicit label + icon colour, so text always shows.</summary>
        private MenuFlyoutItem BuildMenuItem(string textKey, string glyph, bool destructive = false)
        {
            var fg = destructive
                ? new SolidColorBrush(Color.FromArgb(255, 229, 57, 53))
                : ThemeRes.Brush(this, "TextFillColorPrimaryBrush");

            return new MenuFlyoutItem
            {
                Text = LocalizationManager.Instance.Get(textKey),
                RequestedTheme = this.ActualTheme,
                Foreground = fg,
                Icon = new FontIcon { Glyph = glyph, Foreground = fg },
            };
        }

        /// <summary>
        /// "Update" for a subscription — icon + label + inline spinner, matching the
        /// "ping all" button so the group header reads as one set of actions.
        /// </summary>
        private Button BuildUpdateSubButton(LocalSubscription sub)
        {
            var loc = LocalizationManager.Instance;
            var accent = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush");

            var ring = new ProgressRing
            {
                Width = 13,
                Height = 13,
                IsActive = false,
                Visibility = Visibility.Collapsed,
                Foreground = accent
            };
            var glyph = new FontIcon { Glyph = "", FontSize = 13, Foreground = accent };
            var label = new TextBlock
            {
                Text = loc.Get("Configs_Update"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(ring);
            content.Children.Add(glyph);
            content.Children.Add(label);

            var btn = new Button
            {
                Style = (Style)Resources["InfoButtonStyle"],
                Content = content
            };
            ToolTipService.SetToolTip(btn, loc.Get("Configs_UpdateSub"));
            btn.Click += (s, e) => _ = UpdateSubscriptionAsync(sub, btn, ring, glyph);
            return btn;
        }

        // ─── Group ping ("ping all") ────────────────────────────────────

        /// <summary>
        /// Builds a group's "ping all" button, wired to a sequential batch over that
        /// group's configs only.
        /// </summary>
        private Button BuildPingAllButton(IReadOnlyList<ConfigCardVisual> targets)
        {
            var loc = LocalizationManager.Instance;

            var label = new TextBlock
            {
                Text = loc.Get("Configs_PingAll"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            };
            var glyph = new FontIcon
            {
                Glyph = "",   // speed / stopwatch
                FontSize = 13,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            };
            var ring = new ProgressRing
            {
                Width = 13,
                Height = 13,
                IsActive = false,
                Visibility = Visibility.Collapsed,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(ring);
            content.Children.Add(glyph);
            content.Children.Add(label);

            var btn = new Button
            {
                Style = (Style)Resources["InfoButtonStyle"],
                Content = content,
                IsEnabled = targets.Count > 0,
                Tag = targets.Count,          // read back by SetPingAllButtonsEnabled
            };
            ToolTipService.SetToolTip(btn, loc.Get("Configs_PingAllTooltip"));
            btn.Click += (s, e) => _ = RunGroupPingAsync(btn, ring, glyph, label, targets);

            _pingAllButtons.Add(btn);
            return btn;
        }

        /// <summary>
        /// Pings every config in one group, sequentially, updating each card's tag as
        /// results arrive. Starting another group's batch (or an individual ping)
        /// supersedes this one; the button is always restored on the way out.
        /// </summary>
        private async Task RunGroupPingAsync(
            Button button, ProgressRing ring, FontIcon glyph, TextBlock label,
            IReadOnlyList<ConfigCardVisual> targets)
        {
            if (targets.Count == 0 || IsBatchRunning) return;

            var loc = LocalizationManager.Instance;
            string idleText = loc.Get("Configs_PingAll");

            var cts = new CancellationTokenSource();
            _batchCts = cts;

            SetPingAllButtonsEnabled(false);
            ring.IsActive = true;
            ring.Visibility = Visibility.Visible;
            glyph.Visibility = Visibility.Collapsed;
            label.Text = $"0/{targets.Count}";

            // Snapshot each tag so a cancelled batch can put back whatever it showed
            // before, instead of leaving cards stuck on "Pinging…".
            var snapshot = new (string Text, Brush? Background, Visibility Visibility)[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                var tag = targets[i].PingTag;
                snapshot[i] = ((tag.Child as TextBlock)?.Text ?? "", tag.Background, tag.Visibility);
                SetTagPending(tag);
            }

            // Written by the batch callback (background thread) before it enqueues the
            // UI update. PingBatchAsync only returns once every callback has run, so
            // by the time the finally executes this array is fully published.
            var completed = new bool[targets.Count];

            try
            {
                var links = targets.Select(t => t.Link).ToList();
                int done = 0;

                await PingService.Instance.PingBatchAsync(links, (index, ping) =>
                {
                    if (index < 0 || index >= targets.Count) return;
                    completed[index] = true;

                    // Callback arrives on a background thread → marshal to the UI.
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ApplyPingToTag(targets[index].PingTag, ping);
                        done++;
                        label.Text = $"{done}/{targets.Count}";
                    });
                }, cts.Token);
            }
            catch (OperationCanceledException) { /* superseded / navigated away */ }
            catch (Exception ex)
            {
                ShowImportInfo(
                    string.Format(loc.Get("Configs_PingAllFailed"), FirstLine(ex.Message)),
                    InfoBarSeverity.Error);
            }
            finally
            {
                // Only clear the shared slot if we still own it — a newer batch may
                // have already replaced it.
                if (ReferenceEquals(_batchCts, cts)) _batchCts = null;
                cts.Dispose();

                // Restore tags that never got a result (batch stopped early). Completed
                // ones are left to their queued UI update.
                for (int i = 0; i < targets.Count; i++)
                {
                    if (completed[i]) continue;
                    var tag = targets[i].PingTag;
                    if (tag.Child is TextBlock tb) tb.Text = snapshot[i].Text;
                    tag.Background = snapshot[i].Background;
                    tag.Visibility = snapshot[i].Visibility;
                }

                SetPingAllButtonsEnabled(true);
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                glyph.Visibility = Visibility.Visible;
                label.Text = idleText;

                // Any re-render we suppressed while the batch ran now gets to run.
                if (_renderPending && !IsBatchRunning)
                    RenderAll();
            }
        }

        private static string FirstLine(string text)
        {
            var nl = text.IndexOf('\n');
            return (nl > 0 ? text[..nl] : text).Trim();
        }

        // ─── Shared ping-tag rendering (single + group) ─────────────────
        private static void SetTagPending(Border tag)
        {
            if (tag.Child is TextBlock tb)
                tb.Text = LocalizationManager.Instance.Get("Common_Pinging");
            tag.Background = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
            tag.Visibility = Visibility.Visible;
        }

        private static void ApplyPingToTag(Border tag, int ping)
        {
            bool firstTime = tag.Visibility == Visibility.Collapsed;

            if (tag.Child is TextBlock tb)
                tb.Text = ping >= 0 ? $"{ping} ms" : "-1 ms";

            tag.Background = new SolidColorBrush(
                ping > 0 ? PingColor(ping) : Color.FromArgb(255, 229, 57, 53));
            tag.Visibility = Visibility.Visible;

            if (tag.RenderTransform is not ScaleTransform st) return;

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

        // ─── Subscription actions ───────────────────────────────────────

        /// <summary>
        /// Re-fetches a subscription DIRECTLY (never through the proxy) and swaps in
        /// the new configs, replacing only that subscription's own entries.
        /// </summary>
        private async Task UpdateSubscriptionAsync(
            LocalSubscription sub, Button button, ProgressRing ring, FontIcon glyph)
        {
            var loc = LocalizationManager.Instance;

            button.IsEnabled = false;
            ring.IsActive = true;
            ring.Visibility = Visibility.Visible;
            glyph.Visibility = Visibility.Collapsed;
            try
            {
                var result = await ConfigImporter.FetchSubscriptionAsync(sub.Url);

                if (result.FetchFailed)
                {
                    ShowImportInfo(
                        string.Format(loc.Get("Configs_SubUpdateFailed"), sub.Name),
                        InfoBarSeverity.Error);
                    return;
                }
                if (!result.AnyAdded)
                {
                    ShowImportInfo(
                        string.Format(loc.Get("Configs_SubUpdateEmpty"), sub.Name),
                        InfoBarSeverity.Warning);
                    return;
                }

                // Rebuilds the cards for this group only (Changed → RenderAll).
                LocalConfigStore.Instance.ReplaceSubscriptionConfigs(sub.Id, result.Configs);

                ShowImportInfo(
                    string.Format(loc.Get("Configs_SubUpdated"), sub.Name, result.Added),
                    InfoBarSeverity.Success);
            }
            finally
            {
                // The card tree is usually rebuilt underneath us; restoring is still
                // correct for the case where nothing changed.
                button.IsEnabled = true;
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                glyph.Visibility = Visibility.Visible;
            }
        }

        private async Task RenameSubscriptionAsync(LocalSubscription sub)
        {
            var loc = LocalizationManager.Instance;
            var input = new TextBox
            {
                Text = sub.Name,
                PlaceholderText = loc.Get("Configs_NamePlaceholder"),
                MaxLength = 60
            };
            var dialog = new ContentDialog
            {
                Title = loc.Get("Configs_RenameSubTitle"),
                Content = input,
                PrimaryButtonText = loc.Get("Common_OK"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                LocalConfigStore.Instance.RenameSubscription(sub.Id, input.Text.Trim());
        }

        private async Task DeleteSubscriptionAsync(LocalSubscription sub)
        {
            var loc = LocalizationManager.Instance;
            int count = LocalConfigStore.Instance.ConfigsFor(sub.Id).Count;
            var dialog = new ContentDialog
            {
                Title = loc.Get("Configs_DeleteSubTitle"),
                Content = string.Format(loc.Get("Configs_DeleteSubBody"), sub.Name, count),
                PrimaryButtonText = loc.Get("Configs_Delete"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                LocalConfigStore.Instance.RemoveSubscription(sub.Id);
        }

        private Grid BuildLocalIconCircle()
        {
            var container = new Grid { Width = 40, Height = 40 };
            container.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80))
            });
            container.Children.Add(new FontIcon
            {
                Glyph = "",   // contact
                FontSize = 18,
                Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return container;
        }

        private static Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo BuildLocalConfigInfo(LocalConfig lc) =>
            new()
            {
                IsLocal = true,
                LocalId = lc.Id,
                ConfigName = lc.Name,
                ConfigCode = lc.Link,
                Country = "",
                CountryCode = "",
                IsAvailable = true,
            };

        // ─── Import from clipboard ───────────────────────────────────────
        private async void AddConfig_Click(object sender, RoutedEventArgs e) =>
            await ImportFromClipboardAsync();

        private async Task ImportFromClipboardAsync()
        {
            var loc = LocalizationManager.Instance;

            string text = "";
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                    text = await content.GetTextAsync();
            }
            catch { /* clipboard access can transiently fail */ }

            if (string.IsNullOrWhiteSpace(text))
            {
                // A dialog, not the InfoBar — the bar lives inside the scroll view,
                // which is hidden in the offline-empty state where this is reachable.
                await ShowImportFailedAsync(loc.Get("Configs_ImportedNone"));
                return;
            }

            var result = await ConfigImporter.ImportFromTextAsync(text);
            if (!result.AnyAdded)
            {
                await ShowImportFailedAsync(loc.Get("Configs_ImportedNone"));
                return;
            }

            var store = LocalConfigStore.Instance;
            string msg;

            if (result.SubscriptionUrl is { } subUrl)
            {
                // A subscription gets its own group. Re-importing the same URL refreshes
                // that group in place instead of creating a duplicate dropdown.
                var existing = store.FindSubscriptionByUrl(subUrl);
                if (existing is not null)
                {
                    store.ReplaceSubscriptionConfigs(existing.Id, result.Configs);
                    msg = string.Format(loc.Get("Configs_SubUpdated"), existing.Name, result.Added);
                }
                else
                {
                    var sub = store.AddSubscription(subUrl, null, result.Configs);
                    msg = string.Format(loc.Get("Configs_SubAdded"), sub.Name, result.Added);
                }
            }
            else
            {
                store.AddManual(result.Configs);   // raises Changed → RenderAll
                msg = result.Failed > 0
                    ? string.Format(loc.Get("Configs_ImportedSome"), result.Added, result.Failed)
                    : string.Format(loc.Get("Configs_ImportedOk"), result.Added);
            }

            // After a successful import the content view is visible again, so the
            // InfoBar there is the right place for the success note.
            ShowImportInfo(msg, InfoBarSeverity.Success);
        }

        private async Task ShowImportFailedAsync(string message)
        {
            var loc = LocalizationManager.Instance;
            await new ContentDialog
            {
                Title = loc.Get("Configs_AddFromClipboard"),
                Content = message,
                CloseButtonText = loc.Get("Common_OK"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }

        private void ShowImportInfo(string message, InfoBarSeverity severity)
        {
            ImportInfoBar.Severity = severity;
            ImportInfoBar.Message = message;
            ImportInfoBar.IsOpen = true;
        }

        private async Task RenameLocalAsync(LocalConfig lc)
        {
            var loc = LocalizationManager.Instance;
            var input = new TextBox
            {
                Text = lc.Name,
                PlaceholderText = loc.Get("Configs_NamePlaceholder"),
                MaxLength = 60
            };
            var dialog = new ContentDialog
            {
                Title = loc.Get("Configs_RenameTitle"),
                Content = input,
                PrimaryButtonText = loc.Get("Common_OK"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                LocalConfigStore.Instance.Rename(lc.Id, input.Text.Trim());
        }

        private async Task DeleteLocalAsync(LocalConfig lc)
        {
            var loc = LocalizationManager.Instance;
            var dialog = new ContentDialog
            {
                Title = loc.Get("Configs_DeleteTitle"),
                Content = string.Format(loc.Get("Configs_DeleteBody"), lc.Name),
                PrimaryButtonText = loc.Get("Configs_Delete"),
                CloseButtonText = loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = loc.FlowDirection,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                LocalConfigStore.Instance.Remove(lc.Id);
        }

        private (Expander Expander, TeachingTip? InfoTip) BuildPurchaseExpander(int index, PurchaseDto purchase)
        {
            var loc = LocalizationManager.Instance;
            var configs = purchase.Configs ?? new List<ConfigDto>();

            // ── Info tip (data / time left) ──
            int planUsage = PlanUsageFor(purchase.Plan);
            // The plan's data quota is SHARED across every config in the purchase —
            // using one config draws down the same pool. So the total is the plan
            // usage itself (not multiplied by config count) and the remaining is the
            // shared figure each config reports (identical), not a sum.
            double leftData = configs.Count > 0 ? configs.Max(c => c.GbLeftValue) : 0;
            double totalData = planUsage > 0 ? planUsage : leftData;
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
            string volume = planUsage > 0 ? $"{planUsage} GB" : "";
            var targets = new List<ConfigCardVisual>();
            foreach (var cfg in configs)
            {
                var visual = BuildConfigCard(cfg, purchase, volume);
                cardsStack.Children.Add(visual.Card);
                targets.Add(visual);
                _firstConfigCard ??= visual.Card;
            }

            // "Ping all" for this purchase, same as the imported groups.
            headerStack.Children.Add(BuildPingAllButton(targets));

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

        private ConfigCardVisual BuildConfigCard(ConfigDto cfg, PurchaseDto purchase, string volume)
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
                Tag = (pingTag, cfg.ConfigCode ?? ""),
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
            connectBtn.Tag = BuildConfigInfo(cfg, purchase, volume);
            connectBtn.Click += Connect_Click;
            Grid.SetColumn(connectBtn, 1);

            row1.Children.Add(pingBtn);
            row1.Children.Add(connectBtn);
            Grid.SetRow(row1, 1);

            outer.Children.Add(row0);
            outer.Children.Add(row1);
            card.Child = outer;

            return new ConfigCardVisual { Card = card, PingTag = pingTag, Link = cfg.ConfigCode ?? "" };
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

        // ─── Ping button: real latency through a temporary Xray tunnel ─
        // Single-flight: PingService supersedes any in-progress ping, so only one
        // probe core is ever alive. While pinging, the button is disabled.
        private async void Ping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not (Border tag, string configCode)) return;
            if (string.IsNullOrWhiteSpace(configCode)) return;

            btn.IsEnabled = false;
            SetTagPending(tag);

            int? result = await Services.Xray.PingService.Instance.PingConfigAsync(configCode);
            btn.IsEnabled = true;

            // Superseded by a newer ping → leave the tag for that request to fill.
            if (result is null) return;

            ApplyPingToTag(tag, result.Value);
            PulseButton(btn);
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

        /// <summary>
        /// Latency rating tuned for this app's audience, where traffic leaves the
        /// country before it reaches a server: green ≤ 400 ms, amber 400–900 ms,
        /// red above 900 ms.
        /// </summary>
        private static Color PingColor(int ping)
        {
            if (ping <= 400) return Color.FromArgb(255, 0, 200, 83);    // green
            if (ping <= 900) return Color.FromArgb(255, 255, 179, 0);   // amber
            return Color.FromArgb(255, 229, 57, 53);                    // red
        }

        // ─── Connect: hand the config to the dashboard, which starts the tunnel ─
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo cfg) return;

            // Navigate to the dashboard (updating the nav selection) and let it
            // apply + connect the chosen config via the page parameter.
            App.RootWindow?.NavigateToPageWith(typeof(DashboardPage), "Dashboard", cfg);
        }

        private Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo BuildConfigInfo(
            ConfigDto cfg, PurchaseDto purchase, string volume)
        {
            string code = cfg.Country ?? "";
            return new Lithiumvpn.Dialogs.ConfigSelectionDialog.ConfigInfo
            {
                PurchaseId = purchase.PurchaseId.ToString(),
                DataVolume = volume,
                GbLeft = cfg.GbLeftValue,
                DaysLeft = cfg.DaysLeft,
                ExpiryDate = DateOnly.FromDateTime(DateTime.Now.AddDays(cfg.DaysLeft)),
                Tag = $"{purchase.PurchaseId}_{cfg.Name}",
                FlagEmoji = "",
                Country = CodeToCountryName(code),
                CountryCode = code,
                ConfigName = cfg.Name ?? "",
                ConfigCode = cfg.ConfigCode ?? "",
                PingMs = 0,
                IsAvailable = true,
            };
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
