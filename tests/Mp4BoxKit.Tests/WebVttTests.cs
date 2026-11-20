using System.Buffers.Binary;
using System.Text;
using Mp4BoxKit.Codecs;
using Xunit;

namespace Mp4BoxKit.Tests;

public sealed class WebVttTests
{
    [Fact]
    public void CueIsSerializedAsVttcWithOptionalChildren()
    {
        var sample = WebVttSample.Create(new WebVttCue("Привет\nмир", "1", "line:90%")).ToArray();

        var cue = Assert.Single(Boxes(sample));
        Assert.Equal("vttc", cue.Type);
        var children = Boxes(cue.Payload);
        Assert.Equal(new[] { "iden", "sttg", "payl" }, children.Select(box => box.Type));
        Assert.Equal("1", Encoding.UTF8.GetString(children[0].Payload));
        Assert.Equal("line:90%", Encoding.UTF8.GetString(children[1].Payload));
        Assert.Equal("Привет\nмир", Encoding.UTF8.GetString(children[2].Payload));
    }

    [Fact]
    public void CueWithoutIdAndSettingsContainsOnlyPayload()
    {
        var sample = WebVttSample.Create(new WebVttCue("A")).ToArray();

        Assert.Equal(
            new byte[] { 0, 0, 0, 17, (byte)'v', (byte)'t', (byte)'t', (byte)'c',
                0, 0, 0, 9, (byte)'p', (byte)'a', (byte)'y', (byte)'l', (byte)'A' },
            sample);
    }

    [Fact]
    public void NoCuesProduceVtte()
    {
        var expected = new byte[] { 0, 0, 0, 8, (byte)'v', (byte)'t', (byte)'t', (byte)'e' };
        Assert.Equal(expected, WebVttSample.Empty.ToArray());
        Assert.Equal(expected, WebVttSample.Create().ToArray());
    }

    [Fact]
    public void SeveralCuesShareOneSample()
    {
        var sample = WebVttSample.Create(new WebVttCue("A"), new WebVttCue("B")).ToArray();

        var boxes = Boxes(sample);
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, box => Assert.Equal("vttc", box.Type));
    }

    [Fact]
    public void CueTextWithTimingArrowIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            WebVttSample.Create(new WebVttCue("00:00.000 --> 00:01.000")));
    }

    [Fact]
    public void BuilderSplitsOverlapsAndFillsGaps()
    {
        var samples = WebVttSampleBuilder.Build(new[]
        {
            new WebVttTimedCue(4000, 5000, new WebVttCue("C")),
            new WebVttTimedCue(0, 2000, new WebVttCue("A")),
            new WebVttTimedCue(1000, 3000, new WebVttCue("B"))
        });

        Assert.Equal(new long[] { 0, 1000, 2000, 3000, 4000 }, samples.Select(s => s.Time));
        Assert.All(samples, sample => Assert.Equal(1000, sample.Duration));
        Assert.Equal(new[] { "A" }, Texts(samples[0]));
        Assert.Equal(new[] { "A", "B" }, Texts(samples[1]));
        Assert.Equal(new[] { "B" }, Texts(samples[2]));
        Assert.Equal(WebVttSample.Empty.ToArray(), samples[3].Data.ToArray());
        Assert.Equal(new[] { "C" }, Texts(samples[4]));
    }

    [Fact]
    public void BuilderFillsGapBeforeFirstCue()
    {
        var samples = WebVttSampleBuilder.Build(
            new[] { new WebVttTimedCue(500, 1500, new WebVttCue("A")) });

        Assert.Equal(2, samples.Count);
        Assert.Equal((0L, 500), (samples[0].Time, samples[0].Duration));
        Assert.Equal(WebVttSample.Empty.ToArray(), samples[0].Data.ToArray());
        Assert.Equal((500L, 1000), (samples[1].Time, samples[1].Duration));
    }

    [Fact]
    public void BuilderKeepsAdjacentCuesSeparate()
    {
        var samples = WebVttSampleBuilder.Build(new[]
        {
            new WebVttTimedCue(0, 1000, new WebVttCue("A")),
            new WebVttTimedCue(1000, 2000, new WebVttCue("B"))
        });

        Assert.Equal(2, samples.Count);
        Assert.Equal(new[] { "A" }, Texts(samples[0]));
        Assert.Equal(new[] { "B" }, Texts(samples[1]));
    }

    [Fact]
    public void BuilderToleranceRemovesSmallOverlapButKeepsGaps()
    {
        var samples = WebVttSampleBuilder.Build(new[]
        {
            new WebVttTimedCue(0, 1010, new WebVttCue("A")),
            new WebVttTimedCue(1000, 2000, new WebVttCue("B")),
            new WebVttTimedCue(2010, 3000, new WebVttCue("C"))
        }, timingTolerance: 50);

        Assert.Equal(new long[] { 0, 1010, 2000, 2010 }, samples.Select(s => s.Time));
        Assert.Equal(new[] { 1010, 990, 10, 990 }, samples.Select(s => s.Duration));
        Assert.Equal(new[] { "A" }, Texts(samples[0]));
        Assert.Equal(new[] { "B" }, Texts(samples[1]));
        Assert.Equal(WebVttSample.Empty.ToArray(), samples[2].Data.ToArray());
        Assert.Equal(new[] { "C" }, Texts(samples[3]));
    }

    [Fact]
    public void BuilderToleranceKeepsLargeOverlap()
    {
        var samples = WebVttSampleBuilder.Build(new[]
        {
            new WebVttTimedCue(0, 1100, new WebVttCue("A")),
            new WebVttTimedCue(1000, 2000, new WebVttCue("B"))
        }, timingTolerance: 50);

        Assert.Equal(new[] { "A", "B" }, Texts(samples[1]));
        Assert.Equal((1000L, 100), (samples[1].Time, samples[1].Duration));
    }

    [Fact]
    public void BuilderRejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => WebVttSampleBuilder.Build(
            new[] { new WebVttTimedCue(1000, 1000, new WebVttCue("A")) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebVttSampleBuilder.Build(
            new[] { new WebVttTimedCue(0, 1000, new WebVttCue("A")) }, start: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebVttSampleBuilder.Build(
            Array.Empty<WebVttTimedCue>(), timingTolerance: -1));
        Assert.Throws<ArgumentException>(() => WebVttSampleBuilder.Build(new[]
        {
            new WebVttTimedCue(0, 1010, new WebVttCue("A")),
            new WebVttTimedCue(1000, 1005, new WebVttCue("B"))
        }, timingTolerance: 50));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackRoundTrips(bool fragmented)
    {
        const string header = "WEBVTT\n\nSTYLE\n::cue { color: yellow }";
        var samples = WebVttSampleBuilder.Build(new[]
        {
            new WebVttTimedCue(0, 2000, new WebVttCue("A")),
            new WebVttTimedCue(1000, 3000, new WebVttCue("Привет", "2", "line:0")),
            new WebVttTimedCue(4000, 5000, new WebVttCue("C"))
        });

        using var output = new MemoryStream();
        using (Mp4Writer writer = fragmented
            ? new Mp4FragmentedWriter(new StreamMp4Output(output))
            : new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.WebVtt,
                CodecConfiguration: WebVttConfiguration.Create(header),
                Name: "Русские", Language: Mp4Languages.Russian));
            foreach (var sample in samples)
                writer.WriteSample(track, sample.Data.Span, sample.Time, sample.Time,
                    duration: sample.Duration);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        Assert.Equal(ProbeStatus.Complete, (await reader.ReadContainerInfoAsync()).Status);
        var info = Assert.IsType<SubtitleTrackInfo>(Assert.Single(reader.Info.Tracks));
        Assert.Equal(Codec.WebVtt, info.Codec);
        Assert.Equal("Русские", info.Name);
        Assert.Equal(Mp4Languages.Russian, info.Language);
        Assert.Equal(header, Encoding.UTF8.GetString(info.CodecConfiguration.Span));
        Assert.Contains("text", Encoding.ASCII.GetString(output.ToArray()));

        var read = new List<Mp4Sample>();
        while (await reader.ReadSampleAsync() is { } sample)
            read.Add(sample);
        Assert.Equal(samples.Count, read.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            Assert.Equal(samples[i].Time, read[i].Dts);
            Assert.Equal(samples[i].Time, read[i].Pts);
            Assert.Equal(samples[i].Duration, read[i].Duration);
            Assert.True((read[i].Flags & SampleFlags.KeyFrame) != 0);
            Assert.Equal(samples[i].Data.ToArray(), read[i].Data.ToArray());
        }
    }

    [Fact]
    public async Task DefaultConfigurationIsPlainHeader()
    {
        using var output = new MemoryStream();
        using (var writer = new Mp4FileWriter(new StreamMp4Output(output)))
        {
            var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.WebVtt));
            Assert.Equal("WEBVTT", Encoding.UTF8.GetString(writer.Tracks[0].CodecConfiguration.Span));
            writer.WriteSample(track, WebVttSample.Create(new WebVttCue("A")).Span, 0, 0, duration: 1000);
            writer.Complete();
        }

        output.Position = 0;
        var reader = new Mp4Reader(output);
        await reader.ReadContainerInfoAsync();
        Assert.Equal("WEBVTT", Encoding.UTF8.GetString(reader.Info.Tracks[0].CodecConfiguration.Span));
    }

    [Theory]
    [InlineData("")]
    [InlineData("WEBVTTX")]
    [InlineData("NOT WEBVTT")]
    [InlineData("WEBVTT\n\n00:00.000 --> 00:01.000\nA")]
    public void InvalidConfigurationIsRejected(string header)
    {
        Assert.Throws<ArgumentException>(() => WebVttConfiguration.Create(header));
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        if (header.Length != 0)
            Assert.Throws<ArgumentException>(() => writer.AddSubtitle(new SubtitleTrackOptions(
                Codec.WebVtt, CodecConfiguration: Encoding.UTF8.GetBytes(header))));
        Assert.Empty(writer.Tracks);
    }

    [Fact]
    public void RawTextSampleIsRejected()
    {
        using var output = new MemoryStream();
        using var writer = new Mp4FragmentedWriter(new StreamMp4Output(output));
        var track = writer.AddSubtitle(new SubtitleTrackOptions(Codec.WebVtt));

        Assert.Throws<ArgumentException>(() => writer.WriteSample(
            track, Encoding.UTF8.GetBytes("Hello"), 0, 0, duration: 1000));
        Assert.Throws<ArgumentException>(() => writer.WriteSample(
            track, Array.Empty<byte>(), 0, 0, duration: 1000));
        var mixed = WebVttSample.Empty.ToArray().Concat(WebVttSample.Create(new WebVttCue("A")).ToArray()).ToArray();
        Assert.Throws<ArgumentException>(() => writer.WriteSample(track, mixed, 0, 0, duration: 1000));
    }

    private static string[] Texts(WebVttTimedSample sample)
        => Boxes(sample.Data.ToArray())
            .Select(cue => Boxes(cue.Payload).Single(box => box.Type == "payl"))
            .Select(payl => Encoding.UTF8.GetString(payl.Payload))
            .ToArray();

    private static List<(string Type, byte[] Payload)> Boxes(byte[] data)
    {
        var result = new List<(string, byte[])>();
        var offset = 0;
        while (offset < data.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            result.Add((Encoding.ASCII.GetString(data, offset + 4, 4), data[(offset + 8)..(offset + size)]));
            offset += size;
        }
        return result;
    }
}
