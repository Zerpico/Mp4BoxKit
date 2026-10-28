using Mp4BoxKit.Boxes;
using Mp4BoxKit.Writer.Classic;
using Mp4BoxKit.Writer;

namespace Mp4BoxKit;

/// <summary>Streams media to mdat and finalizes sample tables into moov.</summary>
public sealed class Mp4FileWriter : Mp4Writer
{
    private readonly ClassicSampleTableBuilder _samples = new();
    private bool _started;
    private long _mdatOffset;
    private readonly List<FileSample> _writeOrder = new();
    private readonly Mp4FileWriterOptions _options;
    private FileStream? _spool;
    private bool FastStart => _options.FastStart;

    public Mp4FileWriter(IMp4Output output, Mp4FileWriterOptions? options = null)
        : base(output)
    {
        _options = options ?? new();
        if (!output.CanSeek)
            throw new NotSupportedException("Classic MP4 writer requires seekable output.");
    }

    private void Start()
    {
        if (_started)
            return;
        LockTracks();
        if (FastStart)
        {
            _spool = new FileStream(Path.Combine(Path.GetTempPath(), $"mp4boxkit-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            _started = true;
            return;
        }
        Output.Write(FileTypeWriter.Serialize());
        _mdatOffset = Output.Position;
        Span<byte> header = stackalloc byte[8];
        BigEndian.W32(header, 0);
        BigEndian.W32(header[4..], BoxTypes.Mdat.Value);
        Output.Write(header);
        _started = true;
    }

    public override void WriteSample(TrackId track, ReadOnlySpan<byte> data,
        long pts, long dts, SampleFlags flags = SampleFlags.None, int? duration = null)
    {
        EnsureTrack(track);
        MapTimestamps(track, ref pts, ref dts);
        ValidateSampleTiming(track, dts, duration);
        ValidateSamplePayload(track, data);
        if (_samples.Tracks.Count >= track.Value)
        {
            var samples = _samples.Tracks[(int)track.Value - 1];
            if (samples.Count > 0 && samples[^1].HasExplicitDuration &&
                dts != checked(samples[^1].Dts + samples[^1].Duration))
            {
                throw new ArgumentException(
                    "Classic MP4 requires contiguous DTS when duration is explicit; " +
                    "timeline gaps need edit lists.",
                    nameof(dts));
            }
        }

        try
        {
            Start();
            var offset = Output.Position;
            if (FastStart)
            {
                _spool!.Write(data);
            }
            else
            {
                Output.Write(data);
            }

            _samples.Add(
                TrackList.Count,
                track,
                Array.Empty<byte>(),
                pts,
                dts,
                flags,
                offset,
                duration,
                data.Length);
            RecordDecodeTime(track, dts);
            if (FastStart)
            {
                _writeOrder.Add(_samples.Tracks[(int)track.Value - 1][^1]);
            }
        }
        catch (Exception error)
        {
            MarkFaulted(error);
            _spool?.Dispose();
            _spool = null;
            throw;
        }
    }

    public override ValueTask WriteSampleAsync(TrackId track, ReadOnlyMemory<byte> data,
        long pts, long dts, SampleFlags flags = SampleFlags.None,
        CancellationToken cancellationToken = default, int? duration = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteSample(track, data.Span, pts, dts, flags, duration);
        return ValueTask.CompletedTask;
    }

    public override void Complete()
    {
        if (IsCompleted)
            return;
        EnsureWritable();
        try
        {
            CompleteCore();
        }
        catch (Exception error)
        {
            MarkFaulted(error);
            throw;
        }
        finally
        {
            _spool?.Dispose();
            _spool = null;
        }
    }

    private void CompleteCore()
    {
        Start();
        _samples.FinalizeDurations();
        if (FastStart)
        {
            try
            {
                CompleteFastStart();
            }
            catch (Exception error)
            {
                MarkFaulted(error);
                throw;
            }
            finally
            {
                _spool?.Dispose();
                _spool = null;
            }

            MarkCompleted();
            return;
        }
        var end = Output.Position;
        Output.Seek(_mdatOffset, SeekOrigin.Begin);
        Span<byte> size = stackalloc byte[4];
        BigEndian.W32(size, checked((uint)(end - _mdatOffset)));
        Output.Write(size);
        Output.Seek(end, SeekOrigin.Begin);
        Output.Write(new ClassicMovieWriter().Serialize(TrackList, _samples.Tracks));
        MarkCompleted();
    }

    public override ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Complete();
        return ValueTask.CompletedTask;
    }

    private void CompleteFastStart()
    {
        var fileType = FileTypeWriter.Serialize();
        var movieWriter = new ClassicMovieWriter();
        var movie = movieWriter.Serialize(TrackList, _samples.Tracks);
        long mediaSize = 8;
        foreach (var sample in _writeOrder)
            mediaSize = checked(mediaSize + sample.Size);
        var size = checked((uint)mediaSize);
        // stco can grow to co64 after relocation, changing moov's own size.
        while (true)
        {
            var position = checked(Output.Position + fileType.Length + movie.Length + 8);
            foreach (var sample in _writeOrder)
            {
                sample.Offset = position;
                position = checked(position + sample.Size);
            }
            var updated = movieWriter.Serialize(TrackList, _samples.Tracks);
            if (updated.Length == movie.Length)
            {
                movie = updated;
                break;
            }
            movie = updated;
        }
        Output.Write(fileType);
        Output.Write(movie);
        Span<byte> header = stackalloc byte[8];
        BigEndian.W32(header, size);
        BigEndian.W32(header[4..], BoxTypes.Mdat.Value);
        Output.Write(header);
        _spool!.Position = 0;
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = _spool.Read(buffer)) != 0)
        {
            Output.Write(buffer.AsSpan(0, read));
        }
        _writeOrder.Clear();
    }
}
