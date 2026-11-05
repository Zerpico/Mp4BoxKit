using System.Buffers.Binary;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ClassicTableValidationTests
{
    [Theory]
    [InlineData("stts", 4, 2u)] // Declared entry absent.
    [InlineData("stts", 8, 2u)] // Too many timed samples.
    [InlineData("stts", 8, 0u)] // Empty run.
    [InlineData("stsc", 4, 2u)]
    [InlineData("stsc", 8, 2u)] // First mapping must start at chunk 1.
    [InlineData("stsc", 12, 0u)]
    [InlineData("stsc", 12, 2u)] // Mapping exceeds sample count.
    [InlineData("stsc", 16, 0u)] // Description indexes are one-based.
    [InlineData("stco", 4, 2u)]
    public async Task RejectsInconsistentClassicTable(string type, int fieldOffset, uint value)
    {
        var bytes = CreateFile();
        var payload = bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(type)) + 4;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(payload + fieldOffset), value);
        using var input = new MemoryStream(bytes);
        var reader = new Mp4Reader(input);
        await Assert.ThrowsAsync<Mp4FormatException>(async () => await reader.ReadSampleAsync());
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    public async Task PresentCompositionTableMustCoverSamples(uint count)
    {
        var bytes = CreateFile(composition: true);
        var payload = bytes.AsSpan().IndexOf("ctts"u8) + 4;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(payload + 8), count);
        using var input = new MemoryStream(bytes);
        await Assert.ThrowsAsync<Mp4FormatException>(async () =>
            await new Mp4Reader(input).ReadSampleAsync());
    }

    private static byte[] CreateFile(bool composition = false)
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.Tx3g));
            writer.WriteSample(track, new byte[] { 0, 1, 65 }, composition ? 1 : 0, 0,
                duration: 1000);
            writer.Complete();
        }
        return output.ToArray();
    }
}
