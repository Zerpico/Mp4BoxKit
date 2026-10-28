using Microsoft.JSInterop;
using Mp4BoxKit.Inspection;

namespace Mp4BoxKit.Blazor;

/// <summary>JSInvokable bridge used by the shared inspector UI.</summary>
public static class InspectorBridge
{
    [JSInvokable]
    public static string Ping() => "ok";

    [JSInvokable]
    public static async Task<string> InspectBase64(
        string base64,
        string optionsJson)
    {
        var bytes = Convert.FromBase64String(base64);
        return await InspectBytes(bytes, optionsJson);
    }

    [JSInvokable]
    public static async Task<string> InspectBytes(
        byte[] bytes,
        string optionsJson)
    {
        var options = InspectionJson.DeserializeOptions(optionsJson);
        var inspection = await Mp4Inspector.InspectAsync(
            bytes,
            "input.mp4",
            options);
        return InspectionJson.Serialize(inspection);
    }

    [JSInvokable]
    public static string Version() => "Blazor WASM";
}
