using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class SparseAudioTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoContinues_WhenDeclaredAudioIsLateOrInterrupted(bool audioArrives)
    {
        var segments = new List<Fmp4Segment>();
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            segmentReady: segments.Add))
        {
            var video = writer.AddVideo(new VideoTrackOptions(
                Codec.H264, 160, 96, 1000, CodecConfiguration: TestMedia.AvcC));
            var audio = writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 1, 48000, CodecConfiguration: TestMedia.Esds));
            for (var second = 0; second < 12; second++)
            {
                writer.WriteSample(video, TestMedia.VideoSample((byte)second),
                    second * 1000L, second * 1000L, SampleFlags.KeyFrame);
                if (audioArrives && second is 5 or 6 or 10)
                    writer.WriteSample(audio, new byte[] { 2, (byte)second },
                        second * 48000L, second * 48000L);
            }
            writer.Complete();
        }

        Assert.Equal(12, segments.Count(segment => segment.Kind == Fmp4SegmentKind.Media));
        output.Position = 0;
        var reader = new Mp4Reader(output);
        Assert.Equal(ProbeStatus.Complete, (await reader.ReadContainerInfoAsync()).Status);
        Assert.Equal(2, reader.Info.Tracks.Count);
        var videoTimes = new List<long>();
        var audioTimes = new List<long>();
        while (await reader.ReadSampleAsync() is { } sample)
        {
            if (sample.Track.Value == 1)
                videoTimes.Add(sample.Dts);
            else
                audioTimes.Add(sample.Dts);
        }
        Assert.Equal(Enumerable.Range(0, 12).Select(second => second * 1000L), videoTimes);
        Assert.Equal(audioArrives ? new long[] { 240000, 288000, 480000 } : [], audioTimes);
    }
}
