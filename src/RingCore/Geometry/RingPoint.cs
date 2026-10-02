namespace FlowRing.RingCore.Geometry;

/// <summary>
/// RingCore 内部坐标点。OS 无关，单位由调用方决定（默认逻辑像素）。
/// 故意不复用 System.Drawing.PointF，避免依赖 System.Drawing.Common。
/// </summary>
public readonly record struct RingPoint(float X, float Y)
{
    public static RingPoint Zero => new(0f, 0f);
}