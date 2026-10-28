using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Mp4BoxKit.FFmpeg.Tests;

public sealed class H264RemuxTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task EncodedAccessUnits_RoundTripThroughWriter(bool fragmented, int bFrames)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fmp4-integration-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.mp4");
            var target = Path.Combine(directory, "target.mp4");
            await Run(
                "ffmpeg",
                "-v", "error",
                "-f", "lavfi",
                "-i", "testsrc2=size=160x96:rate=10",
                "-frames:v", "20",
                "-c:v", "libx264",
                "-pix_fmt", "yuv420p",
                "-bf", bFrames.ToString(CultureInfo.InvariantCulture),
                "-g", "5",
                "-sc_threshold", "0",
                "-video_track_timescale", "10000",
                "-avoid_negative_ts", "make_zero",
                "-use_editlist", "0",
                "-y", source);

            using var probe = await Probe(source);
            var stream = probe.RootElement.GetProperty("streams")[0];
            var configuration = ParseHexDump(stream.GetProperty("extradata").GetString()!);
            var sourceBytes = await File.ReadAllBytesAsync(source);
            var packets = probe.RootElement.GetProperty("packets");
            await AssertReader(source, probe.RootElement);
            using (var output = File.Create(target))
            {
                using Mp4Writer writer = fragmented
                    ? new Mp4FragmentedWriter(new StreamMp4Output(output))
                    : new Mp4FileWriter(new StreamMp4Output(output));
                var track = writer.AddVideo(new VideoTrackOptions(Codec.H264,
                    160, 96, 10000, configuration));
                foreach (var packet in packets.EnumerateArray())
                {
                    var offset = int.Parse(packet.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
                    var size = int.Parse(packet.GetProperty("size").GetString()!, CultureInfo.InvariantCulture);
                    writer.WriteSample(track, sourceBytes.AsSpan(offset, size),
                        packet.GetProperty("pts").GetInt64(), packet.GetProperty("dts").GetInt64(),
                        packet.GetProperty("flags").GetString()!.Contains('K')
                            ? SampleFlags.KeyFrame : SampleFlags.None);
                }
                writer.Complete();
            }

            using var result = await Probe(target);
            await AssertReader(target, result.RootElement);
            var actualPackets = result.RootElement.GetProperty("packets");
            Assert.Equal(20, actualPackets.GetArrayLength());
            Assert.Equal("h264", result.RootElement.GetProperty("streams")[0]
                .GetProperty("codec_name").GetString());
            var targetBytes = await File.ReadAllBytesAsync(target);
            for (var i = 0; i < packets.GetArrayLength(); i++)
            {
                var expected = packets[i];
                var actual = actualPackets[i];
                Assert.Equal(expected.GetProperty("pts").GetInt64(), actual.GetProperty("pts").GetInt64());
                Assert.Equal(expected.GetProperty("dts").GetInt64(), actual.GetProperty("dts").GetInt64());
                Assert.Equal(expected.GetProperty("duration").GetInt64(), actual.GetProperty("duration").GetInt64());
                var size = int.Parse(expected.GetProperty("size").GetString()!, CultureInfo.InvariantCulture);
                Assert.Equal(size, int.Parse(actual.GetProperty("size").GetString()!, CultureInfo.InvariantCulture));
                var from = int.Parse(expected.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
                var to = int.Parse(actual.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
                Assert.True(sourceBytes.AsSpan(from, size).SequenceEqual(targetBytes.AsSpan(to, size)));
            }
            var expectedFrames = await Run("ffmpeg", "-v", "error", "-i", source,
                "-map", "0:v:0", "-f", "framemd5", "-");
            var actualFrames = await Run("ffmpeg", "-v", "error", "-xerror", "-i", target,
                "-map", "0:v:0", "-f", "framemd5", "-");
            Assert.Equal(expectedFrames, actualFrames);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<JsonDocument> Probe(string path) => JsonDocument.Parse(
        await Run("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_streams",
            "-show_packets", "-show_data", "-of", "json", path));

    private static async Task AssertReader(string path, JsonElement reference)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        await using var input = File.OpenRead(path);
        var reader = new Mp4Reader(input);
        var probe = await reader.ReadContainerInfoAsync();
        Assert.Equal(ProbeStatus.Complete, probe.Status);
        var track = Assert.IsType<VideoTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal(Codec.H264, track.Codec);
        Assert.Equal(160, track.Width);
        Assert.Equal(96, track.Height);
        Assert.Equal(10000u, track.TimeScale);
        Assert.Equal(ParseHexDump(reference.GetProperty("streams")[0]
            .GetProperty("extradata").GetString()!), track.CodecConfiguration.ToArray());
        foreach (var packet in reference.GetProperty("packets").EnumerateArray())
        {
            var sample = await reader.ReadSampleAsync();
            Assert.NotNull(sample);
            Assert.Equal(packet.GetProperty("pts").GetInt64(), sample.Pts);
            Assert.Equal(packet.GetProperty("dts").GetInt64(), sample.Dts);
            Assert.Equal(packet.GetProperty("duration").GetInt32(), sample.Info.Duration);
            var offset = int.Parse(packet.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
            var size = int.Parse(packet.GetProperty("size").GetString()!, CultureInfo.InvariantCulture);
            Assert.True(bytes.AsSpan(offset, size).SequenceEqual(sample.Data.Span));
            Assert.Equal(packet.GetProperty("flags").GetString()!.Contains('K'),
                (sample.Info.Flags & SampleFlags.KeyFrame) != 0);
        }
        Assert.Null(await reader.ReadSampleAsync());
    }

    private static byte[] ParseHexDump(string dump)
    {
        var bytes = new List<byte>();
        foreach (var line in dump.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;
            var hex = line[(colon + 1)..].TrimStart();
            var end = hex.IndexOf("  ", StringComparison.Ordinal);
            if (end >= 0)
                hex = hex[..end];
            bytes.AddRange(Convert.FromHexString(hex.Replace(" ", "")));
        }
        return bytes.ToArray();
    }

    private static async Task<string> Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var diagnostics = await error;
        Assert.True(process.ExitCode == 0, $"{executable} failed: {diagnostics}");
        return await output;
    }
}
