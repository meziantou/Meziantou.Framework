using System.Buffers.Binary;

namespace Meziantou.Framework.WasmEngine.Text;

// The canonical definitions, the start definitions and the value definitions
internal sealed partial class ComponentWatParser
{
    /// <summary>The built-ins that have no immediate: their opcode.</summary>
    private static readonly Dictionary<string, byte[]> SimpleBuiltins = new(StringComparer.Ordinal)
    {
        ["backpressure.inc"] = [0x24],
        ["backpressure.dec"] = [0x25],
        ["task.cancel"] = [0x05],
        ["subtask.drop"] = [0x0D],
        ["error-context.drop"] = [0x1E],
        ["waitable-set.new"] = [0x1F],
        ["waitable-set.drop"] = [0x22],
        ["waitable.join"] = [0x23],
        ["thread.index"] = [0x26],
        ["thread.resume-later"] = [0x28],
    };

    /// <summary>The built-ins that have a type index immediate: their opcode.</summary>
    private static readonly Dictionary<string, byte> TypeBuiltins = new(StringComparer.Ordinal)
    {
        ["resource.new"] = 0x02,
        ["resource.drop"] = 0x03,
        ["resource.rep"] = 0x04,
        ["stream.new"] = 0x0E,
        ["stream.drop-readable"] = 0x13,
        ["stream.drop-writable"] = 0x14,
        ["stream.forward"] = 0x2E,
        ["future.new"] = 0x15,
        ["future.drop-readable"] = 0x1A,
        ["future.drop-writable"] = 0x1B,
        ["future.forward"] = 0x2F,
    };

    /// <summary>The built-ins that have a type index and options: their opcode.</summary>
    private static readonly Dictionary<string, byte> TypeAndOptionsBuiltins = new(StringComparer.Ordinal)
    {
        ["stream.read"] = 0x0F,
        ["stream.write"] = 0x10,
        ["future.read"] = 0x16,
        ["future.write"] = 0x17,
    };

    /// <summary>The built-ins that have a type index and an optional <c>async</c>: their opcode.</summary>
    private static readonly Dictionary<string, byte> TypeAndAsyncBuiltins = new(StringComparer.Ordinal)
    {
        ["stream.cancel-read"] = 0x11,
        ["stream.cancel-write"] = 0x12,
        ["future.cancel-read"] = 0x18,
        ["future.cancel-write"] = 0x19,
    };

    /// <summary>The built-ins that are followed by a vestigial <c>0x00</c> (the removed <c>cancellable</c> immediate): their opcode.</summary>
    private static readonly Dictionary<string, byte> CancellableBuiltins = new(StringComparer.Ordinal)
    {
        ["thread.yield"] = 0x0C,
        ["thread.suspend"] = 0x29,
        ["thread.suspend-then-resume"] = 0x2A,
        ["thread.yield-then-resume"] = 0x2B,
        ["thread.suspend-then-promote"] = 0x2C,
        ["thread.yield-then-promote"] = 0x2D,
    };

    private static bool IsCoreFuncTarget(SNode node) => node.IsList && node.Items!.Count is 2 or 3 && node.Items[0].IsKeyword("core") && node.Items[1].IsKeyword("func") && (node.Items.Count == 2 || node.Items[2] is { IsList: false, Kind: TokenKind.Id });

    /// <summary>
    /// Encodes a canonical definition: <c>(canon lift ... (func $id? type))</c>, <c>(canon lower ... (core func $id?))</c> or a built-in
    /// <c>(canon name ... (core func $id?))</c>. In the abbreviations <c>(core func $id? (canon ...))</c> and <c>(func $id? type (canon lift ...))</c>
    /// (<paramref name="inverted"/>), the canonical definition has no target: the id is <paramref name="invertedId"/>, and the type of a lifted
    /// function is the type use of <paramref name="invertedFuncType"/>.
    /// </summary>
    private uint EncodeCanon(SNode canon, bool inverted, string? invertedId, (SNode Owner, int Start, int End)? invertedFuncType)
    {
        var items = canon.Items!;
        if (items.Count < 2 || items[1] is not { IsList: false, Kind: TokenKind.Keyword } nameNode)
            throw new WatException("malformed canonical definition", canon.Position);

        var name = nameNode.Text;
        var end = items.Count;
        SNode? target = null;
        if (!inverted && end > 2 && (name == "lift" ? items[^1].IsListOf("func") : IsCoreFuncTarget(items[^1])))
        {
            target = items[^1];
            end--;
        }

        string? id;
        var targetStart = 0;
        if (inverted)
        {
            id = invertedId;
        }
        else if (target is not null)
        {
            targetStart = name == "lift" ? 1 : 2;
            id = ReadId(target.Items!, ref targetStart);
        }
        else
        {
            id = null;
        }

        var content = new ByteWriter();
        if (name == "lift")
        {
            if (end < 3)
                throw new WatException("malformed canon lift: expected a core function", canon.Position);

            var (owner, start, typeEnd) = invertedFuncType ?? (target is not null ? (target, targetStart, target.Items!.Count) : throw new WatException("malformed canon lift: expected a function type", canon.Position));

            // The inline function type is defined first
            PrepareFunctionTypeUse(owner, owner.Items!, start, typeEnd);
            var function = ResolveReference(items[2], Sort.CoreFunc);
            var options = EncodeCanonOptions(items, 3, end);
            var type = EncodeFunctionTypeUse(owner, owner.Items!, start, typeEnd);
            content.Byte(0x00);
            content.Byte(0x00);
            content.U32(function);
            content.Append(options);
            content.U32(type);
            _scope.Add(ItemKind.Canon, content, canon.Position);
            return _scope[Sort.Func].Add(id, Sort.Func, canon.Position);
        }

        if (target is not null && targetStart != target.Items!.Count)
            throw new WatException("malformed canonical definition: unexpected items in the core func", target.Position);

        EncodeBuiltin(canon, name, items, end, content);
        _scope.Add(ItemKind.Canon, content, canon.Position);
        return _scope[Sort.CoreFunc].Add(id, Sort.CoreFunc, canon.Position);
    }

    private void EncodeBuiltin(SNode canon, string name, List<SNode> items, int end, ByteWriter content)
    {
        var index = 2;
        if (name == "lower")
        {
            if (end < 3)
                throw new WatException("malformed canon lower: expected a function", canon.Position);

            var function = ResolveReference(items[2], Sort.Func);
            var options = EncodeCanonOptions(items, 3, end);
            content.Byte(0x01);
            content.Byte(0x00);
            content.U32(function);
            content.Append(options);
            return;
        }

        if (SimpleBuiltins.TryGetValue(name, out var code))
        {
            content.Bytes(code);
        }
        else if (TypeBuiltins.TryGetValue(name, out var typeCode))
        {
            var type = ResolveTypeImmediate(canon, items, ref index, end);
            content.Byte(typeCode);
            content.U32(type);
        }
        else if (TypeAndOptionsBuiltins.TryGetValue(name, out var optionsCode))
        {
            var type = ResolveTypeImmediate(canon, items, ref index, end);
            var options = EncodeCanonOptions(items, index, end);
            index = end;
            content.Byte(optionsCode);
            content.U32(type);
            content.Append(options);
        }
        else if (TypeAndAsyncBuiltins.TryGetValue(name, out var asyncCode))
        {
            var type = ResolveTypeImmediate(canon, items, ref index, end);
            content.Byte(asyncCode);
            content.U32(type);
            content.Byte(ReadFlag(items, ref index, end, "async"));
        }
        else if (CancellableBuiltins.TryGetValue(name, out var cancellableCode))
        {
            content.Byte(cancellableCode);
            content.Byte(ReadFlag(items, ref index, end, "cancellable"));
        }
        else
        {
            switch (name)
            {
                case "subtask.cancel":
                    content.Byte(0x06);
                    content.Byte(ReadFlag(items, ref index, end, "async"));
                    break;

                case "task.return":
                {
                    SNode? result = null;
                    if (index < end && items[index].IsListOf("result"))
                    {
                        result = items[index++];
                    }

                    var resultType = new ByteWriter();
                    if (result is null || result.Items!.Count == 1)
                    {
                        resultType.Byte(0x01);
                        resultType.Byte(0x00);
                    }
                    else if (result.Items.Count == 2)
                    {
                        PrepareValueType(result.Items[1]);
                        resultType.Byte(0x00);
                        EncodeValueType(result.Items[1], resultType);
                    }
                    else
                    {
                        throw new WatException("malformed result: expected a type", result.Position);
                    }

                    var options = EncodeCanonOptions(items, index, end);
                    index = end;
                    content.Byte(0x09);
                    content.Append(resultType);
                    content.Append(options);
                    break;
                }

                case "context.get" or "context.set":
                {
                    if (index + 1 >= end || items[index] is not { IsList: false, Kind: TokenKind.Keyword } valueType)
                        throw new WatException("malformed " + name + ": expected a type and an index", canon.Position);

                    content.Byte(name == "context.get" ? (byte)0x0A : (byte)0x0B);
                    CreateCoreTypeEncoder().EncodeCoreValueType(valueType, content);
                    content.U32(ParseIndex(items[index + 1]));
                    index += 2;
                    break;
                }

                case "error-context.new" or "error-context.debug-message":
                {
                    var options = EncodeCanonOptions(items, index, end);
                    index = end;
                    content.Byte(name == "error-context.new" ? (byte)0x1C : (byte)0x1D);
                    content.Append(options);
                    break;
                }

                case "waitable-set.wait" or "waitable-set.poll":
                {
                    if (index >= end || !items[index].IsListOf("memory") || items[index].Items!.Count != 2)
                        throw new WatException("malformed " + name + ": expected a memory", canon.Position);

                    var memory = ResolveReference(items[index].Items![1], Sort.CoreMemory);
                    index++;
                    content.Byte(name == "waitable-set.wait" ? (byte)0x20 : (byte)0x21);
                    content.Byte(ReadFlag(items, ref index, end, "cancellable"));
                    content.U32(memory);
                    break;
                }

                case "thread.new-indirect":
                {
                    if (index + 1 >= end)
                        throw new WatException("malformed thread.new-indirect: expected a type and a table", canon.Position);

                    var type = ResolveReference(items[index], Sort.CoreType);
                    var table = ResolveReference(items[index + 1], Sort.CoreTable);
                    index += 2;
                    content.Byte(0x27);
                    content.U32(type);
                    content.U32(table);
                    break;
                }

                case "thread.spawn-ref" or "thread.spawn-indirect":
                {
                    var shared = ReadFlag(items, ref index, end, "shared");
                    if (index >= end)
                        throw new WatException("malformed " + name + ": expected a type", canon.Position);

                    var type = ResolveReference(items[index++], Sort.CoreType);
                    content.Byte(name == "thread.spawn-ref" ? (byte)0x40 : (byte)0x41);
                    content.Byte(shared);
                    content.U32(type);
                    if (name == "thread.spawn-indirect")
                    {
                        if (index >= end)
                            throw new WatException("malformed thread.spawn-indirect: expected a table", canon.Position);

                        content.U32(ResolveReference(items[index++], Sort.CoreTable));
                    }

                    break;
                }

                case "thread.available-parallelism":
                    content.Byte(0x42);
                    content.Byte(ReadFlag(items, ref index, end, "shared"));
                    break;

                default:
                    throw new WatException("unknown canonical definition " + name, canon.Position);
            }
        }

        if (index != end)
            throw new WatException("malformed canonical definition " + name, items[index].Position);
    }

    private uint ResolveTypeImmediate(SNode canon, List<SNode> items, ref int index, int end)
    {
        if (index >= end)
            throw new WatException("malformed canonical definition: expected a type", canon.Position);

        return ResolveTypeIndex(items[index++]);
    }

    private static byte ReadFlag(List<SNode> items, ref int index, int end, string keyword)
    {
        if (index < end && items[index].IsKeyword(keyword))
        {
            index++;
            return 0x01;
        }

        return 0x00;
    }

    /// <summary>Encodes the options of a canonical definition: <c>vec(canonopt)</c>.</summary>
    private ByteWriter EncodeCanonOptions(List<SNode> items, int start, int end)
    {
        var options = new ByteWriter();
        for (var i = start; i < end; i++)
        {
            var option = items[i];
            if (!option.IsList)
            {
                options.Byte(option.Text switch
                {
                    "string-encoding=utf8" => 0x00,
                    "string-encoding=utf16" => 0x01,
                    "string-encoding=latin1+utf16" or "string-encoding=compact-utf16" => 0x02,
                    "async" => 0x06,
                    _ => throw new WatException("unknown canonical option " + option, option.Position),
                });
                continue;
            }

            var optionItems = option.Items!;
            if (optionItems.Count != 2)
                throw new WatException("malformed canonical option", option.Position);

            switch (option.Head)
            {
                case "memory":
                    options.Byte(0x03);
                    options.U32(ResolveReference(optionItems[1], Sort.CoreMemory));
                    break;

                case "realloc":
                    options.Byte(0x04);
                    options.U32(ResolveReference(optionItems[1], Sort.CoreFunc));
                    break;

                case "post-return":
                    options.Byte(0x05);
                    options.U32(ResolveReference(optionItems[1], Sort.CoreFunc));
                    break;

                case "callback":
                    options.Byte(0x07);
                    options.U32(ResolveReference(optionItems[1], Sort.CoreFunc));
                    break;

                default:
                    throw new WatException("unknown canonical option " + option.Head, option.Position);
            }
        }

        var result = new ByteWriter();
        result.Vector(options, end - start);
        return result;
    }

    /// <summary>Encodes <c>(start funcidx (value validx)* (result (value $id?))*)</c>.</summary>
    private void EncodeStart(SNode field)
    {
        var items = field.Items!;
        if (items.Count < 2)
            throw new WatException("malformed start", field.Position);

        var function = ResolveReference(items[1], Sort.Func);
        var arguments = new ByteWriter();
        var argumentCount = 0;
        var index = 2;
        for (; index < items.Count && items[index].IsListOf("value"); index++)
        {
            if (items[index].Items!.Count != 2)
                throw new WatException("malformed start argument", items[index].Position);

            arguments.U32(ResolveReference(items[index].Items![1], Sort.Value));
            argumentCount++;
        }

        var results = new List<string?>();
        for (; index < items.Count && items[index].IsListOf("result"); index++)
        {
            var result = items[index].Items!;
            if (result.Count != 2 || !result[1].IsListOf("value"))
                throw new WatException("malformed start result", items[index].Position);

            var valueIndex = 1;
            results.Add(ReadId(result[1].Items!, ref valueIndex));
        }

        if (index != items.Count)
            throw new WatException("malformed start", field.Position);

        var content = new ByteWriter();
        content.U32(function);
        content.Vector(arguments, argumentCount);
        content.U32((uint)results.Count);
        _scope.Add(ItemKind.Start, content, field.Position);
        foreach (var result in results)
        {
            _scope[Sort.Value].Add(result, Sort.Value, field.Position);
        }
    }

    /// <summary>Encodes <c>(value $id? valtype val)</c>: the value is <c>(binary "..."*)</c>, or a value of a primitive type.</summary>
    private uint EncodeValue(SNode field, List<SNode> items, int start, string? id)
    {
        if (start != items.Count - 2)
            throw new WatException("malformed value", field.Position);

        var type = items[start];
        var value = items[start + 1];
        PrepareValueType(type);
        var content = new ByteWriter();
        EncodeValueType(type, content);
        var bytes = new ByteWriter();
        if (value.IsListOf("binary"))
        {
            bytes.Bytes(ConcatenateStrings(value.Items!, 1));
        }
        else
        {
            EncodePrimitiveValue(type, value, bytes);
        }

        content.U32((uint)bytes.Length);
        content.Append(bytes);
        _scope.Add(ItemKind.Value, content, field.Position);
        return _scope[Sort.Value].Add(id, Sort.Value, field.Position);
    }

    private static void EncodePrimitiveValue(SNode type, SNode value, ByteWriter writer)
    {
        if (type is not { IsList: false, Kind: TokenKind.Keyword } || value.IsList)
            throw new WatException("unsupported value: only the values of the primitive types, and (binary ...), are supported", value.Position);

        switch (type.Text)
        {
            case "bool":
                writer.Byte(value.Text switch
                {
                    "true" => 1,
                    "false" => 0,
                    _ => throw new WatException("malformed bool value", value.Position),
                });
                break;

            case "s8" or "u8" or "s16" or "u16" or "s32" or "u32" or "s64" or "u64":
            {
                var bits = int.Parse(type.Text.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
                if (!WatNumbers.TryParseInteger(value.Text, bits, out var integer))
                    throw new WatException("malformed integer value", value.Position);

                if (bits == 8)
                {
                    writer.Byte((byte)integer);
                }
                else if (type.Text[0] == 's')
                {
                    writer.S64(integer);
                }
                else
                {
                    writer.U32(bits == 64 ? (ulong)integer : (ulong)integer & ((1UL << bits) - 1));
                }

                break;
            }

            case "f32":
            {
                if (!WatNumbers.TryParseF32(value.Text, out var bits))
                    throw new WatException("malformed f32 value", value.Position);

                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, bits);
                writer.Bytes(buffer);
                break;
            }

            case "f64":
            {
                if (!WatNumbers.TryParseF64(value.Text, out var bits))
                    throw new WatException("malformed f64 value", value.Position);

                Span<byte> buffer = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, bits);
                writer.Bytes(buffer);
                break;
            }

            case "char" when value.Kind == TokenKind.String:
                writer.Bytes(value.Bytes);
                break;

            case "string" when value.Kind == TokenKind.String:
                WriteName(value, writer);
                break;

            default:
                throw new WatException("unsupported value of type " + type.Text, value.Position);
        }
    }
}
