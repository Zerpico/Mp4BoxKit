namespace Mp4BoxKit.Reader.Classic;

/// <summary>Single-pass traversal of sample tables without per-sample allocations.</summary>
internal struct ClassicSampleCursor(ClassicTrack track)
{
    private int _sampleIndex;
    private int _chunkIndex;
    private int _chunkRun;
    private uint _samplesLeftInChunk;
    private long _offset;
    private long _dts;
    private int _timeRun;
    private uint _timeLeft;
    private int _duration;
    private int _compositionRun;
    private uint _compositionLeft;
    private int _compositionOffset;

    public bool TryRead(out ClassicSample sample)
    {
        sample = default;
        if (_sampleIndex >= track.SampleSizes.Count)
            return false;

        while (_samplesLeftInChunk == 0)
        {
            if (_chunkIndex >= track.ChunkOffsets.Count)
                return false;
            var chunkNumber = (uint)_chunkIndex + 1;
            while (_chunkRun < track.ChunkRuns.Count &&
                track.ChunkRuns[_chunkRun].FirstChunk <= chunkNumber)
                _chunkRun++;
            _samplesLeftInChunk = _chunkRun == 0 ? 0 : track.ChunkRuns[_chunkRun - 1].SamplesPerChunk;
            _offset = checked((long)track.ChunkOffsets[_chunkIndex++]);
        }

        while (_timeLeft == 0 && _timeRun < track.TimeRuns.Count)
        {
            var run = track.TimeRuns[_timeRun++];
            _timeLeft = run.Count;
            _duration = checked((int)run.Duration);
        }
        var duration = _timeLeft == 0 ? 0 : _duration;
        if (_timeLeft > 0) _timeLeft--;

        while (_compositionLeft == 0 && _compositionRun < track.CompositionRuns.Count)
        {
            var run = track.CompositionRuns[_compositionRun++];
            _compositionLeft = run.Count;
            _compositionOffset = run.Offset;
        }
        var composition = _compositionLeft == 0 ? 0 : _compositionOffset;
        if (_compositionLeft > 0) _compositionLeft--;

        var size = checked((int)track.SampleSizes[_sampleIndex]);
        var flags = !track.HasSyncSamples || track.SyncSamples.Contains((uint)_sampleIndex + 1)
            ? SampleFlags.KeyFrame : SampleFlags.None;
        sample = new ClassicSample(_offset, size, _dts, checked(_dts + composition), duration, flags);
        _offset = checked(_offset + size);
        _dts = checked(_dts + duration);
        _sampleIndex++;
        _samplesLeftInChunk--;
        return true;
    }
}
