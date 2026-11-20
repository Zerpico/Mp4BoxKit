namespace Mp4BoxKit.Codecs;

/// <summary>A cue with its presentation interval [Start, End) in track time-scale ticks.</summary>
public sealed record WebVttTimedCue(long Start, long End, WebVttCue Cue);

/// <summary>A ready-to-write WebVTT sample: pass Time as PTS and DTS, and Duration explicitly.</summary>
public readonly record struct WebVttTimedSample(long Time, int Duration, ReadOnlyMemory<byte> Data);

/// <summary>
/// Converts cues with arbitrary, possibly overlapping intervals into the contiguous,
/// non-overlapping sample sequence required by ISO/IEC 14496-30: overlaps are split
/// so each sample carries every cue visible during it, and gaps become vtte samples.
/// </summary>
public static class WebVttSampleBuilder
{
    /// <param name="cues">Cues in any order; cues with equal start keep their input order.</param>
    /// <param name="start">
    /// Timeline start; a gap before the first cue becomes a vtte sample. Must not be
    /// later than the first cue.
    /// </param>
    /// <param name="timingTolerance">
    /// Maximum overlap, in ticks, removed automatically: a cue starting before the end
    /// of the preceding cue by no more than this value is moved to start at that end.
    /// Gaps are never changed. 0 disables it.
    /// </param>
    public static IReadOnlyList<WebVttTimedSample> Build(
        IEnumerable<WebVttTimedCue> cues,
        long start = 0,
        int timingTolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentOutOfRangeException.ThrowIfNegative(timingTolerance);

        var ordered = cues
            .Select((cue, index) =>
            {
                ArgumentNullException.ThrowIfNull(cue);
                ArgumentNullException.ThrowIfNull(cue.Cue);
                if (cue.End <= cue.Start)
                    throw new ArgumentException(
                        $"WebVTT cue end ({cue.End}) must be greater than its start ({cue.Start}).",
                        nameof(cues));
                return (Cue: cue, Index: index);
            })
            .OrderBy(item => item.Cue.Start)
            .ThenBy(item => item.Index)
            .Select(item => item.Cue)
            .ToList();
        if (ordered.Count == 0)
            return [];
        if (ordered[0].Start < start)
            throw new ArgumentOutOfRangeException(nameof(start),
                "Timeline start must not be later than the first cue.");

        for (var i = 1; i < ordered.Count && timingTolerance > 0; i++)
        {
            var previousEnd = ordered[i - 1].End;
            var cue = ordered[i];
            if (cue.Start >= previousEnd || previousEnd - cue.Start > timingTolerance)
                continue;
            if (cue.End <= previousEnd)
                throw new ArgumentException(
                    $"WebVTT cue [{cue.Start}, {cue.End}) has no duration left after its " +
                    "overlap with the previous cue is removed.", nameof(cues));
            ordered[i] = cue with { Start = previousEnd };
        }

        var boundaries = new SortedSet<long> { start };
        foreach (var cue in ordered)
        {
            boundaries.Add(cue.Start);
            boundaries.Add(cue.End);
        }

        var points = boundaries.ToArray();
        var samples = new List<WebVttTimedSample>(points.Length - 1);
        for (var i = 0; i + 1 < points.Length; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var duration = to - from;
            if (duration > int.MaxValue)
                throw new ArgumentException(
                    $"WebVTT sample interval [{from}, {to}) is longer than {int.MaxValue} ticks.",
                    nameof(cues));
            var visible = ordered
                .Where(cue => cue.Start <= from && cue.End >= to)
                .Select(cue => cue.Cue);
            samples.Add(new WebVttTimedSample(from, (int)duration, WebVttSample.Create(visible)));
        }

        return samples;
    }
}
