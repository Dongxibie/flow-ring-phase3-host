namespace FlowRing.DesktopHost.Host;

/// <summary>
/// WebView2 宿主占位。MVP 不实例化；
/// Phase 3 起：创建 CoreWebView2Environment、订阅 WebMessageReceived、加载 packages/web/dist/index.html。
/// </summary>
public sealed class WebView2Host : IDisposable
{
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        IsInitialized = true;
    }

    public void Dispose()
    {
        IsInitialized = false;
    }
}