using System;
using System.Collections.Generic;
using System.Text;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

/// <summary>
/// Reads the exports of a module of the binary format, and the types of its exported functions
/// (https://webassembly.github.io/spec/core/binary/modules.html). It reads the sections of the types, the imports, the
/// functions and the exports, and skips the others: it does not validate the module.
/// </summary>
internal static class WasmBinaryDescriptionReader
{
    private enum CompositeKind : byte
    {
        Func,
        Struct,
        Array,
    }

    /// <exception cref="WasmDescriptionException">The module is malformed.</exception>
    public static WasmModuleDescription Read(byte[] bytes)
    {
        var reader = new Reader(bytes, 0, bytes.Length);
        if (bytes.Length < 8 || bytes[0] != 0 || bytes[1] != 0x61 || bytes[2] != 0x73 || bytes[3] != 0x6D)
            throw new WasmDescriptionException("magic header not detected", 0);

        if (bytes[4] != 1 || bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0)
            throw new WasmDescriptionException("unknown binary version", 4);

        reader.Position = 8;
        var types = new List<RawType>();
        var functions = new List<uint>();
        var counts = new int[5];
        var exports = new List<(string Name, WasmExportKind Kind, uint Index, int Position)>();
        while (!reader.AtEnd)
        {
            var id = reader.ReadByte();
            var size = reader.ReadU32();
            if (size > (uint)(reader.End - reader.Position))
                throw new WasmDescriptionException("section size mismatch: unexpected end", reader.Position);

            var section = new Reader(bytes, reader.Position, reader.Position + (int)size);
            reader.Position += (int)size;
            switch (id)
            {
                case 1:
                    ReadTypeSection(ref section, types);
                    break;
                case 2:
                    ReadImportSection(ref section, types, functions, counts);
                    break;
                case 3:
                    var count = section.ReadCount();
                    for (var i = 0; i < count; i++)
                    {
                        functions.Add(ReadFunctionType(ref section, types));
                    }

                    counts[(int)WasmExportKind.Function] += count;
                    break;
                case 4:
                    counts[(int)WasmExportKind.Table] += section.ReadCount();
                    break;
                case 5:
                    counts[(int)WasmExportKind.Memory] += section.ReadCount();
                    break;
                case 6:
                    counts[(int)WasmExportKind.Global] += section.ReadCount();
                    break;
                case 13:
                    counts[(int)WasmExportKind.Tag] += section.ReadCount();
                    break;
                case 7:
                    ReadExportSection(ref section, exports);
                    break;
                default:
                    if (id > 13)
                        throw new WasmDescriptionException("malformed section id", section.Start - 1);

                    break;
            }
        }

        var result = new List<WasmExportDescription>(exports.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, kind, index, position) in exports)
        {
            if (index >= (uint)counts[(int)kind])
                throw new WasmDescriptionException("unknown " + kind.ToString().ToLowerInvariant() + " " + index, position);

            if (!names.Add(name))
                throw new WasmDescriptionException("duplicate export name", position);

            WasmFunctionSignature? signature = null;
            if (kind == WasmExportKind.Function)
            {
                var type = types[(int)functions[(int)index]];
                signature = new WasmFunctionSignature(Resolve(type.Parameters, types, position), new string?[type.Parameters.Length], Resolve(type.Results, types, position));
            }

            result.Add(new WasmExportDescription(name, kind, signature));
        }

        return new WasmModuleDescription(result);
    }

    private static void ReadTypeSection(ref Reader reader, List<RawType> types)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            if (reader.PeekByte() == 0x4E)
            {
                reader.ReadByte();
                var groupCount = reader.ReadCount();
                for (var j = 0; j < groupCount; j++)
                {
                    types.Add(ReadSubType(ref reader));
                }
            }
            else
            {
                types.Add(ReadSubType(ref reader));
            }
        }
    }

    private static RawType ReadSubType(ref Reader reader)
    {
        var b = reader.PeekByte();
        if (b is 0x50 or 0x4F)
        {
            reader.ReadByte();
            var superCount = reader.ReadCount();
            for (var i = 0; i < superCount; i++)
            {
                reader.ReadU32();
            }
        }

        var position = reader.Position;
        switch (reader.ReadByte())
        {
            case 0x60:
                return new RawType(CompositeKind.Func, ReadValueTypes(ref reader), ReadValueTypes(ref reader));

            case 0x5F:
                var fieldCount = reader.ReadCount();
                for (var i = 0; i < fieldCount; i++)
                {
                    ReadFieldType(ref reader);
                }

                return new RawType(CompositeKind.Struct, [], []);

            case 0x5E:
                ReadFieldType(ref reader);
                return new RawType(CompositeKind.Array, [], []);

            default:
                throw new WasmDescriptionException("malformed composite type", position);
        }
    }

    private static void ReadFieldType(ref Reader reader)
    {
        if (reader.PeekByte() is 0x78 or 0x77)
        {
            reader.ReadByte();
        }
        else
        {
            ReadValueType(ref reader);
        }

        var position = reader.Position;
        if (reader.ReadByte() > 1)
            throw new WasmDescriptionException("malformed mutability", position);
    }

    private static RawValueType[] ReadValueTypes(ref Reader reader)
    {
        var count = reader.ReadCount();
        var result = new RawValueType[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ReadValueType(ref reader);
        }

        return result;
    }

    private static RawValueType ReadValueType(ref Reader reader)
    {
        var position = reader.Position;
        var b = reader.ReadByte();
        switch (b)
        {
            case 0x7F:
                return new RawValueType(WasmTypeInfo.I32);
            case 0x7E:
                return new RawValueType(WasmTypeInfo.I64);
            case 0x7D:
                return new RawValueType(WasmTypeInfo.F32);
            case 0x7C:
                return new RawValueType(WasmTypeInfo.F64);
            case 0x7B:
                return new RawValueType(WasmTypeInfo.V128);
            case 0x63 or 0x64:
                var heapPosition = reader.Position;
                var value = reader.ReadS33();
                if (value >= 0)
                    return new RawValueType(nullable: b == 0x63, typeIndex: value);

                // A negative value is the abstract heap type of a single byte (-0x10 is 0x70...)
                if (value < -64 || GetAbstractHeapType((byte)(value & 0x7F)) is not { } heap)
                    throw new WasmDescriptionException("malformed heap type", heapPosition);

                return new RawValueType(WasmTypeInfo.Reference(heap, nullable: b == 0x63));
            default:
                if (GetAbstractHeapType(b) is { } abstractHeap)
                    return new RawValueType(WasmTypeInfo.Reference(abstractHeap, nullable: true));

                throw new WasmDescriptionException("malformed value type", position);
        }
    }

    private static WasmHeapTypeKind? GetAbstractHeapType(byte code) => code switch
    {
        0x70 => WasmHeapTypeKind.Func,
        0x6F => WasmHeapTypeKind.Extern,
        0x6E => WasmHeapTypeKind.Any,
        0x6D => WasmHeapTypeKind.Eq,
        0x6C => WasmHeapTypeKind.I31,
        0x6B => WasmHeapTypeKind.Struct,
        0x6A => WasmHeapTypeKind.Array,
        0x69 => WasmHeapTypeKind.Exn,
        0x71 => WasmHeapTypeKind.None,
        0x72 => WasmHeapTypeKind.NoExtern,
        0x73 => WasmHeapTypeKind.NoFunc,
        0x74 => WasmHeapTypeKind.NoExn,
        _ => null,
    };

    private static void ReadImportSection(ref Reader reader, List<RawType> types, List<uint> functions, int[] counts)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            reader.ReadName();
            reader.ReadName();
            var position = reader.Position;
            var kind = reader.ReadByte();
            switch (kind)
            {
                case 0x00:
                    functions.Add(ReadFunctionType(ref reader, types));
                    break;
                case 0x01:
                    ReadValueType(ref reader);
                    ReadLimits(ref reader);
                    break;
                case 0x02:
                    ReadLimits(ref reader);
                    break;
                case 0x03:
                    ReadValueType(ref reader);
                    var mutabilityPosition = reader.Position;
                    if (reader.ReadByte() > 1)
                        throw new WasmDescriptionException("malformed mutability", mutabilityPosition);

                    break;
                case 0x04:
                    var attributePosition = reader.Position;
                    if (reader.ReadByte() != 0)
                        throw new WasmDescriptionException("malformed tag attribute", attributePosition);

                    ReadFunctionType(ref reader, types);
                    break;
                default:
                    throw new WasmDescriptionException("malformed import kind", position);
            }

            counts[kind]++;
        }
    }

    private static void ReadLimits(ref Reader reader)
    {
        var position = reader.Position;
        var flags = reader.ReadByte();
        if ((flags & ~0x07) != 0)
            throw new WasmDescriptionException("malformed limits flags", position);

        var is64 = (flags & 0x04) != 0;
        var count = (flags & 0x01) != 0 ? 2 : 1;
        for (var i = 0; i < count; i++)
        {
            if (is64)
            {
                reader.ReadU64();
            }
            else
            {
                reader.ReadU32();
            }
        }
    }

    private static uint ReadFunctionType(ref Reader reader, List<RawType> types)
    {
        var position = reader.Position;
        var index = reader.ReadU32();
        if (index >= (uint)types.Count)
            throw new WasmDescriptionException("unknown type " + index, position);

        if (types[(int)index].Kind != CompositeKind.Func)
            throw new WasmDescriptionException("type " + index + " is not a function type", position);

        return index;
    }

    private static void ReadExportSection(ref Reader reader, List<(string Name, WasmExportKind Kind, uint Index, int Position)> exports)
    {
        var count = reader.ReadCount();
        for (var i = 0; i < count; i++)
        {
            var position = reader.Position;
            var name = reader.ReadName();
            var kindPosition = reader.Position;
            var kind = reader.ReadByte();
            if (kind > 4)
                throw new WasmDescriptionException("malformed export kind", kindPosition);

            exports.Add((name, (WasmExportKind)kind, reader.ReadU32(), position));
        }
    }

    private static WasmTypeInfo[] Resolve(RawValueType[] types, List<RawType> definitions, int position)
    {
        var result = new WasmTypeInfo[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (type.TypeIndex < 0)
            {
                result[i] = type.Type;
                continue;
            }

            if (type.TypeIndex >= definitions.Count)
                throw new WasmDescriptionException("unknown type " + type.TypeIndex, position);

            var heap = definitions[(int)type.TypeIndex].Kind switch
            {
                CompositeKind.Func => WasmHeapTypeKind.ConcreteFunc,
                CompositeKind.Struct => WasmHeapTypeKind.ConcreteStruct,
                _ => WasmHeapTypeKind.ConcreteArray,
            };
            result[i] = WasmTypeInfo.Reference(heap, type.Nullable);
        }

        return result;
    }

    private sealed class RawType(CompositeKind kind, RawValueType[] parameters, RawValueType[] results)
    {
        public CompositeKind Kind { get; } = kind;

        public RawValueType[] Parameters { get; } = parameters;

        public RawValueType[] Results { get; } = results;
    }

    /// <summary>A value type whose concrete heap type (<see cref="TypeIndex"/>) is resolved once all the types are read.</summary>
    private readonly struct RawValueType
    {
        public RawValueType(WasmTypeInfo type)
        {
            Type = type;
            TypeIndex = -1;
        }

        public RawValueType(bool nullable, long typeIndex)
        {
            Nullable = nullable;
            TypeIndex = typeIndex;
        }

        public WasmTypeInfo Type { get; }

        public bool Nullable { get; }

        public long TypeIndex { get; }
    }

    private struct Reader(byte[] bytes, int start, int end)
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public int Position { get; set; } = start;

        public int Start { get; } = start;

        public int End { get; } = end;

        public readonly bool AtEnd => Position >= End;

        public byte ReadByte()
        {
            if (Position >= End)
                throw new WasmDescriptionException("unexpected end", Position);

            return bytes[Position++];
        }

        public readonly byte PeekByte()
        {
            if (Position >= End)
                throw new WasmDescriptionException("unexpected end", Position);

            return bytes[Position];
        }

        public uint ReadU32() => (uint)ReadUnsigned(32);

        public ulong ReadU64() => ReadUnsigned(64);

        /// <summary>Reads a count of items: each item has at least one byte, so a count larger than the section is malformed.</summary>
        public int ReadCount()
        {
            var position = Position;
            var count = ReadU32();
            if (count > (uint)(End - Position))
                throw new WasmDescriptionException("unexpected end: the count is larger than the section", position);

            return (int)count;
        }

        public string ReadName()
        {
            var length = ReadCount();
            var position = Position;
            Position += length;
            try
            {
                return Utf8.GetString(bytes, position, length);
            }
            catch (DecoderFallbackException)
            {
                throw new WasmDescriptionException("malformed UTF-8 encoding", position);
            }
        }

        /// <summary>Reads a signed 33-bit integer (a heap type, or a type index).</summary>
        public long ReadS33()
        {
            var position = Position;
            long result = 0;
            var shift = 0;
            byte b;
            do
            {
                if (shift >= 35)
                    throw new WasmDescriptionException("integer representation too long", position);

                b = ReadByte();
                result |= (long)(b & 0x7F) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);

            // The unused bits of the last byte must be the sign extension of the value
            if (shift == 35 && (b & 0x70) is not 0 and not 0x70)
                throw new WasmDescriptionException("integer too large", position);

            if (shift < 64 && (b & 0x40) != 0)
            {
                result |= -1L << shift;
            }

            if (result is < -(1L << 32) or >= 1L << 32)
                throw new WasmDescriptionException("integer too large", position);

            return result;
        }

        private ulong ReadUnsigned(int bits)
        {
            var position = Position;
            ulong result = 0;
            var shift = 0;
            while (true)
            {
                var b = ReadByte();
                if (shift + 7 >= bits)
                {
                    // The last byte: its unused bits must be zero
                    var remainingBits = bits - shift;
                    if ((b & 0x80) != 0 || (b >> remainingBits) != 0)
                        throw new WasmDescriptionException("integer representation too long or too large", position);
                }

                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;

                shift += 7;
            }
        }
    }
}
