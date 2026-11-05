namespace Mp4BoxKit.Reader.Fragments;

internal sealed class FragmentLoader(
    ContainerInput input,
    FragmentParser parser,
    Mp4ProbeOptions options,
    Action<SkippedFragmentInfo> fragmentSkipped)
{
    private bool _decodeTimeUnknown;
    private readonly Dictionary<uint, long> _decodeEnds = new();
    private long? _expectedPosition;
    private List<(long Start, long End)>? _mediaRegions;
    internal sealed record DecodeState(Dictionary<uint, long> Ends, bool Unknown, long? Position);

    public bool HasUnanchoredDecodeTime { get; private set; }

    public DecodeState CaptureDecodeState() =>
        new(new Dictionary<uint, long>(_decodeEnds), _decodeTimeUnknown, _expectedPosition);

    public void RestoreDecodeState(DecodeState state)
    {
        _decodeEnds.Clear();
        foreach (var pair in state.Ends)
            _decodeEnds.Add(pair.Key, pair.Value);
        _decodeTimeUnknown = state.Unknown;
        _expectedPosition = state.Position;
        HasUnanchoredDecodeTime = false;
    }

    public void ResetDecodeState(long position)
    {
        RestoreDecodeState(new DecodeState(new Dictionary<uint, long>(), false, position));
    }
    public async ValueTask<FragmentReaderState?> ReadNextAsync(
        IReadOnlyList<MediaTrackInfo> tracks, CancellationToken cancellationToken)
    {
        if (_expectedPosition != input.Position)
        {
            _decodeEnds.Clear();
        }
        while (true)
        {
            var header = await input.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (header is null)
                return null;
            if (header.Value.Type != BoxTypes.Moof)
            {
                await input.SkipAsync(header.Value.PayloadSize, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (header.Value.PayloadSize > (ulong)options.MaxBoxPayloadBytes)
                throw new Mp4FormatException("Fragment moof exceeds configured payload limit.");

            var moofStart = input.Position - header.Value.HeaderSize;
            var moof = await input.ReadPayloadAsync(header.Value.PayloadSize,
                cancellationToken).ConfigureAwait(false);
            if (input.CanSeek)
            {
                var continuation = input.Position;
                var seekable = new FragmentReaderState();
                var parsed = ParseRecoverableFragment(moof, 0, moofStart);
                if (parsed is null)
                {
                    continue;
                }

                seekable.Tracks.AddRange(parsed);
                ValidateMediaBudget(seekable.Tracks);
                using var media = new MemoryStream();
                try
                {
                    await EnsureMediaRegionsAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var track in seekable.Tracks)
                        for (var i = 0; i < track.Samples.Count; i++)
                        {
                            var sample = track.Samples[i];
                            if (sample.Size > options.MaxBoxPayloadBytes)
                                throw new Mp4FormatException("Fragment sample exceeds configured payload limit.");
                            var absolute = checked(moofStart + sample.DataOffset);
                            if (absolute < 0 || sample.Size < 0 || absolute > input.Length - sample.Size)
                                throw new Mp4FormatException("Fragment sample points outside the input file.");
                            if (!ContainsRegion(_mediaRegions!, absolute, sample.Size))
                                throw new Mp4FormatException("Fragment sample points outside mdat payloads.");
                            input.Seek(absolute);
                            var data = await input.ReadPayloadAsync(
                                    (ulong)sample.Size,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            track.Samples[i] = sample with { DataOffset = checked((int)media.Position) };
                            media.Write(data);
                        }
                    seekable.Media = media.ToArray();
                }
                finally { input.Seek(continuation); }
                ApplyDecodeTimes(seekable, tracks);
                _expectedPosition = input.Position;
                if (seekable.Tracks.Count != 0)
                {
                    return seekable;
                }
                continue;
            }
            var mdat = await input.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            while (mdat is not null && mdat.Value.Type != BoxTypes.Mdat)
            {
                if (mdat.Value.Type == BoxTypes.Moof || mdat.Value.Size == 0)
                {
                    throw new Mp4FormatException("Fragment has no associated mdat.");
                }
                await input.SkipAsync(mdat.Value.PayloadSize, cancellationToken).ConfigureAwait(false);
                mdat = await input.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            }
            if (mdat is null)
            {
                throw new Mp4FormatException("Fragment has no associated mdat.");
            }
            if (mdat.Value.PayloadSize > (ulong)options.MaxBoxPayloadBytes)
                throw new Mp4FormatException("Fragment mdat exceeds configured payload limit.");
            var mediaPayloadOffset = checked((int)(input.Position - moofStart));
            var state = new FragmentReaderState();
            var parsedTracks = ParseRecoverableFragment(moof, mediaPayloadOffset, moofStart);
            if (parsedTracks is null)
            {
                await input.SkipAsync(mdat.Value.PayloadSize, cancellationToken).ConfigureAwait(false);
                continue;
            }
            state.Tracks.AddRange(parsedTracks);
            ValidateMediaBudget(state.Tracks);
            state.Media = await input.ReadPayloadAsync(mdat.Value.PayloadSize,
                cancellationToken).ConfigureAwait(false);
            foreach (var fragmentTrack in state.Tracks)
                foreach (var sample in fragmentTrack.Samples)
                    if (sample.DataOffset < 0 || sample.Size < 0 ||
                        sample.DataOffset > state.Media.Length - sample.Size)
                        throw new Mp4FormatException("Fragment sample points outside the associated mdat payload.");
            ApplyDecodeTimes(state, tracks);
            _expectedPosition = input.Position;
            if (state.Tracks.Count != 0)
                return state;
        }
    }

    private List<FragmentTrack>? ParseRecoverableFragment(byte[] moof, int mediaPayloadOffset, long moofStart)
    {
        List<FragmentTrack> tracks;
        try
        {
            tracks = parser.Parse(moof, mediaPayloadOffset, moofStart);
        }
        catch (Mp4FormatException error) when (options.SkipInvalidFragments)
        {
            DiscardFragment(moofStart, error.Message);
            return null;
        }

        if (_decodeTimeUnknown && tracks.Any(track => track.Samples.Count > 0 &&
            !track.HasDecodeTime && !_decodeEnds.ContainsKey(track.TrackId)))
        {
            DiscardFragment(moofStart, "Decode time is unknown after a discarded fragment; tfdt is required.");
            return null;
        }
        return tracks;
    }

    private void DiscardFragment(long offset, string reason)
    {
        _decodeEnds.Clear();
        _decodeTimeUnknown = true;
        fragmentSkipped(new SkippedFragmentInfo(offset, reason));
    }

    private void ValidateMediaBudget(IReadOnlyList<FragmentTrack> tracks)
    {
        // Offsets into the materialized media buffer are represented by int.
        var remaining = Math.Min(options.MaxBoxPayloadBytes, Array.MaxLength);
        foreach (var track in tracks)
            foreach (var sample in track.Samples)
            {
                if (sample.Size < 0 || sample.Size > remaining)
                    throw new Mp4FormatException("Fragment media exceeds configured payload limit.");
                remaining -= sample.Size;
            }
    }

    private void ApplyDecodeTimes(FragmentReaderState state, IReadOnlyList<MediaTrackInfo> tracks)
    {
        HasUnanchoredDecodeTime = false;
        foreach (var track in state.Tracks)
        {
            if (track.Samples.Count > 0 && !track.HasDecodeTime && !_decodeEnds.ContainsKey(track.TrackId))
                HasUnanchoredDecodeTime = true;
            track.TimeScale = tracks.FirstOrDefault(item => item.Id.Value == track.TrackId)
                ?.TimeScale ?? 1;
            if (!track.HasDecodeTime && _decodeEnds.TryGetValue(track.TrackId, out var origin))
                for (var i = 0; i < track.Samples.Count; i++)
                {
                    var sample = track.Samples[i];
                    track.Samples[i] = sample with
                    {
                        Dts = checked(sample.Dts + origin),
                        Pts = checked(sample.Pts + origin)
                    };
                }
            if (track.Samples.Count > 0)
                _decodeEnds[track.TrackId] = checked(track.Samples[^1].Dts + track.Samples[^1].Duration);
        }
    }

    private static bool ContainsRegion(
        List<(long Start, long End)> regions,
        long offset,
        int size)
    {
        var low = 0;
        var high = regions.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var region = regions[middle];
            if (offset < region.Start)
            {
                high = middle - 1;
            }
            else if (offset >= region.End)
            {
                low = middle + 1;
            }
            else
            {
                return offset <= region.End - size;
            }
        }

        return false;
    }

    private async ValueTask EnsureMediaRegionsAsync(CancellationToken cancellationToken)
    {
        if (_mediaRegions is not null)
        {
            return;
        }
        var regions = new List<(long Start, long End)>();
        input.Seek(0);
        while (input.Position < input.Length)
        {
            var start = input.Position;
            var header = await input.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (header is null)
            {
                break;
            }
            var end = header.Value.Size == 0 ? input.Length : checked(start + (long)header.Value.Size);
            if (end < input.Position || end > input.Length)
                throw new Mp4FormatException("Invalid top-level box extent.");
            if (header.Value.Type == BoxTypes.Mdat)
            {
                regions.Add((input.Position, end));
            }
            input.Seek(end);
        }
        _mediaRegions = regions;
    }
}
