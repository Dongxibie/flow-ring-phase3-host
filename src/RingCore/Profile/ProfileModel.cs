namespace FlowRing.RingCore.Profile;

public sealed record ProfileMetadata(
    string Id,
    string Name,
    string Version,
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Checksum);

public sealed record ApplicationContext(
    string ProcessName,
    string WindowTitle,
    nint WindowHandle,
    DateTimeOffset Timestamp);

public sealed record ProfileResolverRule(
    string Kind,
    string Pattern,
    string ProfileId);