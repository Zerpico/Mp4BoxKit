namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicIndexLoader(
    Stream stream, ContainerInput input, ClassicSampleIndexBuilder builder,
    Mp4ProbeOptions options)
{
    public async ValueTask<ClassicReaderState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
            throw new NotSupportedException("Classic MP4 sample reading requires a seekable stream.");
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            while (true)
            {
                var header = await input.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
                if (header is null)
                    throw new Mp4FormatException("Classic MP4 does not contain moov.");
                if (header.Value.Type == BoxTypes.Moov)
                {
                    if (header.Value.PayloadSize > (ulong)options.MaxMetadataBytes)
                        throw new Mp4FormatException("Metadata exceeds configured limit.");
                    var payload = await input.ReadPayloadAsync(header.Value.PayloadSize,
                        cancellationToken).ConfigureAwait(false);
                    return builder.Build(payload);
                }
                await input.SkipAsync(header.Value.PayloadSize, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            stream.Position = position;
        }
    }
}
