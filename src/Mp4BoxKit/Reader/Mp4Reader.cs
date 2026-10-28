using Mp4BoxKit.Reader.Metadata;
using Mp4BoxKit.Reader.Classic;
using Mp4BoxKit.Reader.Fragments;
using Mp4BoxKit.Reader;

namespace Mp4BoxKit;

/// <summary>
/// Reads classic and fragmented MP4. Instances are not thread-safe; serialize
/// calls externally. The caller owns and disposes the input stream.
/// </summary>
public sealed partial class Mp4Reader
{
    private readonly ClassicSampleTableParser _classicTables = new();
    private readonly ClassicSampleIndexBuilder _classicIndexBuilder;
    private readonly ClassicIndexLoader _classicIndexLoader;
    private readonly ClassicSampleSeeker _classicSeeker = new();
    private readonly ClassicSampleDataReader _classicDataReader;
    private readonly FragmentParser _fragmentParser = new();
    private readonly FragmentSampleCursor _fragmentCursor = new();
    private readonly Stream _stream;
    private readonly Mp4ProbeOptions _options;
    private readonly InputBuffer _input;
    private readonly ContainerInfo _info = new();
    private bool _seenMoov;
    private readonly ContainerInput _containerInput;
    private readonly FragmentLoader _fragmentLoader;
    private readonly FragmentSeeker _fragmentSeeker;
    private ClassicReaderState? _classicState;
    private FragmentReaderState? _fragmentState;

    public Mp4Reader(Stream input, Mp4ProbeOptions? options = null)
    {
        _stream = input ?? throw new ArgumentNullException(nameof(input));
        _options = options ?? new();
        _input = new InputBuffer(_stream);
        _classicIndexBuilder = new ClassicSampleIndexBuilder(_classicTables);
        _classicDataReader = new ClassicSampleDataReader(_stream);
        _containerInput = new ContainerInput(_input);
        _classicIndexLoader = new ClassicIndexLoader(_stream, _containerInput,
            _classicIndexBuilder, _options);
        _fragmentLoader = new FragmentLoader(_containerInput, _fragmentParser, _options);
        _fragmentSeeker = new FragmentSeeker(_stream, _input, _fragmentLoader);
    }

    public ContainerInfo Info => _info;

    /// <summary>
    /// Moves the next-sample position to the requested presentation time.
    /// For classic MP4, video seeks are aligned to the preceding keyframe.
    /// </summary>
    public ValueTask SeekAsync(
        TimeSpan timestamp,
        TrackId? track = null,
        CancellationToken cancellationToken = default)
    {
        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        return SeekCoreAsync(
            timestamp.TotalSeconds,
            track,
            cancellationToken);
    }

    /// <summary>
    /// Moves the next-sample position using the selected track's timescale.
    /// Without a track, the container movie timeline is approximated using
    /// the first available track timescale.
    /// </summary>
    public ValueTask SeekAsync(
        long timestamp,
        TrackId? track = null,
        CancellationToken cancellationToken = default)
    {
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        return SeekCoreUnitsAsync(timestamp, track, cancellationToken);
    }

    private async ValueTask SeekCoreAsync(
        double seconds,
        TrackId? track,
        CancellationToken cancellationToken)
    {
        await EnsureProbedAsync(cancellationToken).ConfigureAwait(false);
        var selectedTrack = track is { } id
            ? _info.Tracks.FirstOrDefault(t => t.Id == id)
            : _info.Tracks.FirstOrDefault(t => t is VideoTrackInfo) ?? _info.Tracks.FirstOrDefault();
        if (track is not null && selectedTrack is null)
            throw new ArgumentException("Unknown track.", nameof(track));
        if (selectedTrack is { TimeScale: > 0 })
            seconds += (double)selectedTrack.PresentationMediaStart / selectedTrack.TimeScale;
        if (!_info.IsFragmented)
        {
            await EnsureClassicReaderStateAsync(cancellationToken)
                .ConfigureAwait(false);
            _classicSeeker.Seek(_classicState!, seconds, selectedTrack?.Id);
            return;
        }

        _fragmentState = await _fragmentSeeker.SeekAsync(seconds, track,
            _info.Tracks, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SeekCoreUnitsAsync(
        long timestamp,
        TrackId? track,
        CancellationToken cancellationToken)
    {
        await EnsureProbedAsync(cancellationToken).ConfigureAwait(false);
        var timeScale = track is { } selected
            ? _info.Tracks.FirstOrDefault(item => item.Id == selected)?.TimeScale ?? 1
            : _info.Tracks.FirstOrDefault()?.TimeScale ?? 1;
        await SeekCoreAsync(
                (double)timestamp / Math.Max(1, timeScale),
                track,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask EnsureProbedAsync(
        CancellationToken cancellationToken)
    {
        if (_seenMoov)
        {
            return;
        }

        var result = await ReadContainerInfoAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != ProbeStatus.Complete)
        {
            throw new Mp4FormatException(
                "Unable to probe MP4 before seeking.");
        }
    }

    /// <summary>
    /// Probes the container and reads samples from either classic MP4 or fMP4.
    /// The caller does not need to know the container layout in advance.
    /// </summary>
    public async ValueTask<int> ReadSamplesAutoAsync(
        SampleHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return await ReadSamplesAsync(handler, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the next sample without requiring the caller to know whether the
    /// input is classic MP4 or fragmented MP4. Returns null at end of input.
    /// </summary>
    public async ValueTask<Mp4Sample?> ReadSampleAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_seenMoov)
        {
            var probe = await ReadContainerInfoAsync(cancellationToken)
                .ConfigureAwait(false);

            if (probe.Status != ProbeStatus.Complete)
            {
                return null;
            }
        }

        if (_info.IsFragmented)
        {
            return ApplyPresentationTimeline(await ReadNextFragmentSampleAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        await EnsureClassicReaderStateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (_classicState!.NextSampleIndex >= _classicState.Samples.Count)
        {
            return null;
        }

        var indexed = _classicState.Samples[
            _classicState.NextSampleIndex++];
        var data = await _classicDataReader.ReadAsync(
                indexed,
                cancellationToken)
            .ConfigureAwait(false);
        return ApplyPresentationTimeline(new Mp4Sample(
            new SampleInfo(
                indexed.Track,
                indexed.Pts,
                indexed.Dts,
                indexed.Duration,
                indexed.Flags),
            data));
    }

    private Mp4Sample? ApplyPresentationTimeline(Mp4Sample? sample)
    {
        if (sample is null)
        {
            return null;
        }

        var track = _info.Tracks.First(t => t.Id == sample.Track);
        var offset = track.PresentationMediaStart;
        if (offset == 0)
        {
            return sample;
        }
        return new Mp4Sample(new SampleInfo(sample.Track,
            checked(sample.Pts - offset), checked(sample.Dts - offset),
            sample.Info.Duration, sample.Info.Flags), sample.Data.Span);
    }

    /// <summary>
    /// Reads all remaining samples from either classic MP4 or fMP4.
    /// Call <see cref="ReadSampleAsync"/> when incremental control is needed.
    /// </summary>
    public async ValueTask<int> ReadSamplesAsync(
        SampleHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var count = 0;
        while (await ReadSampleAsync(cancellationToken)
                   .ConfigureAwait(false) is { } sample)
        {
            var info = sample.Info;
            handler(in info, sample.Data.Span);
            count++;
        }

        return count;
    }

    private async ValueTask EnsureClassicReaderStateAsync(
        CancellationToken cancellationToken)
    {
        if (_classicState is not null)
        {
            return;
        }

        _classicState = await _classicIndexLoader.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<Mp4Sample?> ReadNextFragmentSampleAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_fragmentState is not null &&
                _fragmentCursor.TryRead(_fragmentState, out var sample))
            {
                return sample;
            }

            if (!await ReadNextFragmentAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                return null;
            }
        }
    }

    private async ValueTask<bool> ReadNextFragmentAsync(
        CancellationToken cancellationToken)
    {
        var state = await _fragmentLoader.ReadNextAsync(_info.Tracks, cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
            return false;
        _fragmentState = state;
        return true;
    }

    /// <summary>
    /// Reads samples from a classic, non-fragmented MP4 file.
    /// The input must be seekable because sample offsets are stored in stco/co64.
    /// </summary>
    [Obsolete("Use ReadSampleAsync or ReadSamplesAsync for format-independent reading.")]
    public int ReadClassicSamples(SampleHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_stream.CanSeek)
        {
            throw new NotSupportedException(
                "Classic MP4 sample reading requires a seekable stream.");
        }

        var position = _stream.Position;
        _stream.Position = 0;

        try
        {
            var file = ReadStreamBytes();
            return ReadClassicSamples(file, handler);
        }
        finally
        {
            _stream.Position = position;
        }
    }

    /// <summary>Async facade for classic MP4 sample reading.</summary>
    [Obsolete("Use ReadSampleAsync or ReadSamplesAsync for format-independent reading.")]
    public async ValueTask<int> ReadClassicSamplesAsync(
        SampleHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_stream.CanSeek)
        {
            throw new NotSupportedException(
                "Classic MP4 sample reading requires a seekable stream.");
        }

        var position = _stream.Position;
        _stream.Position = 0;

        try
        {
            using var buffer = new MemoryStream();
            await _stream.CopyToAsync(buffer, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return ReadClassicSamples(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), handler);
        }
        finally
        {
            _stream.Position = position;
        }
    }

    private byte[] ReadStreamBytes()
    {
        using var buffer = new MemoryStream();
        _stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private int ReadClassicSamples(
        ReadOnlySpan<byte> file,
        SampleHandler handler)
    {
        var moovRange = _classicTables.FindTopLevelBox(file, BoxTypes.Moov);
        if (moovRange.Length == 0)
        {
            throw new Mp4FormatException("Classic MP4 does not contain moov.");
        }

        var tracks = _classicTables.ParseTracks(
            file.Slice(moovRange.Offset, moovRange.Length));
        var count = 0;

        foreach (var track in tracks)
        {
            var samples = _classicTables.BuildSamples(track);

            foreach (var sample in samples)
            {
                if (sample.Offset < 0 || sample.Size < 0 ||
                    sample.Offset > file.Length - sample.Size)
                {
                    throw new Mp4FormatException(
                        "Classic MP4 sample points outside the file.");
                }

                var info = new SampleInfo(
                    track.Track.Id,
                    sample.Pts,
                    sample.Dts,
                    sample.Duration,
                    sample.Flags);

                handler(
                    in info,
                    file.Slice(checked((int)sample.Offset), sample.Size));
                count++;
            }
        }

        return count;
    }

    /// <summary>Reads successive fMP4 samples without seeking; callback data is valid for the call.</summary>
    [Obsolete("Use ReadSampleAsync or ReadSamplesAsync for format-independent reading.")]
    public async ValueTask<int> ReadFragmentSamplesAsync(SampleHandler handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        while (true)
        {
            var header = await ReadHeaderAsync(ct).ConfigureAwait(false);
            if (header is null)
            {
                return 0;
            }

            if (header.Value.Type == BoxTypes.Moov)
            {
                await ReadMoovAsync(header.Value, ct).ConfigureAwait(false);
                _seenMoov = true;
                _info.IsFragmented = ContainsMvex;
                continue;
            }

            if (header.Value.Type != BoxTypes.Moof)
            {
                await SkipAsync(header.Value.PayloadSize, ct).ConfigureAwait(false);
                continue;
            }

            var moof = await ReadPayloadAsync(header.Value.PayloadSize, ct).ConfigureAwait(false);
            var fragment = ParseFragment(moof);
            var mdat = await ReadHeaderAsync(ct).ConfigureAwait(false);
            if (mdat is null || mdat.Value.Type != BoxTypes.Mdat)
            {
                return -1;
            }

            var media = await ReadPayloadAsync(mdat.Value.PayloadSize, ct)
                .ConfigureAwait(false);
            var offset = 0;
            foreach (var track in fragment)
            {
                foreach (var sample in track.Samples)
                {
                    if (sample.Size > media.Length - offset)
                    {
                        throw new Mp4FormatException("trun sample exceeds mdat payload.");
                    }

                    var info = new SampleInfo(
                        new TrackId(track.TrackId),
                        sample.Pts,
                        sample.Dts,
                        sample.Duration,
                        sample.Flags);
                    handler(in info, media.AsSpan(offset, sample.Size));
                    offset += sample.Size;
                }
            }
            return fragment.Sum(static x => x.Samples.Count);
        }
    }

    private List<FragmentTrack> ParseFragment(ReadOnlySpan<byte> moof)
        => _fragmentParser.Parse(moof);
}
