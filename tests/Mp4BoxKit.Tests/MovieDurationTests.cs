using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class MovieDurationTests
{
    [Fact]
    public void MovieAndTrackHeadersUseMovieTimescale()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var audio = writer.AddAudio(new AudioTrackOptions(
                Codec.Aac, 1, 48000, CodecConfiguration: TestMedia.Esds));
            var text = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(audio, new byte[] { 1 }, 0, 0);
            writer.WriteSample(audio, new byte[] { 2 }, 48000, 48000);
            writer.WriteSample(text, new byte[] { 0, 0 }, 0, 0);
            writer.WriteSample(text, new byte[] { 0, 0 }, 1500, 1500);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var movie = Find("mvhd", 0);
        Assert.Equal(1000u, Read(movie + 16));
        Assert.Equal(3000u, Read(movie + 20));
        var audioHeader = Find("tkhd", 0);
        var textHeader = Find("tkhd", audioHeader + 4);
        Assert.Equal(2000u, Read(audioHeader + 24));
        Assert.Equal(3000u, Read(textHeader + 24));
        var audioMedia = Find("mdhd", 0);
        Assert.Equal(48000u, Read(audioMedia + 16));
        Assert.Equal(96000u, Read(audioMedia + 20));

        int Find(string type, int start) => start + bytes.AsSpan(start)
            .IndexOf(System.Text.Encoding.ASCII.GetBytes(type));
        uint Read(int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
    }
}
