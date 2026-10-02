using FlowRing.RingCore;
using FlowRing.RingCore.Geometry;
using Xunit;

namespace FlowRing.RingCore.Tests;

/// <summary>
/// 占位测试，验证脚手架可被 xUnit 编译并发现。
/// 完整覆盖矩阵在 Phase 4（RingCore 实现）补齐；硬门槛 &gt; 90%。
/// </summary>
public sealed class DirectionResolverTests
{
    [Fact]
    public void DeadZoneReturnsCenter()
    {
        var resolver = new DirectionResolver();
        var result = resolver.Resolve(new RingPoint(0f, 0f), new RingPoint(10f, 10f));
        Assert.Equal(Direction.Center, result);
    }

    [Fact]
    public void EastOfCenterReturnsRight()
    {
        var resolver = new DirectionResolver();
        var result = resolver.Resolve(new RingPoint(0f, 0f), new RingPoint(100f, 0f));
        Assert.Equal(Direction.Right, result);
    }
}