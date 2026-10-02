namespace FlowRing.RingCore.Geometry;

/// <summary>
/// 8 方向枚举。Center 表示在 Dead Zone 内，本次触发取消。
/// 数值固定以保证 JSON 序列化与跨进程消息兼容。
/// </summary>
public enum Direction
{
    Center = 0,
    Top = 1,
    TopRight = 2,
    Right = 3,
    BottomRight = 4,
    Bottom = 5,
    BottomLeft = 6,
    Left = 7,
    TopLeft = 8,
}