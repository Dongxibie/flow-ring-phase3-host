using System.Runtime.InteropServices;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// P/Invoke 占位集合。Phase 3 在此添加 SetWindowsHookEx / SendInput /
/// GetForegroundWindow / GetWindowThreadProcessId / RegisterHotKey 等真实签名。
/// MVP 阶段所有方法抛 PlatformNotSupportedException；scaffold 阶段本类用于验证编译路径通畅。
/// </summary>
internal static class NativeMethods
{
    internal const int WH_MOUSE_LL = 14;
    internal const int WH_KEYBOARD_LL = 13;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        internal int X;
        internal int Y;
        internal uint mouseData;
        internal uint flags;
        internal uint time;
        internal nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookExW(int idHook, nint lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint nInputs, nint pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}