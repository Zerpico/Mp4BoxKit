using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class BoxConformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VideoMediaHeaderHasSpecSizeAndFields(bool fragmented)
    {
        var bytes = CreateInitialization(fragmented);
        var box = Find(bytes, BoxTypes.Vmhd);
        Assert.Equal(20, box.Size);
        var payload = Payload(bytes, box);
        Assert.Equal(12, payload.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(payload)); // version 0, flags 1
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload[4..])); // graphicsmode
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload[6..])); // opcolor red
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload[8..])); // opcolor green
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload[10..])); // opcolor blue
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AudioMediaHeaderHasSpecSizeAndFields(bool fragmented)
    {
        var bytes = CreateInitialization(fragmented);
        var box = Find(bytes, BoxTypes.Smhd);
        Assert.Equal(16, box.Size);
        var payload = Payload(bytes, box);
        Assert.Equal(8, payload.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(payload)); // version 0, flags 0
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload[4..])); // balance
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(payload[6..])); // reserved
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubtitleMediaHeaderIsNmhd(bool fragmented)
    {
        var bytes = CreateInitialization(fragmented);
        var box = Find(bytes, BoxTypes.Nmhd);
        Assert.Equal(12, box.Size);
        var payload = Payload(bytes, box);
        Assert.Equal(4, payload.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(payload));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataInformationContainsSelfContainedDref(bool fragmented)
    {
        var bytes = CreateInitialization(fragmented);

        var dinf = Find(bytes, BoxTypes.Dinf);
        Assert.Equal(36, dinf.Size);

        var dref = Find(bytes, BoxTypes.Dref);
        Assert.Equal(28, dref.Size);
        var drefPayload = Payload(bytes, dref);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(drefPayload[4..]));

        var url = Find(bytes, BoxTypes.Url);
        Assert.Equal(12, url.Size);
        var urlPayload = Payload(bytes, url);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(urlPayload));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovieHeaderNextTrackIdIsTrackCountPlusOne(bool fragmented)
    {
        var bytes = CreateInitialization(fragmented);
        var mvhd = Find(bytes, BoxTypes.Mvhd);
        var payload = Payload(bytes, mvhd);
        var nextTrackId = BinaryPrimitives.ReadUInt32BigEndian(payload[^4..]);
        Assert.Equal(4u, nextTrackId);
    }

    [Fact]
    public void TrunVersionIsZeroWhenCompositionOffsetsAreNonNegative()
    {
        var bytes = CreateMediaFragment(pts: 10, dts: 5);
        var trun = Find(bytes, BoxTypes.Trun);
        Assert.Equal(0, bytes[trun.Offset + 8]);
    }

    [Fact]
    public async Task TrunVersionIsOneWhenCompositionOffsetIsNegative()
    {
        var bytes = CreateMediaFragment(pts: 5, dts: 10);
        var trun = Find(bytes, BoxTypes.Trun);
        Assert.Equal(1, bytes[trun.Offset + 8]);

        using var input = new MemoryStream(bytes);
        var sample = await new Mp4Reader(input).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(5, sample.Pts);
        Assert.Equal(10, sample.Dts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileTypeContainsIso5ForDefaultBaseIsMoof(bool fragmented)
    {
        var bytes = CreateInitialization(fragmented);
        var ftyp = Find(bytes, BoxTypes.Ftyp);
        var payload = Payload(bytes, ftyp);
        var brands = new List<string>();
        for (var offset = 8; offset + 4 <= payload.Length; offset += 4)
            brands.Add(Encoding.ASCII.GetString(payload.Slice(offset, 4)));

        Assert.Contains("isom", brands);
        if (fragmented)
        {
            Assert.Contains("iso6", brands);
            Assert.Contains("iso5", brands);
        }
    }

    private static byte[] CreateInitialization(bool fragmented)
    {
        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output)))
        {
            writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 160, 96, 90_000, TestMedia.AvcC));
            writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 2, 48_000, CodecConfiguration: TestMedia.Esds));
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));

            if (fragmented)
                _ = ((Mp4FragmentedWriter)writer).InitializationSegment;
            else
                writer.Complete();
        }

        return output.ToArray();
    }

    private static byte[] CreateMediaFragment(long pts, long dts)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, pts, dts, duration: 1000);
            writer.Complete();
        }

        return output.ToArray();
    }

    private readonly record struct BoxRef(FourCc Type, int Offset, int Size);

    private static readonly HashSet<FourCc> ContainerTypes = new()
    {
        BoxTypes.Moov, BoxTypes.Trak, BoxTypes.Mdia, BoxTypes.Minf,
        BoxTypes.Dinf, BoxTypes.Dref, BoxTypes.Moof, BoxTypes.Traf
    };

    private static BoxRef Find(byte[] data, FourCc wanted)
    {
        var found = Find(data, 0, data.Length, wanted);
        Assert.True(found.HasValue, $"Box {wanted} was not found.");
        return found.Value;
    }

    private static BoxRef? Find(byte[] data, int offset, int length, FourCc wanted)
    {
        foreach (var box in ReadChildren(data, offset, length))
        {
            if (box.Type == wanted)
                return box;

            if (ContainerTypes.Contains(box.Type))
            {
                var childOffset = box.Offset + 8;
                var childLength = box.Size - 8;
                // dref is a FullBox with version/flags and entry_count before its children.
                if (box.Type == BoxTypes.Dref)
                {
                    childOffset += 8;
                    childLength -= 8;
                }

                var found = Find(data, childOffset, childLength, wanted);
                if (found.HasValue)
                    return found;
            }
        }

        return null;
    }

    private static List<BoxRef> ReadChildren(byte[] data, int offset, int length)
    {
        var result = new List<BoxRef>();
        var end = checked(offset + length);
        while (offset + 8 <= end)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            if (size < 8 || size > (uint)(end - offset))
                break;
            result.Add(new BoxRef(new FourCc(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4))),
                offset, checked((int)size)));
            offset += checked((int)size);
        }

        return result;
    }

    private static ReadOnlySpan<byte> Payload(byte[] data, BoxRef box)
        => data.AsSpan(box.Offset + 8, box.Size - 8);
}
