namespace Meziantou.Framework.WasmEngine.Components.Binary;

/// <summary>Decodes the binary format of a component (https://github.com/WebAssembly/component-model/blob/main/design/mvp/Binary.md).</summary>
internal static class ComponentDecoder
{
    private const int MaxNesting = 100;

    /// <summary>Whether the bytes start with the preamble of a component (and not of a core module).</summary>
    public static bool IsComponent(ReadOnlySpan<byte> bytes) => bytes is [0x00, 0x61, 0x73, 0x6D, _, _, 0x01, 0x00, ..];

    public static ComponentAst Decode(byte[] bytes)
    {
        var reader = new WasmReader(bytes, 0, bytes.Length);
        var component = DecodeComponent(ref reader, depth: 0);
        if (!reader.AtEnd)
            throw new WasmDecodeException("unexpected content after last section");

        return component;
    }

    private static ComponentAst DecodeComponent(ref WasmReader reader, int depth)
    {
        if (depth > MaxNesting)
            throw new WasmDecodeException("nesting too deep");

        var magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual("\0asm"u8))
            throw new WasmDecodeException("magic header not detected");

        var version = reader.ReadBytes(4);
        if (version is not [0x0d, 0x00, 0x01, 0x00])
            throw new WasmDecodeException(version is [_, _, 0x01, 0x00] ? "unknown binary version" : "not a component");

        var component = new ComponentAst();
        while (!reader.AtEnd)
        {
            var id = reader.ReadByte();
            var size = (int)reader.ReadU32();
            if (size > reader.End - reader.Position)
                throw new WasmDecodeException("section size mismatch");

            var end = reader.Position + size;
            var section = new WasmReader(reader.Bytes, reader.Position, end);
            DecodeSection(ref section, id, component, depth);
            if (section.Position != end)
                throw new WasmDecodeException("section size mismatch");

            reader.Position = end;
        }

        return component;
    }

    private static void DecodeSection(ref WasmReader reader, byte id, ComponentAst component, int depth)
    {
        var definitions = component.Definitions;
        switch (id)
        {
            case 0: // custom
                _ = reader.ReadName();
                reader.Position = reader.End;
                break;

            case 1: // core module
                definitions.Add(new CoreModuleDefinition(reader.Bytes.AsMemory(reader.Position, reader.End - reader.Position)));
                reader.Position = reader.End;
                break;

            case 2:
                ReadVector(ref reader, definitions, ReadCoreInstance);
                break;

            case 3:
                ReadVector(ref reader, definitions, (ref WasmReader r) => new CoreTypeDefinition(ReadCoreType(ref r, inModuleType: false)));
                break;

            case 4:
                var nested = DecodeComponent(ref reader, depth + 1);
                definitions.Add(new NestedComponentDefinition(nested));
                break;

            case 5:
                ReadVector(ref reader, definitions, ReadInstance);
                break;

            case 6:
                ReadVector(ref reader, definitions, (ref WasmReader r) => ReadAlias(ref r));
                break;

            case 7:
                ReadVector(ref reader, definitions, (ref WasmReader r) => new TypeDefinition(ReadDefType(ref r, 0)));
                break;

            case 8:
                ReadVector(ref reader, definitions, ReadCanon);
                break;

            case 9:
                var function = (int)reader.ReadU32();
                var arguments = new int[reader.ReadCount()];
                for (var i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = (int)reader.ReadU32();
                }

                definitions.Add(new StartDefinition(function, arguments, (int)reader.ReadU32()));
                break;

            case 10:
                ReadVector(ref reader, definitions, (ref WasmReader r) =>
                {
                    var (name, attributes) = ReadNameAttributes(ref r);
                    return new ImportDefinition(name, ReadExternType(ref r), attributes);
                });
                break;

            case 11:
                ReadVector(ref reader, definitions, ReadExport);
                break;

            case 12:
                throw new WasmDecodeException("value definitions are not supported");

            default:
                throw new WasmDecodeException("malformed section id");
        }
    }

    private delegate ComponentDefinition DefinitionReader(ref WasmReader reader);

    private static void ReadVector(ref WasmReader reader, List<ComponentDefinition> definitions, DefinitionReader read)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            definitions.Add(read(ref reader));
        }
    }

    private static T[] ReadVector<T>(ref WasmReader reader, ItemReader<T> read)
    {
        var count = reader.ReadCount();
        var items = new T[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = read(ref reader);
        }

        return items;
    }

    private delegate T ItemReader<T>(ref WasmReader reader);

    // ------------------------------------------------------------------------------------------------------------
    // Instances, aliases, exports
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentDefinition ReadCoreInstance(ref WasmReader reader)
    {
        switch (reader.ReadByte())
        {
            case 0x00:
                var module = (int)reader.ReadU32();
                var arguments = ReadVector(ref reader, (ref WasmReader r) =>
                {
                    var name = r.ReadName();
                    if (r.ReadByte() != 0x12)
                        throw new WasmDecodeException("malformed instantiate argument");

                    return (name, (int)r.ReadU32());
                });

                return new CoreInstanceDefinition(module, arguments, null);

            case 0x01:
                var exports = ReadVector(ref reader, (ref WasmReader r) =>
                {
                    var name = r.ReadName();
                    var sort = ReadCoreSort(ref r);
                    return (name, sort, (int)r.ReadU32());
                });

                return new CoreInstanceDefinition(-1, null, exports);

            default:
                throw new WasmDecodeException("malformed core instance");
        }
    }

    private static ComponentDefinition ReadInstance(ref WasmReader reader)
    {
        switch (reader.ReadByte())
        {
            case 0x00:
                var component = (int)reader.ReadU32();
                var arguments = ReadVector(ref reader, (ref WasmReader r) =>
                {
                    var name = r.ReadName();
                    var sort = ReadSort(ref r);
                    return (name, sort, (int)r.ReadU32());
                });

                return new InstanceDefinition(component, arguments, null);

            case 0x01:
                var exports = ReadVector(ref reader, (ref WasmReader r) =>
                {
                    var (name, attributes) = ReadNameAttributes(ref r);
                    var sort = ReadSort(ref r);
                    return (name, sort, (int)r.ReadU32(), attributes);
                });

                return new InstanceDefinition(-1, null, [.. exports.Select(export => (export.name, export.sort, export.Item3))], [.. exports.Select(export => export.attributes)]);

            default:
                throw new WasmDecodeException("malformed instance");
        }
    }

    private static ComponentSort ReadCoreSort(ref WasmReader reader) => reader.ReadByte() switch
    {
        0x00 => ComponentSort.CoreFunc,
        0x01 => ComponentSort.CoreTable,
        0x02 => ComponentSort.CoreMemory,
        0x03 => ComponentSort.CoreGlobal,
        0x04 => ComponentSort.CoreTag,
        0x10 => ComponentSort.CoreType,
        0x11 => ComponentSort.CoreModule,
        0x12 => ComponentSort.CoreInstance,
        _ => throw new WasmDecodeException("malformed sort"),
    };

    private static ComponentSort ReadSort(ref WasmReader reader) => reader.ReadByte() switch
    {
        0x00 => ReadCoreSort(ref reader),
        0x01 => ComponentSort.Func,
        0x02 => ComponentSort.Value,
        0x03 => ComponentSort.Type,
        0x04 => ComponentSort.Component,
        0x05 => ComponentSort.Instance,
        _ => throw new WasmDecodeException("malformed sort"),
    };

    private static AliasDefinition ReadAlias(ref WasmReader reader)
    {
        var sort = ReadSort(ref reader);
        return reader.ReadByte() switch
        {
            0x00 => new AliasDefinition(sort, AliasKind.Export, (int)reader.ReadU32(), reader.ReadName(), 0),
            0x01 => new AliasDefinition(sort, AliasKind.CoreExport, (int)reader.ReadU32(), reader.ReadName(), 0),
            0x02 => ReadOuterAlias(ref reader, sort),
            _ => throw new WasmDecodeException("malformed alias"),
        };
    }

    private static AliasDefinition ReadOuterAlias(ref WasmReader reader, ComponentSort sort)
    {
        if (sort is not (ComponentSort.CoreModule or ComponentSort.CoreType or ComponentSort.Type or ComponentSort.Component))
            throw new WasmDecodeException("invalid leading byte for component outer alias kind");

        var count = (int)reader.ReadU32();
        var index = (int)reader.ReadU32();
        return new AliasDefinition(sort, AliasKind.Outer, index, null, count);
    }

    private static ComponentDefinition ReadExport(ref WasmReader reader)
    {
        var (name, attributes) = ReadNameAttributes(ref reader);
        var sort = ReadSort(ref reader);
        var index = (int)reader.ReadU32();
        var type = reader.ReadByte() switch
        {
            0x00 => null,
            0x01 => ReadExternType(ref reader),
            _ => throw new WasmDecodeException("malformed export"),
        };

        return new ExportDefinition(name, sort, index, type, attributes);
    }

    private static (string Name, ComponentNameAttributes? Attributes) ReadNameAttributes(ref WasmReader reader)
    {
        var kind = reader.ReadByte();
        if (kind > 0x02)
            throw new WasmDecodeException("malformed name");

        var name = reader.ReadName();
        if (kind != 0x02)
            return (name, null);

        string? implements = null;
        string? versionSuffix = null;
        string? externalId = null;
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            switch (reader.ReadByte())
            {
                case 0x00:
                    implements = implements is null ? reader.ReadName() : throw new WasmDecodeException("duplicate 'implements' option in name");
                    break;

                case 0x01:
                    versionSuffix = versionSuffix is null ? reader.ReadName() : throw new WasmDecodeException("duplicate 'version-suffix' option in name");
                    break;

                case 0x02:
                    externalId = externalId is null ? reader.ReadName() : throw new WasmDecodeException("duplicate 'external-id' option in name");
                    break;

                default:
                    throw new WasmDecodeException("malformed attribute");
            }
        }

        return (name, new ComponentNameAttributes(implements, versionSuffix, externalId));
    }

    private static ExternTypeExpr ReadExternType(ref WasmReader reader)
    {
        switch (reader.ReadByte())
        {
            case 0x00:
                if (reader.ReadByte() != 0x11)
                    throw new WasmDecodeException("malformed extern type");

                return new ExternTypeExpr(ComponentExternKind.CoreModule, (int)reader.ReadU32());

            case 0x01:
                return new ExternTypeExpr(ComponentExternKind.Function, (int)reader.ReadU32());

            case 0x02:
                return reader.ReadByte() switch
                {
                    0x00 => new ExternTypeExpr(ComponentExternKind.Value, (int)reader.ReadU32()),
                    0x01 => new ExternTypeExpr(ComponentExternKind.Value, -1, ReadValType(ref reader)),
                    _ => throw new WasmDecodeException("malformed value bound"),
                };

            case 0x03:
                return reader.ReadByte() switch
                {
                    0x00 => new ExternTypeExpr(ComponentExternKind.Type, (int)reader.ReadU32()),
                    0x01 => new ExternTypeExpr(ComponentExternKind.Type, -1),
                    _ => throw new WasmDecodeException("malformed type bound"),
                };

            case 0x04:
                return new ExternTypeExpr(ComponentExternKind.Component, (int)reader.ReadU32());

            case 0x05:
                return new ExternTypeExpr(ComponentExternKind.Instance, (int)reader.ReadU32());

            default:
                throw new WasmDecodeException("malformed extern type");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------------------------------------------------

    private static TypeExpr ReadDefType(ref WasmReader reader, int depth)
    {
        if (depth > MaxNesting)
            throw new WasmDecodeException("nesting too deep");

        var code = reader.PeekByte();
        switch (code)
        {
            case 0x40 or 0x43:
                reader.ReadByte();
                var parameters = ReadVector(ref reader, (ref WasmReader r) => (ReadLabel(ref r), ReadValType(ref r)));
                TypeExpr? result = reader.ReadByte() switch
                {
                    0x00 => ReadValType(ref reader),
                    0x01 when reader.ReadByte() == 0x00 => null,
                    _ => throw new WasmDecodeException("malformed result list"),
                };

                return new FunctionTypeExpr(parameters, result, code == 0x43);

            case 0x41:
            {
                reader.ReadByte();
                var count = reader.ReadCount();
                var declarations = new TypeDeclaration[count];
                for (var i = 0; i < count; i++)
                {
                    if (reader.PeekByte() == 0x03)
                    {
                        reader.ReadByte();
                        var (name, attributes) = ReadNameAttributes(ref reader);
                        declarations[i] = new ImportTypeDeclaration(name, ReadExternType(ref reader), attributes);
                    }
                    else
                    {
                        declarations[i] = ReadInstanceDeclaration(ref reader, depth);
                    }
                }

                return new ComponentTypeExpr(declarations);
            }

            case 0x42:
            {
                reader.ReadByte();
                var count = reader.ReadCount();
                var declarations = new TypeDeclaration[count];
                for (var i = 0; i < count; i++)
                {
                    declarations[i] = ReadInstanceDeclaration(ref reader, depth);
                }

                return new InstanceTypeExpr(declarations);
            }

            case 0x3F:
                reader.ReadByte();
                var is64 = reader.ReadByte() switch
                {
                    0x7F => false,
                    0x7E => true,
                    _ => throw new WasmDecodeException("malformed resource representation"),
                };

                int? destructor = reader.ReadByte() switch
                {
                    0x00 => null,
                    0x01 => (int)reader.ReadU32(),
                    _ => throw new WasmDecodeException("malformed resource destructor"),
                };

                return new ResourceTypeExpr(is64, destructor);

            default:
                return ReadDefValType(ref reader);
        }
    }

    private static TypeDeclaration ReadInstanceDeclaration(ref WasmReader reader, int depth)
    {
        switch (reader.ReadByte())
        {
            case 0x00:
                return new CoreTypeDeclaration(ReadCoreType(ref reader, inModuleType: false));

            case 0x01:
                return new TypeTypeDeclaration(ReadDefType(ref reader, depth + 1));

            case 0x02:
                return new AliasTypeDeclaration(ReadAlias(ref reader));

            case 0x04:
                var (name, attributes) = ReadNameAttributes(ref reader);
                return new ExportTypeDeclaration(name, ReadExternType(ref reader), attributes);

            default:
                throw new WasmDecodeException("malformed type declaration");
        }
    }

    private static string ReadLabel(ref WasmReader reader) => reader.ReadName();

    private static TypeExpr ReadValType(ref WasmReader reader)
    {
        var code = reader.PeekByte();
        if (GetPrimitiveKind(code) is { } primitive)
        {
            reader.ReadByte();
            return new PrimitiveTypeExpr(primitive);
        }

        var index = reader.ReadS33();
        if (index < 0 || index > int.MaxValue)
            throw new WasmDecodeException("malformed value type");

        return new IndexTypeExpr((int)index);
    }

    private static ComponentValueTypeKind? GetPrimitiveKind(byte code) => code switch
    {
        0x7F => ComponentValueTypeKind.Bool,
        0x7E => ComponentValueTypeKind.S8,
        0x7D => ComponentValueTypeKind.U8,
        0x7C => ComponentValueTypeKind.S16,
        0x7B => ComponentValueTypeKind.U16,
        0x7A => ComponentValueTypeKind.S32,
        0x79 => ComponentValueTypeKind.U32,
        0x78 => ComponentValueTypeKind.S64,
        0x77 => ComponentValueTypeKind.U64,
        0x76 => ComponentValueTypeKind.F32,
        0x75 => ComponentValueTypeKind.F64,
        0x74 => ComponentValueTypeKind.Char,
        0x73 => ComponentValueTypeKind.String,
        0x64 => ComponentValueTypeKind.ErrorContext,
        _ => null,
    };

    private static TypeExpr ReadDefValType(ref WasmReader reader)
    {
        var code = reader.ReadByte();
        if (GetPrimitiveKind(code) is { } primitive)
            return new PrimitiveTypeExpr(primitive);

        switch (code)
        {
            case 0x72:
                var fields = ReadVector(ref reader, (ref WasmReader r) => (ReadLabel(ref r), ReadValType(ref r)));
                if (fields.Length == 0)
                    throw new WasmDecodeException("record type must have at least one field");

                return new RecordTypeExpr(fields);

            case 0x71:
                var cases = ReadVector(ref reader, (ref WasmReader r) =>
                {
                    var label = ReadLabel(ref r);
                    var type = ReadOptional(ref r, (ref WasmReader inner) => ReadValType(ref inner));
                    if (r.ReadByte() != 0x00)
                        throw new WasmDecodeException("malformed variant case");

                    return (label, type);
                });

                if (cases.Length == 0)
                    throw new WasmDecodeException("variant type must have at least one case");

                return new VariantTypeExpr(cases);

            case 0x70:
                return new ListTypeExpr(ReadValType(ref reader), null);

            case 0x67:
                var element = ReadValType(ref reader);
                var length = reader.ReadU32();
                if (length == 0 || length > int.MaxValue)
                    throw new WasmDecodeException("fixed-length list must have a positive length");

                return new ListTypeExpr(element, (int)length);

            case 0x6F:
                var types = ReadVector(ref reader, (ref WasmReader r) => ReadValType(ref r));
                if (types.Length == 0)
                    throw new WasmDecodeException("tuple type must have at least one type");

                return new TupleTypeExpr(types);

            case 0x6E:
                var flags = ReadVector(ref reader, (ref WasmReader r) => ReadLabel(ref r));
                if (flags.Length is 0 or > 32)
                    throw new WasmDecodeException("flags type must have between 1 and 32 labels");

                return new FlagsTypeExpr(flags);

            case 0x6D:
                var labels = ReadVector(ref reader, (ref WasmReader r) => ReadLabel(ref r));
                if (labels.Length == 0)
                    throw new WasmDecodeException("enum type must have at least one label");

                return new EnumTypeExpr(labels);

            case 0x6B:
                return new OptionTypeExpr(ReadValType(ref reader));

            case 0x6A:
                var ok = ReadOptional(ref reader, (ref WasmReader r) => ReadValType(ref r));
                var error = ReadOptional(ref reader, (ref WasmReader r) => ReadValType(ref r));
                return new ResultTypeExpr(ok, error);

            case 0x69:
                return new HandleTypeExpr(Own: true, (int)reader.ReadU32());

            case 0x68:
                return new HandleTypeExpr(Own: false, (int)reader.ReadU32());

            case 0x66:
                return new AsyncValueTypeExpr(Stream: true, ReadOptional(ref reader, (ref WasmReader r) => ReadValType(ref r)));

            case 0x65:
                return new AsyncValueTypeExpr(Stream: false, ReadOptional(ref reader, (ref WasmReader r) => ReadValType(ref r)));

            case 0x63:
                return new MapTypeExpr(ReadValType(ref reader), ReadValType(ref reader));

            default:
                throw new WasmDecodeException("malformed type");
        }
    }

    private static T? ReadOptional<T>(ref WasmReader reader, ItemReader<T> read)
        where T : class
    {
        return reader.ReadByte() switch
        {
            0x00 => null,
            0x01 => read(ref reader),
            _ => throw new WasmDecodeException("malformed optional"),
        };
    }

    // ------------------------------------------------------------------------------------------------------------
    // Core types
    // ------------------------------------------------------------------------------------------------------------

    private static CoreTypeExpr ReadCoreType(ref WasmReader reader, bool inModuleType)
    {
        var code = reader.PeekByte();
        if (!inModuleType && code == 0x50)
        {
            reader.ReadByte();
            var count = reader.ReadCount();
            var declarations = new CoreModuleDeclaration[count];
            for (var i = 0; i < count; i++)
            {
                declarations[i] = reader.ReadByte() switch
                {
                    0x00 => new CoreImportDeclaration(reader.ReadName(), reader.ReadName(), ReadCoreExternType(ref reader)),
                    0x01 => new CoreTypeModuleDeclaration(ReadCoreType(ref reader, inModuleType: true)),
                    0x02 => ReadCoreAlias(ref reader),
                    0x03 => new CoreExportDeclaration(reader.ReadName(), ReadCoreExternType(ref reader)),
                    _ => throw new WasmDecodeException("malformed module type declaration"),
                };
            }

            return new CoreModuleTypeExpr(declarations);
        }

        if (!inModuleType && code == 0x00)
        {
            // A non-final sub type is prefixed by 0x00 outside of a module type
            reader.ReadByte();
            if (reader.ReadByte() != 0x50)
                throw new WasmDecodeException("malformed core type");

            return ReadSubTypeBody(ref reader);
        }

        return ReadRecType(ref reader);
    }

    private static CoreAliasDeclaration ReadCoreAlias(ref WasmReader reader)
    {
        if (reader.ReadByte() != 0x10 || reader.ReadByte() != 0x01)
            throw new WasmDecodeException("malformed core alias");

        return new CoreAliasDeclaration((int)reader.ReadU32(), (int)reader.ReadU32());
    }

    private static CoreTypeExpr ReadRecType(ref WasmReader reader)
    {
        if (reader.PeekByte() == 0x4E)
        {
            reader.ReadByte();
            var count = reader.ReadCount();
            CoreTypeExpr? first = null;
            for (var i = 0; i < count; i++)
            {
                var type = ReadSubType(ref reader);
                first ??= type;
            }

            return first ?? new CoreOpaqueTypeExpr();
        }

        return ReadSubType(ref reader);
    }

    private static CoreTypeExpr ReadSubType(ref WasmReader reader)
    {
        var code = reader.PeekByte();
        if (code is 0x50 or 0x4F)
        {
            reader.ReadByte();
            return ReadSubTypeBody(ref reader);
        }

        return ReadCompType(ref reader);
    }

    private static CoreTypeExpr ReadSubTypeBody(ref WasmReader reader)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            _ = reader.ReadU32();
        }

        return ReadCompType(ref reader);
    }

    private static CoreTypeExpr ReadCompType(ref WasmReader reader)
    {
        switch (reader.ReadByte())
        {
            case 0x60:
                var parameters = ReadVector(ref reader, (ref WasmReader r) => ReadCoreValType(ref r));
                var results = ReadVector(ref reader, (ref WasmReader r) => ReadCoreValType(ref r));
                return new CoreFunctionTypeExpr([.. parameters.Select(p => p.Kind)], [.. results.Select(r => r.Kind)])
                {
                    ParameterTypes = [.. parameters.Select(p => p.Type)],
                    ResultTypes = [.. results.Select(r => r.Type)],
                };

            case 0x5F:
                var count = reader.ReadCount();
                for (var i = 0; i < count; i++)
                {
                    ReadFieldType(ref reader);
                }

                return new CoreOpaqueTypeExpr();

            case 0x5E:
                ReadFieldType(ref reader);
                return new CoreOpaqueTypeExpr();

            default:
                throw new WasmDecodeException("malformed core type");
        }
    }

    private static void ReadFieldType(ref WasmReader reader)
    {
        if (reader.PeekByte() is 0x78 or 0x77)
        {
            reader.ReadByte();
        }
        else
        {
            _ = ReadCoreValType(ref reader);
        }

        if (reader.ReadByte() > 1)
            throw new WasmDecodeException("malformed mutability");
    }

    private static (CoreValKind Kind, WasmValueType Type) ReadCoreValType(ref WasmReader reader)
    {
        var code = reader.ReadByte();
        switch (code)
        {
            case 0x7F:
                return (CoreValKind.I32, WasmValueType.I32);
            case 0x7E:
                return (CoreValKind.I64, WasmValueType.I64);
            case 0x7D:
                return (CoreValKind.F32, WasmValueType.F32);
            case 0x7C:
                return (CoreValKind.F64, WasmValueType.F64);
            case 0x7B:
                return (CoreValKind.V128, WasmValueType.V128);
            case 0x63 or 0x64:
                var heap = reader.ReadS33();
                var kind = heap < 0 ? ModuleDecoder.GetAbstractHeapType((byte)(heap & 0x7F)) ?? WasmHeapKind.Any : WasmHeapKind.ConcreteFunc;
                return (CoreValKind.Ref, new WasmValueType(WasmValueKind.Ref, kind, Nullable: code == 0x63));
            default:
                if (ModuleDecoder.GetAbstractHeapType(code) is { } abstractHeap)
                    return (CoreValKind.Ref, new WasmValueType(WasmValueKind.Ref, abstractHeap));

                throw new WasmDecodeException("malformed value type");
        }
    }

    private static CoreExternTypeExpr ReadCoreExternType(ref WasmReader reader)
    {
        switch (reader.ReadByte())
        {
            case 0x00:
                return new CoreExternTypeExpr(WasmExternKind.Function, (int)reader.ReadU32());

            case 0x01:
                var (_, elementType) = ReadCoreValType(ref reader);
                var (tableMinimum, tableMaximum, table64, _) = ReadLimits(ref reader);
                return new CoreExternTypeExpr(WasmExternKind.Table, -1, Table: new WasmTableType(elementType, tableMinimum, tableMaximum, table64));

            case 0x02:
                var (minimum, maximum, is64, shared) = ReadLimits(ref reader);
                return new CoreExternTypeExpr(WasmExternKind.Memory, -1, Memory: new WasmMemoryType(minimum, maximum, is64, shared));

            case 0x03:
                var (_, valueType) = ReadCoreValType(ref reader);
                var mutable = reader.ReadByte() switch
                {
                    0x00 => false,
                    0x01 => true,
                    _ => throw new WasmDecodeException("malformed mutability"),
                };

                return new CoreExternTypeExpr(WasmExternKind.Global, -1, Global: new WasmGlobalType(valueType, mutable));

            case 0x04:
                if (reader.ReadByte() != 0x00)
                    throw new WasmDecodeException("malformed tag");

                return new CoreExternTypeExpr(WasmExternKind.Tag, (int)reader.ReadU32());

            default:
                throw new WasmDecodeException("malformed import kind");
        }
    }

    private static (ulong Minimum, ulong? Maximum, bool Is64, bool Shared) ReadLimits(ref WasmReader reader)
    {
        var flags = reader.ReadByte();
        if ((flags & ~0x07) != 0)
            throw new WasmDecodeException("malformed limits flags");

        var is64 = (flags & 0x04) != 0;
        var minimum = is64 ? reader.ReadU64() : reader.ReadU32();
        ulong? maximum = (flags & 0x01) != 0 ? (is64 ? reader.ReadU64() : reader.ReadU32()) : null;
        return (minimum, maximum, is64, (flags & 0x02) != 0);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Canonical definitions
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentDefinition ReadCanon(ref WasmReader reader)
    {
        var code = reader.ReadByte();
        switch (code)
        {
            case 0x00:
                if (reader.ReadByte() != 0x00)
                    throw new WasmDecodeException("malformed canon lift");

                var core = (int)reader.ReadU32();
                var liftOptions = ReadCanonOptions(ref reader);
                return new CanonDefinition(CanonKind.Lift, core, (int)reader.ReadU32(), liftOptions);

            case 0x01:
                if (reader.ReadByte() != 0x00)
                    throw new WasmDecodeException("malformed canon lower");

                var function = (int)reader.ReadU32();
                return new CanonDefinition(CanonKind.Lower, function, -1, ReadCanonOptions(ref reader));

            case 0x02:
                return new CanonDefinition(CanonKind.ResourceNew, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x03:
                return new CanonDefinition(CanonKind.ResourceDrop, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x07:
                return new CanonDefinition(CanonKind.ResourceDropAsync, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x04:
                return new CanonDefinition(CanonKind.ResourceRep, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x24:
                return new CanonDefinition(CanonKind.BackpressureInc, -1, -1, CanonOptions.Empty);
            case 0x25:
                return new CanonDefinition(CanonKind.BackpressureDec, -1, -1, CanonOptions.Empty);

            case 0x09:
                TypeExpr? resultType = reader.ReadByte() switch
                {
                    0x00 => ReadValType(ref reader),
                    0x01 when reader.ReadByte() == 0x00 => null,
                    _ => throw new WasmDecodeException("malformed result list"),
                };

                return new CanonDefinition(CanonKind.TaskReturn, -1, -1, ReadCanonOptions(ref reader), ResultType: resultType, HasResultType: true);

            case 0x05:
                return new CanonDefinition(CanonKind.TaskCancel, -1, -1, CanonOptions.Empty);

            case 0x0A or 0x0B:
                var valueType = reader.ReadByte() switch
                {
                    0x7F => CoreValKind.I32,
                    0x7E => CoreValKind.I64,
                    _ => throw new WasmDecodeException("malformed context type"),
                };

                return new CanonDefinition(code == 0x0A ? CanonKind.ContextGet : CanonKind.ContextSet, (int)reader.ReadU32(), -1, CanonOptions.Empty, valueType);

            case 0x06:
                return new CanonDefinition(CanonKind.SubtaskCancel, -1, -1, CanonOptions.Empty, Async: ReadAsync(ref reader));
            case 0x0D:
                return new CanonDefinition(CanonKind.SubtaskDrop, -1, -1, CanonOptions.Empty);
            case 0x0E:
                return new CanonDefinition(CanonKind.StreamNew, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x0F:
                return ReadTypeAndOptions(ref reader, CanonKind.StreamRead);
            case 0x10:
                return ReadTypeAndOptions(ref reader, CanonKind.StreamWrite);
            case 0x11:
                return ReadTypeAndAsync(ref reader, CanonKind.StreamCancelRead);
            case 0x12:
                return ReadTypeAndAsync(ref reader, CanonKind.StreamCancelWrite);
            case 0x13:
                return new CanonDefinition(CanonKind.StreamDropReadable, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x14:
                return new CanonDefinition(CanonKind.StreamDropWritable, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x2E:
                return new CanonDefinition(CanonKind.StreamForward, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x15:
                return new CanonDefinition(CanonKind.FutureNew, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x16:
                return ReadTypeAndOptions(ref reader, CanonKind.FutureRead);
            case 0x17:
                return ReadTypeAndOptions(ref reader, CanonKind.FutureWrite);
            case 0x18:
                return ReadTypeAndAsync(ref reader, CanonKind.FutureCancelRead);
            case 0x19:
                return ReadTypeAndAsync(ref reader, CanonKind.FutureCancelWrite);
            case 0x1A:
                return new CanonDefinition(CanonKind.FutureDropReadable, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x1B:
                return new CanonDefinition(CanonKind.FutureDropWritable, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x2F:
                return new CanonDefinition(CanonKind.FutureForward, (int)reader.ReadU32(), -1, CanonOptions.Empty);
            case 0x1C:
                return new CanonDefinition(CanonKind.ErrorContextNew, -1, -1, ReadCanonOptions(ref reader));
            case 0x1D:
                return new CanonDefinition(CanonKind.ErrorContextDebugMessage, -1, -1, ReadCanonOptions(ref reader));
            case 0x1E:
                return new CanonDefinition(CanonKind.ErrorContextDrop, -1, -1, CanonOptions.Empty);
            case 0x1F:
                return new CanonDefinition(CanonKind.WaitableSetNew, -1, -1, CanonOptions.Empty);

            case 0x20 or 0x21:
                // The vestigial 0x00 was the "cancellable" immediate of older versions
                ReadZeroByte(ref reader);
                var memory = (int)reader.ReadU32();
                return new CanonDefinition(code == 0x20 ? CanonKind.WaitableSetWait : CanonKind.WaitableSetPoll, -1, -1, CanonOptions.Empty with { Memory = memory });

            case 0x22:
                return new CanonDefinition(CanonKind.WaitableSetDrop, -1, -1, CanonOptions.Empty);
            case 0x23:
                return new CanonDefinition(CanonKind.WaitableJoin, -1, -1, CanonOptions.Empty);
            case 0x26:
                return new CanonDefinition(CanonKind.ThreadIndex, -1, -1, CanonOptions.Empty);
            case 0x27:
                var functionType = (int)reader.ReadU32();
                return new CanonDefinition(CanonKind.ThreadNewIndirect, functionType, (int)reader.ReadU32(), CanonOptions.Empty);
            case 0x28:
                return new CanonDefinition(CanonKind.ThreadResumeLater, -1, -1, CanonOptions.Empty);
            case 0x29:
                return ReadVestigialZeroByte(ref reader, CanonKind.ThreadSuspend);
            case 0x0C:
                return ReadVestigialZeroByte(ref reader, CanonKind.ThreadYield);
            case 0x2A:
                return ReadVestigialZeroByte(ref reader, CanonKind.ThreadSuspendThenResume);
            case 0x2B:
                return ReadVestigialZeroByte(ref reader, CanonKind.ThreadYieldThenResume);
            case 0x2C:
                return ReadVestigialZeroByte(ref reader, CanonKind.ThreadSuspendThenPromote);
            case 0x2D:
                return ReadVestigialZeroByte(ref reader, CanonKind.ThreadYieldThenPromote);

            default:
                throw new WasmDecodeException("malformed canonical definition");
        }
    }

    private static CanonDefinition ReadVestigialZeroByte(ref WasmReader reader, CanonKind kind)
    {
        ReadZeroByte(ref reader);
        return new CanonDefinition(kind, -1, -1, CanonOptions.Empty);
    }

    private static void ReadZeroByte(ref WasmReader reader)
    {
        var value = reader.ReadByte();
        if (value != 0x00)
            throw new WasmDecodeException("invalid leading byte (0x" + value.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + ") for zero byte");
    }

    private static bool ReadAsync(ref WasmReader reader) => reader.ReadByte() switch
    {
        0x00 => false,
        0x01 => true,
        _ => throw new WasmDecodeException("malformed async immediate"),
    };

    private static CanonDefinition ReadTypeAndOptions(ref WasmReader reader, CanonKind kind)
    {
        var type = (int)reader.ReadU32();
        return new CanonDefinition(kind, type, -1, ReadCanonOptions(ref reader));
    }

    private static CanonDefinition ReadTypeAndAsync(ref WasmReader reader, CanonKind kind)
    {
        var type = (int)reader.ReadU32();
        return new CanonDefinition(kind, type, -1, CanonOptions.Empty, Async: ReadAsync(ref reader));
    }

    private static CanonOptions ReadCanonOptions(ref WasmReader reader)
    {
        var options = CanonOptions.Empty;
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            var code = reader.ReadByte();
            options = code switch
            {
                0x00 or 0x01 or 0x02 when options.StringEncoding is not null => throw new WasmDecodeException("canonical option `string-encoding` is specified more than once"),
                0x00 => options with { StringEncoding = ComponentStringEncoding.Utf8 },
                0x01 => options with { StringEncoding = ComponentStringEncoding.Utf16 },
                0x02 => options with { StringEncoding = ComponentStringEncoding.Latin1Utf16 },
                0x03 when options.Memory is not null => throw new WasmDecodeException("canonical option `memory` is specified more than once"),
                0x03 => options with { Memory = (int)reader.ReadU32() },
                0x04 when options.Realloc is not null => throw new WasmDecodeException("canonical option `realloc` is specified more than once"),
                0x04 => options with { Realloc = (int)reader.ReadU32() },
                0x05 when options.PostReturn is not null => throw new WasmDecodeException("canonical option `post-return` is specified more than once"),
                0x05 => options with { PostReturn = (int)reader.ReadU32() },
                0x06 when options.Async => throw new WasmDecodeException("canonical option `async` is specified more than once"),
                0x06 => options with { Async = true },
                0x07 when options.Callback is not null => throw new WasmDecodeException("canonical option `callback` is specified more than once"),
                0x07 => options with { Callback = (int)reader.ReadU32() },
                _ => throw new WasmDecodeException("malformed canonical option"),
            };
        }

        return options;
    }
}
