using FlowRing.DesktopBridge.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowRing.DesktopHost.Host;

/// <summary>
/// 全局控制器：负责 paused 状态切换、激活、退出、跳转到 WebView2 页面。
/// 注入到 TrayIcon 与 WebView2Host。
/// </summary>
public sealed class HostController : IDisposable
{
    private readonly ILogger<HostController> _logger;
    private readonly Win32DesktopBridge _bridge;
    private readonly Tray.TrayIcon _tray;
    private readonly WebView2Host _webView2;
    private readonly BridgeServer _pipeServer;
    private volatile bool _isPaused;
    private volatile bool _isActive;

    public HostController(
        ILoggerFactory? loggerFactory = null,
        Tray.TrayIcon? tray = null,
        Win32DesktopBridge? bridge = null,
        WebView2Host? webView2 = null,
        BridgeServer? pipeServer = null)
    {
        var lf = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = lf.CreateLogger<HostController>();
        _bridge = bridge ?? new Win32DesktopBridge(lf);
        _tray = tray ?? new Tray.TrayIcon(this);
        _webView2 = webView2 ?? new WebView2Host(this, lf);
        _pipeServer = pipeServer ?? new BridgeServer(this, lf);
    }

    public bool IsPaused => _isPaused;
    public bool IsActive => _isActive;

    public async Task StartAsync(CancellationToken ct)
    {
        await _bridge.InitializeAsync(ct).ConfigureAwait(false);
        _tray.Initialize();
        await _pipeServer.StartAsync(ct).ConfigureAwait(false);
        await _webView2.InitializeAsync(ct).ConfigureAwait(false);
        _isActive = true;
        _tray.UpdateIcon();
        _logger.LogInformation("Host 启动完成，托盘图标已显示");
    }

    public void NavigateTo(string route)
    {
        _webView2.NavigateTo(route);
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        _logger.LogInformation("Paused 状态切换：{State}", paused ? "已暂停" : "运行中");
        _tray.UpdateIcon();
    }

    public void ExitApp()
    {
        _logger.LogInformation("收到退出请求，关闭 Host");
        Dispose();
        Application.Exit();
    }

    /// <summary>
    /// 接收 WebView2 端的消息（profile list / studio load / 等）。
    /// Phase 5 接 useBridge() 后真正发挥；当前仅打印日志。
    /// </summary>
    public void OnWebMessage(string json)
    {
        _logger.LogInformation("WebView2 → Host message received, length={Len}", json.Length);
    }

    public void Dispose()
    {
        _pipeServer.Dispose();
        _webView2.Dispose();
        _bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _tray.Dispose();
    }
}