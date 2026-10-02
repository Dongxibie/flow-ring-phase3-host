using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowRing.DesktopHost.Host;

/// <summary>
/// Named Pipe 服务端：接收 WebView2 端或 Host 内部的高频消息（60fps mousemove）。
/// 低频消息走 WebMessage（见 WebView2Host）；高频消息走 Named Pipe 避免阻塞 UI 线程。
/// Pipe 名：\\.\pipe\FlowRing.{pid}
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private const int MaxConcurrentClients = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HostController _controller;
    private readonly ILogger<BridgeServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public static string PipeName => $"FlowRing.{Environment.ProcessId}";

    public bool IsRunning { get; private set; }

    public BridgeServer(HostController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<BridgeServer>();
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }
        IsRunning = true;
        _acceptLoop = Task.Run(AcceptLoopAsync, _cts.Token);
        _logger.LogInformation("BridgeServer 已启动，pipe = \\\\.\\pipe\\{Pipe}", PipeName);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    MaxConcurrentClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _logger.LogDebug("Named Pipe 客户端已连接");

                // 单连接单次接收（MVP 简化）
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is not null)
                {
                    _controller.OnWebMessage(line);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Named Pipe 异常");
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        try
        {
            _acceptLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 忽略
        }
        IsRunning = false;
    }
}