using System.Runtime.InteropServices;
using Fowan.Windows.AppPorts;
using Fowan.Windows.Platform.Contracts;

namespace Fowan.Windows.Platform.Windows;

internal sealed class WindowsTrayService : ITrayService
{
    private const int GwlWndProc = -4;
    private const uint NotifyIconId = 1;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint MfString = 0;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x10;
    private const uint LoadDefaultSize = 0x40;
    private static readonly IntPtr IdiApplication = new(32512);

    private readonly IntPtr _windowHandle;
    private readonly TrayMessageRouter _messageRouter;
    private readonly TrayCommandRouter _commandRouter = new();
    private readonly Func<string> _restoreLabel;
    private readonly Func<string> _exitLabel;
    private WndProcDelegate? _windowProcedure;
    private IntPtr _originalWindowProcedure;
    private IntPtr _iconHandle;
    private bool _isVisible;
    private bool _ownsIconHandle;
    private bool _disposed;

    public WindowsTrayService(
        IntPtr windowHandle,
        IUiDispatcher dispatcher,
        Func<bool> shouldMinimizeOnClose,
        Func<string> restoreLabel,
        Func<string> exitLabel)
    {
        _windowHandle = windowHandle;
        _restoreLabel = restoreLabel;
        _exitLabel = exitLabel;
        _messageRouter = new TrayMessageRouter(
            dispatcher,
            shouldMinimizeOnClose,
            () => MinimizeRequested?.Invoke(),
            () => RestoreRequested?.Invoke(),
            ShowContextMenu);
    }

    public event Action? MinimizeRequested;
    public event Action? RestoreRequested;
    public event Action? ExitRequested;

    public PlatformOperationResult EnsureVisible()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var hookResult = InstallWindowMessageHook();
        if (!hookResult.Succeeded)
        {
            return hookResult;
        }
        if (_isVisible)
        {
            return PlatformOperationResult.Success();
        }

        ReleaseIconHandle();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fowan.ico");
        if (File.Exists(iconPath))
        {
            _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LoadFromFile | LoadDefaultSize);
            _ownsIconHandle = _iconHandle != IntPtr.Zero;
        }
        else
        {
            _iconHandle = LoadIcon(IntPtr.Zero, IdiApplication);
            _ownsIconHandle = false;
        }

        var data = CreateNotifyIconData();
        _isVisible = Shell_NotifyIcon(NimAdd, ref data);
        if (!_isVisible)
        {
            ReleaseIconHandle();
        }
        return _isVisible
            ? PlatformOperationResult.Success()
            : PlatformOperationResult.Failure("Windows could not create the notification-area icon.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_isVisible)
        {
            var data = CreateNotifyIconData();
            Shell_NotifyIcon(NimDelete, ref data);
            _isVisible = false;
        }

        ReleaseIconHandle();
        if (_originalWindowProcedure != IntPtr.Zero)
        {
            SetWindowLongPtr(_windowHandle, GwlWndProc, _originalWindowProcedure);
            _originalWindowProcedure = IntPtr.Zero;
            _windowProcedure = null;
        }
    }

    private void ReleaseIconHandle()
    {
        if (_iconHandle != IntPtr.Zero && _ownsIconHandle)
        {
            DestroyIcon(_iconHandle);
        }

        _iconHandle = IntPtr.Zero;
        _ownsIconHandle = false;
    }

    private PlatformOperationResult InstallWindowMessageHook()
    {
        if (_originalWindowProcedure != IntPtr.Zero)
        {
            return PlatformOperationResult.Success();
        }

        _windowProcedure = WindowProc;
        _originalWindowProcedure = SetWindowLongPtr(
            _windowHandle,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_windowProcedure));
        if (_originalWindowProcedure == IntPtr.Zero)
        {
            _windowProcedure = null;
            return PlatformOperationResult.Failure("Windows could not install the tray message hook.");
        }

        return PlatformOperationResult.Success();
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (_messageRouter.Route(message, lParam) == TrayMessageDisposition.Handled)
        {
            return IntPtr.Zero;
        }

        return CallWindowProc(_originalWindowProcedure, hwnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, new UIntPtr(TrayCommandRouter.RestoreCommandId), _restoreLabel());
            AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenu(menu, MfString, new UIntPtr(TrayCommandRouter.ExitCommandId), _exitLabel());
            if (!GetCursorPos(out var point))
            {
                return;
            }

            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(
                menu,
                TpmReturnCmd | TpmRightButton | TpmNonotify,
                point.X,
                point.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            switch (_commandRouter.Route(command))
            {
                case TrayCommand.Restore:
                    RestoreRequested?.Invoke();
                    break;
                case TrayCommand.Exit:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _windowHandle,
        uID = NotifyIconId,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = TrayMessageRouter.TrayIconMessage,
        hIcon = _iconHandle,
        szTip = "Fowan"
    };

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) => IntPtr.Size == 8
        ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
        : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previousWindowProcedure, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
