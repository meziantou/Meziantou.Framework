namespace Meziantou.Framework.WasmEngine;

internal sealed partial class FunctionCompiler
{
    private void CompileGc(ref WasmReader reader)
    {
        var opcode = reader.ReadU32();
        switch (opcode)
        {
            case 0: // struct.new
            {
                var type = ReadStructType(ref reader);
                for (var i = type.Fields.Length - 1; i >= 0; i--)
                {
                    Pop(type.Fields[i].Type.Unpacked);
                }

                Push(new ValType(nullable: false, type));
                Emit(Op.StructNew, o: type);
                break;
            }

            case 1: // struct.new_default
            {
                var type = ReadStructType(ref reader);
                foreach (var field in type.Fields)
                {
                    if (!field.Type.IsDefaultable)
                        throw Error("type mismatch: struct.new_default requires defaultable fields");
                }

                Push(new ValType(nullable: false, type));
                Emit(Op.StructNewDefault, o: type);
                break;
            }

            case 2 or 3 or 4: // struct.get, struct.get_s, struct.get_u
            {
                var type = ReadStructType(ref reader);
                var fieldIndex = ReadIndex(ref reader);
                if ((uint)fieldIndex >= (uint)type.Fields.Length)
                    throw Error("unknown field " + fieldIndex);

                var field = type.Fields[fieldIndex];
                if (field.Type.IsPacked != (opcode != 2))
                    throw Error("type mismatch: " + (opcode == 2 ? "struct.get requires an unpacked field" : "struct.get_s and struct.get_u require a packed field"));

                Pop(new ValType(nullable: true, type));
                Push(field.Type.Unpacked);
                Emit((ushort)(Op.GcPrefix + opcode), i0: fieldIndex, o: type);
                break;
            }

            case 5: // struct.set
            {
                var type = ReadStructType(ref reader);
                var fieldIndex = ReadIndex(ref reader);
                if ((uint)fieldIndex >= (uint)type.Fields.Length)
                    throw Error("unknown field " + fieldIndex);

                var field = type.Fields[fieldIndex];
                if (!field.Mutable)
                    throw Error("field is immutable");

                Pop(field.Type.Unpacked);
                Pop(new ValType(nullable: true, type));
                Emit(Op.StructSet, i0: fieldIndex, o: type);
                break;
            }

            case 6: // array.new
            {
                var type = ReadArrayType(ref reader);
                Pop(ValType.I32);
                Pop(type.Element.Type.Unpacked);
                Push(new ValType(nullable: false, type));
                Emit(Op.ArrayNew, o: type);
                break;
            }

            case 7: // array.new_default
            {
                var type = ReadArrayType(ref reader);
                if (!type.Element.Type.IsDefaultable)
                    throw Error("type mismatch: array.new_default requires a defaultable element type");

                Pop(ValType.I32);
                Push(new ValType(nullable: false, type));
                Emit(Op.ArrayNewDefault, o: type);
                break;
            }

            case 8: // array.new_fixed
            {
                var type = ReadArrayType(ref reader);
                var count = reader.ReadU32();
                if (count > 10000)
                    throw Error("array.new_fixed has too many operands");

                for (var i = 0; i < count; i++)
                {
                    Pop(type.Element.Type.Unpacked);
                }

                Push(new ValType(nullable: false, type));
                Emit(Op.ArrayNewFixed, i0: (int)count, o: type);
                break;
            }

            case 9: // array.new_data
            {
                var type = ReadArrayType(ref reader);
                var dataIndex = ReadIndex(ref reader);
                if (type.Element.Type.IsRef)
                    throw Error("type mismatch: array.new_data requires a numeric or vector element type");

                CheckData(dataIndex);
                Pop(ValType.I32);
                Pop(ValType.I32);
                Push(new ValType(nullable: false, type));
                Emit(Op.ArrayNewData, i0: dataIndex, o: type);
                break;
            }

            case 10: // array.new_elem
            {
                var type = ReadArrayType(ref reader);
                var elementIndex = ReadIndex(ref reader);
                var segment = GetElement(elementIndex);
                if (!TypeRelations.IsSubtype(segment.Type, type.Element.Type))
                    throw Error("type mismatch: array.new_elem element segment does not match the array type");

                Pop(ValType.I32);
                Pop(ValType.I32);
                Push(new ValType(nullable: false, type));
                Emit(Op.ArrayNewElem, i0: elementIndex, o: type);
                break;
            }

            case 11 or 12 or 13: // array.get, array.get_s, array.get_u
            {
                var type = ReadArrayType(ref reader);
                var element = type.Element;
                if (element.Type.IsPacked != (opcode != 11))
                    throw Error("type mismatch: " + (opcode == 11 ? "array.get requires an unpacked element" : "array.get_s and array.get_u require a packed element"));

                Pop(ValType.I32);
                Pop(new ValType(nullable: true, type));
                Push(element.Type.Unpacked);
                Emit((ushort)(Op.GcPrefix + opcode), o: type);
                break;
            }

            case 14: // array.set
            {
                var type = ReadArrayType(ref reader);
                if (!type.Element.Mutable)
                    throw Error("array is immutable");

                Pop(type.Element.Type.Unpacked);
                Pop(ValType.I32);
                Pop(new ValType(nullable: true, type));
                Emit(Op.ArraySet, o: type);
                break;
            }

            case 15: // array.len
                Pop(new ValType(nullable: true, WasmHeapKind.Array));
                Push(ValType.I32);
                Emit(Op.ArrayLen);
                break;

            case 16: // array.fill
            {
                var type = ReadArrayType(ref reader);
                if (!type.Element.Mutable)
                    throw Error("array is immutable");

                Pop(ValType.I32);
                Pop(type.Element.Type.Unpacked);
                Pop(ValType.I32);
                Pop(new ValType(nullable: true, type));
                Emit(Op.ArrayFill, o: type);
                break;
            }

            case 17: // array.copy
            {
                var destination = ReadArrayType(ref reader);
                var source = ReadArrayType(ref reader);
                if (!destination.Element.Mutable)
                    throw Error("array is immutable");

                var compatible = destination.Element.Type.IsPacked || source.Element.Type.IsPacked
                    ? destination.Element.Type.Code == source.Element.Type.Code
                    : TypeRelations.IsSubtype(source.Element.Type, destination.Element.Type);
                if (!compatible)
                    throw Error("type mismatch: array.copy element types are not compatible");

                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(new ValType(nullable: true, source));
                Pop(ValType.I32);
                Pop(new ValType(nullable: true, destination));
                Emit(Op.ArrayCopy, o: destination);
                break;
            }

            case 18: // array.init_data
            {
                var type = ReadArrayType(ref reader);
                var dataIndex = ReadIndex(ref reader);
                if (!type.Element.Mutable)
                    throw Error("array is immutable");

                if (type.Element.Type.IsRef)
                    throw Error("type mismatch: array.init_data requires a numeric or vector element type");

                CheckData(dataIndex);
                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(new ValType(nullable: true, type));
                Emit(Op.ArrayInitData, i0: dataIndex, o: type);
                break;
            }

            case 19: // array.init_elem
            {
                var type = ReadArrayType(ref reader);
                var elementIndex = ReadIndex(ref reader);
                if (!type.Element.Mutable)
                    throw Error("array is immutable");

                var segment = GetElement(elementIndex);
                if (!TypeRelations.IsSubtype(segment.Type, type.Element.Type))
                    throw Error("type mismatch: array.init_elem element segment does not match the array type");

                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(new ValType(nullable: true, type));
                Emit(Op.ArrayInitElem, i0: elementIndex, o: type);
                break;
            }

            case 20 or 21 or 22 or 23: // ref.test, ref.test null, ref.cast, ref.cast null
            {
                var nullable = opcode is 21 or 23;
                var (heap, defined) = ReadHeapType(ref reader);
                var target = MakeRef(nullable, heap, defined);
                var operand = PopRef();
                CheckSameHierarchy(operand, target);
                Push(opcode <= 21 ? ValType.I32 : target);
                Emit((ushort)(Op.GcPrefix + opcode), i0: (int)target.Heap, i1: nullable ? 1 : 0, o: defined);
                break;
            }

            case 24 or 25: // br_on_cast, br_on_cast_fail
            {
                var flags = reader.ReadByte();
                if (flags > 3)
                    throw Error("malformed br_on_cast flags");

                var label = GetLabel(reader.ReadU32());
                var (heap1, defined1) = ReadHeapType(ref reader);
                var (heap2, defined2) = ReadHeapType(ref reader);
                var source = MakeRef((flags & 1) != 0, heap1, defined1);
                var target = MakeRef((flags & 2) != 0, heap2, defined2);
                if (!TypeRelations.IsSubtype(target, source))
                    throw Error("type mismatch: the target type of br_on_cast must be a subtype of the source type");

                var types = label.LabelTypes;
                if (types.Length == 0 || !types[^1].IsRef)
                    throw Error("type mismatch: br_on_cast requires a label whose last type is a reference");

                // The value that goes to the label, and the value that stays on the stack
                var difference = target.Nullable ? source.AsNonNullable() : source;
                var branchType = opcode == 24 ? target : difference;
                var fallthroughType = opcode == 24 ? difference : target;
                if (!TypeRelations.IsSubtype(branchType, types[^1]))
                    throw Error("type mismatch: br_on_cast type does not match the label");

                Pop(source);
                var heightBefore = Height + 1;
                var prefix = types[..^1];
                Pop(prefix);
                Push(prefix);
                Push(fallthroughType);
                var needsCopy = heightBefore - types.Length != label.Height;
                var instruction = Emit((ushort)(Op.GcPrefix + opcode), i1: types.Length, i2: label.Height, l0: needsCopy ? 1 : 0, l1: ((long)target.Heap << 1) | (target.Nullable ? 1L : 0L), o: defined2);
                SetBranchTarget(instruction, label);
                break;
            }

            case 26: // any.convert_extern
            {
                var type = Pop(ValType.ExternRef);
                Push(new ValType(type.Code == ValueCode.Bottom || type.Nullable, WasmHeapKind.Any));
                Emit(Op.AnyConvertExtern);
                break;
            }

            case 27: // extern.convert_any
            {
                var type = Pop(ValType.AnyRef);
                Push(new ValType(type.Code == ValueCode.Bottom || type.Nullable, WasmHeapKind.Extern));
                Emit(Op.ExternConvertAny);
                break;
            }

            case 28: // ref.i31
                Pop(ValType.I32);
                Push(new ValType(nullable: false, WasmHeapKind.I31));
                Emit(Op.RefI31);
                break;

            case 29 or 30: // i31.get_s, i31.get_u
                Pop(new ValType(nullable: true, WasmHeapKind.I31));
                Push(ValType.I32);
                Emit((ushort)(Op.GcPrefix + opcode));
                break;

            default:
                throw Error("illegal opcode fb " + opcode);
        }
    }

    private static void CheckSameHierarchy(ValType operand, ValType target)
    {
        if (operand.Code == ValueCode.Bottom)
            return;

        if (TypeRelations.GetTop(operand.Heap) != TypeRelations.GetTop(target.Heap))
            throw Error("type mismatch: the reference types are in different hierarchies");
    }

    private void CompileMisc(ref WasmReader reader)
    {
        var opcode = reader.ReadU32();
        switch (opcode)
        {
            case <= 7: // trunc_sat
            {
                var (parameter, result) = opcode switch
                {
                    0 or 1 => (ValType.F32, ValType.I32),
                    2 or 3 => (ValType.F64, ValType.I32),
                    4 or 5 => (ValType.F32, ValType.I64),
                    _ => (ValType.F64, ValType.I64),
                };
                Pop(parameter);
                Push(result);
                Emit((ushort)(Op.MiscPrefix + opcode));
                break;
            }

            case 8: // memory.init
            {
                var dataIndex = ReadIndex(ref reader);
                var memoryIndex = ReadIndex(ref reader);
                var memory = GetMemory(memoryIndex);
                CheckData(dataIndex);
                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(memory.AddressType);
                Emit(Op.MemoryInit, i0: memoryIndex, i1: dataIndex);
                break;
            }

            case 9: // data.drop
            {
                var dataIndex = ReadIndex(ref reader);
                CheckData(dataIndex);
                Emit(Op.DataDrop, i0: dataIndex);
                break;
            }

            case 10: // memory.copy
            {
                var destinationIndex = ReadIndex(ref reader);
                var sourceIndex = ReadIndex(ref reader);
                var destination = GetMemory(destinationIndex);
                var source = GetMemory(sourceIndex);
                Pop(destination.Is64 && source.Is64 ? ValType.I64 : ValType.I32);
                Pop(source.AddressType);
                Pop(destination.AddressType);
                Emit(Op.MemoryCopy, i0: destinationIndex, i1: sourceIndex);
                break;
            }

            case 11: // memory.fill
            {
                var memoryIndex = ReadIndex(ref reader);
                var memory = GetMemory(memoryIndex);
                Pop(memory.AddressType);
                Pop(ValType.I32);
                Pop(memory.AddressType);
                Emit(Op.MemoryFill, i0: memoryIndex);
                break;
            }

            case 12: // table.init
            {
                var elementIndex = ReadIndex(ref reader);
                var tableIndex = ReadIndex(ref reader);
                var segment = GetElement(elementIndex);
                var table = GetTable(tableIndex);
                if (!TypeRelations.IsSubtype(segment.Type, table.ElementType))
                    throw Error("type mismatch: table.init element segment does not match the table");

                Pop(ValType.I32);
                Pop(ValType.I32);
                Pop(table.AddressType);
                Emit(Op.TableInit, i0: tableIndex, i1: elementIndex);
                break;
            }

            case 13: // elem.drop
            {
                var elementIndex = ReadIndex(ref reader);
                GetElement(elementIndex);
                Emit(Op.ElemDrop, i0: elementIndex);
                break;
            }

            case 14: // table.copy
            {
                var destinationIndex = ReadIndex(ref reader);
                var sourceIndex = ReadIndex(ref reader);
                var destination = GetTable(destinationIndex);
                var source = GetTable(sourceIndex);
                if (!TypeRelations.IsSubtype(source.ElementType, destination.ElementType))
                    throw Error("type mismatch: table.copy source does not match the destination");

                Pop(destination.Is64 && source.Is64 ? ValType.I64 : ValType.I32);
                Pop(source.AddressType);
                Pop(destination.AddressType);
                Emit(Op.TableCopy, i0: destinationIndex, i1: sourceIndex);
                break;
            }

            case 15: // table.grow
            {
                var tableIndex = ReadIndex(ref reader);
                var table = GetTable(tableIndex);
                Pop(table.AddressType);
                Pop(table.ElementType);
                Push(table.AddressType);
                Emit(Op.TableGrow, i0: tableIndex);
                break;
            }

            case 16: // table.size
            {
                var tableIndex = ReadIndex(ref reader);
                var table = GetTable(tableIndex);
                Push(table.AddressType);
                Emit(Op.TableSize, i0: tableIndex);
                break;
            }

            case 17: // table.fill
            {
                var tableIndex = ReadIndex(ref reader);
                var table = GetTable(tableIndex);
                Pop(table.AddressType);
                Pop(table.ElementType);
                Pop(table.AddressType);
                Emit(Op.TableFill, i0: tableIndex);
                break;
            }

            case 19 or 20: // i64.add128, i64.sub128
                Pop(I64x2);
                Pop(I64x2);
                Push(ValType.I64);
                Push(ValType.I64);
                Emit((ushort)(Op.MiscPrefix + opcode));
                break;

            case 21 or 22: // i64.mul_wide_s, i64.mul_wide_u
                Pop(I64x2);
                Push(ValType.I64);
                Push(ValType.I64);
                Emit((ushort)(Op.MiscPrefix + opcode));
                break;

            default:
                throw Error("illegal opcode fc " + opcode);
        }
    }
}
