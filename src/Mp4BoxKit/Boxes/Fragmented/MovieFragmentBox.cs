using Mp4BoxKit.Writer;

namespace Mp4BoxKit.Boxes.Fragmented;

internal sealed class MovieFragmentBox
{
    private readonly IReadOnlyList<MediaTrackInfo> _tracks;
    private readonly IReadOnlyList<List<PendingSample>> _pending;
    private readonly uint _sequence;
    private readonly int[] _offsets;

    public MovieFragmentBox(
        IReadOnlyList<MediaTrackInfo> tracks,
        IReadOnlyList<List<PendingSample>> pending,
        uint sequence,
        int[] offsets)
    {
        _tracks = tracks;
        _pending = pending;
        _sequence = sequence;
        _offsets = offsets;
    }

    public byte[] Serialize()
    {
        var buffer = new IsoBoxBuffer();

        IsoBoxWriter.Box(buffer, BoxTypes.Moof, moof =>
        {
            new MovieFragmentHeaderBox(_sequence).WriteTo(moof);

            for (var index = 0; index < _tracks.Count; index++)
            {
                if (_pending[index].Count == 0)
                {
                    continue;
                }

                new TrackFragmentBox(
                    _tracks[index],
                    _pending[index],
                    _offsets[index]).WriteTo(moof);
            }
        });

        return buffer.ToArray();
    }
}
