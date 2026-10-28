using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentAddressingTests
{
    [Fact]
    public async Task SeekableReaderReadsTwoMdatsIncludingMediaBeforeMoof()
    {
        using var init = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(init)))
        {
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            _ = writer.InitializationSegment;
        }
        byte[] Box(string type, byte[] payload)
        {
            var result = new byte[payload.Length + 8];
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(result, 4);
            payload.CopyTo(result, 8);
            return result;
        }
        byte[] Words(params uint[] values)
        {
            var result = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4), values[i]);
            return result;
        }
        var firstMedia = Box("mdat", new byte[] { 0, 1, 65 });
        var tfhd = Box("tfhd", Words(0x020018, 1, 1000, 3));
        var run1 = Box("trun", Words(1, 1, 0));
        var run2 = Box("trun", Words(1, 1, 0));
        var moof = Box("moof", Box("traf", tfhd.Concat(run1).Concat(run2).ToArray()));
        var firstRun = moof.AsSpan().IndexOf("trun"u8);
        var secondRun = firstRun + 4 + moof.AsSpan(firstRun + 4).IndexOf("trun"u8);
        BinaryPrimitives.WriteInt32BigEndian(moof.AsSpan(firstRun + 12), -3);
        BinaryPrimitives.WriteInt32BigEndian(moof.AsSpan(secondRun + 12), moof.Length + 8);
        using var input = new MemoryStream(init.ToArray().Concat(firstMedia).Concat(moof)
            .Concat(Box("mdat", new byte[] { 0, 1, 66 })).ToArray());
        var reader = new Mp4Reader(input);
        for (var i = 0; i < 2; i++)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(i * 1000L, sample.Dts);
            Assert.Equal(new byte[] { 0, 1, (byte)(65 + i) }, sample.Data.ToArray());
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultBaseIsMoofUsesMoofStartNotMediaStart(bool invalidZeroOffset)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 0, 0);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var moofStart = bytes.AsSpan().IndexOf("moof"u8) - 4;
        var tfhd = bytes.AsSpan().IndexOf("tfhd"u8);
        var trun = bytes.AsSpan().IndexOf("trun"u8);
        var payloadStart = bytes.AsSpan().IndexOf("mdat"u8) + 4;
        var flags = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(tfhd + 4));
        Assert.Equal(0u, flags & 1); // no explicit base field
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tfhd + 4), flags | 0x020000);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(trun + 12),
            invalidZeroOffset ? 0 : payloadStart - moofStart);
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        if (invalidZeroOffset)
        {
            await Assert.ThrowsAsync<Mp4FormatException>(async () => await reader.ReadSampleAsync());
            return;
        }
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(new byte[] { 0, 1, 65 }, sample.Data.ToArray());
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Fact]
    public async Task ExplicitAbsoluteBaseAndMissingTfdtAreApplied()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 1000, 1000);
            writer.WriteSample(track, new byte[] { 0, 1, 66 }, 2000, 2000);
            writer.FlushFragment();
            writer.WriteSample(track, new byte[] { 0, 1, 67 }, 3000, 3000);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var moof = bytes.AsSpan().IndexOf("moof"u8);
        var traf = moof + bytes.AsSpan(moof).IndexOf("traf"u8);
        var tfhd = traf + bytes.AsSpan(traf).IndexOf("tfhd"u8);
        var trun = tfhd + bytes.AsSpan(tfhd).IndexOf("trun"u8);
        var mdatPayload = bytes.AsSpan().IndexOf("mdat"u8) + 4;
        foreach (var box in new[] { moof, traf, tfhd })
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(box - 4),
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(box - 4)) + 8);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tfhd + 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(trun + 12), 0);
        var absoluteBase = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(absoluteBase, (ulong)(mdatPayload + 8));
        bytes = bytes.Take(tfhd + 12).Concat(absoluteBase).Concat(bytes.Skip(tfhd + 12)).ToArray();
        var secondMoof = moof + 4 + bytes.AsSpan(moof + 4).IndexOf("moof"u8);
        var secondTfdt = secondMoof + bytes.AsSpan(secondMoof).IndexOf("tfdt"u8);
        "free"u8.CopyTo(bytes.AsSpan(secondTfdt));
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        for (var i = 0; i < 3; i++)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal((i + 1) * 1000L, sample.Dts);
            Assert.Equal(new byte[] { 0, 1, (byte)(65 + i) }, sample.Data.ToArray());
        }
    }

    [Fact]
    public async Task ReadsMediaAfterInterveningFreeBoxAndExtendedMdatHeader()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, 1000, 1000);
            writer.Complete();
        }
        var bytes = output.ToArray();
        var mdat = bytes.AsSpan().IndexOf("mdat"u8) - 4;
        var trun = bytes.AsSpan().IndexOf("trun"u8);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(trun + 12),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(trun + 12)) + 16);
        var replacement = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(replacement, 8);
        "free"u8.CopyTo(replacement.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(8), 1);
        "mdat"u8.CopyTo(replacement.AsSpan(12));
        BinaryPrimitives.WriteUInt64BigEndian(replacement.AsSpan(16), 19);
        using var input = new MemoryStream(bytes.Take(mdat).Concat(replacement)
            .Concat(bytes.Skip(mdat + 8)).ToArray());
        var reader = new Mp4Reader(input);
        var sample = await reader.ReadSampleAsync();
        Assert.NotNull(sample);
        Assert.Equal(1000, sample.Dts);
        Assert.Equal(new byte[] { 0, 1, 65 }, sample.Data.ToArray());
        Assert.Null(await reader.ReadSampleAsync());
    }
}
