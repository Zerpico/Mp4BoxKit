using Mp4BoxKit.Codecs;
using Xunit;
using Xunit.Abstractions;

namespace Mp4BoxKit.FFmpeg.Tests;

// Temporary: saves a preview file for manual WebVTT playback checks.
public sealed class TemporaryWebVttPreviewTests(ITestOutputHelper log)
{
    private const int Seconds = 10;

    [Fact]
    public async Task SaveTenSeconds_WithVideoAndWebVttClock()
    {
        var video = await Read(NativeEncoderTests.EncodeAndWrite(false).File);
        var videoInfo = (VideoTrackInfo)video.Info;
        var directory = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "video-webvtt-clock-10s.mp4");
        using (var output = File.Create(path))
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var v = writer.AddVideo(new VideoTrackOptions(Codec.H264, 160, 96, 10,
                videoInfo.CodecConfiguration));
            var s = writer.AddSubtitle(new SubtitleTrackOptions(Codec.WebVtt,
                Width: 160, Height: 96, Name: "Время кадра",
                IsDefault: true, Language: Mp4Languages.Russian));
            for (var second = 0; second < Seconds; second++)
            {
                writer.WriteSample(s, WebVttSample.Create(new WebVttCue(Clock(second))).Span,
                    second * 1000L, second * 1000L, duration: 1000);
                for (var i = 0; i < 10; i++)
                {
                    var index = second * 10 + i;
                    var sample = video.Samples[index % video.Samples.Count];
                    writer.WriteSample(v, sample.Data.Span, index, index, sample.Info.Flags);
                }
            }
            writer.Complete();
        }
        log.WriteLine($"Saved preview: {path}");

        using var file = File.OpenRead(path);
        var reader = new Mp4Reader(file);
        Assert.Equal(ProbeStatus.Complete, (await reader.ReadContainerInfoAsync()).Status);
        var subtitle = Assert.IsType<SubtitleTrackInfo>(reader.Info.Tracks[1]);
        Assert.Equal(Codec.WebVtt, subtitle.Codec);
        var cues = new List<Mp4Sample>();
        var frames = 0;
        while (await reader.ReadSampleAsync() is { } sample)
        {
            if (sample.Track == subtitle.Id)
                cues.Add(sample);
            else
                frames++;
        }
        Assert.Equal(Seconds * 10, frames);
        Assert.Equal(Seconds, cues.Count);
        for (var i = 0; i < cues.Count; i++)
        {
            Assert.Equal(i * 1000L, cues[i].Dts);
            Assert.Equal(1000, cues[i].Info.Duration);
            Assert.Equal(WebVttSample.Create(new WebVttCue(Clock(i))).ToArray(), cues[i].Data.ToArray());
        }
    }

    private static string Clock(int second) => TimeSpan.FromSeconds(second).ToString(@"hh\:mm\:ss");

    private static async Task<(MediaTrackInfo Info, List<Mp4Sample> Samples)> Read(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        var samples = new List<Mp4Sample>();
        while (await reader.ReadSampleAsync() is { } sample)
            samples.Add(sample);
        return (Assert.Single(reader.Info.Tracks), samples);
    }
}
