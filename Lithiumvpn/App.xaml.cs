using DevWinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using Windows.Graphics;

namespace Lithiumvpn
{
    public partial class App : Application
    {
        private Window? _window;
        private SplashScreenWindow? _splash;

        private static ThemeService? _themeService;
        public static ThemeService GetThemeService => _themeService!;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _themeService = new ThemeService();

            _splash = new SplashScreenWindow();
            _splash.SplashCompleted += OnSplashCompleted;

            _themeService
                .ConfigureAutoSave(true)
                .ConfigureBackdrop(BackdropType.AcrylicThin)
                .ConfigureElementTheme(ElementTheme.Light)
                .Initialize(_splash);

            // ✅ اندازه و مرکز splash
            SetWindowSizeAndCenter(_splash, 480, 320);

            _splash.Activate();
        }

        private void OnSplashCompleted()
        {
            _window = new MainWindow();

            // ✅ اندازه و مرکز main window
            SetWindowSizeAndCenter(_window, 635, 700);

            _themeService?.Initialize(_window);
            _window.Activate();

            var splashRef = _splash;
            _splash = null;
            splashRef?.Close();
        }

        // ─── Helper: اندازه + مرکز صفحه ─────────────────────────────
        private static void SetWindowSizeAndCenter(Window window, int width, int height)
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                // DPI-aware scaling
                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
                var dpi = GetDpiForWindow(hWnd);
                double scale = dpi / 96.0;

                int scaledW = (int)(width * scale);
                int scaledH = (int)(height * scale);

                // محاسبه مرکز صفحه
                int screenW = displayArea.WorkArea.Width;
                int screenH = displayArea.WorkArea.Height;
                int x = displayArea.WorkArea.X + (screenW - scaledW) / 2;
                int y = displayArea.WorkArea.Y + (screenH - scaledH) / 2;

                appWindow.MoveAndResize(new RectInt32(x, y, scaledW, scaledH));
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hWnd);
    }
}