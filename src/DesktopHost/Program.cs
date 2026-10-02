using FlowRing.DesktopHost.Tray;

namespace FlowRing.DesktopHost;

/// <summary>
/// DesktopHost 入口。MVP 仅初始化系统托盘并阻塞主线程；
/// Phase 3 起补充：WebView2 宿主 + Named Pipe 服务端 + 协议路由 + 启动引导对话框。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetCompatibleTextRenderingDefault(false);

        using var tray = new TrayIcon();
        tray.Initialize();

        Application.Run();
        return 0;
    }
}