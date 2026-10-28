using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using Mp4BoxKit;
using Xunit;

namespace Mp4BoxKit.FFmpeg.Tests;

public sealed class H264AccessUnitTests
{
    [Fact]
    public async Task FirstPacketWithSpsPpsAndIdrIsWrittenAsOneSample()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "fmp4-h264-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.mp4");
            await CreateSource(source);
            using var probe = await Probe(source);
            var stream = probe.RootElement.GetProperty("streams")[0];
            var configuration = ParseHexDump(stream.GetProperty("extradata").GetString()!);
            Assert.Equal(1, configuration[0]); // avcC

            var sourceBytes = await File.ReadAllBytesAsync(source);
            var packets = probe.RootElement.GetProperty("packets");
            var first = ReadPacket(sourceBytes, packets[0]);
            var firstTypes = NalTypes(first);
            Assert.Contains(7, firstTypes); // SPS
            Assert.Contains(8, firstTypes); // PPS
            Assert.Contains(5, firstTypes); // IDR

            var target = Path.Combine(directory, "target.mp4");
            using (var output = File.Create(target))
            using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
            {
                var track = writer.AddVideo(new VideoTrackOptions(
                    Codec.H264, 160, 96, 10_000, configuration));
                foreach (var packet in packets.EnumerateArray())
                {
                    writer.WriteSample(track, ReadPacket(sourceBytes, packet),
                        packet.GetProperty("pts").GetInt64(),
                        packet.GetProperty("dts").GetInt64(),
                        packet.GetProperty("flags").GetString()!.Contains('K')
                            ? SampleFlags.KeyFrame : SampleFlags.None);
                }
                writer.Complete();
            }

            await AssertSamplesMatchPackets(target, packets.GetArrayLength());
            await AssertFramesMatch(source, target);
            SaveArtifact(target, "h264-first-packet-fmp4.mp4");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RtspStyleOutOfBandSpsPpsAndVclOnlySamplesDecode()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "fmp4-h264-rtsp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.mp4");
            await CreateSource(source);
            using var probe = await Probe(source);
            var stream = probe.RootElement.GetProperty("streams")[0];
            var configuration = ParseHexDump(stream.GetProperty("extradata").GetString()!);
            var sourceBytes = await File.ReadAllBytesAsync(source);
            var packets = probe.RootElement.GetProperty("packets");

            var target = Path.Combine(directory, "target.mp4");
            using (var output = File.Create(target))
            using (var writer = new Mp4FragmentedWriter(new StreamMp4Output(output)))
            {
                var track = writer.AddVideo(new VideoTrackOptions(
                    Codec.H264, 160, 96, 10_000, configuration));
                foreach (var packet in packets.EnumerateArray())
                {
                    var nalUnits = NalUnits(ReadPacket(sourceBytes, packet))
                        .Select(nal => (ReadOnlyMemory<byte>)nal)
                        .ToArray();
                    writer.WriteSampleNalUnits(track, nalUnits,
                        packet.GetProperty("pts").GetInt64(),
                        packet.GetProperty("dts").GetInt64(),
                        packet.GetProperty("flags").GetString()!.Contains('K')
                            ? SampleFlags.KeyFrame : SampleFlags.None);
                }
                writer.Complete();
            }

            using (var input = File.OpenRead(target))
            {
                var reader = new Mp4Reader(input);
                var count = 0;
                while (await reader.ReadSampleAsync() is { } sample)
                {
                    var types = NalTypes(sample.Data.Span);
                    Assert.DoesNotContain(7, types);
                    Assert.DoesNotContain(8, types);
                    count++;
                }
                Assert.Equal(packets.GetArrayLength(), count);
            }

            await AssertFramesMatch(source, target);
            SaveArtifact(target, "h264-rtsp-style-fmp4.mp4");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void SaveArtifact(string source, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        File.Copy(source, Path.Combine(directory, name), overwrite: true);
    }

    private static async Task CreateSource(string path)
    {
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i",
            "testsrc2=size=160x96:rate=10", "-frames:v", "20", "-c:v", "libx264",
            "-pix_fmt", "yuv420p", "-bf", "0", "-g", "5", "-sc_threshold", "0",
            "-video_track_timescale", "10000", "-avoid_negative_ts", "make_zero",
            "-use_editlist", "0", "-x264-params", "repeat-headers=1",
            "-y", path);
    }

    private static async Task<JsonDocument> Probe(string path)
        => JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-select_streams",
            "v:0", "-show_streams", "-show_packets", "-show_data", "-of", "json", path));

    private static async Task AssertSamplesMatchPackets(string path, int expected)
    {
        using var input = File.OpenRead(path);
        var reader = new Mp4Reader(input);
        var count = 0;
        while (await reader.ReadSampleAsync() is not null)
            count++;
        Assert.Equal(expected, count);
    }

    private static async Task AssertFramesMatch(string source, string target)
    {
        var expected = await Run("ffmpeg", "-v", "error", "-i", source,
            "-map", "0:v:0", "-f", "framemd5", "-");
        var actual = await Run("ffmpeg", "-v", "error", "-xerror", "-i", target,
            "-map", "0:v:0", "-f", "framemd5", "-");
        Assert.Equal(expected, actual);
    }

    private static ReadOnlySpan<byte> ReadPacket(byte[] source, JsonElement packet)
    {
        var offset = int.Parse(packet.GetProperty("pos").GetString()!);
        var size = int.Parse(packet.GetProperty("size").GetString()!);
        return source.AsSpan(offset, size);
    }

    private static List<byte[]> NalUnits(ReadOnlySpan<byte> data)
    {
        var result = new List<byte[]>();
        var offset = 0;
        while (offset < data.Length)
        {
            Assert.True(offset + 4 <= data.Length);
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += 4;
            Assert.True(length != 0 && length <= data.Length - offset);
            result.Add(data.Slice(offset, (int)length).ToArray());
            offset += (int)length;
        }
        return result;
    }

    private static List<int> NalTypes(ReadOnlySpan<byte> data)
        => NalUnits(data).Select(nal => nal[0] & 0x1F).ToList();

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
