using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentBoundaryTests
{
    [Fact]
    public void AudioOnlyStreamFlushesAtNonVideoDuration()
    {
        var media = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions(),
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); }))
        {
            var track = writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 1, 48_000, CodecConfiguration: TestMedia.Esds));
            for (var i = 0; i < 5; i++)
            {
                writer.WriteSample(track, new byte[] { 0, 1, (byte)i },
                    i * 48_000L, i * 48_000L, duration: 48_000);
            }
            writer.Complete();
        }

        Assert.Equal(3, media.Count);
        Assert.Equal(2, media[0].SampleCount);
        Assert.Equal(2, media[1].SampleCount);
        Assert.Equal(1, media[2].SampleCount);
    }

    [Fact]
    public void TargetDurationOverridesNonVideoDefault()
    {
        var media = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                TargetFragmentDuration = TimeSpan.FromSeconds(1)
            },
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); }))
        {
            var track = writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 1, 48_000, CodecConfiguration: TestMedia.Esds));
            for (var i = 0; i < 5; i++)
            {
                writer.WriteSample(track, new byte[] { 0, 1, (byte)i },
                    i * 48_000L, i * 48_000L, duration: 48_000);
            }
            writer.Complete();
        }

        Assert.Equal(5, media.Count);
        Assert.All(media, segment => Assert.Equal(1, segment.SampleCount));
    }

    [Fact]
    public void TargetDurationCutsVideoWithoutKeyFrame()
    {
        var media = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.TargetDuration,
                TargetFragmentDuration = TimeSpan.FromSeconds(1)
            },
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); }))
        {
            var video = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            var audio = writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 1, 48_000, CodecConfiguration: TestMedia.Esds));
            for (var i = 0; i < 10; i++)
            {
                writer.WriteSample(video, TestMedia.VideoSample((byte)i, keyFrame: i == 0),
                    i * 45_000L, i * 45_000L, i == 0 ? SampleFlags.KeyFrame : SampleFlags.None,
                    duration: 45_000);
                writer.WriteSample(audio, new byte[] { 0, 1, (byte)i },
                    i * 24_000L, i * 24_000L, duration: 24_000);
            }
            writer.Complete();
        }

        Assert.Equal(5, media.Count);
        Assert.All(media, segment => Assert.Equal(4, segment.SampleCount));
        Assert.True(media[0].StartsWithKeyFrame);
        Assert.All(media.Skip(1), segment => Assert.False(segment.StartsWithKeyFrame));
    }

    [Fact]
    public void TargetDurationDoesNotCutAtEarlyKeyFrame()
    {
        var media = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.TargetDuration,
                TargetFragmentDuration = TimeSpan.FromSeconds(1)
            },
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); }))
        {
            var video = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            for (var i = 0; i < 4; i++)
            {
                writer.WriteSample(video, TestMedia.VideoSample((byte)i), i * 45_000L, i * 45_000L,
                    SampleFlags.KeyFrame, duration: 45_000);
            }
            writer.Complete();
        }

        Assert.Equal(2, media.Count);
        Assert.All(media, segment => Assert.Equal(2, segment.SampleCount));
    }

    [Fact]
    public void TargetDurationRequiresTarget()
    {
        Assert.Throws<ArgumentException>(() => new Mp4FragmentedWriter(
            new StreamMp4Output(new MemoryStream()),
            new Mp4FragmentedWriterOptions { FragmentBoundary = Fmp4FragmentBoundary.TargetDuration }));
    }

    [Fact]
    public void SegmentForceFlushesBeforeSample()
    {
        var media = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual
            },
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); }))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 0 }, 0, 0, duration: 1000);
            writer.WriteSample(track, new byte[] { 0, 1 }, 1000, 1000,
                SampleFlags.SegmentForce, duration: 1000);
            Assert.Single(media);

            writer.Complete();
        }

        Assert.Equal(2, media.Count);
        Assert.Equal(1, media[0].SampleCount);
        Assert.Equal(1, media[1].SampleCount);
    }

    [Fact]
    public void SegmentDisableSuppressesAutomaticKeyFrameBoundary()
    {
        var media = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(
            new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.EveryKeyFrame
            },
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); }))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0,
                SampleFlags.KeyFrame, duration: 90_000);
            writer.WriteSample(track, TestMedia.VideoSample(2), 90_000, 90_000,
                SampleFlags.KeyFrame | SampleFlags.SegmentDisable, duration: 90_000);
            Assert.Empty(media);

            writer.Complete();
        }

        Assert.Single(media);
        Assert.Equal(2, media[0].SampleCount);
    }
}
