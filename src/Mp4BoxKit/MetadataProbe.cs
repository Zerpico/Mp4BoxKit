using Mp4BoxKit.Reader.Metadata;

namespace Mp4BoxKit;

public sealed class Mp4ProbeOptions
{
    public long MaxMetadataBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Maximum payload of a fragment box read into memory.</summary>
    public long MaxBoxPayloadBytes { get; init; } = 256 * 1024 * 1024;
}

public readonly record struct ContainerProbeResult(
    ProbeStatus Status,
    ContainerInfo? Info,
    long BytesConsumed);

public static class Mp4Metadata
{
    public static async ValueTask<ContainerProbeResult> ProbeAsync(
        Stream input,
        Mp4ProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reader = new Mp4Reader(input, options);
        return await reader.ReadContainerInfoAsync(cancellationToken).ConfigureAwait(false);
    }
}
