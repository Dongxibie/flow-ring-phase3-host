using FlowRing.RingCore.Geometry;

namespace FlowRing.RingCore;

public interface IDirectionResolver
{
    Direction Resolve(RingPoint center, RingPoint current, float deadZoneRadiusPx = 30f);
}

/// <summary>
/// 8 方向 + 30px Dead Zone 默认实现。
/// 距离 &lt; deadZoneRadiusPx 时返回 Direction.Center，触发取消。
/// 否则按 atan2 角所在的 45° 扇区返回 8 方向之一。
/// </summary>
public sealed class DirectionResolver : IDirectionResolver
{
    public Direction Resolve(RingPoint center, RingPoint current, float deadZoneRadiusPx = 30f)
    {
        var dx = current.X - center.X;
        var dy = current.Y - center.Y;
        var distSq = dx * dx + dy * dy;
        var rSq = deadZoneRadiusPx * deadZoneRadiusPx;
        if (distSq < rSq)
        {
            return Direction.Center;
        }

        var angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angleDeg < 0)
        {
            angleDeg += 360.0;
        }

        var sector = (int)Math.Round(angleDeg / 45.0) % 8;
        return sector switch
        {
            0 => Direction.Right,
            1 => Direction.BottomRight,
            2 => Direction.Bottom,
            3 => Direction.BottomLeft,
            4 => Direction.Left,
            5 => Direction.TopLeft,
            6 => Direction.Top,
            7 => Direction.TopRight,
            _ => Direction.Center,
        };
    }
}