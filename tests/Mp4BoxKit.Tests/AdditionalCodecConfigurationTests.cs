using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class AdditionalCodecConfigurationTests
{
    [Fact]
    public void OpusDopsConfigurationContainsTheCompleteFamilyZeroPayload()
    {
        var configuration = OpusConfiguration.CreateDops(
            outputChannelCount: 2,
            preSkip: 3840,
            inputSampleRate: 48_000,
            outputGain: 0);

        Assert.Equal(new byte[]
        {
            0, 2, 0x0f, 0, 0, 0, 0xbb, 0x80, 0, 0, 0
        }, configuration.ToArray());
    }

    [Fact]
    public void OpusDopsConfigurationIncludesMappingFamilyFields()
    {
        var configuration = OpusConfiguration.CreateDops(
            outputChannelCount: 2,
            preSkip: 312,
            inputSampleRate: 48_000,
            outputGain: -2,
            channelMappingFamily: 1,
            streamCount: 1,
            coupledCount: 1,
            channelMapping: new byte[] { 0, 1 });

        Assert.Equal(new byte[]
        {
            0, 2, 1, 0x38, 0, 0, 0xbb, 0x80, 0xff, 0xfe,
            1, 1, 1, 0, 1
        }, configuration.ToArray());
    }

    [Fact]
    public void Vp9ConfigurationEncodesRecordFieldsAndCodecInitializationData()
    {
        var configuration = Vp9Configuration.CreateVpcC(
            profile: 2,
            level: 40,
            bitDepth: 10,
            chromaSubsampling: 3,
            videoFullRange: true,
            colourPrimaries: 9,
            transferCharacteristics: 16,
            matrixCoefficients: 9,
            codecInitializationData: new byte[] { 0xaa, 0xbb });

        Assert.Equal(new byte[]
        {
            1, 0, 0, 0,
            2, 40, 0xa7, 9, 16, 9, 0, 2, 0xaa, 0xbb
        }, configuration.ToArray());
    }

    [Fact]
    public void EsdsConfigurationBuildsAnMpegAudioDecoderDescriptor()
    {
        var configuration = EsdsConfiguration.Create(
            objectTypeIndication: 0x6b,
            streamType: 5);

        Assert.Equal(new byte[]
        {
            0, 0, 0, 0,
            3, 21, 0, 1, 0,
            4, 13, 0x6b, 0x15, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            6, 1, 2
        }, configuration.ToArray());
    }

    [Fact]
    public void EsdsConfigurationUsesVariableLengthDescriptorSizes()
    {
        var decoderInfo = Enumerable.Repeat((byte)0x5a, 130).ToArray();
        var configuration = EsdsConfiguration.Create(
            objectTypeIndication: 0x20,
            streamType: 4,
            decoderSpecificInfo: decoderInfo).ToArray();

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, configuration[..4]);
        Assert.Equal(3, configuration[4]);
        Assert.Equal(0x81, configuration[5]);
        Assert.Equal(0x1b, configuration[6]);
        Assert.Equal(decoderInfo, configuration[29..159]);
    }

    [Fact]
    public void CodecConfigurationHelpersRejectInvalidFields()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpusConfiguration.CreateDops(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpusConfiguration.CreateDops(2, channelMappingFamily: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Vp9Configuration.CreateVpcC(4, 31, 8, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EsdsConfiguration.Create(0x40, 64));
    }
}
