using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class SubtitleOptionsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MetadataRoundTrips(bool fragmented, bool isDefault)
    {
        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g,
                Name: "Русский текст", IsDefault: isDefault, Language: Mp4Languages.Russian));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0);
            writer.Complete();
        }
        output.Position = 0;
        var reader = new Mp4Reader(output);
        Assert.Equal(ProbeStatus.Complete, (await reader.ReadContainerInfoAsync()).Status);
        var subtitle = Assert.IsType<SubtitleTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal("Русский текст", subtitle.Name);
        Assert.Equal(Mp4Languages.Russian, subtitle.Language);
        Assert.Equal(isDefault, subtitle.IsDefault);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("RUS")]
    [InlineData("123")]
    public void InvalidLanguageIsRejected(string language)
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        Assert.Throws<ArgumentException>(() => writer.AddSubtitle(
            new SubtitleTrackOptions(Codec.Tx3g, Language: language)));
    }
}
