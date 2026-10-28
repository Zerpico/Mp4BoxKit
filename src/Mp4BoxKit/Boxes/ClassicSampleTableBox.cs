using Mp4BoxKit.Boxes.SampleEntries;
using Mp4BoxKit.Writer;

namespace Mp4BoxKit.Boxes;

internal sealed class ClassicSampleTableBox(
    MediaTrackInfo track,
    IReadOnlyList<FileSample> samples) : IWritableBox
{
    public void WriteTo(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Stbl, table =>
        {
            IsoBoxWriter.Box(table, BoxTypes.Stsd, description =>
            {
                description.WriteUInt32(0);
                description.WriteUInt32(1);
                SampleEntryWriter.WriteTo(description, track);
            });

            WriteTiming(table, BoxTypes.Stts, static sample => sample.Duration);
            WriteTiming(table, BoxTypes.Ctts, static sample => sample.Pts - sample.Dts);
            WriteSampleToChunk(table);
            WriteSampleSizes(table);
            WriteChunkOffsets(table);
            WriteSyncSamples(table);
        });
    }

    private void WriteTiming(
        IsoBoxBuffer buffer,
        FourCc type,
        Func<FileSample, long> selector)
    {
        var runs = new List<(uint Count, long Value)>();
        foreach (var sample in samples)
        {
            var value = selector(sample);
            if (runs.Count != 0 && runs[^1].Value == value)
                runs[^1] = (runs[^1].Count + 1, value);
            else
                runs.Add((1, value));
        }

        var composition = type == BoxTypes.Ctts;
        if (composition && runs.Count == 1 && runs[0].Value == 0)
            return;

        IsoBoxWriter.Box(buffer, type, box =>
        {
            box.WriteUInt32(composition && runs.Any(run => run.Value < 0)
                ? IsoBmffConstants.FullBoxVersionOne : 0);
            box.WriteUInt32((uint)runs.Count);
            foreach (var run in runs)
            {
                box.WriteUInt32(run.Count);
                if (composition)
                    box.WriteInt32(checked((int)run.Value));
                else
                    box.WriteUInt32((uint)run.Value);
            }
        });
    }

    private void WriteSampleToChunk(IsoBoxBuffer buffer)
    {
        IsoBoxWriter.Box(buffer, BoxTypes.Stsc, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(samples.Count == 0 ? 0u : 1u);
            if (samples.Count == 0)
                return;
            box.WriteUInt32(1);
            // stco/co64 stores one offset per sample: each chunk holds one sample.
            box.WriteUInt32(1);
            box.WriteUInt32(1);
        });
    }

    private void WriteSampleSizes(IsoBoxBuffer buffer)
    {
        var sameSize = samples.Count == 0 ||
            samples.All(sample => sample.Size == samples[0].Size);
        IsoBoxWriter.Box(buffer, BoxTypes.Stsz, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32(sameSize && samples.Count != 0
                ? (uint)samples[0].Size : 0);
            box.WriteUInt32((uint)samples.Count);
            if (!sameSize)
            {
                foreach (var sample in samples)
                    box.WriteUInt32((uint)sample.Size);
            }
        });
    }

    private void WriteChunkOffsets(IsoBoxBuffer buffer)
    {
        var wide = samples.Any(sample => sample.Offset > uint.MaxValue);
        IsoBoxWriter.Box(buffer, wide ? BoxTypes.Co64 : BoxTypes.Stco, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32((uint)samples.Count);
            foreach (var sample in samples)
            {
                if (wide)
                    box.WriteUInt64((ulong)sample.Offset);
                else
                    box.WriteUInt32((uint)sample.Offset);
            }
        });
    }

    private void WriteSyncSamples(IsoBoxBuffer buffer)
    {
        if (track is not VideoTrackInfo)
            return;
        var keyframes = samples.Select((sample, index) => (sample, index))
            .Where(item => (item.sample.Flags & SampleFlags.KeyFrame) != 0)
            .Select(item => item.index + 1).ToArray();
        if (keyframes.Length == 0 || keyframes.Length == samples.Count)
            return;
        IsoBoxWriter.Box(buffer, BoxTypes.Stss, box =>
        {
            box.WriteUInt32(0);
            box.WriteUInt32((uint)keyframes.Length);
            foreach (var keyframe in keyframes)
                box.WriteUInt32((uint)keyframe);
        });
    }
}
