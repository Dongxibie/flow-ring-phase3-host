namespace FlowRing.RingCore;

public enum InputState
{
    Idle,
    Pressed,
    HoldDetected,
    RingOpening,
    Selecting,
    Executing,
    Closing,
}

/// <summary>
/// 状态机骨架。MVP 仅做状态切换 + 当前态查询；
/// 完整的事件 → 状态迁移合法性校验留到 Phase 4（InputStateMachine 实现）。
/// </summary>
public sealed class InputStateMachine
{
    public InputState Current { get; private set; } = InputState.Idle;

    public void Transition(InputState target) => Current = target;

    public void Reset() => Current = InputState.Idle;
}