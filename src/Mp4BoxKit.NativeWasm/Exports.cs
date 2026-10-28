using System.Runtime.InteropServices;
using System.Text;
using Mp4BoxKit.Inspection;

public static class NativeExports
{
    [UnmanagedCallersOnly(EntryPoint = "mp4boxkit_alloc")]
    public static unsafe byte* Alloc(int length)
    {
        return length <= 0 ? null : (byte*)NativeMemory.Alloc((nuint)length);
    }

    /// <summary>
    /// Returns 0 on success, -1 for invalid arguments, and -2 when inspection
    /// throws. <paramref name="result"/> receives UTF-8 JSON.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "mp4boxkit_inspect")]
    public static unsafe int Inspect(
        byte* data,
        int length,
        byte* optionsJson,
        int optionsLength,
        byte** result,
        int* resultLength)
    {
        try
        {
            if (data is null || length < 0 || result is null || resultLength is null)
            {
                return -1;
            }

            var options = optionsLength > 0 && optionsJson is not null
                ? InspectionJson.DeserializeOptions(Encoding.UTF8.GetString(
                    new ReadOnlySpan<byte>(optionsJson, optionsLength)))
                : new InspectOptions();
            var inspection = Mp4Inspector.Inspect(data, length, "input.mp4", options);
            var json = InspectionJson.Serialize(inspection);
            var jsonLength = Encoding.UTF8.GetByteCount(json);
            var memory = (byte*)NativeMemory.Alloc((nuint)jsonLength);
            Encoding.UTF8.GetBytes(json.AsSpan(), new Span<byte>(memory, jsonLength));
            *result = memory;
            *resultLength = jsonLength;
            return 0;
        }
        catch
        {
            return -2;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "mp4boxkit_free")]
    public static unsafe void Free(byte* data)
    {
        NativeMemory.Free(data);
    }

}
