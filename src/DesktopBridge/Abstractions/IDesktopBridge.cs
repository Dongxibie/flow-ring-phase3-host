using FlowRing.RingCore;
using FlowRing.RingCore.Action;
using FlowRing.RingCore.Profile;

namespace FlowRing.DesktopBridge.Abstractions;

/// <summary>
/// OS 隔离层根接口。所有平台特定实现都要实现它。
/// </summary>
public interface IDesktopBridge : IAsyncDisposable
{
    IInputAdapter Input { get; }
    IContextDetector Context { get; }
    IReadOnlyDictionary<ActionKind, IActionExecutor> Executors { get; }
    ISystemInfo System { get; }

    Task InitializeAsync(CancellationToken ct);
}

/// <summary>
/// 设备专属输入适配器。Win32 实现走 WH_MOUSE_LL / WH_KEYBOARD_LL Hook。
/// </summary>
public interface IInputAdapter : IAsyncDisposable
{
    event EventHandler<SpatialIntentEvent>? IntentEmitted;
    event EventHandler<RawInputEvent>? RawInputEmitted;

    Task InstallAsync(CancellationToken ct);
    Task UninstallAsync(CancellationToken ct);
}

/// <summary>
/// 上下文检测器。Win32 版本走 GetForegroundWindow。
/// </summary>
public interface IContextDetector
{
    Task<ApplicationContext> DetectAsync(CancellationToken ct);
}

/// <summary>
/// 系统信息。用于 Profile 切换触发。
/// </summary>
public interface ISystemInfo
{
    string Platform { get; }
    string OSVersion { get; }
    string AppVersion { get; }
    string UserDataPath { get; }
}