using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.System;
using Lithiumvpn.Localization;
using Lithiumvpn.Services.Xray;

namespace Lithiumvpn.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private bool _suppressLanguageChange;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;

            // Re-render the (dynamically built) exception rows on language switch so
            // their Edit/Delete labels follow the current language.
            LocalizationManager.Instance.LanguageChanged += () =>
                DispatcherQueue.TryEnqueue(RenderProxyList);
        }

        private void SettingsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Reflect the currently active language without firing a switch.
            _suppressLanguageChange = true;
            string current = LocalizationManager.Instance.CurrentLanguage;
            foreach (var item in LanguageComboBox.Items)
            {
                if (item is ComboBoxItem cbi && (cbi.Tag as string) == current)
                {
                    LanguageComboBox.SelectedItem = cbi;
                    break;
                }
            }
            _suppressLanguageChange = false;

            RenderProxyList();
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLanguageChange) return;
            if (LanguageComboBox.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string code) return;
            if (code == LocalizationManager.Instance.CurrentLanguage) return;

            // Fade the whole window out, flip language + flow direction while hidden, fade back in.
            if (App.RootWindow is { } window)
                window.AnimateLanguageChange(() => LocalizationManager.Instance.ChangeLanguage(code));
            else
                LocalizationManager.Instance.ChangeLanguage(code);
        }

        // ─── Proxy exceptions ───────────────────────────────────────────
        private void ProxyEntryBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter) ProxyAdd_Click(sender, e);
        }

        private void ProxyAdd_Click(object sender, RoutedEventArgs e)
        {
            var text = ProxyEntryBox.Text?.Trim() ?? "";
            if (text.Length == 0) return;

            if (!ProxyBypassStore.Add(text))
            {
                ShowProxyInfo(LocalizationManager.Instance.Get("Settings_ProxyDuplicate"));
                return;
            }

            ProxyEntryBox.Text = "";
            ProxyInfoBar.IsOpen = false;
            RenderProxyList();
            ConnectionService.Instance.ReapplyProxyIfConnected();
        }

        private void ShowProxyInfo(string message)
        {
            ProxyInfoBar.Message = message;
            ProxyInfoBar.IsOpen = true;
        }

        private void RenderProxyList()
        {
            ProxyList.Items.Clear();

            var entries = ProxyBypassStore.Entries;
            ProxyEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var entry in entries)
                ProxyList.Items.Add(BuildEntryRow(entry));
        }

        private Border BuildEntryRow(string entry)
        {
            var loc = LocalizationManager.Instance;

            var border = new Border
            {
                Background = ThemeRes.Brush(this, "SubtleFillColorSecondaryBrush"),
                BorderBrush = ThemeRes.Brush(this, "CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 8, 6)
            };

            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Display text (swapped for an editable box in edit mode)
            var text = new TextBlock
            {
                Text = entry,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeRes.Brush(this, "TextFillColorPrimaryBrush")
            };
            Grid.SetColumn(text, 0);

            var editBtn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Content = new FontIcon { Glyph = "", FontSize = 14 }  // edit
            };
            ToolTipService.SetToolTip(editBtn, loc.Get("Settings_ProxyEdit"));
            Grid.SetColumn(editBtn, 1);

            var deleteBtn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Content = new FontIcon
                {
                    Glyph = "", // delete
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 57, 53))
                }
            };
            ToolTipService.SetToolTip(deleteBtn, loc.Get("Settings_ProxyDelete"));
            Grid.SetColumn(deleteBtn, 2);

            deleteBtn.Click += (s, e) =>
            {
                ProxyBypassStore.Remove(entry);
                RenderProxyList();
                ConnectionService.Instance.ReapplyProxyIfConnected();
            };

            editBtn.Click += (s, e) => EnterEditMode(border, grid, entry);

            grid.Children.Add(text);
            grid.Children.Add(editBtn);
            grid.Children.Add(deleteBtn);
            border.Child = grid;
            return border;
        }

        private void EnterEditMode(Border border, Grid grid, string entry)
        {
            var loc = LocalizationManager.Instance;

            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new TextBox { Text = entry, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(box, 0);

            var saveBtn = new Button
            {
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon
                {
                    Glyph = "", // checkmark
                    FontSize = 14,
                    Foreground = ThemeRes.Brush(this, "AccentAAFillColorDefaultBrush")
                }
            };
            ToolTipService.SetToolTip(saveBtn, loc.Get("Settings_ProxySave"));
            Grid.SetColumn(saveBtn, 1);

            var cancelBtn = new Button
            {
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "", FontSize = 14 } // cancel
            };
            ToolTipService.SetToolTip(cancelBtn, loc.Get("Settings_ProxyCancel"));
            Grid.SetColumn(cancelBtn, 2);

            void Commit()
            {
                var newValue = box.Text?.Trim() ?? "";
                if (newValue.Length == 0 || newValue == entry) { RenderProxyList(); return; }
                if (!ProxyBypassStore.Update(entry, newValue))
                    ShowProxyInfo(loc.Get("Settings_ProxyDuplicate"));
                else
                    ConnectionService.Instance.ReapplyProxyIfConnected();
                RenderProxyList();
            }

            box.KeyDown += (s, e) =>
            {
                if (e.Key == VirtualKey.Enter) Commit();
                else if (e.Key == VirtualKey.Escape) RenderProxyList();
            };
            saveBtn.Click += (s, e) => Commit();
            cancelBtn.Click += (s, e) => RenderProxyList();

            grid.Children.Add(box);
            grid.Children.Add(saveBtn);
            grid.Children.Add(cancelBtn);
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        }
    }
}
