(function () {
    const runtime = {
        label: 'Blazor WASM',
        ready: null,
        inspectBytes: null,
        version: () => 'Blazor WASM'
    };

    globalThis.Mp4BoxKitRuntime = runtime;

    runtime.ready = (async () => {
        await waitForBlazor();
        runtime.inspectBytes = async (bytes, options = {}) => {
            const json = await DotNet.invokeMethodAsync(
                'Mp4BoxKit.Blazor',
                'InspectBytes',
                bytes,
                JSON.stringify(options));
            return JSON.parse(json);
        };
        return true;
    })();

    async function waitForBlazor() {
        for (let attempt = 0; attempt < 300; attempt++) {
            if (globalThis.DotNet) {
                try {
                    await DotNet.invokeMethodAsync('Mp4BoxKit.Blazor', 'Ping');
                    return;
                } catch {
                    // The assembly may still be starting.
                }
            }

            await delay(100);
        }

        throw new Error('Blazor WebAssembly runtime did not become ready.');
    }

    function delay(milliseconds) {
        return new Promise((resolve) => setTimeout(resolve, milliseconds));
    }
})();
