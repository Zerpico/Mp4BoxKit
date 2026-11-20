using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class FragmentMediaBudgetTests
{
    [Theory]
    [InlineData(false, 511)]
    [InlineData(true, 511)]
    [InlineData(false, 512)]
    [InlineData(true, 512)]
    public async Task RepeatedReferencesShareBudgetAcrossRunsAndTracks(bool multipleTracks, int limit)
    {
        using var initialization = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(initialization)))
        {
            writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            if (multipleTracks)
                writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            _ = writer.InitializationSegment;
        }

        byte[] Track(uint id, int runCount) => Box("traf",
            Box("tfhd", Words(0x020018, id, 1000, 256))
                .Concat(Enumerable.Range(0, runCount)
                    .SelectMany(_ => Box("trun", Words(1, 1, 0)))).ToArray());

        var moof = Box("moof", multipleTracks
            ? Track(1, 1).Concat(Track(2, 1)).ToArray()
            : Track(1, 2));
        for (var offset = 0; offset < moof.Length - 4; offset++)
            if (moof.AsSpan(offset, 4).SequenceEqual("trun"u8))
                BinaryPrimitives.WriteInt32BigEndian(moof.AsSpan(offset + 12), moof.Length + 8);

        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var bytes = initialization.ToArray().Concat(moof).Concat(Box("mdat", payload)).ToArray();
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input, new Mp4ProbeOptions { MaxBoxPayloadBytes = limit });
        if (limit < 512)
        {
            var error = await Assert.ThrowsAsync<Mp4FormatException>(async () =>
                await reader.ReadSampleAsync());
            Assert.Contains("Fragment media", error.Message);
            // Rejection happens before scanning mdat regions or reading sample bytes.
            Assert.Equal(initialization.Length + moof.Length, input.Position);
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(payload, sample.Data.ToArray());
        }
        Assert.Null(await reader.ReadSampleAsync());
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
}
