using System.Buffers.Binary;
using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class IsoBoxReaderTests
{
    [Fact]
    public async Task MetadataProbeAcceptsExtendedSizeBoxes()
    {
        var box = new byte[16 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4), BoxTypes.Ftyp.Value);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(16), new FourCc("isom").Value);

        using var stream = new MemoryStream(box);
        var reader = new Mp4Reader(stream);

        var result = await reader.ReadContainerInfoAsync();

        Assert.Equal(ProbeStatus.EndOfStream, result.Status);
        Assert.Equal(new FourCc("isom"), reader.Info.MajorBrand);
    }

    [Fact]
    public async Task MetadataProbeStopsOnTruncatedBox()
    {
        var box = new byte[10];
        BinaryPrimitives.WriteUInt32BigEndian(box, 20);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4), BoxTypes.Ftyp.Value);

        using var stream = new MemoryStream(box);
        var reader = new Mp4Reader(stream);

        var result = await reader.ReadContainerInfoAsync();

        Assert.Equal(ProbeStatus.EndOfStream, result.Status);
        Assert.Empty(reader.Info.Tracks);
    }
}
