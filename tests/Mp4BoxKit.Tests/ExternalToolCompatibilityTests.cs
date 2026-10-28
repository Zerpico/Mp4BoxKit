using System.Diagnostics;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class ExternalToolCompatibilityTests
{
    [Fact]
    public async Task MP4BoxAndBento4AcceptGeneratedFiles()
    {
        var mp4box = Environment.GetEnvironmentVariable("MP4BOXKIT_MP4BOX");
        var mp4dump = Environment.GetEnvironmentVariable("MP4BOXKIT_MP4DUMP");
        if (string.IsNullOrWhiteSpace(mp4box))
            return;

        var directory = Path.Combine(
            Path.GetTempPath(), "fmp4-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var classic = Path.Combine(directory, "classic.mp4");
            var fragmented = Path.Combine(directory, "fragmented.mp4");
            CreateClassic(classic);
            CreateFragmented(fragmented);

            foreach (var file in new[] { classic, fragmented })
            {
                var info = await Run(mp4box, "-info", file);
                Assert.Equal(0, info.ExitCode);
                Assert.DoesNotContain("Invalid IsoMedia", info.Output,
                    StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(mp4dump))
                {
                    var dump = await Run(mp4dump, "--verbosity", "1", file);
                    Assert.Equal(0, dump.ExitCode);
                    Assert.Contains("[ftyp]", dump.Output);
                    Assert.Contains("[moov]", dump.Output);
                    Assert.Contains("[mdat]", dump.Output);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CreateClassic(string path)
    {
        using var output = File.Create(path);
        using var writer = new Mp4FileWriter(new StreamMp4Output(output));
        var track = writer.AddVideo(new VideoTrackOptions(
            Codec.H264, 160, 96, CodecConfiguration: TestMedia.AvcC));
        writer.WriteSample(track, TestMedia.VideoSample(), 0, 0, SampleFlags.KeyFrame);
        writer.Complete();
    }

    private static void CreateFragmented(string path)
    {
        using var output = File.Create(path);
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output),
            new Mp4FragmentedWriterOptions { WriteSegmentIndex = true });
        var track = writer.AddVideo(new VideoTrackOptions(
            Codec.H264, 160, 96, CodecConfiguration: TestMedia.AvcC));
        writer.WriteSample(track, TestMedia.VideoSample(), 0, 0, SampleFlags.KeyFrame);
        writer.Complete();
    }

    private static async Task<(int ExitCode, string Output)> Run(
        string executable,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Failed to start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }
}
