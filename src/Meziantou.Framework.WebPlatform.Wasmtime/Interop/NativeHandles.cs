using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Wasmtime.Interop;

/// <summary>A <c>wasm_engine_t</c>.</summary>
internal sealed class EngineHandle : SafeHandle
{
    public EngineHandle(nint handle)
        : base(0, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        NativeMethods.wasm_engine_delete(handle);
        return true;
    }
}

/// <summary>A <c>wasmtime_store_t</c>, and the weak handle of its managed store (the data of the store).</summary>
internal sealed class StoreHandle : SafeHandle
{
    private readonly GCHandle _data;

    public StoreHandle(nint handle, GCHandle data)
        : base(0, ownsHandle: true)
    {
        _data = data;
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        NativeMethods.wasmtime_store_delete(handle);
        _data.Free();
        return true;
    }
}

/// <summary>A rooted <c>wasmtime_anyref_t</c> or <c>wasmtime_exnref_t</c> (the handle is a <c>WasmtimeRef*</c>), unrooted when the handle is released.</summary>
internal sealed unsafe class RootedReferenceHandle : SafeHandle
{
    private readonly bool _isException;

    public RootedReferenceHandle(WasmtimeRef reference, bool isException)
        : base(0, ownsHandle: true)
    {
        _isException = isException;
        var pointer = (WasmtimeRef*)NativeMemory.Alloc((nuint)sizeof(WasmtimeRef));
        *pointer = reference;
        SetHandle((nint)pointer);
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        var pointer = (WasmtimeRef*)handle;
        if (_isException)
        {
            NativeMethods.wasmtime_exnref_unroot(pointer);
        }
        else
        {
            NativeMethods.wasmtime_anyref_unroot(pointer);
        }

        NativeMemory.Free(pointer);
        return true;
    }
}

/// <summary>
/// A reference on a <see cref="SafeHandle"/>, held while native calls use its native object (or a value derived from it,
/// such as the context of a store). <c>Dispose</c> can run during these calls (re-entrantly, from a host function called
/// by WebAssembly, or from another thread), and so can the finalizer once the owner is no longer used: the native object
/// is released when the last lease is disposed, not before.
/// </summary>
internal readonly ref struct SafeHandleLease
{
    private readonly SafeHandle? _handle;

    /// <summary>Takes a reference on the handle, whose value is the handle.</summary>
    /// <exception cref="ObjectDisposedException">The handle is closed.</exception>
    public SafeHandleLease(SafeHandle handle)
        : this(handle, handle.DangerousGetHandle())
    {
    }

    /// <summary>Takes a reference on the handle, whose value is derived from the handle.</summary>
    /// <exception cref="ObjectDisposedException">The handle is closed.</exception>
    public SafeHandleLease(SafeHandle handle, nint value)
    {
        var added = false;
        handle.DangerousAddRef(ref added);
        _handle = handle;
        Value = value;
    }

    /// <summary>The native value, valid until the lease is disposed.</summary>
    public nint Value { get; }

    public void Dispose() => _handle?.DangerousRelease();
}
