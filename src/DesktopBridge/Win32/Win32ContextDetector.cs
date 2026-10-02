using System.Diagnostics;
using FlowRing.DesktopBridge.Abstractions;
using FlowRing.RingCore.Profile;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// 走 GetForegroundWindow + GetWindowThreadProcessId + QueryFullProcessImageName 获取前台进程名。
/// 不依赖 UI Automation。
/// </summary>
public sealed class Win32ContextDetector : IContextDetector
{
    private readonly ILogger<Win32ContextDetector> _logger;

    public Win32ContextDetector(ILogger<Win32ContextDetector> logger)
    {
        _logger = logger;
    }

    public Task<ApplicationContext> DetectAsync(CancellationToken ct)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            return Task.FromResult(new ApplicationContext("unknown", string.Empty, nint.Zero, DateTimeOffset.UtcNow));
        }

        // 跳到顶层窗口，避免命中隐藏的 IME 子窗口
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        if (root != nint.Zero)
        {
            hwnd = root;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var processName = TryGetProcessName(pid);
        var windowTitle = TryGetWindowTitle(hwnd);
        var ctx = new ApplicationContext(processName, windowTitle, hwnd, DateTimeOffset.UtcNow);
        return Task.FromResult(ctx);
    }

    private static string TryGetProcessName(uint pid)
    {
        if (pid == 0)
        {
            return "unknown";
        }
        try
        {
            var hProc = NativeMethods.OpenProcess(0x00000400 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (hProc == nint.Zero)
            {
                return "unknown";
            }
            try
            {
                var sb = new System.Text.StringBuilder(256);
                uint size = (uint)sb.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(hProc, 0, sb, ref size))
                {
                    var fullPath = sb.ToString();
                    return Path.GetFileName(fullPath);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }
        catch (Exception)
        {
            // 静默返回 unknown；不允许抛异常污染调用方
        }
        return "unknown";
    }

    private static string TryGetWindowTitle(nint hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        var n = NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }
}