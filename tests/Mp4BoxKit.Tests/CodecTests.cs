using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class CodecTests
{
    [Theory]
    [InlineData(Codec.H264, MediaType.Video)]
    [InlineData(Codec.H265, MediaType.Video)]
    [InlineData(Codec.Mpeg4Video, MediaType.Video)]
    [InlineData(Codec.Vp8, MediaType.Video)]
    [InlineData(Codec.Vp9, MediaType.Video)]
    [InlineData(Codec.Av1, MediaType.Video)]
    [InlineData(Codec.Aac, MediaType.Audio)]
    [InlineData(Codec.Mp3, MediaType.Audio)]
    [InlineData(Codec.Opus, MediaType.Audio)]
    [InlineData(Codec.Ac3, MediaType.Audio)]
    [InlineData(Codec.EAc3, MediaType.Audio)]
    [InlineData(Codec.Pcm, MediaType.Audio)]
    public void WriterAcceptsSupportedCodecFamilies(
        Codec codec,
        MediaType type)
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FileWriter(new StreamMp4Output(output));
        TrackId track;

        if (type == MediaType.Video)
        {
            track = writer.AddVideo(new VideoTrackOptions(
                codec, 16, 16, CodecConfiguration: TestMedia.Configuration(codec)));
        }
        else
        {
            track = writer.AddAudio(new AudioTrackOptions(
                codec, 2, 48_000, CodecConfiguration: TestMedia.Configuration(codec)));
        }

        var payload = codec switch
        {
            Codec.H264 => TestMedia.VideoSample(),
            Codec.H265 => TestMedia.H265VideoSample(),
            _ => new byte[] { 1, 2, 3 }
        };
        writer.WriteSample(track, payload, 0, 0);
        writer.Complete();
        Assert.NotEmpty(output.ToArray());
    }
}
