using Mp4BoxKit.Boxes;
using Mp4BoxKit.Boxes.Fragmented;
using Mp4BoxKit.Writer.Fragments;
using Mp4BoxKit.Writer.Initialization;
using Mp4BoxKit.Writer;

namespace Mp4BoxKit;

/// <summary>Writes initialization and media segments for fragmented MP4.</summary>
public sealed class Mp4FragmentedWriter : Mp4Writer
{
    private bool _headerWritten;
    private byte[]? _initializationSegment;
    private readonly FragmentSampleBuffer _pending = new();
    private FragmentBoundaryPolicy _boundaryPolicy = null!;
    private uint _sequence = 1;
    private readonly Action<Fmp4Segment>? _segmentReady;
    private readonly IFmp4SegmentSink? _sink;
    private readonly RandomAccessIndexWriter _randomAccess = new();
    private readonly Mp4FragmentedWriterOptions _options;
    private long _bytesWritten;

    private Mp4FragmentedWriterOptions Options => _options;

    public Mp4FragmentedWriter(IMp4Output output, Mp4FragmentedWriterOptions? options = null,
        Action<Fmp4Segment>? segmentReady = null) : base(output)
    {
        _options = options ?? new();
        _segmentReady = segmentReady;
        Initialize();
    }

    public Mp4FragmentedWriter(IMp4Output output, Mp4FragmentedWriterOptions? options,
        IFmp4SegmentSink sink) : base(output)
    {
        _options = options ?? new();
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        Initialize();
    }

    public Mp4FragmentedWriter(Mp4FragmentedWriterOptions? options, Action<Fmp4Segment> segmentReady)
        : this(new DiscardingMp4Output(), options, segmentReady)
    {
    }

    public Mp4FragmentedWriter(Mp4FragmentedWriterOptions? options, IFmp4SegmentSink sink)
        : this(new DiscardingMp4Output(), options, sink)
    {
    }

    private void Initialize()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Options.MaxFragmentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Options.MaxFragmentSamples);
        if (Options.TargetFragmentDuration is { } target && target <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Options.TargetFragmentDuration),
                "TargetFragmentDuration must be positive.");
        if (Options.FragmentBoundary == Fmp4FragmentBoundary.KeyFrameAfterTargetDuration &&
            Options.TargetFragmentDuration is null)
            throw new ArgumentException("KeyFrameAfterTargetDuration requires TargetFragmentDuration.", nameof(Options));

        _boundaryPolicy = new FragmentBoundaryPolicy(Options);
        try
        {
            _bytesWritten = Output.Position;
        }
        catch (NotSupportedException)
        {
            _bytesWritten = 0;
        }
    }

    /// <summary>Returns the initialization segment generated once for this writer.</summary>
    public ReadOnlyMemory<byte> InitializationSegment
    {
        get
        {
            EnsureHeader();
            return _initializationSegment!;
        }
    }

    private void EnsureHeader()
    {
        if (_headerWritten)
            return;
        EnsureWritable();
        LockTracks();
        _initializationSegment = InitializationSegmentWriter.Serialize(TrackList);
        var offset = _bytesWritten;
        try
        {
            Output.Write(_initializationSegment);
            _bytesWritten = checked(_bytesWritten + _initializationSegment.Length);
            _headerWritten = true;
            Deliver(new Fmp4Segment(
                Fmp4SegmentKind.Initialization, 0, _initializationSegment, offset));
        }
        catch (Exception error)
        {
            MarkFaulted(error);
            _pending.Clear();
            throw;
        }
    }

    private async ValueTask EnsureHeaderAsync(CancellationToken cancellationToken)
    {
        if (_headerWritten)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();
        LockTracks();
        _initializationSegment = InitializationSegmentWriter.Serialize(TrackList);
        var offset = _bytesWritten;
        try
        {
            await Output.WriteAsync(_initializationSegment, cancellationToken).ConfigureAwait(false);
            _bytesWritten = checked(_bytesWritten + _initializationSegment.Length);
            _headerWritten = true;
            await DeliverAsync(new Fmp4Segment(
                Fmp4SegmentKind.Initialization, 0, _initializationSegment, offset),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            MarkFaulted(error);
            _pending.Clear();
            throw;
        }
    }

    public override void WriteSample(TrackId track, ReadOnlySpan<byte> data,
        long pts, long dts, SampleFlags flags = SampleFlags.None, int? duration = null)
    {
        EnsureTrack(track);
        MapTimestamps(track, ref pts, ref dts);
        ValidateSampleTiming(track, dts, duration);
        ValidateSamplePayload(track, data);
        _pending.EnsureTracks(TrackList.Count);
        var previousSamples = _pending.GetTrack(track);
        if (previousSamples.Count > 0 && previousSamples[^1].HasExplicitDuration &&
            dts != checked(previousSamples[^1].Dts + previousSamples[^1].Duration))
            FlushFragment();
        if (_boundaryPolicy.ShouldFlush(_pending, TrackList, track, dts, flags))
            FlushFragment();
        if (data.Length > Options.MaxFragmentBytes - _pending.PayloadBytes ||
            _pending.SampleCount >= Options.MaxFragmentSamples)
            throw FragmentLimitExceeded();
        EnsureHeader();
        _pending.Add(track, data, pts, dts, flags, duration);
        RecordDecodeTime(track, dts);
    }

    public override async ValueTask WriteSampleAsync(TrackId track, ReadOnlyMemory<byte> data,
        long pts, long dts, SampleFlags flags = SampleFlags.None,
        CancellationToken cancellationToken = default, int? duration = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTrack(track);
        MapTimestamps(track, ref pts, ref dts);
        ValidateSampleTiming(track, dts, duration);
        ValidateSamplePayload(track, data.Span);
        _pending.EnsureTracks(TrackList.Count);
        var previousSamples = _pending.GetTrack(track);
        if (previousSamples.Count > 0 && previousSamples[^1].HasExplicitDuration &&
            dts != checked(previousSamples[^1].Dts + previousSamples[^1].Duration))
            await FlushFragmentAsync(cancellationToken).ConfigureAwait(false);
        if (_boundaryPolicy.ShouldFlush(_pending, TrackList, track, dts, flags))
            await FlushFragmentAsync(cancellationToken).ConfigureAwait(false);
        if (data.Length > Options.MaxFragmentBytes - _pending.PayloadBytes ||
            _pending.SampleCount >= Options.MaxFragmentSamples)
            throw FragmentLimitExceeded();
        await EnsureHeaderAsync(cancellationToken).ConfigureAwait(false);
        _pending.Add(track, data.Span, pts, dts, flags, duration);
        RecordDecodeTime(track, dts);
    }

    public override void WriteSampleBorrowed(TrackId track, ReadOnlyMemory<byte> data,
        long pts, long dts, SampleFlags flags = SampleFlags.None, int? duration = null)
    {
        EnsureTrack(track);
        MapTimestamps(track, ref pts, ref dts);
        ValidateSampleTiming(track, dts, duration);
        ValidateSamplePayload(track, data.Span);
        _pending.EnsureTracks(TrackList.Count);
        var previousSamples = _pending.GetTrack(track);
        if (previousSamples.Count > 0 && previousSamples[^1].HasExplicitDuration &&
            dts != checked(previousSamples[^1].Dts + previousSamples[^1].Duration))
            FlushFragment();
        if (_boundaryPolicy.ShouldFlush(_pending, TrackList, track, dts, flags))
            FlushFragment();
        if (data.Length > Options.MaxFragmentBytes - _pending.PayloadBytes ||
            _pending.SampleCount >= Options.MaxFragmentSamples)
            throw FragmentLimitExceeded();
        EnsureHeader();
        _pending.AddBorrowed(track, data, pts, dts, flags, duration);
        RecordDecodeTime(track, dts);
    }

    public override async ValueTask WriteSampleBorrowedAsync(TrackId track,
        ReadOnlyMemory<byte> data, long pts, long dts,
        SampleFlags flags = SampleFlags.None,
        CancellationToken cancellationToken = default, int? duration = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTrack(track);
        MapTimestamps(track, ref pts, ref dts);
        ValidateSampleTiming(track, dts, duration);
        ValidateSamplePayload(track, data.Span);
        _pending.EnsureTracks(TrackList.Count);
        var previousSamples = _pending.GetTrack(track);
        if (previousSamples.Count > 0 && previousSamples[^1].HasExplicitDuration &&
            dts != checked(previousSamples[^1].Dts + previousSamples[^1].Duration))
            await FlushFragmentAsync(cancellationToken).ConfigureAwait(false);
        if (_boundaryPolicy.ShouldFlush(_pending, TrackList, track, dts, flags))
            await FlushFragmentAsync(cancellationToken).ConfigureAwait(false);
        if (data.Length > Options.MaxFragmentBytes - _pending.PayloadBytes ||
            _pending.SampleCount >= Options.MaxFragmentSamples)
            throw FragmentLimitExceeded();
        await EnsureHeaderAsync(cancellationToken).ConfigureAwait(false);
        _pending.AddBorrowed(track, data, pts, dts, flags, duration);
        RecordDecodeTime(track, dts);
    }

    public override void Complete()
    {
        if (IsCompleted)
            return;
        EnsureHeader();
        FlushFragment();
        if (Options.WriteRandomAccessIndex)
        {
            try
            {
                var index = _randomAccess.Serialize();
                var offset = _bytesWritten;
                Output.Write(index);
                _bytesWritten = checked(_bytesWritten + index.Length);
                Deliver(new Fmp4Segment(
                    Fmp4SegmentKind.Index, _sequence, index, offset));
            }
            catch (Exception error)
            {
                MarkFaulted(error);
                throw;
            }
        }
        MarkCompleted();
    }

    public override async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (IsCompleted)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureHeaderAsync(cancellationToken).ConfigureAwait(false);
        await FlushFragmentAsync(cancellationToken).ConfigureAwait(false);
        if (Options.WriteRandomAccessIndex)
        {
            try
            {
                var index = _randomAccess.Serialize();
                var offset = _bytesWritten;
                await Output.WriteAsync(index, cancellationToken).ConfigureAwait(false);
                _bytesWritten = checked(_bytesWritten + index.Length);
                await DeliverAsync(new Fmp4Segment(
                    Fmp4SegmentKind.Index, _sequence, index, offset),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                MarkFaulted(error);
                throw;
            }
        }
        MarkCompleted();
    }

    public void FlushFragment()
    {
        EnsureWritable();
        var prepared = PrepareFragment();
        if (prepared is null)
            return;
        try
        {
            WritePrepared(prepared);
            Commit(prepared);
            if (prepared.Segment is { } segment)
                Deliver(segment);
        }
        catch (Exception error)
        {
            MarkFaulted(error);
            _pending.Clear();
            throw;
        }
    }

    public async ValueTask FlushFragmentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable();
        var prepared = PrepareFragment();
        if (prepared is null)
            return;
        try
        {
            await WritePreparedAsync(prepared, cancellationToken).ConfigureAwait(false);
            Commit(prepared);
            if (prepared.Segment is { } segment)
                await DeliverAsync(segment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            MarkFaulted(error);
            _pending.Clear();
            throw;
        }
    }

    private void WritePrepared(PreparedFragment prepared)
    {
        if (prepared.Data is { } data)
        {
            Output.Write(data);
            return;
        }

        Output.Write(prepared.Prefix);
        Output.Write(prepared.Moof);
        Output.Write(prepared.MdatHeader);
        foreach (var samples in _pending.Tracks)
            foreach (var sample in samples)
                Output.Write(sample.Data);
    }

    private async ValueTask WritePreparedAsync(
        PreparedFragment prepared,
        CancellationToken cancellationToken)
    {
        if (prepared.Data is { } data)
        {
            await Output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            return;
        }

        await Output.WriteAsync(prepared.Prefix, cancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(prepared.Moof, cancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(prepared.MdatHeader, cancellationToken).ConfigureAwait(false);
        foreach (var samples in _pending.Tracks)
            foreach (var sample in samples)
                await Output.WriteAsync(sample.Memory, cancellationToken).ConfigureAwait(false);
    }

    private static InvalidOperationException FragmentLimitExceeded()
    {
        return new InvalidOperationException(
            "Fragment buffer limit exceeded. " +
            "Flush the fragment or configure shorter fragment boundaries.");
    }

    private PreparedFragment? PrepareFragment()
    {
        if (!_pending.HasSamples)
            return null;

        _pending.FinalizeDurations();
        var sequence = _sequence;
        var moof = MediaFragmentWriter.SerializeMoof(
            TrackList, _pending.Tracks, sequence, out var payloadSize);
        var reference = SelectReferenceTrack();
        var referenceSamples = _pending.GetTrack(reference.Id);
        var earliest = long.MaxValue;
        var end = long.MinValue;
        foreach (var sample in referenceSamples)
        {
            if (sample.Pts < earliest)
                earliest = sample.Pts;
            var sampleEnd = checked(sample.Pts + sample.Duration);
            if (sampleEnd > end)
                end = sampleEnd;
        }

        var duration = end - earliest;
        var startsWithKeyFrame = reference is not VideoTrackInfo ||
            (referenceSamples[0].Flags & SampleFlags.KeyFrame) != 0;

        var styp = Options.WriteSegmentType
            ? FileTypeWriter.SerializeSegmentType()
            : Array.Empty<byte>();
        var index = Options.WriteSegmentIndex
            ? SegmentIndexWriter.Serialize(
                reference, referenceSamples, moof.Length + 8 + payloadSize)
            : Array.Empty<byte>();
        var prefix = new byte[checked(styp.Length + index.Length)];
        styp.CopyTo(prefix, 0);
        index.CopyTo(prefix, styp.Length);

        var mdatHeader = new byte[8];
        BigEndian.W32(mdatHeader, checked((uint)(8 + payloadSize)));
        BigEndian.W32(mdatHeader.AsSpan(4), BoxTypes.Mdat.Value);
        var totalLength = checked(prefix.Length + moof.Length + 8 + payloadSize);

        var segmentOffset = _bytesWritten;
        var moofOffset = checked(_bytesWritten + prefix.Length);
        var entries = new List<RandomAccessEntry>();
        if (Options.WriteRandomAccessIndex)
        {
            uint traf = 0;
            foreach (var track in TrackList)
            {
                var samples = _pending.GetTrack(track.Id);
                if (samples.Count == 0)
                    continue;
                traf++;
                if (track is VideoTrackInfo &&
                    (samples[0].Flags & SampleFlags.KeyFrame) == 0)
                    continue;
                entries.Add(new RandomAccessEntry(
                    track.Id.Value, samples[0].Pts, moofOffset, traf, 1));
            }
        }

        byte[]? data = null;
        Fmp4Segment? segment = null;
        if (_sink is not null || _segmentReady is not null)
        {
            data = new byte[totalLength];
            prefix.CopyTo(data, 0);
            moof.CopyTo(data, prefix.Length);
            mdatHeader.CopyTo(data, prefix.Length + moof.Length);
            var position = prefix.Length + moof.Length + 8;
            foreach (var samples in _pending.Tracks)
                foreach (var sample in samples)
                {
                    sample.Data.CopyTo(data.AsSpan(position));
                    position += sample.Data.Length;
                }

            segment = new Fmp4Segment(
                Fmp4SegmentKind.Media,
                sequence,
                data,
                segmentOffset,
                reference.Id,
                reference.TimeScale,
                earliest,
                duration,
                startsWithKeyFrame,
                _pending.SampleCount);
        }

        return new PreparedFragment(
            prefix, moof, mdatHeader, data, segment, entries, totalLength);
    }

    private MediaTrackInfo SelectReferenceTrack()
    {
        MediaTrackInfo? first = null;
        foreach (var track in TrackList)
        {
            if (_pending.GetTrack(track.Id).Count == 0)
                continue;
            if (track is VideoTrackInfo)
                return track;
            first ??= track;
        }

        return first ?? throw new InvalidOperationException("Fragment has no samples.");
    }

    private void Commit(PreparedFragment prepared)
    {
        _sequence = checked(_sequence + 1);
        foreach (var entry in prepared.IndexEntries)
            _randomAccess.Add(entry.Track, entry.Time, entry.Offset, entry.Traf, entry.Sample);
        _pending.Clear();
        _bytesWritten = checked(_bytesWritten + prepared.TotalLength);
    }

    private void Deliver(Fmp4Segment segment)
    {
        if (_sink is null)
        {
            DeliverSegment(_segmentReady, segment);
            return;
        }

        DeliverSegmentAsync(_sink, segment, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask DeliverAsync(Fmp4Segment segment, CancellationToken cancellationToken)
    {
        if (_sink is null)
        {
            DeliverSegment(_segmentReady, segment);
            return;
        }

        await DeliverSegmentAsync(_sink, segment, cancellationToken).ConfigureAwait(false);
    }

    private sealed record PreparedFragment(
        byte[] Prefix,
        byte[] Moof,
        byte[] MdatHeader,
        byte[]? Data,
        Fmp4Segment? Segment,
        IReadOnlyList<RandomAccessEntry> IndexEntries,
        int TotalLength);

    private readonly record struct RandomAccessEntry(
        uint Track, long Time, long Offset, uint Traf, uint Sample);
}
