using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class EmptyEditTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeadingEmptyEditDelaysPresentation(bool fragmented)
    {
        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddAudio(new AudioTrackOptions(Codec.Aac, 1, 48000,
                CodecConfiguration: TestMedia.Esds,
                Timeline: new TrackTimeline(-1024)));
            writer.WriteSample(track, new byte[] { 1, 2, 3 }, 0, -1024);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var elst = bytes.AsSpan().IndexOf("elst"u8);
        var insertion = elst + 12;
        var entry = new byte[20];
        BinaryPrimitives.WriteUInt64BigEndian(entry, 5000); // movie ticks, timescale 1000
        BinaryPrimitives.WriteUInt64BigEndian(entry.AsSpan(8), ulong.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(16), 0x10000);
        foreach (var name in new[] { "moov", "trak", "edts", "elst" })
        {
            var offset = bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(name)) - 4;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset),
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) + 20);
        }
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(elst + 8), 2);
        var edited = bytes.Take(insertion).Concat(entry).Concat(bytes.Skip(insertion)).ToArray();
        using var input = new MemoryStream(edited);
        var reader = new Mp4Reader(input);
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(240000, sample.Pts);
        Assert.Equal(240000 - 1024, sample.Dts);
        Assert.Equal(new byte[] { 1, 2, 3 }, sample.Data.ToArray());
    }
}
