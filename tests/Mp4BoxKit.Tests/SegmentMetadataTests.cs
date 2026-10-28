using System.Text;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class SegmentMetadataTests
{
    [Fact]
    public void MediaSegmentsCarryOffsetsAndTiming()
    {
        var segments = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual,
                WriteRandomAccessIndex = true
            }, segments.Add))
        {
            var video = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            var audio = writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 1, 48_000, CodecConfiguration: TestMedia.Esds));

            writer.WriteSample(video, TestMedia.VideoSample(1), 0, 0,
                SampleFlags.KeyFrame, duration: 90_000);
            writer.WriteSample(audio, new byte[] { 1, 2, 3 }, 0, 0, duration: 48_000);
            writer.FlushFragment();

            writer.WriteSample(video, TestMedia.VideoSample(2), 90_000, 90_000,
                SampleFlags.KeyFrame, duration: 90_000);
            writer.WriteSample(audio, new byte[] { 4, 5, 6 }, 48_000, 48_000,
                duration: 48_000);
            writer.Complete();
        }

        var init = segments[0];
        var media = segments.Where(segment => segment.Kind == Fmp4SegmentKind.Media).ToArray();
        var index = Assert.Single(segments, segment => segment.Kind == Fmp4SegmentKind.Index);

        Assert.Equal(0, init.Offset);
        Assert.Equal(init.Data.Length, media[0].Offset);
        Assert.Equal(media[0].Offset + media[0].Data.Length, media[1].Offset);
        Assert.Equal(media[1].Offset + media[1].Data.Length, index.Offset);

        Assert.Equal(new TrackId(1), media[0].ReferenceTrack);
        Assert.Equal(90_000u, media[0].TimeScale);
        Assert.Equal(0, media[0].EarliestPresentationTime);
        Assert.Equal(90_000, media[0].Duration);
        Assert.True(media[0].StartsWithKeyFrame);
        Assert.Equal(2, media[0].SampleCount);
        Assert.Equal(90_000, media[1].EarliestPresentationTime);
        Assert.Equal(90_000, media[1].Duration);

        Assert.Equal("styp", Encoding.ASCII.GetString(media[0].Data.Span[4..8]));
        Assert.Contains("msdh", Encoding.ASCII.GetString(media[0].Data.Span));
    }

    [Fact]
    public void SegmentOffsetsStartAtOutputPosition()
    {
        var segments = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        output.Write(new byte[100]);
        output.Position = 100;

        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { FragmentBoundary = Fmp4FragmentBoundary.Manual },
            segments.Add))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            writer.FlushFragment();
            writer.Complete();
        }

        var init = segments[0];
        var media = Assert.Single(segments, segment => segment.Kind == Fmp4SegmentKind.Media);
        Assert.Equal(100, init.Offset);
        Assert.Equal(100 + init.Data.Length, media.Offset);
    }

    [Fact]
    public void SegmentTypeCanBeDisabled()
    {
        var segments = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual,
                WriteSegmentIndex = true,
                WriteSegmentType = false
            }, segments.Add))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            writer.FlushFragment();
            writer.Complete();
        }

        var media = Assert.Single(segments, segment => segment.Kind == Fmp4SegmentKind.Media);
        Assert.Equal("sidx", Encoding.ASCII.GetString(media.Data.Span[4..8]));
    }
}
