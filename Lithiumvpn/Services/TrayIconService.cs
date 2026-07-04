using System;
using System.Runtime.InteropServices;
using Lithiumvpn.Localization;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Windows notification-area (system tray) icon for the app, implemented with
    /// raw Shell_NotifyIcon P/Invoke — no extra packages.
    ///
    /// Behaviour mirrors v2rayN: closing the main window hides it to the tray;
    /// the tray icon offers Open / Disconnect / Exit on right-click and restores
    /// the window on double-click. Must be created on the UI thread (its hidden
    /// message window relies on that thread's message loop).
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private const uint WM_TRAY = 0x8000 + 1;         // WM_APP + 1
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_CONTEXTMENU = 0x007B;

        private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
        private const uint NIM_ADD = 0x0, NIM_DELETE = 0x2;

        private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
        private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;

        private const int CMD_OPEN = 1, CMD_DISCONNECT = 2, CMD_EXIT = 3;

        public event Action? OpenRequested;
        public event Action? DisconnectRequested;
        public event Action? ExitRequested;

        private readonly WndProcDelegate _wndProc;   // rooted so the GC can't collect the thunk
        private IntPtr _hwnd;
        private IntPtr _hIcon;
        private bool _added;

        public TrayIconService(string iconPath, string tooltip)
        {
            _wndProc = WndProc;

            var hInstance = GetModuleHandleW(null);
            var wc = new WNDCLASSW
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = "LithiumvpnTrayWindow"
            };
            RegisterClassW(ref wc);

            // Hidden message-only window that receives the tray callbacks.
            _hwnd = CreateWindowExW(0, wc.lpszClassName, string.Empty, 0,
                0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create the tray message window.");

            _hIcon = LoadImageW(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */,
                0, 0, 0x10 /* LR_LOADFROMFILE */ | 0x40 /* LR_DEFAULTSIZE */);

            var data = new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAY,
                hIcon = _hIcon,
                szTip = tooltip
            };
            _added = Shell_NotifyIconW(NIM_ADD, ref data);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAY)
            {
                switch ((uint)(lParam.ToInt64() & 0xFFFF))
                {
                    // Single left-click restores the window (v2rayN-style);
                    // the double-click case only fires after a click already
                    // opened it, so handling both is harmless.
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        OpenRequested?.Invoke();
                        break;
                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:
                        ShowMenu();
                        break;
                }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void ShowMenu()
        {
            var loc = LocalizationManager.Instance;
            var menu = CreatePopupMenu();
            try
            {
                AppendMenuW(menu, MF_STRING, CMD_OPEN, loc.Get("Tray_Open"));
                AppendMenuW(menu, MF_STRING, CMD_DISCONNECT, loc.Get("Tray_Disconnect"));
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
                AppendMenuW(menu, MF_STRING, CMD_EXIT, loc.Get("Tray_Exit"));

                GetCursorPos(out var pt);
                // Required so the menu closes when the user clicks elsewhere.
                SetForegroundWindow(_hwnd);

                int cmd = TrackPopupMenuEx(menu,
                    TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                    pt.X, pt.Y, _hwnd, IntPtr.Zero);

                switch (cmd)
                {
                    case CMD_OPEN: OpenRequested?.Invoke(); break;
                    case CMD_DISCONNECT: DisconnectRequested?.Invoke(); break;
                    case CMD_EXIT: ExitRequested?.Invoke(); break;
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        public void Dispose()
        {
            if (_added)
            {
                var data = new NOTIFYICONDATAW
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = _hwnd,
                    uID = 1
                };
                Shell_NotifyIconW(NIM_DELETE, ref data);
                _added = false;
            }
            if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }

        // ─── P/Invoke ────────────────────────────────────────────────
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSW
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName,
            string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type,
            int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y,
            IntPtr hWnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
