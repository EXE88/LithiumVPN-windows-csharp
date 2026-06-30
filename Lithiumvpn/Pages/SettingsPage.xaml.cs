using Microsoft.UI.Xaml.Controls;
using Lithiumvpn.Localization;

namespace Lithiumvpn.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private bool _suppressLanguageChange;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
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
    }
}
