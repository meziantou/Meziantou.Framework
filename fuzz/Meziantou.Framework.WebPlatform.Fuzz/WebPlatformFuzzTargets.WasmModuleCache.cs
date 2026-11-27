using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Wasm;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the module cache of <see cref="WasmWebAssemblyRuntime"/>: the input is a module, compiled with a runtime
/// whose cache is small (so that the modules of the previous inputs are removed) and with a runtime without cache, together
/// with variants of the module (a byte changed, the last byte removed). The two runtimes agree on whether a binary is valid
/// and on its imports and exports, the same bytes give the same compiled module (when the binary is large enough to be
/// kept), other bytes give another module, and the size of the cache does not exceed its capacity.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private const int WasmModuleCacheCapacity = 4096;

    private static readonly WasmWebAssemblyRuntime CachedWasmRuntime = new() { ModuleCacheCapacity = WasmModuleCacheCapacity };
    private static readonly WasmWebAssemblyRuntime UncachedWasmRuntime = new() { ModuleCacheCapacity = 0 };

    // The magic number and the version, the section ids, the types, and the opcodes of the instructions of the seeds
    private static string[] WasmModuleCacheTokens => ["\0asm\u0001\0\0\0", "\u0001", "\u0002", "\u0003", "\u0005", "\u0006", "\u0007", "\u000A", "\u000B", "`", "\u007F", "~", "}", "|", "p", "o", " \u0000", "A\u0000", "j", "\u0010\u0000"];

    /// <summary>The modules of the tests of the cache, and the modules of the WebAssembly seeds.</summary>
    private static IEnumerable<FuzzSeed> GetWasmModuleCacheSeeds()
    {
        string[] modules =
        [
            """(module (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add))""",
            """(module (func (export "f") (result i32) i32.const 1))""",
            """(module (func (export "f") (result i32) i32.const 2))""",
            """(module (func (export "a")))""",
            """(module (import "env" "f" (func)) (memory (export "m") 1) (global (export "g") i32 (i32.const 7)))""",

            // Large enough to be kept by the cache (WasmModuleCache.MinimumModuleSize)
            "(module (memory 1) (data (i32.const 0) \"" + new string('a', 1100) + "\") (func (export \"f\") (result i32) i32.const 1))",
        ];
        for (var i = 0; i < modules.Length; i++)
        {
            yield return new FuzzSeed("module-cache-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), WasmEngine.WasmRuntime.WatToWasm(modules[i]));
        }

        foreach (var seed in GetSeeds("wasm"))
        {
            yield return seed;
        }
    }

    /// <summary>Compiles the module and its variants with and without the cache, and checks that they agree.</summary>
    public static void WasmModuleCacheTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        byte[][] inputs = [bytes, [.. bytes], Changed(bytes), bytes.Length > 0 ? bytes[..^1] : [1], bytes];
        var modules = new WasmEngine.WasmModule?[inputs.Length];
        for (var i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            var uncached = TryCompile(UncachedWasmRuntime, [.. input]);
            var cached = TryCompile(CachedWasmRuntime, [.. input]);
            FuzzAssert.True((uncached is null) == (cached is null), $"the runtime with a cache and the runtime without agree on whether the module {i} is valid");
            if (cached is null || uncached is null)
                continue;

            FuzzAssert.True(cached.Imports.SequenceEqual(uncached.Imports, WasmImportComparer.Instance), $"the imports of the module {i} are the same with the cache");
            FuzzAssert.True(cached.Exports.Select(export => (export.Name, export.Type.Kind)).SequenceEqual(uncached.Exports.Select(export => (export.Name, export.Type.Kind))), $"the exports of the module {i} are the same with the cache");
            modules[i] = ((AdapterModule)cached).Value;
        }

        var cache = CachedWasmRuntime.ModuleCache;
        FuzzAssert.True(cache.Size <= cache.Capacity, $"the size of the cache ({cache.Size}) does not exceed its capacity ({cache.Capacity})");
        for (var i = 0; i < inputs.Length; i++)
        {
            for (var j = i + 1; j < inputs.Length; j++)
            {
                if (modules[i] is not { } first || modules[j] is not { } second)
                    continue;

                if (!inputs[i].AsSpan().SequenceEqual(inputs[j]))
                {
                    FuzzAssert.True(!ReferenceEquals(first, second), $"the modules {i} and {j} have different bytes and are different modules");
                }
                else if (j == i + 1 && inputs[i].Length is >= WasmModuleCache.MinimumModuleSize and <= WasmModuleCacheCapacity)
                {
                    // The module compiled just before is the most recently used module: it is still in the cache
                    FuzzAssert.True(ReferenceEquals(first, second), $"the modules {i} and {j} have the same bytes and are the same compiled module");
                }
            }
        }

        static byte[] Changed(byte[] bytes)
        {
            if (bytes.Length == 0)
                return [0];

            byte[] changed = [.. bytes];
            changed[changed.Length / 2] ^= 0x01;
            return changed;
        }

        static WasmModule? TryCompile(WebAssemblyRuntime runtime, byte[] bytes)
        {
            try
            {
                return runtime.Compile(bytes);
            }
            catch (WasmCompileException)
            {
                return null;
            }
        }
    }

    private sealed class WasmImportComparer : IEqualityComparer<WasmImport>
    {
        public static WasmImportComparer Instance { get; } = new();

        public bool Equals(WasmImport? x, WasmImport? y) => x is not null && y is not null && x.Module == y.Module && x.Name == y.Name && x.Type.Kind == y.Type.Kind;

        public int GetHashCode(WasmImport obj) => HashCode.Combine(obj.Module, obj.Name, obj.Type.Kind);
    }
}
