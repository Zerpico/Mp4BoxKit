using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class SampleMetadataLimitsTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public void RejectsHugeClassicSampleCount(uint uniformSize)
    {
        var stsz = Box("stsz", Words(0, uniformSize, uint.MaxValue));
        using var input = new MemoryStream(Classic(stsz));
        var reader = new Mp4Reader(input);
#pragma warning disable CS0618
        Assert.Throws<Mp4FormatException>(() => reader.ReadClassicSamples(Ignore));
#pragma warning restore CS0618
    }

    [Fact]
    public void ClassicBudgetIsSharedAcrossTracks()
    {
        var track = Box("trak", Box("mdia", Box("minf", Box("stbl",
            ValidSampleTable()))));
        using var input = new MemoryStream(Box("moov", track.Concat(track).ToArray()));
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { MaxMetadataBytes = 300 });
#pragma warning disable CS0618
        Assert.Throws<Mp4FormatException>(() => reader.ReadClassicSamples(Ignore));
#pragma warning restore CS0618
    }

    [Fact]
    public void AcceptsClassicSampleAtBudgetBoundary()
    {
        using var input = new MemoryStream(Classic(ValidSampleTable()));
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { MaxMetadataBytes = 192 });
#pragma warning disable CS0618
        Assert.Equal(1, reader.ReadClassicSamples(Ignore));
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData(0u, uint.MaxValue)]
    [InlineData(0x100u, 2u)]
    [InlineData(4u, 0u)]
    [InlineData(1u, 0u)]
    public async Task RejectsHugeOrTruncatedTrackRun(uint flags, uint count)
    {
        using var input = new MemoryStream(Fragment(Box("trun", Words(flags, count))));
        var reader = new Mp4Reader(input);
#pragma warning disable CS0618
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await reader.ReadFragmentSamplesAsync(Ignore));
#pragma warning restore CS0618
    }

    [Fact]
    public async Task FragmentBudgetIsSharedAcrossRuns()
    {
        var run = Box("trun", Words(0, 2));
        using var input = new MemoryStream(Fragment(run.Concat(run).ToArray()));
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { MaxMetadataBytes = 300 });
#pragma warning disable CS0618
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await reader.ReadFragmentSamplesAsync(Ignore));
#pragma warning restore CS0618
    }

    [Fact]
    public async Task FragmentBudgetIsSharedAcrossTracks()
    {
        var run = Box("trun", Words(0, 2));
        var first = Box("traf", Box("tfhd", Words(0x020000, 1)).Concat(run).ToArray());
        var second = Box("traf", Box("tfhd", Words(0x020000, 2)).Concat(run).ToArray());
        using var input = new MemoryStream(Box("moof", first.Concat(second).ToArray()));
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { MaxMetadataBytes = 300 });
#pragma warning disable CS0618
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await reader.ReadFragmentSamplesAsync(Ignore));
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void RejectsNonPositiveLimits(long metadata, long payload)
    {
        using var input = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4Reader(input,
            new Mp4ProbeOptions { MaxMetadataBytes = metadata, MaxBoxPayloadBytes = payload }));
    }

    private static void Ignore(in SampleInfo info, ReadOnlySpan<byte> data) { }

    private static byte[] ValidSampleTable() => Box("stsz", Words(0, 1, 1))
        .Concat(Box("stts", Words(0, 1, 1, 1)))
        .Concat(Box("stsc", Words(0, 1, 1, 1, 1)))
        .Concat(Box("stco", Words(0, 1, 0))).ToArray();

    private static byte[] Classic(byte[] table) =>
        Box("moov", Box("trak", Box("mdia", Box("minf", Box("stbl", table)))));

    private static byte[] Fragment(byte[] runs) => Box("moof", Box("traf",
        Box("tfhd", Words(0x020000, 1)).Concat(runs).ToArray()));

    private static byte[] Words(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        payload.CopyTo(bytes, 8);
        return bytes;
    }
}
