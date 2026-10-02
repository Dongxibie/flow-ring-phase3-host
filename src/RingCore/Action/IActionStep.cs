using FlowRing.RingCore.Profile;

namespace FlowRing.RingCore.Action;

public enum ActionKind
{
    Keyboard,
    System,
    Application,
    AI,
    Workflow,
}

public enum PermissionTier
{
    Safe,
    Normal,
    Dangerous,
}

public sealed record ActionContext(
    string ProfileId,
    ApplicationContext CurrentApp,
    long StartTimestampMs);

public sealed record ExecutionResult(
    bool Success,
    string? Error,
    long DurationMs);

public interface IActionStep
{
    string Id { get; }
    ActionContext Context { get; init; }
    ValueTask<ExecutionResult> ExecuteAsync(CancellationToken ct);
}

public interface IActionExecutor
{
    ActionKind Kind { get; }
    PermissionTier RequiredTier { get; }
    ValueTask<ExecutionResult> ExecuteAsync(string actionId, ActionContext ctx, CancellationToken ct);
}