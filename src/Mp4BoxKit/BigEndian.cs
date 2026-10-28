using System.Buffers.Binary;

namespace Mp4BoxKit;

internal static class BigEndian
{
    public static uint U32(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt32BigEndian(p);
    public static ulong U64(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt64BigEndian(p);
    public static ushort U16(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt16BigEndian(p);
    public static int I32(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadInt32BigEndian(p);
    public static void W32(Span<byte> p, uint v) => BinaryPrimitives.WriteUInt32BigEndian(p, v);
    public static void W64(Span<byte> p, ulong v) => BinaryPrimitives.WriteUInt64BigEndian(p, v);
    public static void W16(Span<byte> p, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(p, v);
}

public readonly record struct BoxHeader(ulong Size, FourCc Type, int HeaderSize)
{
    public ulong PayloadSize => Size >= (ulong)HeaderSize ? Size - (ulong)HeaderSize : 0;
}
