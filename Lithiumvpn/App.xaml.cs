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
        private Services.TrayIconService? _tray;
        private bool _exitRequested;

        private static ThemeService? _themeService;
        public static ThemeService GetThemeService => _themeService!;

        /// <summary>The single main window — used for app-wide visual transitions (e.g. language change).</summary>
        public static MainWindow? RootWindow { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _themeService = new ThemeService();

            // Undo anything a crashed previous session left behind (orphaned xray.exe,
            // system proxy still pointing at a dead local port).
            Services.Xray.ConnectionService.CleanupFromPreviousRun();

            // Create single main window and start with pages inside its Frame (SplashPage -> DashboardPage)
            _window = new MainWindow();

            // Tear the tunnel down (and restore the system proxy) when the app closes.
            _window.Closed += (_, _) =>
            {
                _tray?.Dispose();
                Services.Xray.ConnectionService.Instance.ShutdownBlocking();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                Services.Xray.ConnectionService.Instance.ShutdownBlocking();

            SetupTrayIcon(_window);
            RootWindow = (MainWindow)_window;

            _themeService
                .ConfigureAutoSave(true)
                .ConfigureBackdrop(BackdropType.AcrylicThin)
                .ConfigureElementTheme(ElementTheme.Light)
                .Initialize(_window);

            // اندازه و مرکز main window (splash page will be inside this window and share same size)
            SetWindowSizeAndCenter(_window, 635, 650);

            _window.Activate();
        }

        // ─── System tray: close hides to tray; Exit really quits ────
        private void SetupTrayIcon(Window window)
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                // Intercept the standard close button: hide to the tray instead of
                // exiting — unless Exit was chosen from the tray menu.
                appWindow.Closing += (s, e) =>
                {
                    if (!_exitRequested)
                    {
                        e.Cancel = true;
                        s.Hide();
                    }
                };

                var iconPath = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
                _tray = new Services.TrayIconService(iconPath, "LithiumVPN");

                _tray.OpenRequested += () => window.DispatcherQueue.TryEnqueue(() =>
                {
                    appWindow.Show();
                    window.Activate();
                });

                _tray.DisconnectRequested += () =>
                    _ = Services.Xray.ConnectionService.Instance.DisconnectAsync();

                _tray.ExitRequested += () => window.DispatcherQueue.TryEnqueue(() =>
                {
                    _exitRequested = true;
                    window.Close();   // Closed handler disposes the tray + tears the tunnel down
                });
            }
            catch
            {
                // If tray setup fails for any reason, fall back to normal window closing.
                _exitRequested = true;
            }
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