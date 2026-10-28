using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class AacConfigurationTests
{
    [Theory]
    [InlineData(48_000, 1, new byte[] { 0x11, 0x88 })]
    [InlineData(44_100, 2, new byte[] { 0x12, 0x10 })]
    [InlineData(96_000, 2, new byte[] { 0x10, 0x10 })]
    public void AudioSpecificConfigMatchesAacLc(
        int sampleRate, int channels, byte[] expected)
    {
        Assert.Equal(expected,
            AacConfiguration.CreateAudioSpecificConfig(sampleRate, channels).ToArray());
    }

    [Fact]
    public void AudioSpecificConfigCanBeWrappedIntoEsds()
    {
        var fromAsc = AacConfiguration.CreateEsdsFromAudioSpecificConfig(
            new byte[] { 0x11, 0x88 });
        var generated = AacConfiguration.CreateEsds(48_000, 1);
        Assert.Equal(generated.ToArray(), fromAsc.ToArray());
    }

    [Fact]
    public async Task AacWithoutConfigurationGetsAacLcEsds()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddAudio(new AudioTrackOptions(Codec.Aac, 1, 48_000));
            writer.WriteSample(track, new byte[] { 1, 2, 3 }, 0, 0, duration: 1024);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        await reader.ReadContainerInfoAsync();
        var audio = Assert.IsType<AudioTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal(Codec.Aac, audio.Codec);
        Assert.False(audio.CodecConfiguration.IsEmpty);
    }

    [Fact]
    public void UnsupportedAacChannelCountIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        Assert.Throws<NotSupportedException>(() =>
            writer.AddAudio(new AudioTrackOptions(Codec.Aac, 8, 48_000)));
    }
}
