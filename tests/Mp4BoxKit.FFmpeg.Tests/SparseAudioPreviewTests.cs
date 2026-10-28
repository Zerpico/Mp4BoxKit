using Xunit;
using Xunit.Abstractions;

namespace Mp4BoxKit.FFmpeg.Tests;

public sealed class SparseAudioPreviewTests(ITestOutputHelper log)
{
    [Fact]
    public async Task SavePreview_Video12Seconds_AudioFrom5To8Seconds()
    {
        var videoFile = NativeEncoderTests.EncodeAndWrite(true).File;
        var audioFile = NativeAacTests.Generate(true);
        using var videoInput = new MemoryStream(videoFile);
        using var audioInput = new MemoryStream(audioFile);
        var videoReader = new Mp4Reader(videoInput);
        var audioReader = new Mp4Reader(audioInput);
        await videoReader.ReadContainerInfoAsync();
        await audioReader.ReadContainerInfoAsync();
        var videoInfo = Assert.IsType<VideoTrackInfo>(Assert.Single(videoReader.Info.Tracks));
        var audioInfo = Assert.IsType<AudioTrackInfo>(Assert.Single(audioReader.Info.Tracks));
        var videoSamples = new List<Mp4Sample>();
        var audioSamples = new List<Mp4Sample>();
        while (await videoReader.ReadSampleAsync() is { } sample)
            videoSamples.Add(sample);
        while (await audioReader.ReadSampleAsync() is { } sample)
            audioSamples.Add(sample);

        var directory = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "video-12s-audio-5s-to-8s.mp4");
        using (var output = File.Create(path))
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var video = writer.AddVideo(new VideoTrackOptions(Codec.H264,
                videoInfo.Width, videoInfo.Height, 10, videoInfo.CodecConfiguration));
            var audio = writer.AddAudio(new AudioTrackOptions(Codec.Aac,
                1, 48000, TimeScale: 48000, CodecConfiguration: audioInfo.CodecConfiguration));
            var audioIndex = 0;
            // Reuse independently decodable two-second video and short tone packets.
            for (var frame = 0; frame < 120; frame++)
            {
                var sample = videoSamples[frame % videoSamples.Count];
                writer.WriteSample(video, sample.Data.Span, frame, frame, sample.Info.Flags);
                var end = Math.Min(8 * 48000L, (frame + 1) * 4800L);
                while (frame >= 50 && 5 * 48000L + audioIndex * 1024L < end)
                {
                    var packet = audioSamples[audioIndex % audioSamples.Count];
                    var timestamp = 5 * 48000L + audioIndex * 1024L;
                    writer.WriteSample(audio, packet.Data.Span, timestamp, timestamp);
                    audioIndex++;
                }
            }
            writer.Complete();
        }
        log.WriteLine($"Saved preview: {path}");
        using var saved = File.OpenRead(path);
        var reader = new Mp4Reader(saved);
        var videoCount = 0;
        var audioCount = 0;
        while (await reader.ReadSampleAsync() is { } sample)
        {
            if (sample.Track.Value == 1)
                videoCount++;
            else
            {
                Assert.InRange(sample.Dts, 240000, 383999);
                audioCount++;
            }
        }
        Assert.Equal(120, videoCount);
        Assert.True(audioCount > 0);
    }
}
