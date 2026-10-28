using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class AacAdtsTests
{
    [Fact]
    public void ParsesAacLcAdtsFrameAndBuildsEsds()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var frame = CreateFrame(payload);

        Assert.True(AacAdts.TryParse(frame, out var header));
        Assert.Equal(2, header.AudioObjectType);
        Assert.Equal(3, header.FrequencyIndex);
        Assert.Equal(48_000, header.SampleRate);
        Assert.Equal(2, header.ChannelConfiguration);
        Assert.Equal(7, header.HeaderLength);
        Assert.Equal(frame.Length, header.FrameLength);
        Assert.Equal(payload, AacAdts.GetPayload(frame).ToArray());
        Assert.Equal(AacConfiguration.CreateEsds(48_000, 2).ToArray(),
            AacAdts.CreateEsds(frame).ToArray());
    }

    [Fact]
    public void ParsesAdtsFrameWithCrcHeader()
    {
        var payload = new byte[] { 9, 8, 7 };
        var frame = CreateFrame(payload, protectionAbsent: false);

        Assert.True(AacAdts.TryParse(frame, out var header));
        Assert.Equal(9, header.HeaderLength);
        Assert.Equal(payload, AacAdts.GetPayload(frame).ToArray());
    }

    [Fact]
    public void RejectsInvalidAdtsFrames()
    {
        var frame = CreateFrame(new byte[] { 1, 2, 3 });
        Assert.False(AacAdts.TryParse(frame.AsSpan(0, 4), out _));

        var badSync = frame.ToArray();
        badSync[0] = 0;
        Assert.False(AacAdts.TryParse(badSync, out _));

        var badLength = frame.ToArray();
        badLength[4] = 0xFF;
        Assert.False(AacAdts.TryParse(badLength, out _));

        var multipleBlocks = frame.ToArray();
        multipleBlocks[6] |= 1;
        Assert.False(AacAdts.TryParse(multipleBlocks, out _));
    }

    private static byte[] CreateFrame(
        byte[] payload,
        int frequencyIndex = 3,
        int channels = 2,
        int profile = 1,
        bool protectionAbsent = true)
    {
        var headerLength = protectionAbsent ? 7 : 9;
        var frameLength = headerLength + payload.Length;
        var header = new byte[headerLength];
        header[0] = 0xFF;
        header[1] = (byte)(0xF0 | (protectionAbsent ? 1 : 0));
        header[2] = (byte)((profile << 6) | (frequencyIndex << 2) | (channels >> 2));
        header[3] = (byte)(((channels & 3) << 6) | (frameLength >> 11));
        header[4] = (byte)(frameLength >> 3);
        header[5] = (byte)(((frameLength & 7) << 5) | 0x1F);
        header[6] = 0xFC;
        return header.Concat(payload).ToArray();
    }
}
