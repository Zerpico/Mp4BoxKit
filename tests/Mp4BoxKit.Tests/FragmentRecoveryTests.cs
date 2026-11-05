using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentRecoveryTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task RecoveryIsOptInAndReportsDiscard(bool recover, bool seekable)
    {
        var bytes = CreateFragments();
        var firstTfdt = bytes.AsSpan().IndexOf("tfdt"u8);
        bytes[firstTfdt + 4] = 2;
        using var input = new TestStream(bytes, seekable);
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { SkipInvalidFragments = recover });
        var discarded = new List<SkippedFragmentInfo>();
        reader.FragmentSkipped += discarded.Add;
        if (!recover)
        {
            await Assert.ThrowsAsync<Mp4FormatException>(async () => await reader.ReadSampleAsync());
            Assert.Empty(discarded);
            return;
        }
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(1000, sample.Dts);
        Assert.Equal(new byte[] { 0, 1, 66 }, sample.Data.ToArray());
        Assert.Single(discarded);
        Assert.Equal(bytes.AsSpan().IndexOf("moof"u8) - 4, discarded[0].Offset);
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Fact]
    public async Task DoesNotInventDecodeTimeAfterDiscard()
    {
        var bytes = CreateFragments();
        var first = bytes.AsSpan().IndexOf("tfdt"u8);
        bytes[first + 4] = 2;
        var second = first + 4 + bytes.AsSpan(first + 4).IndexOf("tfdt"u8);
        // Replace tfdt with an unknown box without changing any boundaries.
        "free"u8.CopyTo(bytes.AsSpan(second, 4));
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { SkipInvalidFragments = true });
        var discarded = new List<SkippedFragmentInfo>();
        reader.FragmentSkipped += discarded.Add;
        Assert.Null(await reader.ReadSampleAsync());
        Assert.Equal(2, discarded.Count);
        Assert.Contains("Decode time", discarded[1].Reason);
    }

    [Fact]
    public async Task TruncatedOuterMoofRemainsFatal()
    {
        var bytes = CreateFragments();
        var moof = bytes.AsSpan().IndexOf("moof"u8) - 4;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(moof), (uint)bytes.Length + 100);
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { SkipInvalidFragments = true });
        await Assert.ThrowsAsync<Mp4FormatException>(async () => await reader.ReadSampleAsync());
    }

    private static byte[] CreateFragments()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0, duration: 1000);
            writer.FlushFragment();
            writer.WriteSample(track, new byte[] { 0, 1, 66 }, 1000, 1000, duration: 1000);
            writer.Complete();
        }
        return output.ToArray();
    }

    private sealed class TestStream(byte[] bytes, bool seekable) : MemoryStream(bytes)
    {
        public override bool CanSeek => seekable;
        public override long Length => seekable ? base.Length : throw new NotSupportedException();
    }
}
