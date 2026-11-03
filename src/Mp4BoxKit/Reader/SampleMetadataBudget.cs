namespace Mp4BoxKit.Reader;

/// <summary>Bounds expansion of compact sample tables, including temporary index copies.</summary>
internal sealed class SampleMetadataBudget(long maximumBytes)
{
    private long _remaining = maximumBytes;

    // Conservative allowances for records, list capacity growth and index construction.
    public const int ClassicSampleBytes = 192;
    public const int FragmentSampleBytes = 96;

    public void Reserve(uint count, int bytesPerSample)
    {
        var bytes = (long)count * bytesPerSample;
        if (bytes > _remaining)
            throw new Mp4FormatException("Expanded sample metadata exceeds configured limit.");
        _remaining -= bytes;
    }
}
