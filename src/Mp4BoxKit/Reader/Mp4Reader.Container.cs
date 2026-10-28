using Mp4BoxKit.Reader.Metadata;

namespace Mp4BoxKit;

public sealed partial class Mp4Reader
{
    public async ValueTask<ContainerProbeResult> ReadContainerInfoAsync(
        CancellationToken cancellationToken = default)
    {
        while (!_seenMoov)
        {
            var result = await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (result is null)
                return new(ProbeStatus.EndOfStream, _info, _input.Position);
            var header = result.Value;
            if (header.Size < (ulong)header.HeaderSize)
                return new(ProbeStatus.InvalidFormat, null, _input.Position);
            if (header.Type == BoxTypes.Ftyp)
            {
                await ReadFtypAsync(header, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (header.Type == BoxTypes.Moov)
            {
                await ReadMoovAsync(header, cancellationToken).ConfigureAwait(false);
                _seenMoov = true;
                _info.IsFragmented = ContainsMvex;
                continue;
            }
            await SkipAsync(header.PayloadSize, cancellationToken).ConfigureAwait(false);
        }
        return new(ProbeStatus.Complete, _info, _input.Position);
    }

    private bool ContainsMvex { get; set; }

    private ValueTask<BoxHeader?> ReadHeaderAsync(CancellationToken cancellationToken)
        => _containerInput.ReadHeaderAsync(cancellationToken);

    private ValueTask<byte[]> ReadPayloadAsync(ulong size, CancellationToken cancellationToken)
        => _containerInput.ReadPayloadAsync(size, cancellationToken);

    private ValueTask SkipAsync(ulong bytes, CancellationToken cancellationToken)
        => _containerInput.SkipAsync(bytes, cancellationToken);

    private async ValueTask ReadFtypAsync(BoxHeader header, CancellationToken cancellationToken)
    {
        var length = (int)Math.Min(header.PayloadSize, 64 * 1024);
        var bytes = new byte[length];
        if (await _input.ReadAtLeastAsync(bytes, bytes.Length, cancellationToken)
                .ConfigureAwait(false) != bytes.Length)
            return;
        if (bytes.Length >= 4)
            _info.MajorBrand = new FourCc(BigEndian.U32(bytes));
        if (header.PayloadSize > (ulong)bytes.Length)
            await SkipAsync(header.PayloadSize - (ulong)bytes.Length, cancellationToken)
                .ConfigureAwait(false);
    }

    private async ValueTask ReadMoovAsync(BoxHeader header, CancellationToken cancellationToken)
    {
        if (header.PayloadSize > (ulong)_options.MaxMetadataBytes)
            throw new Mp4FormatException("Metadata exceeds configured limit.");
        var bytes = new byte[(int)header.PayloadSize];
        if (await _input.ReadAtLeastAsync(bytes, bytes.Length, cancellationToken)
                .ConfigureAwait(false) != bytes.Length)
            return;
        ContainsMvex = Mp4MetadataParser.Parse(bytes, _info.Tracks);
        _fragmentParser.ReadMovieDefaults(bytes);
    }
}
