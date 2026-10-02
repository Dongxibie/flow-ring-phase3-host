using FlowRing.RingCore.Geometry;

namespace FlowRing.RingCore;

/// <summary>
/// 触发来源类型。MVP 默认仅实现 MouseSideButton；其余为 v1.1 预留。
/// </summary>
[Flags]
public enum TriggerType
{
    None = 0,
    MouseSideButton = 1 << 0,
    MiddleButton = 1 << 1,
    RightButtonLongPress = 1 << 2,
    HotKey = 1 << 3,
}

/// <summary>
/// 修饰键状态（Shift / Ctrl / Alt / Win）。打包为位掩码减少跨进程传输体积。
/// </summary>
[Flags]
public enum ModifierState
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3,
}

/// <summary>
/// 输入适配器归一化后的空间意图事件。RingCore 的唯一输入源。
/// OriginPoint 允许为空（如纯键盘触发）；Pressure 主要服务于未来压感笔。
/// </summary>
public readonly record struct SpatialIntentEvent(
    TriggerType TriggerType,
    RingPoint? OriginPoint,
    float Pressure,
    long TimestampMs,
    ModifierState Modifiers);

/// <summary>
/// 原始鼠标输入事件，专供 UI mousemove 跟踪使用（不进入 RingCore 逻辑）。
/// </summary>
public readonly record struct RawInputEvent(int RawX, int RawY, long TimestampMs);