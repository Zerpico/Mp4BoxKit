using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

internal static class HvcConfiguration
{
    internal static int GetNalLengthSize(ReadOnlySpan<byte> hvcC)
        => (hvcC[21] & 3) + 1;

    internal static void Validate(ReadOnlySpan<byte> hvcC)
    {
        if (hvcC.Length < 23)
            throw new ArgumentException("hvcC payload is too short.");
        if (hvcC[0] != 1)
            throw new ArgumentException("hvcC configurationVersion must be 1.");

        var lengthSize = (hvcC[21] & 3) + 1;
        if (lengthSize == 3)
            throw new ArgumentException(
                "hvcC lengthSizeMinusOne=2 (3-byte NAL lengths) is not allowed by ISO-BMFF.");

        var arrayCount = hvcC[22];
        if (arrayCount == 0)
            throw new ArgumentException("hvcC must contain at least one parameter set array.");

        var offset = 23;
        for (var array = 0; array < arrayCount; array++)
        {
            if (offset > hvcC.Length - 3)
                throw new ArgumentException("Truncated hvcC NAL unit array.");

            var count = BinaryPrimitives.ReadUInt16BigEndian(hvcC[(offset + 1)..]);
            offset += 3;
            if (count == 0)
                throw new ArgumentException("hvcC parameter set array is empty.");

            for (var i = 0; i < count; i++)
            {
                if (offset > hvcC.Length - 2)
                    throw new ArgumentException("Truncated hvcC NAL unit length.");

                var length = BinaryPrimitives.ReadUInt16BigEndian(hvcC[offset..]);
                offset += 2;
                if (length == 0 || length > hvcC.Length - offset)
                    throw new ArgumentException("Invalid hvcC NAL unit length.");
                offset += length;
            }
        }
    }
}
