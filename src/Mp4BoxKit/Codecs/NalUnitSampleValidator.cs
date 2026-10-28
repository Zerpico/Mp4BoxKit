using System.Buffers.Binary;

namespace Mp4BoxKit.Codecs;

internal static class NalUnitSampleValidator
{
    public static void Validate(ReadOnlySpan<byte> sample, int lengthSize, bool hevc)
    {
        try
        {
            ValidateCore(sample, lengthSize, hevc);
        }
        catch (ArgumentException error) when (StartsWithAnnexBStartCode(sample) &&
            !error.Message.Contains("parameter sets", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Annex B start code detected. H.264/H.265 samples must be length-prefixed " +
                "AVCC/HVCC, not Annex B; convert with AvcSample.FromAnnexB before WriteSample.",
                "data",
                error);
        }
    }

    private static void ValidateCore(ReadOnlySpan<byte> sample, int lengthSize, bool hevc)
    {
        if (sample.IsEmpty)
            throw new ArgumentException("Sample payload is empty.", "data");
        if (lengthSize is < 1 or > 4 || lengthSize == 3)
            throw new ArgumentException("Unsupported NAL length size.", "lengthSize");

        var offset = 0;
        var hasVcl = false;
        while (offset < sample.Length)
        {
            if (sample.Length - offset < lengthSize)
                throw new ArgumentException("Truncated NAL length prefix; the sample is not a complete length-prefixed access unit.", "data");

            var length = ReadLength(sample[offset..], lengthSize);
            offset += lengthSize;
            if (length == 0 || length > (uint)(sample.Length - offset))
                throw new ArgumentException("NAL length exceeds the sample payload.", "data");

            var nal = sample.Slice(offset, (int)length);
            var type = hevc ? (nal[0] >> 1) & 0x3F : nal[0] & 0x1F;
            if (hevc ? type <= 31 : type is >= 1 and <= 5)
                hasVcl = true;
            offset += (int)length;
        }

        if (!hasVcl)
            throw new ArgumentException(
                "Sample contains only parameter sets or non-VCL NAL units; " +
                "write one complete access unit per WriteSample call.",
                "data");
    }

    private static uint ReadLength(ReadOnlySpan<byte> data, int lengthSize) => lengthSize switch
    {
        1 => data[0],
        2 => BinaryPrimitives.ReadUInt16BigEndian(data),
        4 => BinaryPrimitives.ReadUInt32BigEndian(data),
        _ => throw new ArgumentException("Unsupported NAL length size.", nameof(lengthSize))
    };

    private static bool StartsWithAnnexBStartCode(ReadOnlySpan<byte> sample)
    {
        if (sample.Length < 3 || sample[0] != 0 || sample[1] != 0)
            return false;
        return sample[2] == 1 ||
            (sample.Length >= 4 && sample[2] == 0 && sample[3] == 1);
    }
}
