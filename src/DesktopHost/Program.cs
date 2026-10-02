using FlowRing.DesktopHost.Host;

namespace FlowRing.DesktopHost;

/// <summary>
/// DesktopHost 入口。MVP 仅初始化系统托盘 + WebView2 + Bridge Pipe。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetCompatibleTextRenderingDefault(false);

        var controller = new HostController();
        using var cts = new CancellationTokenSource();
        Application.ApplicationExit += (_, _) => cts.Cancel();

        var startTask = controller.StartAsync(cts.Token);

        try
        {
            Application.Run();
        }
        finally
        {
            controller.Dispose();
        }

        try
        {
            startTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Host 启动失败：{ex.Message}");
            return 1;
        }
        return 0;
    }
}