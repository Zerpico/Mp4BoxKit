using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class LegacyFragmentReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsOneFragmentPerCallUsingExplicitBaseAndPadding(bool seekable)
    {
        using var init = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(init)))
        {
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            _ = writer.InitializationSegment;
        }
        byte[] Fragment(long start, uint time, byte value)
        {
            var tfhd = Box("tfhd", Words(0x19, 1, 0, 0, 1000, 3));
            var moof = Box("moof", Box("traf", tfhd
                .Concat(Box("tfdt", Words(0, time)))
                .Concat(Box("trun", Words(1, 1, 8))).ToArray()));
            var baseField = moof.AsSpan().IndexOf("tfhd"u8) + 12;
            BinaryPrimitives.WriteUInt64BigEndian(moof.AsSpan(baseField), (ulong)(start + moof.Length + 8));
            return moof.Concat(Box("mdat", new byte[8].Concat(new byte[] { 0, 1, value }).ToArray())).ToArray();
        }
        var first = Fragment(init.Length, 0, 65);
        var second = Fragment(init.Length + first.Length, 1000, 66);
        using var input = new TestStream(init.ToArray().Concat(first).Concat(second).ToArray(), seekable);
        var reader = new Mp4Reader(input);
        var values = new List<byte[]>();
        var times = new List<long>();
        void Handle(in SampleInfo info, ReadOnlySpan<byte> data)
        {
            values.Add(data.ToArray());
            times.Add(info.Dts);
        }
#pragma warning disable CS0618
        Assert.Equal(1, await reader.ReadFragmentSamplesAsync(Handle));
        Assert.Single(values);
        Assert.Equal(1, await reader.ReadFragmentSamplesAsync(Handle));
        Assert.Equal(0, await reader.ReadFragmentSamplesAsync(Handle));
#pragma warning restore CS0618
        Assert.Equal(new byte[] { 0, 1, 65 }, values[0]);
        Assert.Equal(new byte[] { 0, 1, 66 }, values[1]);
        Assert.Equal(new long[] { 0, 1000 }, times);
        Assert.Single(reader.Info.Tracks);
    }

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

    private sealed class TestStream(byte[] bytes, bool seekable) : MemoryStream(bytes)
    {
        public override bool CanSeek => seekable;
        public override long Length => seekable ? base.Length : throw new NotSupportedException();
    }
}
