using System.Buffers.Binary;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Mp4BoxKit.FFmpeg.Tests;

public sealed class AudioVideoSubtitlePreviewTests(ITestOutputHelper log)
{
    [Fact]
    public async Task SaveTenSeconds_WithAudioVideoAndOneSecondCaptions()
    {
        var video = await Read(NativeEncoderTests.EncodeAndWrite(true).File);
        var audio = await Read(NativeAacTests.Generate(true));
        var videoInfo = (VideoTrackInfo)video.Info;
        var audioInfo = (AudioTrackInfo)audio.Info;
        var directory = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "video-audio-subtitles-10s.mp4");
        using (var output = File.Create(path))
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { FragmentBoundary = Fmp4FragmentBoundary.Manual }))
        {
            var v = writer.AddVideo(new VideoTrackOptions(Codec.H264, 160, 96, 10,
                videoInfo.CodecConfiguration));
            var a = writer.AddAudio(new AudioTrackOptions(Codec.Aac, 1, 48000,
                TimeScale: 48000, CodecConfiguration: audioInfo.CodecConfiguration));
            var s = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g,
                Width: 160, Height: 96, Name: "Текущая секунда",
                IsDefault: true, Language: Mp4Languages.Russian));
            var audioIndex = 0;
            for (var second = 0; second < 10; second++)
            {
                // Keep captions in one fragment so consecutive DTS values define duration.
                writer.WriteSample(s, Text($"Second {second}"), second * 1000L, second * 1000L);
                for (var i = 0; i < 10; i++)
                {
                    var index = second * 10 + i;
                    var sample = video.Samples[index % video.Samples.Count];
                    writer.WriteSample(v, sample.Data.Span, index, index, sample.Info.Flags);
                    while (audioIndex * 1024L < (index + 1) * 4800L && audioIndex * 1024L < 480000)
                    {
                        var packet = audio.Samples[audioIndex % audio.Samples.Count];
                        writer.WriteSample(a, packet.Data.Span, audioIndex * 1024L, audioIndex * 1024L);
                        audioIndex++;
                    }
                }
            }
            // The final caption inherits the previous caption's one-second duration.
            writer.Complete();
        }
        log.WriteLine($"Saved preview: {path}");
        using var file = File.OpenRead(path);
        var reader = new Mp4Reader(file);
        Assert.Equal(ProbeStatus.Complete, (await reader.ReadContainerInfoAsync()).Status);
        var subtitle = Assert.IsType<SubtitleTrackInfo>(reader.Info.Tracks[2]);
        Assert.True(subtitle.IsDefault);
        Assert.Equal(Mp4Languages.Russian, subtitle.Language);
        Assert.Equal("Текущая секунда", subtitle.Name);
        var captions = new List<Mp4Sample>();
        var videos = 0;
        var audios = 0;
        while (await reader.ReadSampleAsync() is { } sample)
        {
            if (sample.Track.Value == 3)
                captions.Add(sample);
            else if (sample.Track.Value == 1)
                videos++;
            else
                audios++;
        }
        Assert.Equal(100, videos);
        Assert.True(audios > 0);
        Assert.Equal(10, captions.Count);
        for (var i = 0; i < captions.Count; i++)
        {
            Assert.Equal(i * 1000L, captions[i].Dts);
            Assert.Equal(1000, captions[i].Info.Duration);
            Assert.Equal(Text($"Second {i}"), captions[i].Data.ToArray());
        }
    }

    private static byte[] Text(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var result = new byte[2 + utf8.Length];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)utf8.Length);
        utf8.CopyTo(result, 2);
        return result;
    }

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
