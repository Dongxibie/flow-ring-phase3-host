using FlowRing.DesktopBridge.Abstractions;
using FlowRing.RingCore.Action;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// Win32 IDesktopBridge 实现。组合所有 OS 相关子模块。
/// </summary>
public sealed class Win32DesktopBridge : IDesktopBridge
{
    private readonly ILogger<Win32DesktopBridge> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<ActionKind, IActionExecutor> _executors;
    private bool _initialized;

    public Win32DesktopBridge(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<Win32DesktopBridge>();
        Input = new MouseInputAdapter(_loggerFactory.CreateLogger<MouseInputAdapter>());
        Context = new Win32ContextDetector(_loggerFactory.CreateLogger<Win32ContextDetector>());
        System = new Win32SystemInfo();

        _executors = new Dictionary<ActionKind, IActionExecutor>
        {
            [ActionKind.Keyboard] = new Win32KeyboardExecutor(_loggerFactory.CreateLogger<Win32KeyboardExecutor>()),
            [ActionKind.System] = new Win32SystemExecutor(_loggerFactory.CreateLogger<Win32SystemExecutor>()),
            // ActionKind.Application / AI / Workflow 在 MVP 暂未注册；Phase 6 补
        };
    }

    public IInputAdapter Input { get; }

    public IContextDetector Context { get; }

    public IReadOnlyDictionary<ActionKind, IActionExecutor> Executors => _executors;

    public ISystemInfo System { get; }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }
        await Input.InstallAsync(ct).ConfigureAwait(false);
        _initialized = true;
        _logger.LogInformation("Win32DesktopBridge 初始化完成");
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            await Input.DisposeAsync().ConfigureAwait(false);
            _initialized = false;
        }
    }
}