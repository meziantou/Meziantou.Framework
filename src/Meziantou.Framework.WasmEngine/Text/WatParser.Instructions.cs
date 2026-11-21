namespace Meziantou.Framework.WasmEngine.Text;

internal sealed partial class WatParser
{
    /// <summary>The locals and the labels of the code being encoded.</summary>
    private sealed class FunctionContext(Dictionary<string, int> locals)
    {
        public Dictionary<string, int> Locals { get; } = locals;

        public List<string?> Labels { get; } = [];

        public int LocalCount { get; set; }
    }

    private void EncodeFunctionBody(List<SNode> items, int index, int typeIndex, List<string?> parameterNames)
    {
        _ = typeIndex;
        var locals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < parameterNames.Count; i++)
        {
            if (parameterNames[i] is { } name && !locals.TryAdd(name, i))
                throw new WatException("duplicate local $" + name, items[0].Position);
        }

        var context = new FunctionContext(locals) { LocalCount = parameterNames.Count };
        var localTypes = new List<SNode>();
        while (index < items.Count && items[index].IsListOf("local"))
        {
            var localItems = items[index].Items!;
            var nameIndex = 1;
            var name = GetId(localItems, ref nameIndex);
            if (name is not null)
            {
                if (localItems.Count != 3)
                    throw new WatException("malformed local", items[index].Position);

                if (!locals.TryAdd(name, context.LocalCount))
                    throw new WatException("duplicate local $" + name, items[index].Position);

                localTypes.Add(localItems[2]);
                context.LocalCount++;
            }
            else
            {
                for (var i = 1; i < localItems.Count; i++)
                {
                    localTypes.Add(localItems[i]);
                    context.LocalCount++;
                }
            }

            index++;
        }

        var body = new ByteWriter();

        // The locals, compressed in runs of the same type
        var runs = new ByteWriter();
        var runCount = 0;
        var position = 0;
        while (position < localTypes.Count)
        {
            var type = new ByteWriter();
            WriteValType(localTypes[position], type);
            var encoding = type.ToArray();
            var length = 1;
            while (position + length < localTypes.Count)
            {
                var next = new ByteWriter();
                WriteValType(localTypes[position + length], next);
                if (!next.ToArray().AsSpan().SequenceEqual(encoding))
                    break;

                length++;
            }

            runs.U32((uint)length);
            runs.Bytes(encoding);
            runCount++;
            position += length;
        }

        body.Vector(runs, runCount);
        while (index < items.Count)
        {
            EncodeInstruction(items, ref index, context, body);
        }

        body.Byte(0x0B);
        _code.U32((uint)body.Length);
        _code.Append(body);
    }

    private static int ResolveLabel(SNode node, FunctionContext context)
    {
        if (!node.IsList && node.Kind == TokenKind.Id)
        {
            for (var i = context.Labels.Count - 1; i >= 0; i--)
            {
                if (context.Labels[i] == node.Text)
                    return context.Labels.Count - 1 - i;
            }

            throw new WatException("unknown label $" + node.Text, node.Position);
        }

        if (!node.IsList && node.Kind == TokenKind.Number && WatNumbers.TryParseUnsigned(node.Text, uint.MaxValue, out var depth))
            return (int)Math.Min(depth, int.MaxValue);

        throw new WatException("expected a label", node.Position);
    }

    private static int ResolveLocal(SNode node, FunctionContext context)
    {
        if (!node.IsList && node.Kind == TokenKind.Id)
        {
            if (!context.Locals.TryGetValue(node.Text, out var index))
                throw new WatException("unknown local $" + node.Text, node.Position);

            return index;
        }

        if (!node.IsList && node.Kind == TokenKind.Number && WatNumbers.TryParseUnsigned(node.Text, uint.MaxValue, out var value))
            return (int)Math.Min(value, int.MaxValue);

        throw new WatException("expected a local", node.Position);
    }

    /// <summary>Writes a block type: <c>(type x)? (param t)* (result t)*</c>.</summary>
    private void WriteBlockType(List<SNode> items, ref int index, ByteWriter writer)
    {
        if (index < items.Count && items[index].IsListOf("type"))
        {
            var (typeIndex, _) = ParseTypeUse(items, ref index);
            writer.S64(typeIndex);
            return;
        }

        var (parameters, results, names) = ParseSignature(items, index, allowNames: false, out var end);
        if (names.Any(name => name is not null))
            throw new WatException("malformed block type: named parameter", items[index].Position);

        index = end;
        if (parameters.Count == 0 && results.Count == 0)
        {
            writer.Byte(0x40);
        }
        else if (parameters.Count == 0 && results.Count == 1)
        {
            WriteValType(results[0], writer);
        }
        else
        {
            writer.S64(GetImplicitType(parameters, results));
        }
    }

    private static string? ReadLabelName(List<SNode> items, ref int index) => GetId(items, ref index);

    /// <summary>Checks the optional label after <c>end</c>, <c>else</c>, <c>catch</c>...: it must be the label of the block.</summary>
    private static void CheckEndLabel(List<SNode> items, ref int index, string? label)
    {
        if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } id)
        {
            if (id.Text != label)
                throw new WatException("mismatching label", id.Position);

            index++;
        }
    }

    private static bool IsBlockEnd(SNode node) => !node.IsList && node.Kind == TokenKind.Keyword && node.Text is "end" or "else" or "catch" or "catch_all" or "delegate";

    /// <summary>Encodes the instruction at <paramref name="index"/> (plain or folded) and moves after it.</summary>
    private void EncodeInstruction(List<SNode> items, ref int index, FunctionContext context, ByteWriter writer)
    {
        var node = items[index];
        if (node.IsList)
        {
            EncodeFolded(node, context, writer);
            index++;
            return;
        }

        if (node.Kind != TokenKind.Keyword)
            throw new WatException("unexpected token " + node, node.Position);

        index++;
        switch (node.Text)
        {
            case "block" or "loop":
            {
                var label = ReadLabelName(items, ref index);
                writer.Byte(node.Text == "block" ? (byte)0x02 : (byte)0x03);
                WriteBlockType(items, ref index, writer);
                context.Labels.Add(label);
                EncodePlainBody(items, ref index, context, writer);
                ExpectKeyword(items, ref index, "end", node);
                CheckEndLabel(items, ref index, label);
                context.Labels.RemoveAt(context.Labels.Count - 1);
                writer.Byte(0x0B);
                return;
            }

            case "if":
            {
                var label = ReadLabelName(items, ref index);
                writer.Byte(0x04);
                WriteBlockType(items, ref index, writer);
                context.Labels.Add(label);
                EncodePlainBody(items, ref index, context, writer);
                if (index < items.Count && items[index].IsKeyword("else"))
                {
                    index++;
                    CheckEndLabel(items, ref index, label);
                    writer.Byte(0x05);
                    EncodePlainBody(items, ref index, context, writer);
                }

                ExpectKeyword(items, ref index, "end", node);
                CheckEndLabel(items, ref index, label);
                context.Labels.RemoveAt(context.Labels.Count - 1);
                writer.Byte(0x0B);
                return;
            }

            case "try_table":
            {
                var label = ReadLabelName(items, ref index);
                writer.Byte(0x1F);
                WriteBlockType(items, ref index, writer);
                WriteCatchClauses(items, ref index, context, writer);
                context.Labels.Add(label);
                EncodePlainBody(items, ref index, context, writer);
                ExpectKeyword(items, ref index, "end", node);
                CheckEndLabel(items, ref index, label);
                context.Labels.RemoveAt(context.Labels.Count - 1);
                writer.Byte(0x0B);
                return;
            }

            case "try":
            {
                // The legacy exception handling: try bt instr* (catch x instr*)* (catch_all instr*)? end, or try bt instr* delegate l
                var label = ReadLabelName(items, ref index);
                writer.Byte(0x06);
                WriteBlockType(items, ref index, writer);
                context.Labels.Add(label);
                EncodePlainBody(items, ref index, context, writer);
                while (index < items.Count && items[index].IsKeyword("catch"))
                {
                    index++;
                    writer.Byte(0x07);
                    writer.U32((uint)ResolveIndex(items[index++], _tagSpace));
                    EncodePlainBody(items, ref index, context, writer);
                }

                if (index < items.Count && items[index].IsKeyword("catch_all"))
                {
                    index++;
                    writer.Byte(0x19);
                    EncodePlainBody(items, ref index, context, writer);
                }

                context.Labels.RemoveAt(context.Labels.Count - 1);
                if (index < items.Count && items[index].IsKeyword("delegate"))
                {
                    index++;
                    writer.Byte(0x18);
                    writer.U32((uint)ResolveLabel(items[index++], context));
                    return;
                }

                ExpectKeyword(items, ref index, "end", node);
                CheckEndLabel(items, ref index, label);
                writer.Byte(0x0B);
                return;
            }
        }

        if (!WatInstructions.TryGet(node.Text, out var info))
            throw new WatException("unknown operator " + node.Text, node.Position);

        EncodePlainInstruction(node, info, items, ref index, context, writer);
    }

    private static void ExpectKeyword(List<SNode> items, ref int index, string keyword, SNode start)
    {
        if (index >= items.Count || !items[index].IsKeyword(keyword))
            throw new WatException("expected " + keyword, index < items.Count ? items[index].Position : start.Position);

        index++;
    }

    private void EncodePlainBody(List<SNode> items, ref int index, FunctionContext context, ByteWriter writer)
    {
        while (index < items.Count && !IsBlockEnd(items[index]))
        {
            EncodeInstruction(items, ref index, context, writer);
        }
    }

    private void WriteCatchClauses(List<SNode> items, ref int index, FunctionContext context, ByteWriter writer)
    {
        var clauses = new ByteWriter();
        var count = 0;
        while (index < items.Count && items[index].Head is "catch" or "catch_ref" or "catch_all" or "catch_all_ref")
        {
            var clause = items[index].Items!;
            switch (clause[0].Text)
            {
                case "catch" or "catch_ref":
                    if (clause.Count != 3)
                        throw new WatException("malformed catch clause", items[index].Position);

                    clauses.Byte(clause[0].Text == "catch" ? (byte)0 : (byte)1);
                    clauses.U32((uint)ResolveIndex(clause[1], _tagSpace));
                    clauses.U32((uint)ResolveLabel(clause[2], context));
                    break;
                default:
                    if (clause.Count != 2)
                        throw new WatException("malformed catch clause", items[index].Position);

                    clauses.Byte(clause[0].Text == "catch_all" ? (byte)2 : (byte)3);
                    clauses.U32((uint)ResolveLabel(clause[1], context));
                    break;
            }

            count++;
            index++;
        }

        writer.Vector(clauses, count);
    }

    private void EncodeFolded(SNode node, FunctionContext context, ByteWriter writer)
    {
        var items = node.Items!;
        if (items.Count == 0 || items[0] is not { IsList: false, Kind: TokenKind.Keyword } head)
            throw new WatException("malformed folded instruction", node.Position);

        var index = 1;
        switch (head.Text)
        {
            case "block" or "loop":
            {
                var label = ReadLabelName(items, ref index);
                writer.Byte(head.Text == "block" ? (byte)0x02 : (byte)0x03);
                WriteBlockType(items, ref index, writer);
                context.Labels.Add(label);
                while (index < items.Count)
                {
                    EncodeInstruction(items, ref index, context, writer);
                }

                context.Labels.RemoveAt(context.Labels.Count - 1);
                writer.Byte(0x0B);
                return;
            }

            case "if":
            {
                var label = ReadLabelName(items, ref index);
                var blockType = new ByteWriter();
                WriteBlockType(items, ref index, blockType);

                // The condition: the folded instructions before (then ...)
                while (index < items.Count && !items[index].IsListOf("then"))
                {
                    if (!items[index].IsList)
                        throw new WatException("unexpected token", items[index].Position);

                    EncodeInstruction(items, ref index, context, writer);
                }

                if (index >= items.Count)
                    throw new WatException("expected (then ...)", node.Position);

                writer.Byte(0x04);
                writer.Append(blockType);
                context.Labels.Add(label);
                var then = items[index++].Items!;
                var thenIndex = 1;
                while (thenIndex < then.Count)
                {
                    EncodeInstruction(then, ref thenIndex, context, writer);
                }

                if (index < items.Count && items[index].IsListOf("else"))
                {
                    writer.Byte(0x05);
                    var otherwise = items[index++].Items!;
                    var elseIndex = 1;
                    while (elseIndex < otherwise.Count)
                    {
                        EncodeInstruction(otherwise, ref elseIndex, context, writer);
                    }
                }

                if (index != items.Count)
                    throw new WatException("malformed if", node.Position);

                context.Labels.RemoveAt(context.Labels.Count - 1);
                writer.Byte(0x0B);
                return;
            }

            case "try_table":
            {
                var label = ReadLabelName(items, ref index);
                writer.Byte(0x1F);
                WriteBlockType(items, ref index, writer);
                WriteCatchClauses(items, ref index, context, writer);
                context.Labels.Add(label);
                while (index < items.Count)
                {
                    EncodeInstruction(items, ref index, context, writer);
                }

                context.Labels.RemoveAt(context.Labels.Count - 1);
                writer.Byte(0x0B);
                return;
            }

            case "try":
            {
                var label = ReadLabelName(items, ref index);
                writer.Byte(0x06);
                WriteBlockType(items, ref index, writer);
                context.Labels.Add(label);
                if (index < items.Count && items[index].IsListOf("do"))
                {
                    var body = items[index++].Items!;
                    var bodyIndex = 1;
                    while (bodyIndex < body.Count)
                    {
                        EncodeInstruction(body, ref bodyIndex, context, writer);
                    }
                }

                var delegated = false;
                while (index < items.Count)
                {
                    var clause = items[index++];
                    var clauseItems = clause.Items ?? throw new WatException("malformed try", clause.Position);
                    var clauseIndex = 1;
                    switch (clause.Head)
                    {
                        case "catch":
                            writer.Byte(0x07);
                            writer.U32((uint)ResolveIndex(clauseItems[clauseIndex++], _tagSpace));
                            break;
                        case "catch_all":
                            writer.Byte(0x19);
                            break;
                        case "delegate":
                            context.Labels.RemoveAt(context.Labels.Count - 1);
                            writer.Byte(0x18);
                            writer.U32((uint)ResolveLabel(clauseItems[1], context));
                            delegated = true;
                            continue;
                        default:
                            throw new WatException("malformed try", clause.Position);
                    }

                    while (clauseIndex < clauseItems.Count)
                    {
                        EncodeInstruction(clauseItems, ref clauseIndex, context, writer);
                    }
                }

                if (!delegated)
                {
                    context.Labels.RemoveAt(context.Labels.Count - 1);
                    writer.Byte(0x0B);
                }

                return;
            }
        }

        if (!WatInstructions.TryGet(head.Text, out var info))
            throw new WatException("unknown operator " + head.Text, head.Position);

        // The immediates follow the operator, then the operands are folded instructions
        var instruction = new ByteWriter();
        EncodePlainInstruction(head, info, items, ref index, context, instruction);
        while (index < items.Count)
        {
            if (!items[index].IsList)
                throw new WatException("unexpected token " + items[index] + " in a folded instruction", items[index].Position);

            EncodeInstruction(items, ref index, context, writer);
        }

        writer.Append(instruction);
    }

    private static void WriteOpcode(InstructionInfo info, ByteWriter writer, uint? code = null)
    {
        if (info.Prefix != 0)
        {
            writer.Byte(info.Prefix);
            writer.U32(code ?? info.Code);
        }
        else
        {
            writer.Byte((byte)(code ?? info.Code));
        }
    }

    private static bool IsAtom(List<SNode> items, int index) => index < items.Count && !items[index].IsList;

    private void EncodePlainInstruction(SNode node, InstructionInfo info, List<SNode> items, ref int index, FunctionContext context, ByteWriter writer)
    {
        switch (info.Immediate)
        {
            case Immediate.None:
                WriteOpcode(info, writer);
                return;

            case Immediate.Select:
            {
                var types = new List<SNode>();
                while (index < items.Count && items[index].IsListOf("result"))
                {
                    types.AddRange(items[index].Items!.Skip(1));
                    index++;
                }

                if (types.Count == 0)
                {
                    writer.Byte(0x1B);
                }
                else
                {
                    writer.Byte(0x1C);
                    WriteTypes(types, writer);
                }

                return;
            }

            case Immediate.RefTest:
            {
                if (index >= items.Count)
                    throw new WatException("expected a reference type", node.Position);

                var type = new ByteWriter();
                var nullable = WriteRefType(items[index++], type);
                var bytes = type.ToArray();
                WriteOpcode(info, writer, info.Code + (nullable ? 1u : 0u));

                // The heap type of the reference type (the shorthands are their heap type)
                writer.Bytes(bytes[0] is 0x63 or 0x64 ? bytes.AsSpan(1) : bytes);
                return;
            }

            case Immediate.BrOnCast:
            {
                if (index + 2 >= items.Count)
                    throw new WatException("malformed br_on_cast", node.Position);

                var label = ResolveLabel(items[index++], context);
                var first = new ByteWriter();
                var second = new ByteWriter();
                var nullable1 = WriteRefType(items[index++], first);
                var nullable2 = WriteRefType(items[index++], second);
                WriteOpcode(info, writer);
                writer.Byte((byte)((nullable1 ? 1 : 0) | (nullable2 ? 2 : 0)));
                writer.U32((uint)label);
                var firstBytes = first.ToArray();
                var secondBytes = second.ToArray();
                writer.Bytes(firstBytes[0] is 0x63 or 0x64 ? firstBytes.AsSpan(1) : firstBytes);
                writer.Bytes(secondBytes[0] is 0x63 or 0x64 ? secondBytes.AsSpan(1) : secondBytes);
                return;
            }
        }

        WriteOpcode(info, writer);
        switch (info.Immediate)
        {
            case Immediate.Label:
                writer.U32((uint)ResolveLabel(Next(items, ref index, node), context));
                break;

            case Immediate.BrTable:
            {
                var labels = new List<int>();
                while (IsAtom(items, index) && items[index].Kind is TokenKind.Id or TokenKind.Number)
                {
                    labels.Add(ResolveLabel(items[index++], context));
                }

                if (labels.Count == 0)
                    throw new WatException("br_table requires a label", node.Position);

                writer.U32((uint)(labels.Count - 1));
                foreach (var label in labels)
                {
                    writer.U32((uint)label);
                }

                break;
            }

            case Immediate.Function:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _functionSpace));
                break;

            case Immediate.CallIndirect:
            {
                var table = 0;
                if (IsAtom(items, index) && IsIndex(items[index]))
                {
                    table = ResolveIndex(items[index++], _tableSpace);
                }

                var (typeIndex, names) = ParseTypeUse(items, ref index);
                if (names.Any(name => name is not null))
                    throw new WatException("malformed call_indirect: named parameter", node.Position);

                writer.U32((uint)typeIndex);
                writer.U32((uint)table);
                break;
            }

            case Immediate.Type:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _typeSpace));
                break;

            case Immediate.Local:
                writer.U32((uint)ResolveLocal(Next(items, ref index, node), context));
                break;

            case Immediate.Global:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _globalSpace));
                break;

            case Immediate.Tag:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _tagSpace));
                break;

            case Immediate.Data:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _dataSpace));
                break;

            case Immediate.Element:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _elementSpace));
                break;

            case Immediate.Table:
                writer.U32((uint)(IsAtom(items, index) && IsIndex(items[index]) ? ResolveIndex(items[index++], _tableSpace) : 0));
                break;

            case Immediate.TableCopy:
                if (IsAtom(items, index) && IsIndex(items[index]))
                {
                    writer.U32((uint)ResolveIndex(items[index++], _tableSpace));
                    writer.U32((uint)ResolveIndex(Next(items, ref index, node), _tableSpace));
                }
                else
                {
                    writer.U32(0);
                    writer.U32(0);
                }

                break;

            case Immediate.TableInit:
            {
                var first = Next(items, ref index, node);
                if (IsAtom(items, index) && IsIndex(items[index]))
                {
                    writer.U32((uint)ResolveIndex(items[index++], _elementSpace));
                    writer.U32((uint)ResolveIndex(first, _tableSpace));
                }
                else
                {
                    writer.U32((uint)ResolveIndex(first, _elementSpace));
                    writer.U32(0);
                }

                break;
            }

            case Immediate.Memory:
                writer.U32((uint)(IsAtom(items, index) && IsIndex(items[index]) ? ResolveIndex(items[index++], _memorySpace) : 0));
                break;

            case Immediate.MemoryCopy:
                if (IsAtom(items, index) && IsIndex(items[index]))
                {
                    writer.U32((uint)ResolveIndex(items[index++], _memorySpace));
                    writer.U32((uint)ResolveIndex(Next(items, ref index, node), _memorySpace));
                }
                else
                {
                    writer.U32(0);
                    writer.U32(0);
                }

                break;

            case Immediate.MemoryInit:
            {
                var first = Next(items, ref index, node);
                if (IsAtom(items, index) && IsIndex(items[index]))
                {
                    writer.U32((uint)ResolveIndex(items[index++], _dataSpace));
                    writer.U32((uint)ResolveIndex(first, _memorySpace));
                }
                else
                {
                    writer.U32((uint)ResolveIndex(first, _dataSpace));
                    writer.U32(0);
                }

                break;
            }

            case Immediate.MemArg:
                WriteMemArg(items, ref index, info.Alignment, writer);
                break;

            case Immediate.MemArgLane:
            {
                // memory? memarg lane: a number followed by another number or by offset= / align= is the memory
                var memory = 0;
                if (IsAtom(items, index) && items[index].Kind == TokenKind.Id)
                {
                    memory = ResolveIndex(items[index++], _memorySpace);
                }
                else if (IsAtom(items, index) && items[index].Kind == TokenKind.Number && IsAtom(items, index + 1) && (items[index + 1].Kind == TokenKind.Number || items[index + 1].Text.StartsWith("offset=", StringComparison.Ordinal) || items[index + 1].Text.StartsWith("align=", StringComparison.Ordinal)))
                {
                    memory = ResolveIndex(items[index++], _memorySpace);
                }

                WriteMemArg(items, ref index, info.Alignment, writer, memory, readMemory: false);
                writer.Byte(ReadLane(Next(items, ref index, node)));
                break;
            }

            case Immediate.I32:
            {
                var token = Next(items, ref index, node);
                if (!WatNumbers.TryParseInteger(token.Text, 32, out var value))
                    throw new WatException("constant out of range", token.Position);

                writer.S64((int)value);
                break;
            }

            case Immediate.I64:
            {
                var token = Next(items, ref index, node);
                if (!WatNumbers.TryParseInteger(token.Text, 64, out var value))
                    throw new WatException("constant out of range", token.Position);

                writer.S64(value);
                break;
            }

            case Immediate.F32:
            {
                var token = Next(items, ref index, node);
                if (!WatNumbers.TryParseF32(token.Text, out var bits))
                    throw new WatException("constant out of range", token.Position);

                writer.Bytes(BitConverter.GetBytes(bits));
                break;
            }

            case Immediate.F64:
            {
                var token = Next(items, ref index, node);
                if (!WatNumbers.TryParseF64(token.Text, out var bits))
                    throw new WatException("constant out of range", token.Position);

                writer.Bytes(BitConverter.GetBytes(bits));
                break;
            }

            case Immediate.V128:
                writer.Bytes(ParseV128(items, ref index, node));
                break;

            case Immediate.Shuffle:
                for (var i = 0; i < 16; i++)
                {
                    writer.Byte(ReadLane(Next(items, ref index, node)));
                }

                break;

            case Immediate.Lane:
                writer.Byte(ReadLane(Next(items, ref index, node)));
                break;

            case Immediate.HeapType:
                WriteHeapType(Next(items, ref index, node), writer);
                break;

            case Immediate.StructField:
            {
                var typeNode = Next(items, ref index, node);
                var typeIndex = ResolveIndex(typeNode, _typeSpace);
                writer.U32((uint)typeIndex);
                var field = Next(items, ref index, node);
                if (!field.IsList && field.Kind == TokenKind.Id)
                {
                    if (typeIndex >= _types.Count || !_types[typeIndex].FieldNames.TryGetValue(field.Text, out var fieldIndex))
                        throw new WatException("unknown field $" + field.Text, field.Position);

                    writer.U32((uint)fieldIndex);
                }
                else
                {
                    writer.U32((uint)ResolveIndex(field, _typeSpace));
                }

                break;
            }

            case Immediate.TwoTypes:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _typeSpace));
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _typeSpace));
                break;

            case Immediate.TypeAndCount:
            {
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _typeSpace));
                var count = Next(items, ref index, node);
                if (!WatNumbers.TryParseUnsigned(count.Text, uint.MaxValue, out var value))
                    throw new WatException("malformed count", count.Position);

                writer.U32(value);
                break;
            }

            case Immediate.TypeAndData:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _typeSpace));
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _dataSpace));
                break;

            case Immediate.TypeAndElement:
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _typeSpace));
                writer.U32((uint)ResolveIndex(Next(items, ref index, node), _elementSpace));
                break;

            case Immediate.Fence:
                writer.Byte(0);
                break;

            default:
                throw new WatException("unsupported immediate", node.Position);
        }
    }

    private static SNode Next(List<SNode> items, ref int index, SNode instruction)
    {
        if (index >= items.Count)
            throw new WatException("missing immediate of " + instruction, instruction.Position);

        return items[index++];
    }

    private static byte ReadLane(SNode node)
    {
        if (node.IsList || !WatNumbers.TryParseUnsigned(node.Text, 255, out var lane))
            throw new WatException("malformed lane index", node.Position);

        return (byte)lane;
    }

    private void WriteMemArg(List<SNode> items, ref int index, int naturalAlignment, ByteWriter writer, int memory = -1, bool readMemory = true)
    {
        if (memory < 0 && readMemory)
        {
            memory = IsAtom(items, index) && IsIndex(items[index]) ? ResolveIndex(items[index++], _memorySpace) : 0;
        }

        ulong offset = 0;
        var alignment = naturalAlignment;
        if (IsAtom(items, index) && items[index].Text.StartsWith("offset=", StringComparison.Ordinal))
        {
            var text = items[index].Text["offset=".Length..];
            if (!WatNumbers.TryParseUnsigned(text, ulong.MaxValue, out offset))
                throw new WatException("malformed offset", items[index].Position);

            index++;
        }

        if (IsAtom(items, index) && items[index].Text.StartsWith("align=", StringComparison.Ordinal))
        {
            var text = items[index].Text["align=".Length..];
            if (!WatNumbers.TryParseUnsigned(text, ulong.MaxValue, out var value) || value == 0 || (value & (value - 1)) != 0)
                throw new WatException("alignment must be a power of two", items[index].Position);

            alignment = System.Numerics.BitOperations.Log2(value);
            index++;
        }

        if (memory != 0)
        {
            writer.U32((uint)alignment | 0x40);
            writer.U32((uint)memory);
        }
        else
        {
            writer.U32((uint)alignment);
        }

        writer.U32(offset);
    }

    private static byte[] ParseV128(List<SNode> items, ref int index, SNode instruction)
    {
        var shape = Next(items, ref index, instruction);
        var result = new byte[16];
        var (count, size) = shape.Text switch
        {
            "i8x16" => (16, 1),
            "i16x8" => (8, 2),
            "i32x4" or "f32x4" => (4, 4),
            "i64x2" or "f64x2" => (2, 8),
            _ => throw new WatException("malformed lane shape", shape.Position),
        };
        for (var i = 0; i < count; i++)
        {
            var token = Next(items, ref index, instruction);
            ulong bits;
            if (shape.Text == "f32x4")
            {
                if (!WatNumbers.TryParseF32(token.Text, out var f32))
                    throw new WatException("constant out of range", token.Position);

                bits = f32;
            }
            else if (shape.Text == "f64x2")
            {
                if (!WatNumbers.TryParseF64(token.Text, out bits))
                    throw new WatException("constant out of range", token.Position);
            }
            else
            {
                if (!WatNumbers.TryParseInteger(token.Text, size * 8, out var value))
                    throw new WatException("constant out of range", token.Position);

                bits = (ulong)value;
            }

            for (var b = 0; b < size; b++)
            {
                result[(i * size) + b] = (byte)(bits >> (8 * b));
            }
        }

        return result;
    }
}
