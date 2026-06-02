using DevWinUI;
using Microsoft.UI.Xaml;
using System;

namespace Lithiumvpn
{
    public partial class App : Application
    {
        private Window? _window;

        private static ThemeService? _themeService;
        public static ThemeService GetThemeService => _themeService!;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();

            var manager = new WindowManager(_window);
            manager.Width = 635;
            manager.Height = 700;
            manager.MinWidth = 500;
            manager.MinHeight = 600;

            _themeService = new ThemeService();
            _themeService
                .ConfigureAutoSave(true)
                .ConfigureBackdrop(BackdropType.AcrylicThin)
                .ConfigureElementTheme(ElementTheme.Light)
                .Initialize(_window);

            _window.Activate();
        }
    }
}