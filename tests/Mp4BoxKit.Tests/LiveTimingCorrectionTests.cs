using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class LiveTimingCorrectionTests
{
    private static Mp4FragmentedWriter CreateWriter(MemoryStream output, List<Fmp4Segment> media,
        TimeSpan maxDtsCorrection = default, bool durationFollowsDts = false,
        Fmp4FragmentBoundary boundary = Fmp4FragmentBoundary.EveryKeyFrame) =>
        new(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = boundary,
                TargetFragmentDuration = boundary == Fmp4FragmentBoundary.KeyFrameAfterTargetDuration
                    ? TimeSpan.FromSeconds(1) : null,
                MaxDtsCorrection = maxDtsCorrection,
                DurationFollowsDts = durationFollowsDts
            },
            segment => { if (segment.Kind == Fmp4SegmentKind.Media) media.Add(segment); });

    private static async Task<List<Mp4Sample>> ReadSamples(MemoryStream output)
    {
        output.Position = 0;
        var reader = new Mp4Reader(output);
        var samples = new List<Mp4Sample>();
        while (await reader.ReadSampleAsync() is { } sample)
            samples.Add(sample);
        return samples;
    }

    [Fact]
    public void NonIncreasingDtsThrowsByDefault()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using var writer = CreateWriter(output, media);
        var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
        writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);

        Assert.Throws<ArgumentException>(() =>
            writer.WriteSample(track, TestMedia.VideoSample(2), 0, 0, SampleFlags.KeyFrame));
    }

    [Fact]
    public async Task RepeatedDtsMovesToPreviousPlusOneTick()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using (var writer = CreateWriter(output, media, TimeSpan.FromMilliseconds(100),
            boundary: Fmp4FragmentBoundary.Manual))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(2), 3600, 3600, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(3, keyFrame: false), 3600, 3600);
            writer.WriteSample(track, TestMedia.VideoSample(4, keyFrame: false), 7200, 7200);
            writer.Complete();
        }

        var samples = await ReadSamples(output);
        Assert.Equal(new long[] { 0, 3600, 3601, 7200 }, samples.Select(s => s.Dts));
        Assert.Equal(new long[] { 0, 3600, 3601, 7200 }, samples.Select(s => s.Pts));
        Assert.Equal(new[] { 3600, 1, 3599, 3599 }, samples.Select(s => s.Duration));
    }

    [Fact]
    public async Task CorrectionKeepsPresentationTime()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using (var writer = CreateWriter(output, media, TimeSpan.FromMilliseconds(100),
            boundary: Fmp4FragmentBoundary.Manual))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 7200, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(2, keyFrame: false), 3600, 0);
            writer.Complete();
        }

        var samples = await ReadSamples(output);
        Assert.Equal(1, samples[1].Dts);
        Assert.Equal(3600, samples[1].Pts);
    }

    [Fact]
    public async Task CorrectionRaisesPtsThatWouldPrecedeDts()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using (var writer = CreateWriter(output, media, TimeSpan.FromMilliseconds(100),
            boundary: Fmp4FragmentBoundary.Manual))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(2, keyFrame: false), 3600, 3600);
            writer.WriteSample(track, TestMedia.VideoSample(3, keyFrame: false), 1800, 1800);
            writer.Complete();
        }

        var samples = await ReadSamples(output);
        Assert.Equal((3601L, 3601L), (samples[2].Dts, samples[2].Pts));
    }

    [Fact]
    public void StepBackBeyondLimitStillThrows()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using var writer = CreateWriter(output, media, TimeSpan.FromMilliseconds(100));
        var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
        writer.WriteSample(track, TestMedia.VideoSample(1), 90_000, 90_000, SampleFlags.KeyFrame);

        // 100 ms = 9000 ticks; 9001 ticks back exceeds the limit.
        Assert.Throws<ArgumentException>(() =>
            writer.WriteSample(track, TestMedia.VideoSample(2), 80_999, 80_999, SampleFlags.KeyFrame));
        writer.WriteSample(track, TestMedia.VideoSample(3), 81_000, 81_000, SampleFlags.KeyFrame);
    }

    [Fact]
    public void ExplicitDurationMismatchFlushesByDefault()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using (var writer = CreateWriter(output, media, boundary: Fmp4FragmentBoundary.Manual))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame, duration: 3600);
            writer.WriteSample(track, TestMedia.VideoSample(2, keyFrame: false), 3510, 3510, duration: 3600);
            writer.Complete();
        }

        Assert.Equal(2, media.Count);
    }

    [Fact]
    public async Task DurationFollowsDtsKeepsVariableFrameRateInOneFragment()
    {
        // Variable frame rate with a 1 s pause before the second keyframe and a repeated DTS.
        long[] dtsMs = { 0, 39, 79, 129, 139, 1139, 1139, 1179, 1218 };
        bool[] keys = { true, false, false, false, false, true, false, false, false };

        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using (var writer = CreateWriter(output, media, TimeSpan.FromMilliseconds(100),
            durationFollowsDts: true, boundary: Fmp4FragmentBoundary.KeyFrameAfterTargetDuration))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            for (var i = 0; i < dtsMs.Length; i++)
            {
                var ticks = dtsMs[i] * 90;
                writer.WriteSample(track, TestMedia.VideoSample((byte)(i + 1), keys[i]), ticks, ticks,
                    keys[i] ? SampleFlags.KeyFrame : SampleFlags.None, duration: 3600);
            }
            writer.Complete();
        }

        Assert.Equal(2, media.Count);
        Assert.Equal(5, media[0].SampleCount);
        Assert.Equal(4, media[1].SampleCount);

        var samples = await ReadSamples(output);
        Assert.Equal(new long[] { 0, 3510, 7110, 11610, 12510, 102510, 102511, 106110, 109620 },
            samples.Select(s => s.Dts));
        Assert.Equal(new[] { 3510, 3600, 4500, 900, 90000, 1, 3599, 3510, 3600 },
            samples.Select(s => s.Duration));
    }

    [Fact]
    public async Task InferredDurationOfSingleSampleFragmentRepeatsLastKnownDuration()
    {
        using var output = new MemoryStream();
        var media = new List<Fmp4Segment>();
        using (var writer = CreateWriter(output, media, boundary: Fmp4FragmentBoundary.Manual))
        {
            var track = writer.AddVideo(new VideoTrackOptions(Codec.H264, 16, 16, 90_000, TestMedia.AvcC));
            writer.WriteSample(track, TestMedia.VideoSample(1), 0, 0, SampleFlags.KeyFrame);
            writer.WriteSample(track, TestMedia.VideoSample(2, keyFrame: false), 3600, 3600);
            writer.FlushFragment();
            writer.WriteSample(track, TestMedia.VideoSample(3), 7200, 7200, SampleFlags.KeyFrame);
            writer.FlushFragment();
            writer.Complete();
        }

        var samples = await ReadSamples(output);
        Assert.Equal(new[] { 3600, 3600, 3600 }, samples.Select(s => s.Duration));
    }
}
