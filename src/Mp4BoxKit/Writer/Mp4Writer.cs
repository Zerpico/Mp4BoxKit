using System.Buffers;
using System.Buffers.Binary;
using Mp4BoxKit.Boxes;
using Mp4BoxKit.Codecs;

namespace Mp4BoxKit;

/// <summary>
/// Common track/sample lifecycle for MP4 and fragmented MP4 writers.
/// One <c>WriteSample</c> call must contain one complete access unit.
/// H.264/H.265 samples must be length-prefixed (AVCC/HVCC), not Annex B.
/// <c>CodecConfiguration</c> is the avcC/hvcC/esds payload without a box header.
/// Instances are not thread-safe; serialize calls externally. The caller owns
/// the output and is responsible for disposing it.
/// </summary>
public abstract class Mp4Writer : IDisposable, IAsyncDisposable
{
    private bool _inCallback;
    protected bool IsCompleted { get; private set; }
    private Exception? _failure;
    private readonly Dictionary<TrackId, long> _lastDecodeTimes = new();
    private bool _tracksLocked;
    protected readonly IMp4Output Output;
    protected readonly List<MediaTrackInfo> TrackList = new();
    public IReadOnlyList<MediaTrackInfo> Tracks => TrackList;

    protected Mp4Writer(IMp4Output output)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public TrackId AddVideo(VideoTrackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateTimeScale(options.TimeScale);
        ValidateVideoCodec(options.Codec);
        if (options.Width is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.Width),
                "Video width must be between 1 and 65535.");
        if (options.Height is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.Height),
                "Video height must be between 1 and 65535.");
        ValidateConfiguration(options.Codec, options.CodecConfiguration, nameof(options));
        return AddTrack(new VideoTrackInfo
        {
            Id = new((uint)(TrackList.Count + 1)),
            Codec = options.Codec,
            TimeScale = options.TimeScale,
            Width = options.Width,
            Height = options.Height,
            CodecConfiguration = options.CodecConfiguration,
            Timeline = options.Timeline
        });
    }

    /// <remarks>
    /// For AAC, an empty <c>CodecConfiguration</c> is interpreted as AAC-LC
    /// and an esds is built from <c>SampleRate</c>/<c>Channels</c>. Pass an
    /// explicit esds for HE-AAC/SBR/PS or other profiles.
    /// </remarks>
    public TrackId AddAudio(AudioTrackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateTimeScale(options.TimeScale);
        ValidateAudioCodec(options.Codec);
        if (options.Channels is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.Channels),
                "Channel count must be between 1 and 65535.");
        if (options.SampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.SampleRate),
                "Sample rate must be positive.");
        if (options.SampleRate > ushort.MaxValue)
            throw new NotSupportedException(
                "Audio sample rates above 65535 require an audio sample entry version 1, which is not implemented.");
        if (options.BitsPerSample is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.BitsPerSample),
                "Bits per sample must be between 1 and 65535.");
        if (options.PcmLittleEndian16 && (options.Codec != Codec.Pcm || options.BitsPerSample != 16))
            throw new ArgumentException("Little-endian PCM mode requires Codec.Pcm and 16 bits per sample.", nameof(options));
        var configuration = options.Codec == Codec.Aac && options.CodecConfiguration.IsEmpty
            ? AacConfiguration.CreateEsds(options.SampleRate, options.Channels)
            : options.CodecConfiguration;
        ValidateConfiguration(options.Codec, configuration, nameof(options));
        return AddTrack(new AudioTrackInfo
        {
            Id = new((uint)(TrackList.Count + 1)),
            Codec = options.Codec,
            TimeScale = options.TimeScale,
            Channels = options.Channels,
            SampleRate = options.SampleRate,
            BitsPerSample = options.BitsPerSample,
            PcmLittleEndian16 = options.PcmLittleEndian16,
            CodecConfiguration = configuration,
            Timeline = options.Timeline
        });
    }

    public TrackId AddSubtitle(SubtitleTrackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateTimeScale(options.TimeScale);
        ValidateSubtitleCodec(options.Codec);
        if (options.Width is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.Width),
                "Subtitle width must be between 0 and 65535.");
        if (options.Height is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options.Height),
                "Subtitle height must be between 0 and 65535.");
        MediaLanguage.Pack(options.Language);
        ValidateConfiguration(options.Codec, options.CodecConfiguration, nameof(options));
        return AddTrack(new SubtitleTrackInfo
        {
            Id = new((uint)(TrackList.Count + 1)),
            Codec = options.Codec,
            TimeScale = options.TimeScale,
            CodecConfiguration = options.CodecConfiguration,
            Width = options.Width,
            Height = options.Height,
            Name = options.Name,
            IsDefault = options.IsDefault,
            Language = options.Language
        });
    }

    private static void ValidateTimeScale(uint timeScale)
    {
        if (timeScale == 0)
            throw new ArgumentOutOfRangeException(nameof(timeScale),
                "Track time scale must be positive.");
    }

    private static void ValidateVideoCodec(Codec codec)
    {
        if (CodecRegistry.IsWritableVideo(codec))
            return;
        if (CodecRegistry.IsVisual(codec))
            throw new NotSupportedException(
                $"Codec {codec} cannot be written as an ISO-BMFF video sample entry.");
        throw new ArgumentException($"{codec} is not a video codec.", nameof(codec));
    }

    private static void ValidateAudioCodec(Codec codec)
    {
        if (CodecRegistry.IsWritableAudio(codec))
            return;
        if (CodecRegistry.IsAudio(codec))
            throw new NotSupportedException(
                $"Codec {codec} cannot be written as an ISO-BMFF audio sample entry.");
        throw new ArgumentException($"{codec} is not an audio codec.", nameof(codec));
    }

    private static void ValidateSubtitleCodec(Codec codec)
    {
        if (CodecRegistry.IsWritableSubtitle(codec))
            return;
        if (CodecRegistry.IsSubtitle(codec))
            throw new NotSupportedException(
                $"Codec {codec} cannot be written as an ISO-BMFF subtitle sample entry.");
        throw new ArgumentException($"{codec} is not a subtitle codec.", nameof(codec));
    }

    private static void ValidateConfiguration(
        Codec codec,
        ReadOnlyMemory<byte> configuration,
        string parameterName)
    {
        var required = CodecRegistry.RequiresConfiguration(codec);
        if (required && configuration.IsEmpty)
            throw new ArgumentException(
                $"Codec {codec} requires CodecConfiguration (avcC/hvcC/esds payload).",
                parameterName);
        if (!required && !configuration.IsEmpty)
            throw new ArgumentException(
                $"Codec {codec} does not use a CodecConfiguration box.",
                parameterName);

        if (codec == Codec.H264)
            AvcConfiguration.Validate(configuration.Span);
        else if (codec == Codec.H265)
            HvcConfiguration.Validate(configuration.Span);
    }

    private (int LengthSize, bool Hevc) GetNalLengthSize(TrackId id)
    {
        var track = TrackList[(int)id.Value - 1];
        return track.Codec switch
        {
            Codec.H264 => (AvcConfiguration.GetNalLengthSize(
                track.CodecConfiguration.Span), false),
            Codec.H265 => (HvcConfiguration.GetNalLengthSize(
                track.CodecConfiguration.Span), true),
            _ => throw new NotSupportedException(
                "WriteSampleNalUnits supports H.264 and H.265 tracks only.")
        };
    }

    private static int GetNalSampleSize(
        IReadOnlyList<ReadOnlyMemory<byte>> nalUnits,
        int lengthSize,
        bool hevc,
        bool includeParameterSets)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        var total = 0;
        foreach (var nal in nalUnits)
        {
            if (nal.IsEmpty ||
                (!includeParameterSets && IsParameterSet(nal.Span, hevc)))
                continue;
            total = checked(total + lengthSize + nal.Length);
        }

        if (total == 0)
            throw new ArgumentException(
                "No NAL units remain after filtering parameter sets.", nameof(nalUnits));
        return total;
    }

    private static void WriteNalSample(
        Span<byte> destination,
        IReadOnlyList<ReadOnlyMemory<byte>> nalUnits,
        int lengthSize,
        bool hevc,
        bool includeParameterSets)
    {
        var offset = 0;
        foreach (var nal in nalUnits)
        {
            if (nal.IsEmpty ||
                (!includeParameterSets && IsParameterSet(nal.Span, hevc)))
                continue;

            WriteLength(destination[offset..], lengthSize, checked((uint)nal.Length));
            offset += lengthSize;
            nal.Span.CopyTo(destination[offset..]);
            offset += nal.Length;
        }
    }

    private static void WriteLength(Span<byte> destination, int lengthSize, uint value)
    {
        switch (lengthSize)
        {
            case 1:
                destination[0] = checked((byte)value);
                break;
            case 2:
                BinaryPrimitives.WriteUInt16BigEndian(destination, checked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32BigEndian(destination, value);
                break;
            default:
                throw new NotSupportedException($"Unsupported NAL length size {lengthSize}.");
        }
    }

    private static bool IsParameterSet(ReadOnlySpan<byte> nal, bool hevc)
        => hevc
            ? ((nal[0] >> 1) & 0x3F) is 32 or 33 or 34
            : (nal[0] & 0x1F) is 7 or 8;

    protected void ValidateSamplePayload(TrackId id, ReadOnlySpan<byte> data)
    {
        var track = TrackList[(int)id.Value - 1];
        switch (track.Codec)
        {
            case Codec.H264:
                NalUnitSampleValidator.Validate(
                    data, AvcConfiguration.GetNalLengthSize(track.CodecConfiguration.Span),
                    hevc: false);
                break;
            case Codec.H265:
                NalUnitSampleValidator.Validate(
                    data, HvcConfiguration.GetNalLengthSize(track.CodecConfiguration.Span),
                    hevc: true);
                break;
        }
    }

    private TrackId AddTrack(MediaTrackInfo track)
    {
        EnsureWritable();
        if (IsCompleted)
            throw new InvalidOperationException("Writer is complete.");
        if (_tracksLocked)
            throw new InvalidOperationException("Tracks cannot be added after writing has started.");
        if (track.Timeline is { } timeline)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(checked(timeline.PresentationStart - timeline.DecodeOrigin));
            ArgumentOutOfRangeException.ThrowIfNegative(timeline.PresentationDelay);
            if (track.TimeScale == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(track.TimeScale));
            }
        }
        TrackList.Add(track);
        return track.Id;
    }

    public abstract void WriteSample(TrackId track, ReadOnlySpan<byte> data, long pts,
        long dts, SampleFlags flags = SampleFlags.None, int? duration = null);
    public abstract ValueTask WriteSampleAsync(TrackId track, ReadOnlyMemory<byte> data,
        long pts, long dts, SampleFlags flags = SampleFlags.None,
        CancellationToken cancellationToken = default, int? duration = null);

    /// <summary>
    /// Joins separate H.264/H.265 NAL units into one length-prefixed access unit.
    /// Parameter sets are omitted by default because avc1/hvc1 keep them in the
    /// decoder configuration record.
    /// </summary>
    public void WriteSampleNalUnits(
        TrackId track,
        IReadOnlyList<ReadOnlyMemory<byte>> nalUnits,
        long pts,
        long dts,
        SampleFlags flags = SampleFlags.None,
        int? duration = null,
        bool includeParameterSets = false)
    {
        EnsureTrack(track);
        var (lengthSize, hevc) = GetNalLengthSize(track);
        var total = GetNalSampleSize(nalUnits, lengthSize, hevc, includeParameterSets);
        var buffer = ArrayPool<byte>.Shared.Rent(total);
        try
        {
            WriteNalSample(buffer.AsSpan(0, total), nalUnits, lengthSize, hevc,
                includeParameterSets);
            WriteSample(track, buffer.AsSpan(0, total), pts, dts, flags, duration);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes a sample without copying its payload in the fragmented writer.
    /// <paramref name="data"/> must remain valid and unchanged until the next
    /// <c>FlushFragment</c>/<c>FlushFragmentAsync</c>/<c>Complete</c>/
    /// <c>CompleteAsync</c> returns. The classic writer consumes it immediately.
    /// </summary>
    public virtual void WriteSampleBorrowed(
        TrackId track,
        ReadOnlyMemory<byte> data,
        long pts,
        long dts,
        SampleFlags flags = SampleFlags.None,
        int? duration = null)
        => WriteSample(track, data.Span, pts, dts, flags, duration);

    /// <inheritdoc cref="WriteSampleBorrowed"/>
    public virtual ValueTask WriteSampleBorrowedAsync(
        TrackId track,
        ReadOnlyMemory<byte> data,
        long pts,
        long dts,
        SampleFlags flags = SampleFlags.None,
        CancellationToken cancellationToken = default,
        int? duration = null)
        => WriteSampleAsync(track, data, pts, dts, flags, cancellationToken, duration);

    public async ValueTask WriteSampleNalUnitsAsync(
        TrackId track,
        IReadOnlyList<ReadOnlyMemory<byte>> nalUnits,
        long pts,
        long dts,
        SampleFlags flags = SampleFlags.None,
        CancellationToken cancellationToken = default,
        int? duration = null,
        bool includeParameterSets = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTrack(track);
        var (lengthSize, hevc) = GetNalLengthSize(track);
        var total = GetNalSampleSize(nalUnits, lengthSize, hevc, includeParameterSets);
        var buffer = ArrayPool<byte>.Shared.Rent(total);
        try
        {
            WriteNalSample(buffer.AsSpan(0, total), nalUnits, lengthSize, hevc,
                includeParameterSets);
            await WriteSampleAsync(track, buffer.AsMemory(0, total), pts, dts,
                flags, cancellationToken, duration).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    public abstract void Complete();
    public abstract ValueTask CompleteAsync(CancellationToken cancellationToken = default);

    public void Dispose()
    {
        EnsureNotInCallback();
        if (!IsCompleted && _failure is null)
        {
            Complete();
            MarkCompleted();
        }
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        EnsureNotInCallback();
        if (!IsCompleted && _failure is null)
        {
            await CompleteAsync().ConfigureAwait(false);
            MarkCompleted();
        }
        GC.SuppressFinalize(this);
    }

    protected void EnsureTrack(TrackId id)
    {
        EnsureWritable();
        if (id.Value == 0 || id.Value > TrackList.Count)
            throw new ArgumentOutOfRangeException(nameof(id));
    }

    protected void EnsureWritable()
    {
        EnsureNotInCallback();
        if (_failure is not null)
            throw new InvalidOperationException("Writer is faulted and cannot be reused.", _failure);
        if (IsCompleted)
            throw new ObjectDisposedException(GetType().Name);
    }

    protected void LockTracks() => _tracksLocked = true;

    private void EnsureNotInCallback()
    {
        if (_inCallback)
            throw new InvalidOperationException("Writer operations cannot be called from its delivery callback.");
    }

    protected void DeliverSegment(Action<Fmp4Segment>? callback, Fmp4Segment segment)
    {
        if (callback is null)
        {
            return;
        }
        _inCallback = true;
        try
        { callback(segment); }
        finally { _inCallback = false; }
    }

    protected async ValueTask DeliverSegmentAsync(
        IFmp4SegmentSink sink,
        Fmp4Segment segment,
        CancellationToken cancellationToken)
    {
        _inCallback = true;
        try
        {
            await sink.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inCallback = false;
        }
    }

    protected void MapTimestamps(TrackId id, ref long pts, ref long dts)
    {
        if (TrackList[(int)id.Value - 1].Timeline is { } timeline)
        {
            pts = checked(pts - timeline.DecodeOrigin);
            dts = checked(dts - timeline.DecodeOrigin);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(dts);
    }

    protected void MarkCompleted() => IsCompleted = true;
    protected void MarkFaulted(Exception error) => _failure = error;

    protected void ValidateSampleTiming(TrackId track, long dts, int? duration)
    {
        if (duration is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        if (_lastDecodeTimes.TryGetValue(track, out var last) && dts <= last)
            throw new ArgumentException("DTS must increase strictly within each track.", nameof(dts));
    }

    protected void RecordDecodeTime(TrackId track, long dts) => _lastDecodeTimes[track] = dts;
}
