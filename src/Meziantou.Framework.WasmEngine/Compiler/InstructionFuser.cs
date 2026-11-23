namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// Creates the instructions that the interpreter runs from the instructions of a function: the first instruction of a
/// frequent sequence becomes an operation that runs the whole sequence (one dispatch instead of two to four, and fewer
/// writes of the operand stack). The other instructions of the sequence stay in place, so the indexes of the instructions
/// do not change: the branch targets, the handlers, the entries of the OSR and the compiler to .NET code use the same
/// indexes, and a branch to an instruction in the middle of a sequence runs the rest of it one instruction at a time.
/// </summary>
internal static class InstructionFuser
{
    // The operations of Op.LocalGetLocalGetBinaryFirst + index, in this order
    private static readonly ushort[] BinaryOperations = [Op.I32Add, Op.I32Sub, Op.I32Mul, Op.I32And, Op.I32Or, Op.I32Xor, Op.I64Add, Op.I64Sub, Op.I64Mul, Op.F64Add, Op.F64Sub, Op.F64Mul, Op.F64Div];

    public static Instr[] Fuse(Instr[] code)
    {
        // The sequences overlap (local.get x, local.get a, local.get b, i32.add is two operations from the second
        // instruction, three from the first): from the end, an instruction is fused when the rest of the code from it runs in
        // no more operations than without the fusion. Each instruction is the best choice for the code that starts at it, so
        // a branch to any instruction runs its best sequence too.
        var result = (Instr[])code.Clone();
        var operations = new int[code.Length + 1];
        for (var i = code.Length - 1; i >= 0; i--)
        {
            operations[i] = 1 + operations[i + 1];
            if (TryFuse(code, i) is var (fused, length) && 1 + operations[i + length] <= operations[i])
            {
                result[i] = fused;
                operations[i] = 1 + operations[i + length];
            }
        }

        return result;
    }

    /// <summary>Returns the fused operation of the sequence that starts at <paramref name="i"/>, and the number of instructions it runs.</summary>
    private static (Instr Fused, int Length)? TryFuse(Instr[] code, int i)
    {
        if (i + 1 >= code.Length)
            return null;

        ref readonly var first = ref code[i];
        ref readonly var second = ref code[i + 1];
        var third = i + 2 < code.Length ? code[i + 2].Op : -1;
        switch (first.Op)
        {
            case Op.LocalGet when second.Op is Op.LocalGet:
            {
                // local.get, local.get, comparison, br_if or if
                if (IsBinaryComparison(third) && GetCompareBranch(code, i + 3, Op.LocalGetLocalGetCompareBrIfFirst, Op.LocalGetLocalGetCompareJumpIfZeroFirst) is { } branch)
                    return (new Instr { Op = (ushort)(branch.Op + third - Op.I32Eq), I0 = branch.I0, I1 = first.I0, I2 = second.I0 }, 4);

                var binary = Array.IndexOf(BinaryOperations, (ushort)third);
                if (third >= 0 && binary >= 0)
                    return (new Instr { Op = (ushort)(Op.LocalGetLocalGetBinaryFirst + binary), I0 = first.I0, I1 = second.I0 }, 3);

                return (new Instr { Op = Op.LocalGetLocalGet, I0 = first.I0, I1 = second.I0 }, 2);
            }

            case Op.LocalGet when second.Op is Op.I32Const:
            {
                // local.get, i32.const, comparison, br_if or if
                if (IsBinaryComparison(third) && GetCompareBranch(code, i + 3, Op.LocalGetConstCompareBrIfFirst, Op.LocalGetConstCompareJumpIfZeroFirst) is { } branch)
                    return (new Instr { Op = (ushort)(branch.Op + third - Op.I32Eq), I0 = branch.I0, I1 = first.I0, L1 = second.L0 }, 4);

                if (third == Op.I32Add)
                    return (new Instr { Op = Op.LocalGetI32ConstI32Add, I0 = first.I0, L0 = second.L0 }, 3);

                return (new Instr { Op = Op.LocalGetConst, I0 = first.I0, L0 = second.L0 }, 2);
            }

            case Op.LocalGet when second.Op is Op.I64Const or Op.F32Const or Op.F64Const:
                return (new Instr { Op = Op.LocalGetConst, I0 = first.I0, L0 = second.L0 }, 2);

            case Op.LocalGet when second.Op is Op.I32Load:
                return (new Instr { Op = Op.LocalGetI32Load, I0 = first.I0, I1 = second.I0, L0 = second.L0 }, 2);

            case Op.LocalSet when second.Op is Op.LocalGet:
                return (new Instr { Op = Op.LocalSetLocalGet, I0 = first.I0, I1 = second.I0 }, 2);

            case Op.LocalTee when second.Op is Op.LocalGet:
                return (new Instr { Op = Op.LocalTeeLocalGet, I0 = first.I0, I1 = second.I0 }, 2);

            case Op.I32Const when second.Op is Op.I32Add:
                return (new Instr { Op = Op.I32ConstI32Add, L0 = first.L0 }, 2);

            // The operation has the operands of the br_if, or the target of the jump of the if
            case >= Op.I32Eqz and <= Op.I32GeU when second.Op is Op.BrIf:
                return (second with { Op = (ushort)(Op.I32CompareBrIfFirst + first.Op - Op.I32Eqz) }, 2);

            case >= Op.I32Eqz and <= Op.I32GeU when second.Op is Op.JumpIfZero:
                return (second with { Op = (ushort)(Op.I32CompareJumpIfZeroFirst + first.Op - Op.I32Eqz) }, 2);

            default:
                return null;
        }
    }

    private static bool IsBinaryComparison(int op) => op is >= Op.I32Eq and <= Op.I32GeU;

    /// <summary>
    /// Returns the first operation of the family when the instruction at <paramref name="index"/> is a <c>br_if</c> that
    /// keeps no value or the jump of an <c>if</c>, and its target.
    /// </summary>
    private static (ushort Op, int I0)? GetCompareBranch(Instr[] code, int index, ushort brIfFirst, ushort jumpIfZeroFirst)
    {
        if (index >= code.Length)
            return null;

        ref readonly var branch = ref code[index];
        return branch.Op switch
        {
            Op.BrIf when branch.L0 == 0 => (brIfFirst, branch.I0),
            Op.JumpIfZero => (jumpIfZeroFirst, branch.I0),
            _ => null,
        };
    }
}
