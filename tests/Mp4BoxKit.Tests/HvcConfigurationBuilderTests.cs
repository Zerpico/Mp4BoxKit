using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public class HvcConfigurationBuilderTests
{
    [Fact]
    public void BuildsConfigurationAcceptedByWriter()
    {
        byte[] vps = { 0x40, 1 };
        var sps = new byte[17];
        sps[0] = 0x42;
        sps[1] = 1;
        sps[2] = 1; // one temporal layer, nested
        sps[3] = 1; // Main profile
        sps[14] = 120; // level
        sps[15] = 0xad; // SPS id=0, chroma=1, width=0, height=0, no crop, luma depth=0
        sps[16] = 0x80; // chroma depth=0
        byte[] pps = { 0x44, 1 };
        var configuration = HvcConfiguration.CreateFromParameterSets(vps, sps, pps);
        Assert.Equal(3, configuration.Span[22]);
        Assert.Equal(0xfd, configuration.Span[16]);
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        writer.AddVideo(new VideoTrackOptions(Codec.H265, 640, 480, 90000, configuration));
    }

    [Fact]
    public void RejectsWrongParameterTypes() =>
        Assert.Throws<ArgumentException>(() => HvcConfiguration.CreateFromParameterSets(
            new byte[] { 0x42, 1 }, new byte[] { 0x42, 1 }, new byte[] { 0x44, 1 }));
}
