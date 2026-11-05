using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class NalSampleConverterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertsHevcAndFiltersOnlyHevcParameterSets(bool includeParameterSets)
    {
        ReadOnlyMemory<byte>[] nals =
        {
            new byte[] { 0x40, 1 }, // VPS
            new byte[] { 0x42, 1 }, // SPS
            new byte[] { 0x44, 1 }, // PPS
            new byte[] { 0x26, 1, 0x88 }, // IDR
            new byte[] { 0x07, 1, 0x99 } // Not HEVC SPS, despite low five bits being 7.
        };
        var annexB = nals.SelectMany((nal, i) =>
            (i % 2 == 0 ? new byte[] { 0, 0, 0, 1 } : new byte[] { 0, 0, 1 })
            .Concat(nal.ToArray())).ToArray();
        var expected = nals.Skip(includeParameterSets ? 0 : 3).SelectMany(nal =>
            new byte[] { 0, 0, 0, (byte)nal.Length }.Concat(nal.ToArray())).ToArray();
        Assert.Equal(expected, NalSampleConverter.FromNalUnits(nals, Codec.H265, includeParameterSets).ToArray());
        Assert.Equal(expected, NalSampleConverter.FromAnnexB(annexB, Codec.H265, includeParameterSets).ToArray());
    }

    [Theory]
    [InlineData(Codec.Aac)]
    [InlineData(Codec.Mpeg4Video)]
    public void RejectsUnsupportedCodec(Codec codec)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NalSampleConverter.FromAnnexB(new byte[] { 0, 0, 1, 0x65 }, codec));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NalSampleConverter.FromNalUnits(Array.Empty<ReadOnlyMemory<byte>>(), codec));
    }

    [Fact]
    public void RejectsTruncatedHevcHeader()
    {
        Assert.Throws<ArgumentException>(() =>
            NalSampleConverter.FromAnnexB(new byte[] { 0, 0, 1, 0x26 }, Codec.H265));
        Assert.Throws<ArgumentException>(() =>
            NalSampleConverter.FromNalUnits(new ReadOnlyMemory<byte>[] { new byte[] { 0x26 } }, Codec.H265));
    }
}
