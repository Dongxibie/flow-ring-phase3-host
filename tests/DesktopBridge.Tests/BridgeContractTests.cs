using FlowRing.DesktopBridge.Abstractions;
using Xunit;

namespace FlowRing.DesktopBridge.Tests;

/// <summary>
/// 占位测试，验证脚手架可被 xUnit 编译并发现。
/// 真实 Win32 集成测试在 Phase 3 起补齐。
/// </summary>
public sealed class BridgeContractTests
{
    [Fact]
    public void ExceptionMessageHintsInitialization()
    {
        var ex = new BridgeNotInitializedException();
        Assert.Contains("InitializeAsync", ex.Message);
    }
}