using System.Diagnostics;
using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.FFmpeg.Tests;

public sealed class AacAdtsCliTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdtsAacIsRemuxedToPlayableMp4(bool fragmented)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "fmp4-adts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var adts = Path.Combine(directory, "source.aac");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i",
                "sine=frequency=440:duration=1", "-c:a", "aac", "-b:a", "96k",
                "-f", "adts", "-y", adts);

            var source = await File.ReadAllBytesAsync(adts);
            var frames = new List<ReadOnlyMemory<byte>>();
            var first = default(AacAdtsHeader);
            var offset = 0;
            while (offset < source.Length)
            {
                Assert.True(AacAdts.TryParse(source.AsSpan(offset), out var header));
                if (frames.Count == 0)
                    first = header;
                Assert.Equal(first.SampleRate, header.SampleRate);
                Assert.Equal(first.ChannelConfiguration, header.ChannelConfiguration);
                frames.Add(AacAdts.GetPayload(source.AsSpan(offset)).ToArray());
                offset += header.FrameLength;
            }
            Assert.True(frames.Count >= 10);

            var target = Path.Combine(directory, "target.mp4");
            using (var output = File.Create(target))
            {
                using Mp4Writer writer = fragmented
                    ? new Mp4FragmentedWriter(new StreamMp4Output(output))
                    : new Mp4FileWriter(new StreamMp4Output(output));
                var track = writer.AddAudio(new AudioTrackOptions(
                    Codec.Aac, first.ChannelConfiguration, first.SampleRate,
                    TimeScale: 48_000,
                    CodecConfiguration: AacAdts.CreateEsds(source)));
                for (var i = 0; i < frames.Count; i++)
                {
                    writer.WriteSample(track, frames[i].Span,
                        i * 1024L, i * 1024L, SampleFlags.KeyFrame, duration: 1024);
                }
                writer.Complete();
            }

            await Run("ffmpeg", "-v", "error", "-xerror", "-i", target,
                "-f", "null", "-");

            var artifacts = Environment.GetEnvironmentVariable("MP4BOXKIT_ARTIFACTS_DIR");
            if (!string.IsNullOrWhiteSpace(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                File.Copy(target, Path.Combine(artifacts,
                    fragmented ? "aac-adts-fmp4.mp4" : "aac-adts-classic.mp4"),
                    overwrite: true);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task Run(string executable, params string[] arguments)
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
        _ = await output;
    }
}
