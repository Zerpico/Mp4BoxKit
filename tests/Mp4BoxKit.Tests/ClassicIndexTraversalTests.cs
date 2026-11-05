using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ClassicIndexTraversalTests
{
    [Fact]
    public async Task PreservesTimingAndPayloadAcrossShortRuns()
    {
        var bytes = CreateFile(300);
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        long dts = 0;
        for (var i = 0; i < 300; i++)
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(dts, sample.Dts);
            Assert.Equal(dts + Composition(i), sample.Pts);
            Assert.Equal(Duration(i), sample.Info.Duration);
            Assert.Equal(new byte[] { 0, 1, (byte)i }, sample.Data.ToArray());
            dts += Duration(i);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    [Fact]
    public void IndexConstructionHasBoundedAllocations()
    {
        // Seek builds the index without allocating sample payloads. MemoryStream
        // completes these reads synchronously, so per-thread measurement is stable.
        BuildIndex(CreateFile(10));
        const int count = 20_000;
        var bytes = CreateFile(count);
        var before = GC.GetAllocatedBytesForCurrentThread();
        BuildIndex(bytes);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Includes two moov reads and backing arrays for parsed run tables,
        // but leaves no room for another full ClassicSample array.
        Assert.True(allocated < count * 192L + 256_000,
            $"Index construction allocated {allocated:N0} bytes for {count} samples.");
    }

    private static void BuildIndex(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        new Mp4Reader(input).SeekAsync(TimeSpan.Zero).GetAwaiter().GetResult();
    }

    private static int Duration(int index) => index % 2 == 0 ? 10 : 20;
    private static int Composition(int index) => index % 3;

    private static byte[] CreateFile(int count)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            var payload = new byte[] { 0, 1, 0 };
            long dts = 0;
            for (var i = 0; i < count; i++)
            {
                payload[2] = (byte)i;
                writer.WriteSample(track, payload, dts + Composition(i), dts,
                    SampleFlags.KeyFrame, Duration(i));
                dts += Duration(i);
            }
            writer.Complete();
        }
        return output.ToArray();
    }
}
