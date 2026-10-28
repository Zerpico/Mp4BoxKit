namespace Mp4BoxKit.Reader.Classic;

internal sealed class ClassicSampleDataReader(Stream stream)
{
    public async ValueTask<byte[]> ReadAsync(
        ClassicIndexedSample sample, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sample.Offset < 0 || sample.Size < 0 || !stream.CanSeek ||
            sample.Offset > stream.Length - sample.Size)
        {
            throw new Mp4FormatException("Classic MP4 sample points outside the file.");
        }

        var data = new byte[sample.Size];
        stream.Position = sample.Offset;
        await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        return data;
    }
}
