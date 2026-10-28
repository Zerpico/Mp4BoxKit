using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class SegmentIndexTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackIndexCanBeConcatenatedIntoSeekableFile(bool withOutput)
    {
        using var output = new MemoryStream();
        using var assembled = new MemoryStream();
        var kinds = new List<Fmp4SegmentKind>();
        void Deliver(Fmp4Segment segment)
        {
            kinds.Add(segment.Kind);
            assembled.Write(segment.Data.Span);
        }
        var options = new Mp4FragmentedWriterOptions
        { WriteRandomAccessIndex = true, WriteSegmentIndex = true };
        using (var writer = withOutput
            ? new Mp4FragmentedWriter(new StreamMp4Output(output), options, Deliver)
            : new Mp4FragmentedWriter(options, Deliver))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            for (var i = 0; i < 3; i++)
            {
                writer.WriteSample(track, new byte[] { 0, 0 }, i * 1000, i * 1000, duration: 1000);
                writer.FlushFragment();
            }
            writer.Complete();
            writer.Complete();
        }
        Assert.Equal(new[] { Fmp4SegmentKind.Initialization, Fmp4SegmentKind.Media,
            Fmp4SegmentKind.Media, Fmp4SegmentKind.Media, Fmp4SegmentKind.Index }, kinds);
        if (withOutput)
            Assert.Equal(output.ToArray(), assembled.ToArray());
        assembled.Position = 0;
        var reader = new Mp4Reader(assembled);
        await reader.SeekAsync(TimeSpan.FromSeconds(2.5));
        Assert.Equal(2000, (await reader.ReadSampleAsync())!.Dts);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task IndexedSeekSupportsForwardAndBackwardPositions(bool sidx, bool mfra)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { WriteSegmentIndex = sidx, WriteRandomAccessIndex = mfra }))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            for (var i = 0; i < 5; i++)
            {
                writer.WriteSample(track, new byte[] { 0, 1, (byte)('a' + i) }, i * 1000, i * 1000, duration: 1000);
                writer.FlushFragment();
            }
            writer.Complete();
        }
        output.Position = 0;
        var reader = new Mp4Reader(output);
        foreach (var second in new[] { 3, 1, 4, 0 })
        {
            await reader.SeekAsync(TimeSpan.FromSeconds(second + 0.5), new TrackId(1));
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(second * 1000, sample.Dts);
        }
    }

    [Fact]
    public async Task IndexedSegmentHasExactReferenceAndRemainsReadable()
    {
        var segments = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { WriteSegmentIndex = true }, segments.Add))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 0 }, 100, 100, duration: 700);
            writer.Complete();
        }
        var data = segments[^1].Data.ToArray();
        Assert.Equal("styp", System.Text.Encoding.ASCII.GetString(data, 4, 4));
        var sidx = data.AsSpan().IndexOf("sidx"u8) - 4;
        Assert.True(sidx >= 0);
        Assert.Equal((uint)(data.Length - sidx - 52),
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(sidx + 40)));
        Assert.Equal(700u,
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(sidx + 44)));
        output.Position = 0;
        var sample = await new Mp4Reader(output).ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(100, sample.Pts);
    }
}
