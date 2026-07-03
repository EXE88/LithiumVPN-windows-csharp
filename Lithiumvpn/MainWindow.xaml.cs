using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
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

            // Start with the SplashPage which will navigate to the dashboard when complete
            MainFrame.Navigated += MainFrame_Navigated;
            MainFrame.Navigate(typeof(Pages.SplashPage));

            // Initialize active nav button to the dashboard button that is selected by default
            _activeNavButton = NavDashboardButton;

            TourManager.TourRequested += OnTourRequested;

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
            UpdateParticle();

            if (App.GetThemeService is not null)
                App.GetThemeService.ThemeChanged += (s, e) =>
                {
                    UpdateLogo();
                    UpdateParticle();
                };
        }

        // Removed duplicate connection state code (DashboardPage implements this behavior).

        private void OnTourRequested(Type pageType)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // Update nav button selection
                var tag = pageType switch
                {
                    _ when pageType == typeof(Pages.DashboardPage)  => "Dashboard",
                    _ when pageType == typeof(Pages.ServersPage)    => "Configs",
                    _ when pageType == typeof(Pages.AccountPage)    => "Account",
                    _ when pageType == typeof(Pages.CoinsPage)      => "Coins",
                    _ when pageType == typeof(Pages.PlansPage)      => "Plans",
                    _ => ""
                };

                foreach (var child in NavItemsPanel.Children)
                {
                    if (child is Button btn)
                    {
                        if (btn.Tag?.ToString() == tag)
                        {
                            if (_activeNavButton != null && _activeNavButton != SettingsIconButton && _activeNavButton != BellIconButton)
                                _activeNavButton.Style = (Style)RootGrid.Resources["NavButtonStyle"];
                            btn.Style = (Style)RootGrid.Resources["SelectedNavButtonStyle"];
                            _activeNavButton = btn;
                        }
                    }
                }

                MainFrame.Navigate(pageType);
            });
        }

        /// <summary>
        /// Navigates the main frame to a page (optionally with a parameter) while
        /// syncing the left-nav selection to the given tag. Used by in-page actions
        /// like the ServersPage "Connect" button jumping to the dashboard.
        /// </summary>
        public void NavigateToPageWith(Type pageType, string navTag, object? parameter)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var child in NavItemsPanel.Children)
                {
                    if (child is Button btn && btn.Tag?.ToString() == navTag)
                    {
                        if (_activeNavButton != null && _activeNavButton != SettingsIconButton && _activeNavButton != BellIconButton)
                            _activeNavButton.Style = (Style)RootGrid.Resources["NavButtonStyle"];
                        btn.Style = (Style)RootGrid.Resources["SelectedNavButtonStyle"];
                        _activeNavButton = btn;
                        break;
                    }
                }
                MainFrame.Navigate(pageType, parameter);
            });
        }

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
            if (clickedButton == HelpIconButton || clickedButton == SettingsIconButton || clickedButton == BellIconButton)
            {
                if (clickedButton == HelpIconButton)
                {
                    MainFrame.Navigate(typeof(Pages.HelpPage));
                    return;
                }

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
        private void MainFrame_Navigated(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            UpdateParticle();
            // If navigated to SplashPage or LoginPage: hide left navigation and expand Frame to full window
            if (e?.SourcePageType == typeof(Pages.SplashPage) || e?.SourcePageType == typeof(Pages.LoginPage))
            {
                // Hide left nav
                try { LeftNavGrid.Visibility = Visibility.Collapsed; } catch { }

                // Expand MainFrame to cover both columns
                try
                {
                    Grid.SetColumn(MainFrame, 0);
                    Grid.SetColumnSpan(MainFrame, 2);
                    MainFrame.Margin = new Thickness(0);
                }
                catch { }

                // If it's SplashPage, navigate to LoginPage when finished
                if (e.Content is Pages.SplashPage splash)
                {
                    splash.SplashCompleted += () =>
                    {
                        // A saved session was validated on the splash → go straight to the
                        // dashboard (revealing the nav); otherwise show the login page.
                        if (splash.GoToDashboard)
                            OnLoginCompleted();
                        else
                            MainFrame.Navigate(typeof(Pages.LoginPage));
                    };
                }

                // If it's LoginPage, navigate to Dashboard when login completes
                if (e.Content is Pages.LoginPage login)
                {
                    // avoid multiple subscriptions
                    login.LoginCompleted -= OnLoginCompleted;
                    login.LoginCompleted += OnLoginCompleted;
                }

                return;
            }

            // For other pages: ensure left nav visible and frame uses right column only
            try
            {
                LeftNavGrid.Visibility = Visibility.Visible;
                Grid.SetColumn(MainFrame, 1);
                Grid.SetColumnSpan(MainFrame, 1);
                MainFrame.Margin = new Thickness(20,15,20,15);
            }
            catch { }
        }

        private void OnLoginCompleted()
        {
            // Fade out current content, navigate to dashboard and fade in
            DispatcherQueue.TryEnqueue(() => { _ = FadeThenNavigateAsync(); });
        }

        private async Task FadeThenNavigateAsync()
        {
            try
            {
                // Step 1: fade out the login page
                var tcs1 = new TaskCompletionSource<bool>();
                var sbOut = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                var fadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 1, To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(280)),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOut, MainFrame);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOut, "Opacity");
                sbOut.Children.Add(fadeOut);
                sbOut.Completed += (_, _) => tcs1.TrySetResult(true);
                sbOut.Begin();
                await tcs1.Task;

                // Step 2: prep layout while everything is invisible
                // Prepare LeftNavGrid: visible but transparent, shifted left
                var navTranslate = new Microsoft.UI.Xaml.Media.TranslateTransform { X = -30 };
                LeftNavGrid.RenderTransform = navTranslate;
                LeftNavGrid.Opacity = 0;
                LeftNavGrid.Visibility = Visibility.Visible;

                // Move MainFrame to right column (still opacity=0, so no visual jump)
                Grid.SetColumn(MainFrame, 1);
                Grid.SetColumnSpan(MainFrame, 1);
                MainFrame.Margin = new Thickness(20, 15, 20, 15);
                MainFrame.Opacity = 0;

                // Step 3: navigate to dashboard
                MainFrame.Navigate(typeof(Pages.DashboardPage));

                // Step 4: simultaneously fade in dashboard + slide in nav
                var easeOut = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                    { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

                var sbIn = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

                // dashboard fade in
                var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0, To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(380)),
                    EasingFunction = easeOut
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, MainFrame);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");

                // nav fade in
                var navFadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0, To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(380)),
                    EasingFunction = easeOut
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(navFadeIn, LeftNavGrid);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(navFadeIn, "Opacity");

                // nav slide in from left
                var navSlideIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = -30, To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(380)),
                    EasingFunction = easeOut
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(navSlideIn, navTranslate);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(navSlideIn, "X");

                sbIn.Children.Add(fadeIn);
                sbIn.Children.Add(navFadeIn);
                sbIn.Children.Add(navSlideIn);
                sbIn.Completed += (_, _) =>
                {
                    // clean up transform after animation
                    LeftNavGrid.RenderTransform = null;
                };
                sbIn.Begin();
            }
            catch { }
        }
        /// <summary>
        /// Smoothly fades the whole window out, applies a change (e.g. switching language /
        /// flow direction) while hidden so the layout flip isn't visible, then fades back in.
        /// </summary>
        public void AnimateLanguageChange(Action applyChange)
        {
            try
            {
                var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                    { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut };

                var sbOut = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                var fadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 1, To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(170)),
                    EasingFunction = ease
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOut, RootGrid);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOut, "Opacity");
                sbOut.Children.Add(fadeOut);

                sbOut.Completed += (_, _) =>
                {
                    try { applyChange?.Invoke(); } catch { }

                    var sbIn = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                    var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                    {
                        From = 0, To = 1,
                        Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                        EasingFunction = ease
                    };
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, RootGrid);
                    Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
                    sbIn.Children.Add(fadeIn);
                    sbIn.Begin();
                };
                sbOut.Begin();
            }
            catch
            {
                // If anything goes wrong, at least apply the change.
                try { applyChange?.Invoke(); } catch { }
                RootGrid.Opacity = 1;
            }
        }

        private void UpdateParticle()
        {
            if (App.GetThemeService is null || ParticleBg is null) return;
            if (App.GetThemeService.IsDark)
            {
                ParticleBg.ParticleColor = Color.FromArgb(255, 90, 90, 110);
                ParticleBg.LineColor = Color.FromArgb(255, 70, 70, 90);
                ParticleBg.Opacity = 0.12;
            }
            else
            {
                ParticleBg.ParticleColor = Color.FromArgb(255, 255, 255, 255);
                ParticleBg.LineColor = Color.FromArgb(255, 255, 255, 255);
                ParticleBg.Opacity = 0.2;
            }
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
