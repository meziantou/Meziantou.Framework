namespace Meziantou.Framework.WasmEngine;

/// <summary>Decodes and validates a module (https://webassembly.github.io/spec/core/binary/modules.html).</summary>
internal sealed class ModuleDecoder
{
    private const int MaxPages32 = 65536;
    private const ulong MaxPages64 = 1UL << 48;
    private const int MaxLocals = 50000;
    private const int MaxFunctionSize = 7654321;

    private readonly byte[] _bytes;
    private readonly TypeRegistry _registry;
    private readonly ModuleInfo _module = new();
    private readonly bool _skipCode;
    private int _functionCount = -1;

    private ModuleDecoder(byte[] bytes, TypeRegistry registry, bool skipCode)
    {
        _bytes = bytes;
        _registry = registry;
        _skipCode = skipCode;
    }

    /// <exception cref="WasmDecodeException">The module is malformed or invalid.</exception>
    public static ModuleInfo Decode(byte[] bytes, TypeRegistry registry) => new ModuleDecoder(bytes, registry, skipCode: false).Decode();

    /// <summary>
    /// Decodes a module without its code section (<see cref="ModuleInfo.Code"/> stays empty): the code comes from a code
    /// cache created for the same binary (<see cref="CodeCache"/>), which was valid.
    /// </summary>
    /// <exception cref="WasmDecodeException">The module is malformed or invalid.</exception>
    public static ModuleInfo DecodeWithoutCode(byte[] bytes, TypeRegistry registry) => new ModuleDecoder(bytes, registry, skipCode: true).Decode();

    private ModuleInfo Decode()
    {
        var reader = new WasmReader(_bytes, 0, _bytes.Length);
        if (_bytes.Length < 4 || _bytes[0] != 0 || _bytes[1] != 0x61 || _bytes[2] != 0x73 || _bytes[3] != 0x6D)
            throw new WasmDecodeException("magic header not detected");

        reader.Position = 4;
        if (_bytes.Length < 8)
            throw new WasmDecodeException("unexpected end");

        if (_bytes[4] != 1 || _bytes[5] != 0 || _bytes[6] != 0 || _bytes[7] != 0)
            throw new WasmDecodeException("unknown binary version");

        reader.Position = 8;
        var lastOrder = 0;
        while (!reader.AtEnd)
        {
            var id = reader.ReadByte();
            var size = reader.ReadU32();
            if (size > (uint)(reader.End - reader.Position))
                throw new WasmDecodeException("section size mismatch: unexpected end");

            var start = reader.Position;
            var end = start + (int)size;
            var section = new WasmReader(_bytes, start, end);
            if (id != 0)
            {
                var order = GetSectionOrder(id);
                if (order <= lastOrder)
                    throw new WasmDecodeException(order == 0 ? "malformed section id" : "unexpected content after last section");

                lastOrder = order;
            }

            switch (id)
            {
                case 0:
                    section.ReadName();
                    break;
                case 1:
                    ReadTypeSection(ref section);
                    break;
                case 2:
                    ReadImportSection(ref section);
                    break;
                case 3:
                    ReadFunctionSection(ref section);
                    break;
                case 4:
                    ReadTableSection(ref section);
                    break;
                case 5:
                    ReadMemorySection(ref section);
                    break;
                case 13:
                    ReadTagSection(ref section);
                    break;
                case 6:
                    ReadGlobalSection(ref section);
                    break;
                case 7:
                    ReadExportSection(ref section);
                    break;
                case 8:
                    ReadStartSection(ref section);
                    break;
                case 9:
                    ReadElementSection(ref section);
                    break;
                case 12:
                    _module.DataCount = (int)Math.Min(section.ReadU32(), int.MaxValue);
                    break;
                case 10:
                    ReadCodeSection(ref section);
                    break;
                case 11:
                    ReadDataSection(ref section);
                    break;
                default:
                    throw new WasmDecodeException("malformed section id");
            }

            if (id != 0 && section.Position != end)
                throw new WasmDecodeException("section size mismatch");

            reader.Position = end;
        }

        if (!_skipCode && _functionCount > 0 && _module.Code.Count != _functionCount)
            throw new WasmDecodeException("function and code section have inconsistent lengths");

        if (_module.DataCount is { } dataCount && dataCount != _module.Data.Count)
            throw new WasmDecodeException("data count and data section have inconsistent lengths");

        return _module;
    }

    private static int GetSectionOrder(byte id) => id switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        5 => 5,
        13 => 6,
        6 => 7,
        7 => 8,
        8 => 9,
        9 => 10,
        12 => 11,
        10 => 12,
        11 => 13,
        _ => 0,
    };

    // ------------------------------------------------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------------------------------------------------

    private void ReadTypeSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            RawSubType[] group;
            if (reader.PeekByte() == 0x4E)
            {
                reader.ReadByte();
                var groupCount = reader.ReadCount();
                group = new RawSubType[groupCount];
                for (var j = 0; j < groupCount; j++)
                {
                    group[j] = ReadSubType(ref reader);
                }
            }
            else
            {
                group = [ReadSubType(ref reader)];
            }

            var groupStart = _module.Types.Count;
            var groupEnd = groupStart + group.Length;
            for (var j = 0; j < group.Length; j++)
            {
                ValidateTypeReferences(group[j], groupStart + j, groupEnd);
            }

            var types = _registry.Canonicalize(group, groupStart, index => _module.Types[index]);
            for (var j = 0; j < group.Length; j++)
            {
                if (types[j].Super is { } super && !TypeRelations.IsValidSubtype(types[j], super))
                    throw new WasmDecodeException("sub type " + (groupStart + j) + " does not match super type");
            }

            _module.Types.AddRange(types);
        }
    }

    private static void ValidateTypeReferences(RawSubType type, int index, int groupEnd)
    {
        if (type.Super >= 0 && type.Super >= index)
            throw new WasmDecodeException("unknown type: super type must be defined before");

        void Check(RawValType value)
        {
            if (value.TypeIndex >= groupEnd)
                throw new WasmDecodeException("unknown type " + value.TypeIndex);
        }

        foreach (var parameter in type.Parameters)
        {
            Check(parameter);
        }

        foreach (var result in type.Results)
        {
            Check(result);
        }

        foreach (var (fieldType, _) in type.Fields)
        {
            Check(fieldType);
        }
    }

    private static RawSubType ReadSubType(ref WasmReader reader)
    {
        var final = true;
        var super = -1;
        var b = reader.PeekByte();
        if (b is 0x50 or 0x4F)
        {
            reader.ReadByte();
            final = b == 0x4F;
            var superCount = reader.ReadCount();
            if (superCount > 1)
                throw new WasmDecodeException("multiple supertypes");

            if (superCount == 1)
            {
                super = (int)Math.Min(reader.ReadU32(), int.MaxValue);
            }
        }

        var kind = reader.ReadByte();
        switch (kind)
        {
            case 0x60:
                var parameters = ReadRawValTypes(ref reader);
                var results = ReadRawValTypes(ref reader);
                return new RawSubType { Final = final, Super = super, Kind = CompositeKind.Func, Parameters = parameters, Results = results };

            case 0x5F:
                var fieldCount = reader.ReadCount();
                var fields = new (RawValType, bool)[fieldCount];
                for (var i = 0; i < fieldCount; i++)
                {
                    fields[i] = ReadFieldType(ref reader);
                }

                return new RawSubType { Final = final, Super = super, Kind = CompositeKind.Struct, Fields = fields };

            case 0x5E:
                return new RawSubType { Final = final, Super = super, Kind = CompositeKind.Array, Fields = [ReadFieldType(ref reader)] };

            default:
                throw new WasmDecodeException("malformed type: integer representation too long or invalid composite type");
        }
    }

    private static (RawValType, bool) ReadFieldType(ref WasmReader reader)
    {
        RawValType type;
        var b = reader.PeekByte();
        if (b == 0x78)
        {
            reader.ReadByte();
            type = RawValType.Of(ValueCode.I8);
        }
        else if (b == 0x77)
        {
            reader.ReadByte();
            type = RawValType.Of(ValueCode.I16);
        }
        else
        {
            type = ReadRawValType(ref reader);
        }

        var mutability = reader.ReadByte();
        if (mutability > 1)
            throw new WasmDecodeException("malformed mutability");

        return (type, mutability == 1);
    }

    private static RawValType[] ReadRawValTypes(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        var result = new RawValType[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ReadRawValType(ref reader);
        }

        return result;
    }

    public static RawValType ReadRawValType(ref WasmReader reader)
    {
        var b = reader.ReadByte();
        switch (b)
        {
            case 0x7F:
                return RawValType.Of(ValueCode.I32);
            case 0x7E:
                return RawValType.Of(ValueCode.I64);
            case 0x7D:
                return RawValType.Of(ValueCode.F32);
            case 0x7C:
                return RawValType.Of(ValueCode.F64);
            case 0x7B:
                return RawValType.Of(ValueCode.V128);
            case 0x63 or 0x64:
                var (heap, index) = ReadHeapType(ref reader);
                return new RawValType(ValueCode.Ref, b == 0x63, heap, index);
            default:
                if (GetAbstractHeapType(b) is { } abstractHeap)
                    return new RawValType(ValueCode.Ref, Nullable: true, abstractHeap, TypeIndex: -1);

                throw new WasmDecodeException("malformed value type");
        }
    }

    public static (WasmHeapKind Heap, int TypeIndex) ReadHeapType(ref WasmReader reader)
    {
        var value = reader.ReadS33();
        if (value >= 0)
            return (default, (int)Math.Min(value, int.MaxValue));

        // A negative value is the abstract heap type of a single byte (-0x10 is 0x70...)
        var code = (byte)(value & 0x7F);
        if (value < -64 || GetAbstractHeapType(code) is not { } heap)
            throw new WasmDecodeException("malformed heap type");

        return (heap, -1);
    }

    public static WasmHeapKind? GetAbstractHeapType(byte code) => code switch
    {
        0x70 => WasmHeapKind.Func,
        0x6F => WasmHeapKind.Extern,
        0x6E => WasmHeapKind.Any,
        0x6D => WasmHeapKind.Eq,
        0x6C => WasmHeapKind.I31,
        0x6B => WasmHeapKind.Struct,
        0x6A => WasmHeapKind.Array,
        0x69 => WasmHeapKind.Exn,
        0x71 => WasmHeapKind.None,
        0x72 => WasmHeapKind.NoExtern,
        0x73 => WasmHeapKind.NoFunc,
        0x74 => WasmHeapKind.NoExn,
        _ => null,
    };

    /// <summary>Resolves a decoded value type with the types of the module.</summary>
    public static ValType Resolve(ModuleInfo module, RawValType type)
    {
        if (type.Code != ValueCode.Ref)
            return new ValType(type.Code);

        if (type.TypeIndex < 0)
            return new ValType(type.Nullable, type.Heap);

        if (type.TypeIndex >= module.Types.Count)
            throw new WasmDecodeException("unknown type " + type.TypeIndex);

        return new ValType(type.Nullable, module.Types[type.TypeIndex]);
    }

    private ValType ReadValType(ref WasmReader reader) => Resolve(_module, ReadRawValType(ref reader));

    private ValType ReadRefType(ref WasmReader reader)
    {
        var type = ReadValType(ref reader);
        if (!type.IsRef)
            throw new WasmDecodeException("malformed reference type");

        return type;
    }

    private DefinedType GetFunctionType(uint index)
    {
        if (index >= (uint)_module.Types.Count)
            throw new WasmDecodeException("unknown type " + index);

        var type = _module.Types[(int)index];
        if (!type.IsFunc)
            throw new WasmDecodeException("type " + index + " is not a function type");

        return type;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Imports and definitions
    // ------------------------------------------------------------------------------------------------------------

    private void ReadImportSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            var moduleName = reader.ReadName();
            var name = reader.ReadName();
            var kind = reader.ReadByte();
            switch (kind)
            {
                case 0x00:
                    _module.Imports.Add(new ImportInfo(moduleName, name, WasmExternKind.Function, _module.Functions.Count));
                    _module.Functions.Add(GetFunctionType(reader.ReadU32()));
                    _module.ImportedFunctionCount++;
                    break;
                case 0x01:
                    _module.Imports.Add(new ImportInfo(moduleName, name, WasmExternKind.Table, _module.Tables.Count));
                    _module.Tables.Add(ReadTableType(ref reader, init: null));
                    _module.ImportedTableCount++;
                    break;
                case 0x02:
                    _module.Imports.Add(new ImportInfo(moduleName, name, WasmExternKind.Memory, _module.Memories.Count));
                    _module.Memories.Add(ReadMemoryType(ref reader));
                    _module.ImportedMemoryCount++;
                    break;
                case 0x03:
                    _module.Imports.Add(new ImportInfo(moduleName, name, WasmExternKind.Global, _module.Globals.Count));
                    var (type, mutable) = ReadGlobalType(ref reader);
                    _module.Globals.Add(new GlobalInfo { Type = type, Mutable = mutable });
                    _module.ImportedGlobalCount++;
                    break;
                case 0x04:
                    _module.Imports.Add(new ImportInfo(moduleName, name, WasmExternKind.Tag, _module.Tags.Count));
                    _module.Tags.Add(ReadTagType(ref reader));
                    _module.ImportedTagCount++;
                    break;
                default:
                    throw new WasmDecodeException("malformed import kind");
            }
        }
    }

    private DefinedType ReadTagType(ref WasmReader reader)
    {
        if (reader.ReadByte() != 0)
            throw new WasmDecodeException("malformed tag attribute");

        var type = GetFunctionType(reader.ReadU32());
        if (type.Results.Length != 0)
            throw new WasmDecodeException("non-empty tag result type");

        return type;
    }

    private (ValType Type, bool Mutable) ReadGlobalType(ref WasmReader reader)
    {
        var type = ReadValType(ref reader);
        var mutability = reader.ReadByte();
        if (mutability > 1)
            throw new WasmDecodeException("malformed mutability");

        return (type, mutability == 1);
    }

    private TableInfo ReadTableType(ref WasmReader reader, CompiledCode? init)
    {
        var elementType = ReadRefType(ref reader);
        var (minimum, maximum, is64, shared) = ReadLimits(ref reader, allowShared: false);
        var limit = is64 ? ulong.MaxValue : uint.MaxValue;
        if (minimum > limit || maximum > limit)
            throw new WasmDecodeException("table size must be at most 2^32-1");

        if (maximum < minimum)
            throw new WasmDecodeException("size minimum must not be greater than maximum");

        _ = shared;
        return new TableInfo { ElementType = elementType, Minimum = minimum, Maximum = maximum, Is64 = is64, Init = init };
    }

    private static MemoryInfo ReadMemoryType(ref WasmReader reader)
    {
        var (minimum, maximum, is64, shared) = ReadLimits(ref reader, allowShared: true);
        var limit = is64 ? MaxPages64 : MaxPages32;
        if (minimum > limit || maximum > limit)
            throw new WasmDecodeException(is64 ? "memory size must be at most 2^48 pages" : "memory size must be at most 65536 pages (4GiB)");

        if (maximum < minimum)
            throw new WasmDecodeException("size minimum must not be greater than maximum");

        if (shared && maximum is null)
            throw new WasmDecodeException("shared memory must have maximum");

        return new MemoryInfo { Minimum = minimum, Maximum = maximum, Is64 = is64, Shared = shared };
    }

    private static (ulong Minimum, ulong? Maximum, bool Is64, bool Shared) ReadLimits(ref WasmReader reader, bool allowShared)
    {
        var flags = reader.ReadByte();
        if ((flags & ~0x07) != 0 || (!allowShared && (flags & 0x02) != 0))
            throw new WasmDecodeException("integer too large: malformed limits flags");

        var is64 = (flags & 0x04) != 0;
        var minimum = is64 ? reader.ReadU64() : reader.ReadU32();
        ulong? maximum = (flags & 0x01) != 0 ? (is64 ? reader.ReadU64() : reader.ReadU32()) : null;
        return (minimum, maximum, is64, (flags & 0x02) != 0);
    }

    private void ReadFunctionSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        _functionCount = count;
        for (var i = 0; i < count; i++)
        {
            _module.Functions.Add(GetFunctionType(reader.ReadU32()));
        }
    }

    private void ReadTableSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            if (reader.PeekByte() == 0x40)
            {
                reader.ReadByte();
                if (reader.ReadByte() != 0)
                    throw new WasmDecodeException("malformed table");

                var start = reader.Position;
                var table = ReadTableType(ref reader, init: null);
                var init = FunctionCompiler.CompileConstant(_module, ref reader, table.ElementType, _module.Globals.Count);
                _ = start;
                _module.Tables.Add(new TableInfo { ElementType = table.ElementType, Minimum = table.Minimum, Maximum = table.Maximum, Is64 = table.Is64, Init = init });
            }
            else
            {
                var table = ReadTableType(ref reader, init: null);
                if (!table.ElementType.Nullable)
                    throw new WasmDecodeException("type mismatch: a table of a non-nullable type requires an initializer");

                _module.Tables.Add(table);
            }
        }
    }

    private void ReadMemorySection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            _module.Memories.Add(ReadMemoryType(ref reader));
        }
    }

    private void ReadTagSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            _module.Tags.Add(ReadTagType(ref reader));
        }
    }

    private void ReadGlobalSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            var (type, mutable) = ReadGlobalType(ref reader);
            var init = FunctionCompiler.CompileConstant(_module, ref reader, type, _module.Globals.Count);
            _module.Globals.Add(new GlobalInfo { Type = type, Mutable = mutable, Init = init });
        }
    }

    private void ReadExportSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadName();
            var kind = reader.ReadByte();
            var index = reader.ReadU32();
            var (externKind, limit) = kind switch
            {
                0x00 => (WasmExternKind.Function, _module.Functions.Count),
                0x01 => (WasmExternKind.Table, _module.Tables.Count),
                0x02 => (WasmExternKind.Memory, _module.Memories.Count),
                0x03 => (WasmExternKind.Global, _module.Globals.Count),
                0x04 => (WasmExternKind.Tag, _module.Tags.Count),
                _ => throw new WasmDecodeException("malformed export kind"),
            };

            if (index >= (uint)limit)
                throw new WasmDecodeException("unknown " + externKind.ToString().ToLowerInvariant() + " " + index);

            if (!names.Add(name))
                throw new WasmDecodeException("duplicate export name");

            if (externKind == WasmExternKind.Function)
            {
                _module.DeclaredFunctionReferences.Add((int)index);
            }

            _module.Exports.Add(new ExportInfo(name, externKind, (int)index));
        }
    }

    private void ReadStartSection(ref WasmReader reader)
    {
        var index = reader.ReadU32();
        if (index >= (uint)_module.Functions.Count)
            throw new WasmDecodeException("unknown function " + index);

        var type = _module.Functions[(int)index];
        if (type.Parameters.Length != 0 || type.Results.Length != 0)
            throw new WasmDecodeException("start function must not have parameters or results");

        _module.Start = (int)index;
    }

    private void ReadElementSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            var flags = reader.ReadU32();
            if (flags > 7)
                throw new WasmDecodeException("malformed elements segment kind");

            var mode = (flags & 0x01) == 0 ? SegmentMode.Active : (flags & 0x02) == 0 ? SegmentMode.Passive : SegmentMode.Declarative;
            var table = 0;
            if ((flags & 0x03) == 0x02)
            {
                table = (int)Math.Min(reader.ReadU32(), int.MaxValue);
            }

            CompiledCode? offset = null;
            if (mode == SegmentMode.Active)
            {
                if (table >= _module.Tables.Count)
                    throw new WasmDecodeException("unknown table " + table);

                offset = FunctionCompiler.CompileConstant(_module, ref reader, _module.Tables[table].AddressType, _module.Globals.Count);
            }

            var usesExpressions = (flags & 0x04) != 0;
            ValType type;
            if ((flags & 0x03) == 0)
            {
                type = usesExpressions ? ValType.FuncRef : new ValType(nullable: false, WasmHeapKind.Func);
            }
            else if (usesExpressions)
            {
                type = ReadRefType(ref reader);
            }
            else
            {
                if (reader.ReadByte() != 0)
                    throw new WasmDecodeException("malformed element kind");

                type = new ValType(nullable: false, WasmHeapKind.Func);
            }

            var itemCount = reader.ReadCount();
            ElementSegment segment;
            if (usesExpressions)
            {
                var items = new CompiledCode[itemCount];
                for (var j = 0; j < itemCount; j++)
                {
                    items[j] = FunctionCompiler.CompileConstant(_module, ref reader, type, _module.Globals.Count);
                }

                segment = new ElementSegment { Mode = mode, Table = table, Offset = offset, Type = type, Items = items };
            }
            else
            {
                var indices = new int[itemCount];
                for (var j = 0; j < itemCount; j++)
                {
                    var index = reader.ReadU32();
                    if (index >= (uint)_module.Functions.Count)
                        throw new WasmDecodeException("unknown function " + index);

                    indices[j] = (int)index;
                    _module.DeclaredFunctionReferences.Add((int)index);
                }

                segment = new ElementSegment { Mode = mode, Table = table, Offset = offset, Type = type, FunctionIndices = indices };
            }

            if (mode == SegmentMode.Active && !TypeRelations.IsSubtype(type, _module.Tables[table].ElementType))
                throw new WasmDecodeException("type mismatch: element segment does not match the table");

            _module.Elements.Add(segment);
        }
    }

    private void ReadCodeSection(ref WasmReader reader)
    {
        if (_skipCode)
        {
            reader.Position = reader.End;
            return;
        }

        var count = reader.ReadCount();
        if (count != Math.Max(_functionCount, 0))
            throw new WasmDecodeException("function and code section have inconsistent lengths");

        for (var i = 0; i < count; i++)
        {
            var size = reader.ReadU32();
            if (size > (uint)(reader.End - reader.Position))
                throw new WasmDecodeException("unexpected end of section or function");

            if (size > MaxFunctionSize)
                throw new WasmDecodeException("function body too large");

            var end = reader.Position + (int)size;
            var body = new WasmReader(_bytes, reader.Position, end);
            var functionIndex = _module.ImportedFunctionCount + i;
            var type = _module.Functions[functionIndex];
            var locals = new List<ValType>(type.Parameters);
            var groupCount = body.ReadCount();
            long total = 0;
            for (var j = 0; j < groupCount; j++)
            {
                var localCount = body.ReadU32();
                total += localCount;
                if (total > MaxLocals)
                    throw new WasmDecodeException("too many locals");

                var localType = ReadValType(ref body);
                for (var k = 0; k < localCount; k++)
                {
                    locals.Add(localType);
                }
            }

            var code = FunctionCompiler.CompileFunction(_module, ref body, type, [.. locals]);
            code.Name = "func" + functionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (body.Position != end)
                throw new WasmDecodeException("section size mismatch");

            _module.Code.Add(code);
            reader.Position = end;
        }
    }

    private void ReadDataSection(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            var flags = reader.ReadU32();
            DataSegment segment;
            switch (flags)
            {
                case 0:
                case 2:
                    var memory = flags == 2 ? (int)Math.Min(reader.ReadU32(), int.MaxValue) : 0;
                    if (memory >= _module.Memories.Count)
                        throw new WasmDecodeException("unknown memory " + memory);

                    var offset = FunctionCompiler.CompileConstant(_module, ref reader, _module.Memories[memory].AddressType, _module.Globals.Count);
                    segment = new DataSegment { Mode = SegmentMode.Active, Memory = memory, Offset = offset, Bytes = ReadData(ref reader) };
                    break;
                case 1:
                    segment = new DataSegment { Mode = SegmentMode.Passive, Bytes = ReadData(ref reader) };
                    break;
                default:
                    throw new WasmDecodeException("malformed data segment kind");
            }

            _module.Data.Add(segment);
        }
    }

    private static byte[] ReadData(ref WasmReader reader)
    {
        var length = reader.ReadU32();
        if (length > (uint)(reader.End - reader.Position))
            throw new WasmDecodeException("unexpected end of section or function");

        return reader.ReadBytes((int)length).ToArray();
    }
}
