using System.Text;
using Mp4BoxKit;
using Mp4BoxKit.Inspection;
using Xunit;

namespace Mp4BoxKit.Inspection.Tests;

public sealed class InspectorTests
{
    private static readonly ReadOnlyMemory<byte> AvcC = new byte[]
    {
        0x01, 0x42, 0x00, 0x1E,
        0xFF, 0xE1,
        0x00, 0x01, 0x67,
        0x01,
        0x00, 0x01, 0x68
    };

    [Fact]
    public async Task FragmentedFileReportsBoxesSamplesAndSegments()
    {
        var bytes = CreateFragmentedFile();
        var inspection = await Mp4Inspector.InspectAsync(
            bytes,
            "fragmented.mp4",
            new InspectOptions
            {
                IncludeSamples = true,
                MaxSamples = 10
            });

        Assert.True(inspection.Movie.IsFragmented);
        Assert.Equal(1, inspection.Movie.TrackCount);
        Assert.Single(inspection.Tracks);
        Assert.Equal("H264", inspection.Tracks[0].Codec);
        Assert.Contains(inspection.Boxes, box => box.Type == "ftyp");
        Assert.Contains(inspection.Boxes, box => box.Type == "moov");
        Assert.Contains(inspection.Boxes, box => box.Type == "moof");
        Assert.Contains(inspection.Boxes, box => box.Type == "mdat");
        Assert.NotEmpty(inspection.Samples);
        Assert.NotEmpty(inspection.Segments);

        var json = InspectionJson.Serialize(inspection);
        Assert.Contains("\"isFragmented\"", json);
        Assert.Contains("\"segments\"", json);
        Assert.Contains("\"samples\"", json);

        var boxesCsv = InspectionCsv.Boxes(inspection.Boxes);
        Assert.StartsWith("offset,size,headerSize,depth,type,path", boxesCsv);
        Assert.Contains("ftyp", boxesCsv);

        var samplesCsv = InspectionCsv.Samples(inspection.Samples);
        Assert.StartsWith("track,pts,dts,duration,size,keyFrame", samplesCsv);

        var segmentsCsv = InspectionCsv.Segments(inspection.Segments);
        Assert.StartsWith(
            "sequenceNumber,offset,size,moofOffset,mdatOffset,mdatSize",
            segmentsCsv);
    }

    [Fact]
    public async Task ClassicFileReportsClassicLayout()
    {
        var bytes = CreateClassicFile();
        var inspection = await Mp4Inspector.InspectAsync(
            bytes,
            "classic.mp4",
            new InspectOptions
            {
                IncludeSamples = true,
                MaxSamples = 10
            });

        Assert.False(inspection.Movie.IsFragmented);
        Assert.Single(inspection.Tracks);
        Assert.Contains(inspection.Boxes, box => box.Type == "moov");
        Assert.DoesNotContain(inspection.Boxes, box => box.Type == "moof");
        Assert.NotEmpty(inspection.Samples);
    }

    [Fact]
    public void BoxLimitProducesWarning()
    {
        var inspection = Mp4Inspector.Inspect(
            CreateClassicFile(),
            "classic.mp4",
            new InspectOptions
            {
                MaxBoxes = 2
            });

        Assert.NotEmpty(inspection.Warnings);
        Assert.Contains(inspection.Warnings,
            warning => warning.Contains("Box limit", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionsRoundTripThroughJson()
    {
        var options = InspectionJson.DeserializeOptions(
            """{"maxDepth":4,"includeSamples":true,"maxSamples":3}""");

        Assert.Equal(4, options.MaxDepth);
        Assert.True(options.IncludeSamples);
        Assert.Equal(3, options.MaxSamples);
        Assert.Equal(10_000, options.MaxBoxes);
    }

    private static byte[] CreateFragmentedFile()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264,
                16,
                16,
                90_000,
                AvcC));
            writer.WriteSample(
                track,
                new byte[] { 0x00, 0x00, 0x00, 0x02, 0x65, 0x88 },
                0,
                0,
                SampleFlags.KeyFrame);
            writer.WriteSample(
                track,
                new byte[] { 0x00, 0x00, 0x00, 0x02, 0x41, 0x88 },
                3_000,
                3_000,
                SampleFlags.None);
            writer.Complete();
        }

        return output.ToArray();
    }

    private static byte[] CreateClassicFile()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddVideo(new VideoTrackOptions(
                Codec.H264,
                16,
                16,
                90_000,
                AvcC));
            writer.WriteSample(
                track,
                new byte[] { 0x00, 0x00, 0x00, 0x02, 0x65, 0x88 },
                0,
                0,
                SampleFlags.KeyFrame);
            writer.WriteSample(
                track,
                new byte[] { 0x00, 0x00, 0x00, 0x02, 0x41, 0x88 },
                3_000,
                3_000,
                SampleFlags.None);
            writer.Complete();
        }

        return output.ToArray();
    }
}
