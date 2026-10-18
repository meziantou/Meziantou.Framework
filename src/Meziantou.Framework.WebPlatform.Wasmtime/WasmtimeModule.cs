using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>A module compiled by Wasmtime (<c>wasmtime_module_t</c>), which can be instantiated in the stores of the engine.</summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A module is shared by the agents: the native module is released when the module is collected")]
internal sealed unsafe class WasmtimeModule : WasmModule
{
    private readonly WasmtimeRuntime _runtime;
    private readonly ModuleHandle _handle;
    private IReadOnlyList<WasmImport>? _imports;
    private IReadOnlyList<WasmExport>? _exports;

    public WasmtimeModule(WasmtimeRuntime runtime, nint module, byte[] bytes)
        : base(bytes)
    {
        _runtime = runtime;
        _handle = new ModuleHandle(module);
    }

    public override WebAssemblyRuntime Runtime => _runtime;

    /// <summary>The native module. The caller keeps the module alive while it uses the handle.</summary>
    public nint Handle => _handle.DangerousGetHandle();

    public override IReadOnlyList<WasmImport> Imports => _imports ??= ReadImports();

    public override IReadOnlyList<WasmExport> Exports => _exports ??= ReadExports();

    private List<WasmImport> ReadImports()
    {
        WasmPointerVec imports;
        NativeMethods.wasmtime_module_imports(Handle, &imports);
        try
        {
            var result = new List<WasmImport>((int)imports.Size);
            var tableIndex = 0;
            for (var i = 0; i < (int)imports.Size; i++)
            {
                var import = imports.Data[i];
                var externType = NativeMethods.wasm_importtype_type(import);
                var tableLimits = NativeMethods.wasm_externtype_kind(externType) == WasmExternTypeKind.Table ? Sections.GetImportedTable(tableIndex++) : null;
                result.Add(new WasmImport(ReadName(NativeMethods.wasm_importtype_module(import)), ReadName(NativeMethods.wasm_importtype_name(import)), WasmtimeTypes.ReadExternType(externType, tableLimits)));
            }

            return result;
        }
        finally
        {
            NativeMethods.wasm_importtype_vec_delete(&imports);
            GC.KeepAlive(this);
        }
    }

    private List<WasmExport> ReadExports()
    {
        WasmPointerVec exports;
        NativeMethods.wasmtime_module_exports(Handle, &exports);
        try
        {
            var result = new List<WasmExport>((int)exports.Size);
            for (var i = 0; i < (int)exports.Size; i++)
            {
                var export = exports.Data[i];
                var name = ReadName(NativeMethods.wasm_exporttype_name(export));
                var externType = NativeMethods.wasm_exporttype_type(export);
                var tableLimits = NativeMethods.wasm_externtype_kind(externType) == WasmExternTypeKind.Table ? Sections.GetExportedTable(name) : null;
                result.Add(new WasmExport(name, WasmtimeTypes.ReadExternType(externType, tableLimits)));
            }

            return result;
        }
        finally
        {
            NativeMethods.wasm_exporttype_vec_delete(&exports);
            GC.KeepAlive(this);
        }
    }

    private static string ReadName(WasmByteVec* name) => Encoding.UTF8.GetString(name->AsSpan());

    private sealed class ModuleHandle : System.Runtime.InteropServices.SafeHandle
    {
        public ModuleHandle(nint handle)
            : base(0, ownsHandle: true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == 0;

        protected override bool ReleaseHandle()
        {
            NativeMethods.wasmtime_module_delete(handle);
            return true;
        }
    }
}
