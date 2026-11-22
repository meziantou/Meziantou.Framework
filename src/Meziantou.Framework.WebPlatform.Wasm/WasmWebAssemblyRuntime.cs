using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.WebAssembly;
using Engine = Meziantou.Framework.WasmEngine;

namespace Meziantou.Framework.WebPlatform.Wasm;

/// <summary>
/// A <see cref="WebAssemblyRuntime"/> based on Meziantou.Framework.WasmEngine (a WebAssembly engine written in .NET): set it in
/// <see cref="Dom.BrowsingContextOptions.WebAssemblyRuntime"/> to define the <c>WebAssembly</c> namespace in the realms of
/// the scripts.
/// </summary>
/// <remarks>
/// <para>
/// The WebAssembly 3.0 features are enabled (the reference types, the GC types, the exceptions, the tail calls, memory64,
/// the multiple memories, the SIMD and relaxed SIMD instructions), and the threads (the shared memories and the atomic
/// instructions).
/// </para>
/// <para>
/// The bytes of a shared memory are a shared data block of the script engine, allocated for the maximum size of the memory:
/// the agents (the windows and the workers) share the memory, and <c>Atomics.wait</c> and <c>Atomics.notify</c> use the
/// waiters of the engine, so that they notify the <c>memory.atomic.wait32</c> waiters and are notified by
/// <c>memory.atomic.notify</c>.
/// </para>
/// <para>
/// The memories are .NET arrays, which the <c>ArrayBuffer</c> objects of JavaScript share. The WebAssembly code is
/// interrupted when the script engine interrupts the scripts (e.g. its timeout). The runtime emits the activities and the
/// metrics of Meziantou.Framework.WasmEngine.
/// </para>
/// </remarks>
public sealed class WasmWebAssemblyRuntime : WebAssemblyRuntime, IDisposable
{
    private readonly bool _ownsRuntime;

    /// <summary>Creates a runtime, and the <see cref="Engine.WasmRuntime"/> it uses.</summary>
    /// <param name="options">The options of the <see cref="Engine.WasmRuntime"/>, or null for the default options.</param>
    public WasmWebAssemblyRuntime(Engine.WasmRuntimeOptions? options = null)
        : this(new Engine.WasmRuntime(options), ownsRuntime: true)
    {
    }

    /// <summary>Creates a runtime that uses a <see cref="Engine.WasmRuntime"/>, which it does not dispose.</summary>
    /// <param name="runtime">The runtime of the engine.</param>
    public WasmWebAssemblyRuntime(Engine.WasmRuntime runtime)
        : this(runtime ?? throw new ArgumentNullException(nameof(runtime)), ownsRuntime: false)
    {
    }

    private WasmWebAssemblyRuntime(Engine.WasmRuntime runtime, bool ownsRuntime)
    {
        Runtime = runtime;
        _ownsRuntime = ownsRuntime;
    }

    /// <summary>Gets the runtime of the engine that compiles and runs the modules.</summary>
    public Engine.WasmRuntime Runtime { get; }

    /// <summary>The wrappers of the objects of the engine (the same wrapper for the same object, as the JavaScript objects of the addresses must be the same).</summary>
    internal ConditionalWeakTable<object, object> Wrappers { get; } = [];

    internal override bool Validate(ReadOnlySpan<byte> bytes) => Runtime.Validate(bytes);

    internal override WasmModule Compile(byte[] bytes)
    {
        try
        {
            return new AdapterModule(this, Runtime.Compile(bytes), bytes);
        }
        catch (Engine.WasmException ex)
        {
            throw ConvertException(ex);
        }
    }

    internal override WasmStore CreateStore(IWasmHost host) => new AdapterStore(this, host);

    /// <summary>Converts an exception of the engine to the exception of the runtime abstraction (the exception of the engine is its inner exception).</summary>
    internal Exception ConvertException(Engine.WasmException exception) => exception switch
    {
        Engine.WasmCompileException => new WasmCompileException(exception.Message, exception),
        Engine.WasmLinkException => new WasmLinkException(exception.Message, exception),
        Engine.WasmTrapException trap => new WasmTrapException(exception.Message, trap.TrapKind is Engine.WasmTrapKind.StackOverflow ? WasmTrapKind.StackOverflow : WasmTrapKind.Other, exception),
        Engine.WasmThrowException thrown => new WasmThrowException(GetException(thrown.Exception)),
        Engine.WasmAllocationException => new WasmAllocationException(exception.Message, exception),
        Engine.WasmTypeMismatchException => new WasmTypeMismatchException(exception.Message, exception),
        _ => exception,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Wrappers
    // ----------------------------------------------------------------------------------------------------------------

    private TWrapper GetWrapper<TEngine, TWrapper>(TEngine value, Func<WasmWebAssemblyRuntime, TEngine, TWrapper> create)
        where TEngine : class
        where TWrapper : class
    {
        if (Wrappers.TryGetValue(value, out var existing))
            return (TWrapper)existing;

        var wrapper = create(this, value);
        return (TWrapper)Wrappers.GetValue(value, _ => wrapper);
    }

    internal WasmExtern GetExtern(Engine.WasmExtern value) => value switch
    {
        Engine.WasmFunction function => GetFunction(function),
        Engine.WasmMemory memory => GetMemory(memory),
        Engine.WasmTable table => GetTable(table),
        Engine.WasmGlobal global => GetGlobal(global),
        Engine.WasmTag tag => GetTag(tag),
        _ => throw new NotSupportedException("The kind of the extern is not supported"),
    };

    internal AdapterFunction GetFunction(Engine.WasmFunction value) => GetWrapper(value, (runtime, value) => new AdapterFunction(runtime, value) { HostFunction = value.HostState });

    internal AdapterMemory GetMemory(Engine.WasmMemory value) => GetWrapper(value, (runtime, value) => new AdapterMemory(runtime, value));

    internal AdapterTable GetTable(Engine.WasmTable value) => GetWrapper(value, (runtime, value) => new AdapterTable(runtime, value));

    internal AdapterGlobal GetGlobal(Engine.WasmGlobal value) => GetWrapper(value, (runtime, value) => new AdapterGlobal(runtime, value));

    internal AdapterTag GetTag(Engine.WasmTag value) => GetWrapper(value, (runtime, value) => new AdapterTag(runtime, value));

    internal AdapterGcObject GetGcObject(Engine.WasmGcObject value) => GetWrapper(value, (runtime, value) => new AdapterGcObject(runtime, value));

    internal AdapterExceptionReference GetException(Engine.WasmExceptionReference value) => GetWrapper(value, (runtime, value) => new AdapterExceptionReference(runtime, value));

    /// <summary>Returns the extern of the engine of a wrapper, or null when the extern is not a wrapper of this runtime.</summary>
    internal Engine.WasmExtern? ToEngine(WasmExtern value) => value switch
    {
        AdapterFunction function when function.Runtime == this => function.Value,
        AdapterMemory memory when memory.Runtime == this => memory.Value,
        AdapterTable table when table.Runtime == this => table.Value,
        AdapterGlobal global when global.Runtime == this => global.Value,
        AdapterTag tag when tag.Runtime == this => tag.Value,
        _ => null,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Values
    // ----------------------------------------------------------------------------------------------------------------

    internal Engine.WasmValue ToEngine(WasmValue value) => value.Kind switch
    {
        WasmValueKind.I32 => Engine.WasmValue.FromI32(value.I32),
        WasmValueKind.I64 => Engine.WasmValue.FromI64(value.I64),
        WasmValueKind.F32 => Engine.WasmValue.FromF32Bits(value.F32Bits),
        WasmValueKind.F64 => Engine.WasmValue.FromF64Bits(value.F64Bits),
        WasmValueKind.V128 => Engine.WasmValue.FromV128(value.V128Low, value.V128High),
        _ => Engine.WasmValue.FromReference(value.Reference switch
        {
            null => null,
            AdapterFunction function => function.Value,
            WasmHostReference host => new Engine.WasmHostReference(host),
            WasmI31Reference i31 => new Engine.WasmI31Reference(i31.Value),
            AdapterGcObject gcObject => gcObject.Value,
            AdapterExceptionReference exception => exception.Value,
            _ => throw new WasmTypeMismatchException("The reference was not created by the runtime"),
        }),
    };

    internal WasmValue FromEngine(Engine.WasmValue value) => value.Kind switch
    {
        Engine.WasmValueKind.I32 => WasmValue.FromI32(value.I32),
        Engine.WasmValueKind.I64 => WasmValue.FromI64(value.I64),
        Engine.WasmValueKind.F32 => WasmValue.FromF32Bits(value.F32Bits),
        Engine.WasmValueKind.F64 => WasmValue.FromF64Bits(value.F64Bits),
        Engine.WasmValueKind.V128 => WasmValue.FromV128(value.V128Low, value.V128High),
        _ => WasmValue.FromReference(value.Reference switch
        {
            null => null,
            Engine.WasmFunction function => GetFunction(function),

            // The host references created by ToEngine keep the host reference of the runtime abstraction, so that its identity is preserved
            Engine.WasmHostReference host => host.Value as WasmHostReference ?? new WasmHostReference(host.Value),
            Engine.WasmI31Reference i31 => new WasmI31Reference(i31.Value),
            Engine.WasmGcObject gcObject => GetGcObject(gcObject),
            Engine.WasmExceptionReference exception => GetException(exception),
            _ => throw new NotSupportedException("The reference is not supported"),
        }),
    };

    internal Engine.WasmValue[] ToEngine(ReadOnlySpan<WasmValue> values)
    {
        if (values.IsEmpty)
            return [];

        var result = new Engine.WasmValue[values.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ToEngine(values[i]);
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Types (the enums of the engine and of the runtime abstraction have the same values)
    // ----------------------------------------------------------------------------------------------------------------

    internal static WasmValueType FromEngine(Engine.WasmValueType type) => new((WasmValueKind)type.Kind, (WasmHeapKind)type.Heap, type.Nullable);

    internal static Engine.WasmValueType ToEngine(WasmValueType type) => new((Engine.WasmValueKind)type.Kind, (Engine.WasmHeapKind)type.Heap, type.Nullable);

    private static WasmValueType[] FromEngine(Engine.WasmValueType[] types) => Array.ConvertAll(types, FromEngine);

    private static Engine.WasmValueType[] ToEngine(WasmValueType[] types) => Array.ConvertAll(types, ToEngine);

    internal static WasmExternType FromEngine(Engine.WasmExternType type) => type switch
    {
        Engine.WasmFunctionType function => FromEngine(function),
        Engine.WasmTableType table => FromEngine(table),
        Engine.WasmMemoryType memory => FromEngine(memory),
        Engine.WasmGlobalType global => FromEngine(global),
        Engine.WasmTagType tag => FromEngine(tag),
        _ => throw new NotSupportedException("The kind of the extern type is not supported"),
    };

    internal static WasmFunctionType FromEngine(Engine.WasmFunctionType type) => new(FromEngine(type.Parameters), FromEngine(type.Results)) { Native = type };

    internal static Engine.WasmFunctionType ToEngine(WasmFunctionType type) => type.Native as Engine.WasmFunctionType ?? new Engine.WasmFunctionType(ToEngine(type.Parameters), ToEngine(type.Results));

    internal static WasmTableType FromEngine(Engine.WasmTableType type) => new(FromEngine(type.ElementType), type.Minimum, type.Maximum, type.Is64) { Native = type };

    internal static Engine.WasmTableType ToEngine(WasmTableType type) => type.Native as Engine.WasmTableType ?? new Engine.WasmTableType(ToEngine(type.ElementType), type.Minimum, type.Maximum, type.Is64);

    internal static WasmMemoryType FromEngine(Engine.WasmMemoryType type) => new(type.Minimum, type.Maximum, type.Is64, type.Shared);

    internal static Engine.WasmMemoryType ToEngine(WasmMemoryType type) => new(type.Minimum, type.Maximum, type.Is64, type.Shared);

    internal static WasmGlobalType FromEngine(Engine.WasmGlobalType type) => new(FromEngine(type.ValueType), type.Mutable) { Native = type };

    internal static Engine.WasmGlobalType ToEngine(WasmGlobalType type) => type.Native as Engine.WasmGlobalType ?? new Engine.WasmGlobalType(ToEngine(type.ValueType), type.Mutable);

    internal static WasmTagType FromEngine(Engine.WasmTagType type) => new(FromEngine(type.Parameters)) { Native = type };

    internal static Engine.WasmTagType ToEngine(WasmTagType type) => type.Native as Engine.WasmTagType ?? new Engine.WasmTagType(ToEngine(type.Parameters));

    /// <summary>Releases the <see cref="Engine.WasmRuntime"/> when the runtime created it. The stores and the modules must not be used afterwards.</summary>
    public void Dispose()
    {
        if (_ownsRuntime)
        {
            Runtime.Dispose();
        }
    }
}
