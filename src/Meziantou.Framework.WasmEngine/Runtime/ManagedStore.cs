using System.Diagnostics;

namespace Meziantou.Framework.WasmEngine;

/// <summary>A store: the instances and the objects created in it, and the interpreter that runs their code.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0055:Do not use finalizer", Justification = "The active stores metric is decremented when a store that is not disposed is collected")]
internal sealed class ManagedStore : WasmStore
{
    private readonly WasmRuntime _runtime;
    private int _disposed;

    // The machine of the OS thread, when it runs the continuations of the asynchronous calls of the components (see
    // Components.Runtime.ContinuationWorker): each continuation needs its own stack of values
    [ThreadStatic]
    private static Machine? s_threadMachine;

    private readonly Machine _machine;
    private Components.Runtime.ComponentStoreState? _components;

    public ManagedStore(WasmRuntime runtime, WasmStoreOptions options)
    {
        _runtime = runtime;
        Options = options;
        _machine = new Machine(this);
        runtime.Telemetry.RecordStoreCreated();
    }

    ~ManagedStore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _runtime.Telemetry.RecordStoreReleased();
        }
    }

    public override WasmRuntime Runtime => _runtime;

    public WasmStoreOptions Options { get; }

    /// <summary>The machine that runs the code on the current OS thread.</summary>
    public Machine Machine => s_threadMachine is { } machine && machine.Store == this ? machine : _machine;

    public TypeRegistry Registry => _runtime.Registry;

    /// <summary>The state of the component model of the store.</summary>
    public Components.Runtime.ComponentStoreState Components => _components ??= new Components.Runtime.ComponentStoreState(this);

    /// <summary>Creates the machine of the current OS thread, a worker of the continuations of the components.</summary>
    public Machine CreateThreadMachine()
    {
        var machine = new Machine(this);
        s_threadMachine = machine;
        return machine;
    }

    public void ReleaseThreadMachine(Machine machine)
    {
        if (Options.CheckInterrupt is not null)
        {
            _runtime.Interruptions.Unregister(machine);
        }

        s_threadMachine = null;
    }

    /// <summary>Calls the interruption action of the store, while the components wait for the host.</summary>
    public void CheckComponentInterrupt() => Options.CheckInterrupt?.Invoke();

    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _runtime.Telemetry.RecordStoreReleased();
            if (Options.CheckInterrupt is not null)
            {
                _runtime.Interruptions.Unregister(_machine);
            }

            _components?.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Converts a value of the host to a slot of a type.</summary>
    /// <exception cref="WasmTypeMismatchException">The value does not match the type.</exception>
    public Slot ToSlot(WasmValue value, ValType type)
    {
        var kind = type.Code switch
        {
            ValueCode.I32 or ValueCode.I8 or ValueCode.I16 => WasmValueKind.I32,
            ValueCode.I64 => WasmValueKind.I64,
            ValueCode.F32 => WasmValueKind.F32,
            ValueCode.F64 => WasmValueKind.F64,
            ValueCode.V128 => WasmValueKind.V128,
            _ => WasmValueKind.Ref,
        };

        if (value.Kind != kind)
            throw new WasmTypeMismatchException("The value " + value + " does not match the type " + type);

        if (kind == WasmValueKind.Ref)
        {
            var reference = value.Reference;
            if (reference is ManagedFunction function && function.Store != this)
                throw new WasmTypeMismatchException("The function belongs to another store");

            if (!RuntimeTypes.Matches(reference, type))
                throw new WasmTypeMismatchException("The value " + value + " does not match the type " + type);

            return new Slot { R = reference };
        }

        return Slot.FromValue(value);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Calls
    // ------------------------------------------------------------------------------------------------------------

    public void Invoke(ManagedFunction function, ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results)
    {
        ThrowIfDisposed();
        if (function.Store != this)
            throw new InvalidOperationException("The function belongs to another store");

        var type = function.DefinedType;
        if (arguments.Length != type.Parameters.Length || results.Length != type.Results.Length)
            throw new WasmTypeMismatchException("The number of arguments or results does not match the type of the function");

        try
        {
            Machine.Invoke(function, arguments, results);
        }
        catch (WasmTrapException ex)
        {
            _runtime.Telemetry.RecordTrap(ex);
            throw;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Instantiation
    // ------------------------------------------------------------------------------------------------------------

    public override WasmInstance Instantiate(WasmModule module, IReadOnlyList<WasmExtern> imports)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(imports);
        ThrowIfDisposed();
        if (module is not ManagedModule managedModule || module.Runtime != _runtime)
            throw new ArgumentException("The module was not compiled by the runtime of the store", nameof(module));

        using var activity = WasmEngineTelemetry.StartInstantiate(module);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var instance = InstantiateCore(managedModule, imports);
            _runtime.Telemetry.RecordInstantiate(Stopwatch.GetElapsedTime(start), exception: null);
            return instance;
        }
        catch (Exception ex)
        {
            if (ex is WasmTrapException trap)
            {
                _runtime.Telemetry.RecordTrap(trap);
            }

            WasmEngineTelemetry.SetError(activity, ex);
            _runtime.Telemetry.RecordInstantiate(Stopwatch.GetElapsedTime(start), ex);
            throw;
        }
    }

    private ManagedInstance InstantiateCore(ManagedModule module, IReadOnlyList<WasmExtern> imports)
    {
        var info = module.Info;
        if (imports.Count != info.Imports.Count)
            throw new WasmLinkException("The module has " + info.Imports.Count + " imports, " + imports.Count + " values were given");

        var instance = new ManagedInstance(module);
        for (var i = 0; i < imports.Count; i++)
        {
            Link(instance, info.Imports[i], imports[i], i);
        }

        // The definitions, in the order of their sections: the constant expressions read the previous definitions
        for (var i = info.ImportedFunctionCount; i < info.Functions.Count; i++)
        {
            instance.Functions[i] = new CodeFunction(this, info.Functions[i], instance, info.Code[i - info.ImportedFunctionCount]);
        }

        for (var i = info.ImportedTagCount; i < info.Tags.Count; i++)
        {
            instance.Tags[i] = new ManagedTag(this, info.Tags[i]);
        }

        for (var i = info.ImportedGlobalCount; i < info.Globals.Count; i++)
        {
            var global = info.Globals[i];
            instance.Globals[i] = new ManagedGlobal(this, global.Type, global.Mutable, Machine.Evaluate(global.Init!, instance));
        }

        for (var i = info.ImportedTableCount; i < info.Tables.Count; i++)
        {
            var table = info.Tables[i];
            var value = table.Init is { } init ? Machine.Evaluate(init, instance).R : null;
            instance.Tables[i] = new ManagedTable(this, table, value);
        }

        for (var i = info.ImportedMemoryCount; i < info.Memories.Count; i++)
        {
            instance.Memories[i] = ManagedMemory.Create(this, info.Memories[i]);
        }

        for (var i = 0; i < info.Elements.Count; i++)
        {
            var segment = info.Elements[i];
            if (segment.Mode == SegmentMode.Declarative)
            {
                instance.Elements[i] = [];
                continue;
            }

            var elements = new object?[segment.Count];
            if (segment.FunctionIndices is { } indices)
            {
                for (var j = 0; j < indices.Length; j++)
                {
                    elements[j] = instance.Functions[indices[j]];
                }
            }
            else
            {
                for (var j = 0; j < segment.Items.Length; j++)
                {
                    elements[j] = Machine.Evaluate(segment.Items[j], instance).R;
                }
            }

            instance.Elements[i] = elements;
        }

        for (var i = 0; i < info.Data.Count; i++)
        {
            instance.Data[i] = info.Data[i].Bytes;
        }

        // The active segments are copied in order: a segment out of bounds traps, the previous copies stay
        for (var i = 0; i < info.Elements.Count; i++)
        {
            var segment = info.Elements[i];
            if (segment.Mode != SegmentMode.Active)
                continue;

            var table = instance.Tables[segment.Table];
            var offset = Machine.Evaluate(segment.Offset!, instance).A;
            var destination = table.Info.Is64 ? (ulong)offset : (uint)offset;
            var elements = instance.Elements[i];
            if (destination > table.Size || (ulong)elements.Length > table.Size - destination)
                throw Traps.OutOfBoundsTable();

            Array.Copy(elements, 0, table.Elements, (long)destination, elements.Length);
            instance.Elements[i] = [];
        }

        for (var i = 0; i < info.Data.Count; i++)
        {
            var segment = info.Data[i];
            if (segment.Mode != SegmentMode.Active)
                continue;

            var memory = instance.Memories[segment.Memory];
            var offset = Machine.Evaluate(segment.Offset!, instance).A;
            var destination = memory.Is64 ? (ulong)offset : (uint)offset;
            unsafe
            {
                var pointer = memory.GetRange(destination, (ulong)segment.Bytes.Length);
                segment.Bytes.CopyTo(new Span<byte>(pointer, segment.Bytes.Length));
            }

            instance.Data[i] = [];
        }

        if (info.Start >= 0)
        {
            Machine.Invoke(instance.Functions[info.Start], [], [], caller: instance);
        }

        return instance;
    }

    private void Link(ManagedInstance instance, ImportInfo import, WasmExtern value, int position)
    {
        var info = instance.Module.Info;
        string Describe() => "The import #" + position + " ('" + import.Module + "'.'" + import.Name + "')";
        switch (import.Kind)
        {
            case WasmExternKind.Function:
                if (value is not ManagedFunction function || function.Store != this)
                    throw new WasmLinkException(Describe() + " must be a function of the store");

                if (!function.DefinedType.IsSubtypeOf(info.Functions[import.Index]))
                    throw new WasmLinkException(Describe() + ": incompatible import type");

                instance.Functions[import.Index] = function;
                break;

            case WasmExternKind.Table:
                if (value is not ManagedTable table || table.Store != this)
                    throw new WasmLinkException(Describe() + " must be a table of the store");

                var expectedTable = info.Tables[import.Index];
                if (table.Info.Is64 != expectedTable.Is64 || !TypeRelations.IsEquivalent(table.ElementType, expectedTable.ElementType) || !MatchLimits(table.Size, table.Info.Maximum, expectedTable.Minimum, expectedTable.Maximum))
                    throw new WasmLinkException(Describe() + ": incompatible import type");

                instance.Tables[import.Index] = table;
                break;

            case WasmExternKind.Memory:
                if (value is not ManagedMemory memory || (memory.Store != this && memory.Store is not null))
                    throw new WasmLinkException(Describe() + " must be a memory of the store");

                var expectedMemory = info.Memories[import.Index];
                if (memory.Info.Is64 != expectedMemory.Is64 || memory.Info.Shared != expectedMemory.Shared || !MatchLimits(memory.Size, memory.Info.Maximum, expectedMemory.Minimum, expectedMemory.Maximum))
                    throw new WasmLinkException(Describe() + ": incompatible import type");

                instance.Memories[import.Index] = memory;
                break;

            case WasmExternKind.Global:
                if (value is not ManagedGlobal global || global.Store != this)
                    throw new WasmLinkException(Describe() + " must be a global of the store");

                var expectedGlobal = info.Globals[import.Index];
                var compatible = global.Mutable == expectedGlobal.Mutable && (global.Mutable ? TypeRelations.IsEquivalent(global.ValueType, expectedGlobal.Type) : TypeRelations.IsSubtype(global.ValueType, expectedGlobal.Type));
                if (!compatible)
                    throw new WasmLinkException(Describe() + ": incompatible import type");

                instance.Globals[import.Index] = global;
                break;

            default:
                if (value is not ManagedTag tag || tag.Store != this)
                    throw new WasmLinkException(Describe() + " must be a tag of the store");

                if (!ReferenceEquals(tag.FunctionType, info.Tags[import.Index]))
                    throw new WasmLinkException(Describe() + ": incompatible import type");

                instance.Tags[import.Index] = tag;
                break;
        }
    }

    private static bool MatchLimits(ulong minimum, ulong? maximum, ulong expectedMinimum, ulong? expectedMaximum)
    {
        if (minimum < expectedMinimum)
            return false;

        if (expectedMaximum is { } expected)
            return maximum is { } actual && actual <= expected;

        return true;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The objects created by the host
    // ------------------------------------------------------------------------------------------------------------

    public DefinedType GetFunctionType(WasmFunctionType type)
    {
        if (type.Native is DefinedType native)
            return native;

        return Registry.GetFunctionType(Array.ConvertAll(type.Parameters, ValType.FromPublic), Array.ConvertAll(type.Results, ValType.FromPublic));
    }

    public override WasmFunction CreateFunction(WasmFunctionType type, WasmHostCallback callback, object? state = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();
        return new HostFunction(this, GetFunctionType(type), callback, callerCallback: null, state);
    }

    private protected override WasmFunction CreateCallerFunction(WasmFunctionType type, WasmHostCallerCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();
        return new HostFunction(this, GetFunctionType(type), callback: null, callback, state);
    }

    public override WasmMemory CreateMemory(WasmMemoryType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ThrowIfDisposed();
        var limit = type.Is64 ? 1UL << 48 : 65536UL;
        if (type.Minimum > limit || type.Maximum > limit || type.Maximum < type.Minimum || (type.Shared && type.Maximum is null))
            throw new WasmAllocationException("The memory type is not valid");

        return ManagedMemory.Create(this, new MemoryInfo { Minimum = type.Minimum, Maximum = type.Maximum, Is64 = type.Is64, Shared = type.Shared });
    }

    public override WasmTable CreateTable(WasmTableType type, WasmValue value)
    {
        ArgumentNullException.ThrowIfNull(type);
        ThrowIfDisposed();
        var elementType = type.Native is ValType native ? native : ValType.FromPublic(type.ElementType);
        if (!elementType.IsRef)
            throw new WasmTypeMismatchException("The elements of a table are references");

        if (type.Maximum < type.Minimum || (!type.Is64 && (type.Minimum > uint.MaxValue || type.Maximum > uint.MaxValue)))
            throw new WasmAllocationException("The table type is not valid");

        var init = ToSlot(value, elementType).R;
        return new ManagedTable(this, new TableInfo { ElementType = elementType, Minimum = type.Minimum, Maximum = type.Maximum, Is64 = type.Is64 }, init);
    }

    public override WasmGlobal CreateGlobal(WasmGlobalType type, WasmValue value)
    {
        ArgumentNullException.ThrowIfNull(type);
        ThrowIfDisposed();
        var valueType = type.Native is ValType native ? native : ValType.FromPublic(type.ValueType);
        return new ManagedGlobal(this, valueType, type.Mutable, ToSlot(value, valueType));
    }

    public override WasmTag CreateTag(WasmTagType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ThrowIfDisposed();
        var functionType = type.Native is DefinedType native ? native : Registry.GetFunctionType(Array.ConvertAll(type.Parameters, ValType.FromPublic), []);
        return new ManagedTag(this, functionType);
    }

    public override WasmExceptionReference CreateException(WasmTag tag, ReadOnlySpan<WasmValue> payload)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ThrowIfDisposed();
        if (tag is not ManagedTag managedTag || managedTag.Store != this)
            throw new WasmTypeMismatchException("The tag belongs to another store");

        var parameters = managedTag.FunctionType.Parameters;
        if (payload.Length != parameters.Length)
            throw new WasmTypeMismatchException("The payload does not match the type of the tag");

        var slots = new Slot[payload.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = ToSlot(payload[i], parameters[i]);
        }

        return new ManagedException(managedTag, slots);
    }

    private static GcArray GetArray(WasmGcObject array) => array as GcArray ?? throw new WasmTrapException("The value is not an array");

    public override int GetArrayLength(WasmGcObject array)
    {
        ThrowIfDisposed();
        return GetArray(array).Length;
    }

    public override WasmValue GetArrayElement(WasmGcObject array, int index)
    {
        ThrowIfDisposed();
        var gcArray = GetArray(array);
        if ((uint)index >= (uint)gcArray.Length)
            throw Traps.OutOfBoundsArray();

        return gcArray.Get(index).ToValue(gcArray.DefinedType.Element.Type);
    }

    public override void SetArrayElement(WasmGcObject array, int index, WasmValue value, WasmValueType type)
    {
        ThrowIfDisposed();
        var gcArray = GetArray(array);
        if ((uint)index >= (uint)gcArray.Length)
            throw Traps.OutOfBoundsArray();

        var element = gcArray.DefinedType.Element;
        if (!element.Mutable)
            throw new WasmTrapException("The array is immutable");

        gcArray.Set(index, ToSlot(value, element.Type.Unpacked));
    }
}
