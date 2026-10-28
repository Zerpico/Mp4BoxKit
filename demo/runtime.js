(function () {
    const runtime = {
        label: 'NativeAOT-LLVM',
        ready: null,
        inspectBytes: null,
        version: () => 'NativeAOT-LLVM'
    };

    globalThis.Mp4BoxKitRuntime = runtime;

    runtime.ready = (async () => {
        await loadScript('Mp4BoxKit.NativeWasm.js');
        const module = globalThis.Module;
        if (!module) {
            throw new Error('Mp4BoxKit.NativeWasm.js did not initialize Module.');
        }

        if (!module.calledRun) {
            await new Promise((resolve, reject) => {
                module.onRuntimeInitialized = resolve;
                module.onAbort = (reason) => {
                    reject(new Error(String(reason)));
                };
            });
        }

        runtime.inspectBytes = (bytes, options = {}) => inspect(module, bytes, options);
        return module;
    })();

    function inspect(module, bytes, options) {
        const alloc = module.cwrap('mp4boxkit_alloc', 'number', ['number']);
        const inspectExport = module.cwrap(
            'mp4boxkit_inspect',
            'number',
            ['number', 'number', 'number', 'number', 'number', 'number']);
        const free = module.cwrap('mp4boxkit_free', null, ['number']);
        const optionsBytes = new TextEncoder().encode(JSON.stringify(options));
        const dataPtr = alloc(bytes.length);
        const optionsPtr = alloc(optionsBytes.length);
        const resultPtr = alloc(4);
        const resultLengthPtr = alloc(4);

        try {
            module.HEAPU8.set(bytes, dataPtr);
            module.HEAPU8.set(optionsBytes, optionsPtr);
            const status = inspectExport(
                dataPtr,
                bytes.length,
                optionsPtr,
                optionsBytes.length,
                resultPtr,
                resultLengthPtr);

            if (status !== 0) {
                throw new Error(`mp4boxkit_inspect returned ${status}`);
            }

            const jsonPtr = module.HEAPU32[resultPtr >>> 2];
            const jsonLength = module.HEAPU32[resultLengthPtr >>> 2];
            const json = new TextDecoder().decode(
                module.HEAPU8.slice(jsonPtr, jsonPtr + jsonLength));
            free(jsonPtr);
            return JSON.parse(json);
        } finally {
            free(dataPtr);
            free(optionsPtr);
            free(resultPtr);
            free(resultLengthPtr);
        }
    }

    function loadScript(source) {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = source;
            script.onload = resolve;
            script.onerror = () => {
                reject(new Error(`Failed to load ${source}.`));
            };
            document.head.append(script);
        });
    }
})();
