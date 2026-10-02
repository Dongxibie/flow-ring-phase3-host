using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace FlowRing.DesktopHost.Host;

/// <summary>
/// WebView2 宿主真实实现：CoreWebView2Environment 创建 + 加载 packages/web/dist/index.html + WebMessage 接收。
/// MVP 阶段只创建 Environment；待 Phase 5 起把 WebView2 控件（WinForms / WPF 二选一）实例化。
/// </summary>
public sealed class WebView2Host : IDisposable
{
    private readonly HostController _controller;
    private readonly ILogger<WebView2Host> _logger;
    private CoreWebView2Environment? _environment;

    public bool IsInitialized { get; private set; }

    public WebView2Host(HostController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<WebView2Host>();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (IsInitialized)
        {
            return;
        }

        var webView2Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRing",
            "WebView2");
        Directory.CreateDirectory(webView2Dir);

        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(webView2Dir);
            IsInitialized = true;
            _logger.LogInformation("WebView2Environment 初始化完成（user data: {Dir}）", webView2Dir);
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            _logger.LogWarning(ex, "WebView2 Runtime 缺失，引导用户下载安装");
            _controller.SetPaused(true);
            return;
        }

        // 触发 WebView2 创建（Phase 5 起当 UI 真正需要显示窗口时创建 window + WebView2）
        // MVP 占位：仅把初始化信息记录下来，不创建真实 WebView2 UI。
        _logger.LogInformation("WebView2 已就绪（待 Phase 5 创建窗口时实例化 WebView2 控件）");
    }

    public async void NavigateTo(string route)
    {
        if (_environment is null)
        {
            _logger.LogWarning("WebView2 未就绪，跳转请求被忽略：{Route}", route);
            return;
        }
        // MVP：仅记录日志；Phase 5 起 WebView2 控件加载完成后调 NavigateTo。
        await Task.Yield();
        _logger.LogInformation("WebView2 收到跳转请求：{Route}", route);
    }

    private static bool IsMissingRuntime(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || (ex.GetType().FullName ?? "").Contains("WebView2RuntimeNotFound", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _environment = null;
        IsInitialized = false;
    }
}