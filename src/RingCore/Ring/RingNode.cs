using FlowRing.RingCore.Geometry;

namespace FlowRing.RingCore.Ring;

public abstract class RingSlot
{
    public required Direction Direction { get; init; }
}

public sealed class ActionSlot : RingSlot
{
    public required string ActionRef { get; init; }
}

public sealed class ChildRingSlot : RingSlot
{
    public required string ChildRingId { get; init; }
}

public sealed class RingNode
{
    public required string Id { get; init; }
    public required string ProfileId { get; init; }
    public Dictionary<Direction, RingSlot> Slots { get; init; } = new();
    public string? ParentRingId { get; init; }
}