using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentDefaultsTests
{
    [Fact]
    public async Task UsesTrexDefaultsWhenRunOmitsSampleFields()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            _ = writer.InitializationSegment;
        }
        // Use only the first init segment (Dispose may append an empty completion).
        var init = output.ToArray();
        var trex = init.AsSpan().IndexOf("trex"u8);
        Write(init, trex + 16, 1000);
        Write(init, trex + 20, 3);
        Write(init, trex + 24, 0x10000);
        var tfhd = Box("tfhd", Words(0x20000, 1));
        var tfdt = Box("tfdt", Words(0, 0));
        var run = Box("trun", Words(1, 2, 0));
        var moof = Box("moof", Box("traf", tfhd.Concat(tfdt).Concat(run).ToArray()));
        var trun = moof.AsSpan().IndexOf("trun"u8);
        Write(moof, trun + 12, (uint)(moof.Length + 8));
        using var input = new MemoryStream(init.Concat(moof)
            .Concat(Box("mdat", new byte[] { 0, 1, 65, 0, 1, 66 })).ToArray());
        var reader = new Mp4Reader(input);
        for (var i = 0; i < 2; i++)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(i * 1000L, sample.Dts);
            Assert.Equal(1000, sample.Info.Duration);
            Assert.Equal(SampleFlags.None, sample.Info.Flags);
            Assert.Equal(new byte[] { 0, 1, (byte)(65 + i) }, sample.Data.ToArray());
        }
    }

    private static byte[] Words(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            Write(bytes, i * 4, values[i]);
        return bytes;
    }
    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static byte[] Box(string type, byte[] payload) => Words((uint)(payload.Length + 8))
        .Concat(System.Text.Encoding.ASCII.GetBytes(type)).Concat(payload).ToArray();
}
