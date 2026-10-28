using Xunit;
using Xunit.Abstractions;

namespace Mp4BoxKit.FFmpeg.Tests;

public sealed class SegmentMemoryTests(ITestOutputHelper log)
{
    [Fact]
    public async Task RepeatedEncodedPacketsDoNotAccumulateCompletedSegments()
    {
        var video = await Read(NativeEncoderTests.EncodeAndWrite(true).File);
        var audio = await Read(NativeAacTests.Generate(true));
        var delivered = 0;
        using var writer = new Mp4FragmentedWriter(
            new Mp4FragmentedWriterOptions
            {
                FragmentBoundary = Fmp4FragmentBoundary.Manual,
                MaxFragmentBytes = 1024 * 1024,
                MaxFragmentSamples = 1000
            },
            segment =>
            {
                if (segment.Kind == Fmp4SegmentKind.Media)
                {
                    delivered++;
                }
            });

        var v = (VideoTrackInfo)video.Info;
        var a = (AudioTrackInfo)audio.Info;
        var videoTrack = writer.AddVideo(new VideoTrackOptions(
            Codec.H264,
            v.Width,
            v.Height,
            10,
            v.CodecConfiguration));
        var audioTrack = writer.AddAudio(new AudioTrackOptions(
            Codec.Aac,
            1,
            48_000,
            CodecConfiguration: a.CodecConfiguration));

        void Run(int count, int first)
        {
            for (var segment = first; segment < first + count; segment++)
            {
                for (var i = 0; i < video.Samples.Count; i++)
                {
                    var sample = video.Samples[i];
                    writer.WriteSample(
                        videoTrack,
                        sample.Data.Span,
                        segment * 20L + i,
                        segment * 20L + i,
                        sample.Info.Flags);
                }

                for (var i = 0; i < audio.Samples.Count; i++)
                {
                    writer.WriteSample(
                        audioTrack,
                        audio.Samples[i].Data.Span,
                        segment * 96000L + i * 1024L,
                        segment * 96000L + i * 1024L);
                }

                writer.FlushFragment();
            }
        }
        Run(100, 0);
        var baseline = GC.GetTotalMemory(true);
        var allocated = GC.GetTotalAllocatedBytes(true);
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        Run(3000, 100);
        var allocationDelta = GC.GetTotalAllocatedBytes(true) - allocated;
        var retained = GC.GetTotalMemory(true) - baseline;
        log.WriteLine(
            $"3000 segments: allocated={allocationDelta:N0} bytes; " +
            $"retained delta={retained:N0} bytes");
        log.WriteLine(
            "GC counts (including final forced collection): " +
            string.Join(
                ", ",
                Enumerable.Range(0, 3)
                    .Select(i => GC.CollectionCount(i) - collections[i])));
        Assert.True(retained < 16 * 1024 * 1024, $"Retained growth: {retained}");
        Assert.Equal(3100, delivered);
        GC.KeepAlive(writer);
    }

    private static async Task<(MediaTrackInfo Info, List<Mp4Sample> Samples)> Read(byte[] file)
    {
        using var input = new MemoryStream(file);
        var reader = new Mp4Reader(input);
        await reader.ReadContainerInfoAsync();
        var samples = new List<Mp4Sample>();
        while (await reader.ReadSampleAsync() is { } sample)
            samples.Add(sample);
        return (Assert.Single(reader.Info.Tracks), samples);
    }
}
