namespace FlowRing.RingCore.FlowCode;

public sealed record FlowCodeOptions(bool Encrypt, string? Passphrase);

public interface IFlowCodeCodec
{
    ValueTask<string> EncodeAsync(string payloadJson, FlowCodeOptions opts, CancellationToken ct);

    ValueTask<string> DecodeAsync(string code, string? passphrase, CancellationToken ct);
}