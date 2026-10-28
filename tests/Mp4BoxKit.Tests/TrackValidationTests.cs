using System.Text;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class TrackValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ZeroTimeScaleIsRejected(int kind)
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            switch (kind)
            {
                case 0:
                    writer.AddVideo(new VideoTrackOptions(
                        Codec.H264, 16, 16, TimeScale: 0, CodecConfiguration: TestMedia.AvcC));
                    break;
                case 1:
                    writer.AddAudio(new AudioTrackOptions(
                        Codec.Aac, 2, 48_000, TimeScale: 0, CodecConfiguration: TestMedia.Esds));
                    break;
                default:
                    writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g, TimeScale: 0));
                    break;
            }
        });
    }

    [Fact]
    public void CodecMustMatchTrackType()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.Aac, 16, 16, CodecConfiguration: TestMedia.Esds)));
        Assert.Throws<ArgumentException>(() => writer.AddAudio(
            new AudioTrackOptions(Codec.H264, 2, 48_000, CodecConfiguration: TestMedia.AvcC)));
        Assert.Throws<ArgumentException>(() => writer.AddSubtitle(
            new SubtitleTrackOptions(Codec.H264)));
    }

    [Fact]
    public void UnsupportedWriterCodecsAreRejectedEarly()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<NotSupportedException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.Vc1, 16, 16)));
        Assert.Throws<NotSupportedException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.Dirac, 16, 16)));
        Assert.Throws<NotSupportedException>(() => writer.AddSubtitle(
            new SubtitleTrackOptions(Codec.WebVtt)));
        Assert.Throws<NotSupportedException>(() => writer.AddSubtitle(
            new SubtitleTrackOptions(Codec.Ttml)));
    }

    [Fact]
    public void MandatoryCodecConfigurationIsRequired()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 16, 16)));
        Assert.Throws<ArgumentException>(() => writer.AddAudio(
            new AudioTrackOptions(Codec.Mp3, 2, 48_000)));

        _ = writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
        var aac = writer.AddAudio(new AudioTrackOptions(Codec.Aac, 2, 48_000));
        Assert.False(writer.Tracks[(int)aac.Value - 1].CodecConfiguration.IsEmpty);
    }

    [Fact]
    public void ConfigurationForCodecWithoutConfigBoxIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentException>(() => writer.AddAudio(
            new AudioTrackOptions(
                Codec.Pcm, 2, 48_000, CodecConfiguration: TestMedia.Esds)));

        _ = writer.AddAudio(new AudioTrackOptions(Codec.Pcm, 2, 48_000));
    }

    [Fact]
    public void InvalidTrackDimensionsAndRatesAreRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 0, 16, CodecConfiguration: TestMedia.AvcC)));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 16, 0, CodecConfiguration: TestMedia.AvcC)));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddAudio(
            new AudioTrackOptions(Codec.Aac, 0, 48_000, CodecConfiguration: TestMedia.Esds)));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddAudio(
            new AudioTrackOptions(Codec.Aac, 2, 0, CodecConfiguration: TestMedia.Esds)));
        Assert.Throws<NotSupportedException>(() => writer.AddAudio(
            new AudioTrackOptions(Codec.Aac, 2, 96_000, CodecConfiguration: TestMedia.Esds)));
    }

    [Fact]
    public void FailedAddDoesNotMutateWriter()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));

        Assert.Throws<ArgumentException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 16, 16)));
        Assert.Empty(writer.Tracks);

        var track = writer.AddVideo(new VideoTrackOptions(
            Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));
        writer.WriteSample(track, TestMedia.VideoSample(), 0, 0, SampleFlags.KeyFrame);
        writer.Complete();
        Assert.NotEmpty(output.ToArray());
    }

    [Fact]
    public void Mpeg2VideoUsesMp2vSampleEntry()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.Mpeg2Video, 16, 16, CodecConfiguration: TestMedia.Esds));
            writer.WriteSample(track, new byte[] { 1, 2, 3 }, 0, 0, SampleFlags.KeyFrame);
            writer.Complete();
        }

        var text = Encoding.ASCII.GetString(output.ToArray());
        Assert.Contains("mp2v", text);
        Assert.DoesNotContain("mp4v", text);
    }

    [Fact]
    public void RawSpsPpsAsCodecConfigurationIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var raw = TestMedia.RawSps.ToArray().Concat(TestMedia.RawPps.ToArray()).ToArray();

        Assert.Throws<ArgumentException>(() => writer.AddVideo(
            new VideoTrackOptions(Codec.H264, 16, 16, CodecConfiguration: raw)));
    }

    [Fact]
    public void AnnexBSampleIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var track = writer.AddVideo(new VideoTrackOptions(
            Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));

        var error = Assert.Throws<ArgumentException>(() => writer.WriteSample(
            track, new byte[] { 0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E }, 0, 0));
        Assert.Contains("Annex B", error.Message);
    }

    [Fact]
    public void ParameterSetOnlySampleIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var track = writer.AddVideo(new VideoTrackOptions(
            Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));

        var error = Assert.Throws<ArgumentException>(() => writer.WriteSample(
            track, new byte[] { 0, 0, 0, 1, 0x67 }, 0, 0));
        Assert.Contains("parameter sets", error.Message);
    }

    [Fact]
    public void LengthPrefixedNalFramingIsValidated()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var track = writer.AddVideo(new VideoTrackOptions(
            Codec.H264, 16, 16, CodecConfiguration: TestMedia.AvcC));

        Assert.Throws<ArgumentException>(() => writer.WriteSample(
            track, new byte[] { 0, 0, 0, 5, 0x65 }, 0, 0));
        Assert.Throws<ArgumentException>(() => writer.WriteSample(
            track, new byte[] { 0, 0, 0, 0 }, 0, 0));
    }
}
