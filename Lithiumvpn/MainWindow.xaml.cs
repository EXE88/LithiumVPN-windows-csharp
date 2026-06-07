using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;

namespace Lithiumvpn
{
    public sealed partial class MainWindow : Window
    {
        // Connection logic moved to DashboardPage; MainWindow should not directly manipulate Dashboard visual states.

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;

            this.Activated += MainWindow_Activated;

            MainFrame.Navigate(typeof(Pages.DashboardPage));
            // Navigate to dashboard; DashboardPage handles its own connection UI state.

            // Initialize active nav button to the dashboard button that is selected by default
            _activeNavButton = NavDashboardButton;

            // Start bell icon animation (settings uses AnimatedIcon states on pointer events)
            try
            {
                var bellStoryboard = (Microsoft.UI.Xaml.Media.Animation.Storyboard)RootGrid.Resources["BellPulseStoryboard"];
                bellStoryboard?.Begin();
            }
            catch { }

            // Make the window fixed size (disable resizing/maximizing)
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                }
            }
            catch { }
        }

        private bool _logoInitialized = false;

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // فقط یه بار اجرا بشه
            if (_logoInitialized) return;
            _logoInitialized = true;

            UpdateLogo();

            if (App.GetThemeService is not null)
                App.GetThemeService.ThemeChanged += (s, e) => UpdateLogo();
        }

        // Removed duplicate connection state code (DashboardPage implements this behavior).

        private Button? _activeNavButton;
        private void SettingsIconButton_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try { AnimatedIcon.SetState(SettingsAnimatedIcon, "PointerOver"); } catch { }
        }

        private void SettingsIconButton_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try { AnimatedIcon.SetState(SettingsAnimatedIcon, "Normal"); } catch { }
        }
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;
            // If settings or bell icon was clicked, don't apply the selected style (they use compact layout)
            if (clickedButton == SettingsIconButton || clickedButton == BellIconButton)
            {
                if (clickedButton == SettingsIconButton)
                {
                    // Play AnimatedIcon 'PointerOver' state briefly to simulate click animation
                    try { AnimatedIcon.SetState(SettingsAnimatedIcon, "PointerOver"); } catch { }
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    timer.Tick += (s, ev) =>
                    {
                        try { AnimatedIcon.SetState(SettingsAnimatedIcon, "Normal"); } catch { }
                        ((DispatcherTimer)s).Stop();
                    };
                    timer.Start();
                    MainFrame.Navigate(typeof(Pages.SettingsPage));
                    return;
                }

                if (clickedButton == BellIconButton)
                {
                    MainFrame.Navigate(typeof(Pages.NotificationsPage));
                    return;
                }
            }

            if (_activeNavButton != null && _activeNavButton != SettingsIconButton && _activeNavButton != BellIconButton)
                _activeNavButton.Style = (Style)RootGrid.Resources["NavButtonStyle"];

            clickedButton.Style = (Style)RootGrid.Resources["SelectedNavButtonStyle"];
            _activeNavButton = clickedButton;
            Type? pageType = clickedButton.Tag?.ToString() switch
            {
                "Dashboard" => typeof(Pages.DashboardPage),
                "Configs" => typeof(Pages.ServersPage),
                "Coins" => typeof(Pages.CoinsPage),
                "Plans" => typeof(Pages.PlansPage),
                "Settings" => typeof(Pages.SettingsPage),
                "Account" => typeof(Pages.AccountPage),
                "Notifications" => typeof(Pages.NotificationsPage),
                _ => null
            };

            if (pageType is not null)
                MainFrame.Navigate(pageType);
        }
        private void UpdateLogo()
        {
            // ✅ double null-safe
            if (App.GetThemeService is null) return;
            if (AppLogo is null) return;

            bool isDark = App.GetThemeService.IsDark;

            var logoPath = isDark
                ? "ms-appx:///Assets/LogoWhite.svg"
                : "ms-appx:///Assets/LogoBlack.svg";

            AppLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri(logoPath));
        }
    }
}
