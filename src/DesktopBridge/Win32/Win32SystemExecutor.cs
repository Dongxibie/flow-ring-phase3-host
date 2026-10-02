using FlowRing.RingCore.Action;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// 系统 Action 执行器：截图 / 音量 / 窗口控制。
/// payload.op ∈ {"screenshot","volumeUp","volumeDown","volumeMute","minimizeWindow","maximizeWindow","closeWindow"}。
/// MVP 仅做 stub：写入占位结果；真实实现留 Phase 6（多媒体 API + UIA / Win32 交互）。
/// </summary>
public sealed class Win32SystemExecutor : IActionExecutor
{
    private readonly ILogger<Win32SystemExecutor> _logger;

    public Win32SystemExecutor(ILogger<Win32SystemExecutor> logger)
    {
        _logger = logger;
    }

    public ActionKind Kind => ActionKind.System;

    public PermissionTier RequiredTier => PermissionTier.Normal;

    public ValueTask<ExecutionResult> ExecuteAsync(string actionId, ActionContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // MVP stub：解析 actionId "system-{op}"，只承认预定义 op
            var op = actionId.StartsWith("system-", StringComparison.OrdinalIgnoreCase)
                ? actionId.Substring("system-".Length)
                : actionId;

            _logger.LogInformation("System Action 执行：actionId={ActionId} op={Op}", actionId, op);

            switch (op.ToLowerInvariant())
            {
                case "screenshot":
                    CaptureScreenToFile();
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(true, null, sw.ElapsedMilliseconds));

                case "minimizewindow":
                    NativeMethods.ShowWindow(ctx.CurrentApp.WindowHandle, 2 /* SW_MINIMIZE */);
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(true, null, sw.ElapsedMilliseconds));

                case "maximizewindow":
                    NativeMethods.ShowWindow(ctx.CurrentApp.WindowHandle, 3 /* SW_MAXIMIZE */);
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(true, null, sw.ElapsedMilliseconds));

                case "closewindow":
                    NativeMethods.ShowWindow(ctx.CurrentApp.WindowHandle, 0 /* SW_HIDE */);
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(true, null, sw.ElapsedMilliseconds));

                case "volumeup":
                case "volumedown":
                case "volumemute":
                    // Volume 上没有简单的 Win32 API，需要经 CoreAudio（Phase 6 实现）。
                    _logger.LogWarning("System Action {Op} 在 MVP 仅占位，未真正调 CoreAudio", op);
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(true, null, sw.ElapsedMilliseconds));

                default:
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(false, $"未知 system op：{op}", sw.ElapsedMilliseconds));
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ValueTask.FromResult(new ExecutionResult(false, ex.Message, sw.ElapsedMilliseconds));
        }
    }

    /// <summary>
    /// 截屏到 %APPDATA%/FlowRing/screenshots/ 下。
    /// MVP 仅生成空位文件占位（路径 + 时间戳），不做 BitBlt 像素拷贝。
    /// Phase 6 起补 GDI BitBlt + PNG 编码。
    /// </summary>
    private static void CaptureScreenToFile()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowRing", "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"shot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.png");
        // MVP：仅创建占位文件
        File.WriteAllText(path + ".mvp-placeholder", $"FlowRing screenshot MVP placeholder at {DateTimeOffset.UtcNow:O}");
    }
}