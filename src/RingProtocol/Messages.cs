using System.Text.Json.Serialization;
using FlowRing.RingCore.Geometry;

namespace FlowRing.RingProtocol;

/// <summary>
/// C# ↔ TS 协议消息统一基类。所有跨进程消息都要实现 IRabbitEnvelopes。
/// Schema 版本号用于不兼容协议时直接拒绝（Phase 3 起补版本协商）。
/// </summary>
public interface IProtocolMessage
{
    string Type { get; }
}

public sealed record ProtocolEnvelope<T>(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("payload")] T Payload) : IProtocolMessage where T : class;

// C# → TS
public sealed record RingOpenPayload(
    RingPoint OriginPoint,
    string RingTree,
    float DeadZonePx,
    string ActiveProfileId);

public sealed record RingHighlightPayload(string Direction);
public sealed record RingClosePayload(string Reason);
public sealed record ProfileListEntry(string Id, string Name, bool IsDefault);
public sealed record ProfileListPayload(IReadOnlyList<ProfileListEntry> Profiles);
public sealed record RingStudioLoadPayload(string ProfileId, string ProfileJson, string RingGraphJson, IReadOnlyList<string> ActionLibrary);

// TS → C#
public sealed record RingDirectionLockPayload(string Direction);
public sealed record RingTriggerPayload(string TriggerType, int Modifiers);
public sealed record ActionPreviewPayload(string ActionId);
public sealed record StudioSavePayload(string ProfileId, string ProfileJson, string RingGraphJson);
public sealed record FlowCodeExportPayload(string ProfileId, bool Encrypt);
public sealed record FlowCodeImportPayload(string Code, string? Passphrase);

public static class ProtocolVersion
{
    public const int Current = 1;
    public const int MinSupported = 1;
}