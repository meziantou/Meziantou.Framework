namespace Meziantou.Framework.WasmEngine;

internal sealed class ManagedTable : WasmTable
{
    private object?[] _elements;
    private ulong _size;

    public ManagedTable(ManagedStore store, TableInfo info, object? initialValue)
    {
        Store = store;
        Info = info;
        if (info.Minimum > (ulong)store.Options.MaxTableElements || info.Minimum > (ulong)Array.MaxLength)
            throw new WasmAllocationException("The table cannot have more than " + store.Options.MaxTableElements + " elements");

        _elements = new object?[(int)info.Minimum];
        if (initialValue is not null)
        {
            Array.Fill(_elements, initialValue);
        }

        _size = info.Minimum;
        Type = new WasmTableType(info.ElementType.ToPublic(), info.Minimum, info.Maximum, info.Is64) { Native = info.ElementType };
    }

    public ManagedStore Store { get; }

    public TableInfo Info { get; }

    public ValType ElementType => Info.ElementType;

    public override WasmTableType Type { get; }

    public override ulong Size => _size;

    public object?[] Elements => _elements;

    public int Length => (int)Size;

    /// <summary>Grows the table and returns its previous size, or -1 when it cannot grow (table.grow).</summary>
    public long TryGrow(ulong delta, object? value)
    {
        var size = Size;
        var maximum = Math.Min(Info.Maximum ?? ulong.MaxValue, (ulong)Math.Min(Store.Options.MaxTableElements, Array.MaxLength));
        if (delta > maximum - Math.Min(size, maximum))
            return -1;

        var newSize = size + delta;
        if (newSize > (ulong)_elements.Length)
        {
            var elements = new object?[(int)Math.Max(newSize, Math.Min((ulong)_elements.Length * 2, maximum))];
            Array.Copy(_elements, elements, (int)size);
            _elements = elements;
        }

        if (value is not null)
        {
            Array.Fill(_elements, value, (int)size, (int)delta);
        }

        _size = newSize;
        return (long)size;
    }

    public override WasmValue Get(ulong index)
    {
        Store.ThrowIfDisposed();
        if (index >= Size)
            throw new WasmAllocationException("The index is out of the bounds of the table");

        return WasmValue.FromReference(_elements[(int)index]);
    }

    public override void Set(ulong index, WasmValue value)
    {
        Store.ThrowIfDisposed();
        if (index >= Size)
            throw new WasmAllocationException("The index is out of the bounds of the table");

        _elements[(int)index] = Store.ToSlot(value, ElementType).R;
    }

    public override ulong Grow(ulong delta, WasmValue value)
    {
        Store.ThrowIfDisposed();
        var reference = Store.ToSlot(value, ElementType).R;
        var previous = TryGrow(delta, reference);
        if (previous < 0)
            throw new WasmAllocationException("The table cannot grow");

        return (ulong)previous;
    }
}

internal sealed class ManagedGlobal : WasmGlobal
{
    public ManagedGlobal(ManagedStore store, ValType valueType, bool mutable, Slot value)
    {
        Store = store;
        ValueType = valueType;
        Mutable = mutable;
        Value = value;
        Type = new WasmGlobalType(valueType.ToPublic(), mutable) { Native = valueType };
    }

    public ManagedStore Store { get; }

    public ValType ValueType { get; }

    public bool Mutable { get; }

    public Slot Value;

    public override WasmGlobalType Type { get; }

    public override WasmValue Get()
    {
        Store.ThrowIfDisposed();
        return Value.ToValue(ValueType);
    }

    public override void Set(WasmValue value)
    {
        Store.ThrowIfDisposed();
        if (!Mutable)
            throw new WasmTypeMismatchException("The global is immutable");

        Value = Store.ToSlot(value, ValueType);
    }
}

internal sealed class ManagedTag(ManagedStore store, DefinedType type) : WasmTag
{
    public ManagedStore Store { get; } = store;

    public DefinedType FunctionType { get; } = type;

    public override WasmTagType Type { get; } = new WasmTagType(Array.ConvertAll(type.Parameters, parameter => parameter.ToPublic())) { Native = type };
}

internal sealed class ManagedException(ManagedTag tag, Slot[] payload) : WasmExceptionReference
{
    public ManagedTag ManagedTag { get; } = tag;

    public Slot[] Payload { get; } = payload;

    public override WasmTag Tag => ManagedTag;

    public override int ArgumentCount => Payload.Length;

    public override WasmValue GetArgument(int index)
    {
        if ((uint)index >= (uint)Payload.Length)
            throw new WasmAllocationException("The index is out of the bounds of the payload");

        return Payload[index].ToValue(ManagedTag.FunctionType.Parameters[index]);
    }
}

internal sealed class GcStruct(DefinedType type, Slot[] fields) : WasmGcObject
{
    public DefinedType DefinedType { get; } = type;

    public Slot[] Fields { get; } = fields;

    public override bool IsArray => false;
}

/// <summary>An array: the elements are stored in an array of their storage type.</summary>
internal sealed class GcArray : WasmGcObject
{
    private readonly byte[]? _bytes;
    private readonly short[]? _shorts;
    private readonly int[]? _ints;
    private readonly long[]? _longs;
    private readonly object?[]? _references;

    public GcArray(DefinedType type, int length)
    {
        DefinedType = type;
        Length = length;
        Code = type.Element.Type.Code;
        switch (Code)
        {
            case ValueCode.I8:
                _bytes = new byte[length];
                break;
            case ValueCode.I16:
                _shorts = new short[length];
                break;
            case ValueCode.I32 or ValueCode.F32:
                _ints = new int[length];
                break;
            case ValueCode.I64 or ValueCode.F64:
                _longs = new long[length];
                break;
            case ValueCode.V128:
                _longs = new long[length * 2L];
                break;
            default:
                _references = new object?[length];
                break;
        }
    }

    public DefinedType DefinedType { get; }

    public ValueCode Code { get; }

    public int Length { get; }

    public override bool IsArray => true;

    /// <summary>Reads an element: a packed element is zero-extended, or sign-extended when <paramref name="signed"/>.</summary>
    public Slot Get(int index, bool signed = false) => Code switch
    {
        ValueCode.I8 => new Slot { A = signed ? (sbyte)_bytes![index] : _bytes![index] },
        ValueCode.I16 => new Slot { A = signed ? _shorts![index] : (ushort)_shorts![index] },
        ValueCode.I32 or ValueCode.F32 => new Slot { A = _ints![index] },
        ValueCode.I64 or ValueCode.F64 => new Slot { A = _longs![index] },
        ValueCode.V128 => new Slot { A = _longs![index * 2], B = _longs[(index * 2) + 1] },
        _ => new Slot { R = _references![index] },
    };

    public void Set(int index, in Slot value)
    {
        switch (Code)
        {
            case ValueCode.I8:
                _bytes![index] = (byte)value.A;
                break;
            case ValueCode.I16:
                _shorts![index] = (short)value.A;
                break;
            case ValueCode.I32 or ValueCode.F32:
                _ints![index] = (int)value.A;
                break;
            case ValueCode.I64 or ValueCode.F64:
                _longs![index] = value.A;
                break;
            case ValueCode.V128:
                _longs![index * 2] = value.A;
                _longs[(index * 2) + 1] = value.B;
                break;
            default:
                _references![index] = value.R;
                break;
        }
    }

    public void Fill(int offset, int count, in Slot value)
    {
        switch (Code)
        {
            case ValueCode.I8:
                _bytes.AsSpan(offset, count).Fill((byte)value.A);
                break;
            case ValueCode.I16:
                _shorts.AsSpan(offset, count).Fill((short)value.A);
                break;
            case ValueCode.I32 or ValueCode.F32:
                _ints.AsSpan(offset, count).Fill((int)value.A);
                break;
            case ValueCode.I64 or ValueCode.F64:
                _longs.AsSpan(offset, count).Fill(value.A);
                break;
            case ValueCode.Ref:
                _references.AsSpan(offset, count).Fill(value.R);
                break;
            default:
                for (var i = 0; i < count; i++)
                {
                    Set(offset + i, value);
                }

                break;
        }
    }

    /// <summary>Copies elements of an array of the same storage type (the ranges may overlap).</summary>
    public void CopyFrom(GcArray source, int sourceOffset, int destinationOffset, int count)
    {
        switch (Code)
        {
            case ValueCode.I8:
                source._bytes.AsSpan(sourceOffset, count).CopyTo(_bytes.AsSpan(destinationOffset));
                break;
            case ValueCode.I16:
                source._shorts.AsSpan(sourceOffset, count).CopyTo(_shorts.AsSpan(destinationOffset));
                break;
            case ValueCode.I32 or ValueCode.F32:
                source._ints.AsSpan(sourceOffset, count).CopyTo(_ints.AsSpan(destinationOffset));
                break;
            case ValueCode.I64 or ValueCode.F64:
                source._longs.AsSpan(sourceOffset, count).CopyTo(_longs.AsSpan(destinationOffset));
                break;
            case ValueCode.V128:
                source._longs.AsSpan(sourceOffset * 2, count * 2).CopyTo(_longs.AsSpan(destinationOffset * 2));
                break;
            default:
                source._references.AsSpan(sourceOffset, count).CopyTo(_references.AsSpan(destinationOffset));
                break;
        }
    }

    /// <summary>Writes elements from the bytes of a data segment (little-endian).</summary>
    public void InitializeFromBytes(int offset, ReadOnlySpan<byte> bytes, int count)
    {
        var size = DefinedType.Element.Type.StorageSize;
        for (var i = 0; i < count; i++)
        {
            var chunk = bytes.Slice(i * size, size);
            var slot = size switch
            {
                1 => new Slot { A = chunk[0] },
                2 => new Slot { A = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(chunk) },
                4 => new Slot { A = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(chunk) },
                8 => new Slot { A = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk) },
                _ => new Slot { A = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk), B = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(chunk[8..]) },
            };
            Set(offset + i, slot);
        }
    }
}

/// <summary>A function of a store: a function of an instance, or a host function.</summary>
internal abstract class ManagedFunction : WasmFunction
{
    /// <summary>
    /// The typed entry of the code of a compiled function (<see cref="CompiledCode.Typed"/>), once the machine called the
    /// function after it was published (see <see cref="Machine"/>), or null: the compiled callers that find the function at
    /// run time (imports, tables, references) call it with <see cref="TypedInstance"/>, without testing the class of the
    /// function.
    /// </summary>
    public Delegate? Typed;

    /// <summary>The instance of a compiled function (the argument of <see cref="Typed"/>), or null.</summary>
    public ManagedInstance? TypedInstance;

    private protected ManagedFunction(ManagedStore store, DefinedType type, object? hostState)
    {
        Store = store;
        DefinedType = type;
        HostState = hostState;
    }

    public ManagedStore Store { get; }

    public DefinedType DefinedType { get; }

    public override WasmFunctionType Type => DefinedType.PublicFunctionType;

    public override void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results) => Store.Invoke(this, arguments, results);
}

internal sealed class CodeFunction : ManagedFunction
{
    public CodeFunction(ManagedStore store, DefinedType type, ManagedInstance instance, CompiledCode code)
        : base(store, type, hostState: null)
    {
        Instance = instance;
        Code = code;
        TypedInstance = instance;
    }

    public ManagedInstance Instance { get; }

    public CompiledCode Code { get; }
}

internal sealed class HostFunction(ManagedStore store, DefinedType type, WasmHostCallback? callback, WasmHostCallerCallback? callerCallback, object? state) : ManagedFunction(store, type, state)
{
    /// <summary>The steps of the function, or null when they receive their caller (<see cref="CallerCallback"/>).</summary>
    public WasmHostCallback? Callback { get; } = callback;

    /// <summary>The steps of the function that receive their caller, or null (<see cref="Callback"/>).</summary>
    public WasmHostCallerCallback? CallerCallback { get; } = callerCallback;

    /// <summary>The kinds of the parameters, or null when the type has a vector or a reference (see <see cref="Machine"/>, which converts the numbers without their types).</summary>
    public WasmValueKind[]? ParameterKinds { get; } = GetNumericKinds(type.Parameters);

    /// <summary>The kinds of the results, or null when the type has a vector or a reference.</summary>
    public WasmValueKind[]? ResultKinds { get; } = GetNumericKinds(type.Results);

    private static WasmValueKind[]? GetNumericKinds(ValType[] types)
    {
        var kinds = new WasmValueKind[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            WasmValueKind? kind = types[i].Code switch
            {
                ValueCode.I32 => WasmValueKind.I32,
                ValueCode.I64 => WasmValueKind.I64,
                ValueCode.F32 => WasmValueKind.F32,
                ValueCode.F64 => WasmValueKind.F64,
                _ => null,
            };

            if (kind is null)
                return null;

            kinds[i] = kind.GetValueOrDefault();
        }

        return kinds;
    }
}

internal sealed class ManagedInstance : WasmInstance
{
    private IReadOnlyList<(string Name, WasmExtern Value)>? _exports;

    public ManagedInstance(ManagedModule module)
    {
        Module = module;
        var info = module.Info;
        Functions = new ManagedFunction[info.Functions.Count];
        Tables = new ManagedTable[info.Tables.Count];
        Memories = new ManagedMemory[info.Memories.Count];
        Globals = new ManagedGlobal[info.Globals.Count];
        Tags = new ManagedTag[info.Tags.Count];
        Elements = new object?[info.Elements.Count][];
        Data = new byte[info.Data.Count][];
    }

    public ManagedModule Module { get; }

    public ManagedFunction[] Functions { get; }

    public ManagedTable[] Tables { get; }

    public ManagedMemory[] Memories { get; }

    public ManagedGlobal[] Globals { get; }

    public ManagedTag[] Tags { get; }

    /// <summary>The references of the element segments (empty once dropped).</summary>
    public object?[][] Elements { get; }

    /// <summary>The bytes of the data segments (empty once dropped).</summary>
    public byte[][] Data { get; }

    /// <summary>The exports of a re-exported import are the imported objects.</summary>
    public override IReadOnlyList<(string Name, WasmExtern Value)> Exports => _exports ??= CreateExports();

    private List<(string Name, WasmExtern Value)> CreateExports()
    {
        var result = new List<(string Name, WasmExtern Value)>(Module.Info.Exports.Count);
        foreach (var export in Module.Info.Exports)
        {
            WasmExtern value = export.Kind switch
            {
                WasmExternKind.Function => Functions[export.Index],
                WasmExternKind.Table => Tables[export.Index],
                WasmExternKind.Memory => Memories[export.Index],
                WasmExternKind.Global => Globals[export.Index],
                _ => Tags[export.Index],
            };
            result.Add((export.Name, value));
        }

        return result;
    }
}

internal sealed class ManagedModule : WasmModule
{
    private IReadOnlyList<WasmImport>? _imports;
    private IReadOnlyList<WasmExport>? _exports;

    public ManagedModule(WasmRuntime runtime, ModuleInfo info, byte[] bytes)
        : base(bytes)
    {
        Runtime = runtime;
        Info = info;
    }

    public override WasmRuntime Runtime { get; }

    public ModuleInfo Info { get; }

    public bool LoadedFromCodeCache { get; init; }

    public override bool IsLoadedFromCodeCache => LoadedFromCodeCache;

    public override byte[] CreateCodeCache() => CodeCache.Serialize(Info, SourceBytes);

    public override IReadOnlyList<WasmImport> Imports => _imports ??= Info.Imports.ConvertAll(import => new WasmImport(import.Module, import.Name, GetExternType(import.Kind, import.Index)));

    public override IReadOnlyList<WasmExport> Exports => _exports ??= Info.Exports.ConvertAll(export => new WasmExport(export.Name, GetExternType(export.Kind, export.Index)));

    private WasmExternType GetExternType(WasmExternKind kind, int index)
    {
        switch (kind)
        {
            case WasmExternKind.Function:
                return Info.Functions[index].PublicFunctionType;
            case WasmExternKind.Table:
                var table = Info.Tables[index];
                return new WasmTableType(table.ElementType.ToPublic(), table.Minimum, table.Maximum, table.Is64) { Native = table.ElementType };
            case WasmExternKind.Memory:
                var memory = Info.Memories[index];
                return new WasmMemoryType(memory.Minimum, memory.Maximum, memory.Is64, memory.Shared);
            case WasmExternKind.Global:
                var global = Info.Globals[index];
                return new WasmGlobalType(global.Type.ToPublic(), global.Mutable) { Native = global.Type };
            default:
                var tag = Info.Tags[index];
                return new WasmTagType(Array.ConvertAll(tag.Parameters, parameter => parameter.ToPublic())) { Native = tag };
        }
    }

    public override bool IsMutableI16ArrayParameter(WasmFunctionType functionType, int parameterIndex)
    {
        if (functionType.Native is not DefinedType type || (uint)parameterIndex >= (uint)type.Parameters.Length)
            return false;

        var parameter = type.Parameters[parameterIndex];
        return parameter is { IsRef: true, Nullable: true } && ReferenceEquals(parameter.Defined, Runtime.Registry.MutableI16Array);
    }
}
