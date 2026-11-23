using System.Globalization;

namespace Meziantou.Framework.WasmEngine;

#pragma warning disable CA1707 // The names mirror the instructions (e.g. I32_TruncSatF32S)

/// <summary>
/// The operations of the compiled code: the single-byte opcodes keep their value; the opcodes of the prefixes 0xFB, 0xFC,
/// 0xFD and 0xFE are offset by 0x100, 0x200, 0x400 and 0x600; the operations of the compiler start at 0x700.
/// </summary>
internal static class Op
{
    public const int GcPrefix = 0x100;
    public const int MiscPrefix = 0x200;
    public const int SimdPrefix = 0x400;
    public const int AtomicPrefix = 0x600;

    public const ushort Unreachable = 0x00;
    public const ushort Nop = 0x01;
    public const ushort Throw = 0x08;
    public const ushort ThrowRef = 0x0A;
    public const ushort Br = 0x0C;
    public const ushort BrIf = 0x0D;
    public const ushort BrTable = 0x0E;
    public const ushort Return = 0x0F;
    public const ushort Call = 0x10;
    public const ushort CallIndirect = 0x11;
    public const ushort ReturnCall = 0x12;
    public const ushort ReturnCallIndirect = 0x13;
    public const ushort CallRef = 0x14;
    public const ushort ReturnCallRef = 0x15;
    public const ushort Drop = 0x1A;
    public const ushort Select = 0x1B;
    public const ushort LocalGet = 0x20;
    public const ushort LocalSet = 0x21;
    public const ushort LocalTee = 0x22;
    public const ushort GlobalGet = 0x23;
    public const ushort GlobalSet = 0x24;
    public const ushort TableGet = 0x25;
    public const ushort TableSet = 0x26;

    public const ushort I32Load = 0x28;
    public const ushort I64Load = 0x29;
    public const ushort F32Load = 0x2A;
    public const ushort F64Load = 0x2B;
    public const ushort I32Load8S = 0x2C;
    public const ushort I32Load8U = 0x2D;
    public const ushort I32Load16S = 0x2E;
    public const ushort I32Load16U = 0x2F;
    public const ushort I64Load8S = 0x30;
    public const ushort I64Load8U = 0x31;
    public const ushort I64Load16S = 0x32;
    public const ushort I64Load16U = 0x33;
    public const ushort I64Load32S = 0x34;
    public const ushort I64Load32U = 0x35;
    public const ushort I32Store = 0x36;
    public const ushort I64Store = 0x37;
    public const ushort F32Store = 0x38;
    public const ushort F64Store = 0x39;
    public const ushort I32Store8 = 0x3A;
    public const ushort I32Store16 = 0x3B;
    public const ushort I64Store8 = 0x3C;
    public const ushort I64Store16 = 0x3D;
    public const ushort I64Store32 = 0x3E;
    public const ushort MemorySize = 0x3F;
    public const ushort MemoryGrow = 0x40;
    public const ushort I32Const = 0x41;
    public const ushort I64Const = 0x42;
    public const ushort F32Const = 0x43;
    public const ushort F64Const = 0x44;

    public const ushort I32Eqz = 0x45;
    public const ushort I32Eq = 0x46;
    public const ushort I32Ne = 0x47;
    public const ushort I32LtS = 0x48;
    public const ushort I32LtU = 0x49;
    public const ushort I32GtS = 0x4A;
    public const ushort I32GtU = 0x4B;
    public const ushort I32LeS = 0x4C;
    public const ushort I32LeU = 0x4D;
    public const ushort I32GeS = 0x4E;
    public const ushort I32GeU = 0x4F;
    public const ushort I64Eqz = 0x50;
    public const ushort I64Eq = 0x51;
    public const ushort I64Ne = 0x52;
    public const ushort I64LtS = 0x53;
    public const ushort I64LtU = 0x54;
    public const ushort I64GtS = 0x55;
    public const ushort I64GtU = 0x56;
    public const ushort I64LeS = 0x57;
    public const ushort I64LeU = 0x58;
    public const ushort I64GeS = 0x59;
    public const ushort I64GeU = 0x5A;
    public const ushort F32Eq = 0x5B;
    public const ushort F32Ne = 0x5C;
    public const ushort F32Lt = 0x5D;
    public const ushort F32Gt = 0x5E;
    public const ushort F32Le = 0x5F;
    public const ushort F32Ge = 0x60;
    public const ushort F64Eq = 0x61;
    public const ushort F64Ne = 0x62;
    public const ushort F64Lt = 0x63;
    public const ushort F64Gt = 0x64;
    public const ushort F64Le = 0x65;
    public const ushort F64Ge = 0x66;
    public const ushort I32Clz = 0x67;
    public const ushort I32Ctz = 0x68;
    public const ushort I32Popcnt = 0x69;
    public const ushort I32Add = 0x6A;
    public const ushort I32Sub = 0x6B;
    public const ushort I32Mul = 0x6C;
    public const ushort I32DivS = 0x6D;
    public const ushort I32DivU = 0x6E;
    public const ushort I32RemS = 0x6F;
    public const ushort I32RemU = 0x70;
    public const ushort I32And = 0x71;
    public const ushort I32Or = 0x72;
    public const ushort I32Xor = 0x73;
    public const ushort I32Shl = 0x74;
    public const ushort I32ShrS = 0x75;
    public const ushort I32ShrU = 0x76;
    public const ushort I32Rotl = 0x77;
    public const ushort I32Rotr = 0x78;
    public const ushort I64Clz = 0x79;
    public const ushort I64Ctz = 0x7A;
    public const ushort I64Popcnt = 0x7B;
    public const ushort I64Add = 0x7C;
    public const ushort I64Sub = 0x7D;
    public const ushort I64Mul = 0x7E;
    public const ushort I64DivS = 0x7F;
    public const ushort I64DivU = 0x80;
    public const ushort I64RemS = 0x81;
    public const ushort I64RemU = 0x82;
    public const ushort I64And = 0x83;
    public const ushort I64Or = 0x84;
    public const ushort I64Xor = 0x85;
    public const ushort I64Shl = 0x86;
    public const ushort I64ShrS = 0x87;
    public const ushort I64ShrU = 0x88;
    public const ushort I64Rotl = 0x89;
    public const ushort I64Rotr = 0x8A;
    public const ushort F32Abs = 0x8B;
    public const ushort F32Neg = 0x8C;
    public const ushort F32Ceil = 0x8D;
    public const ushort F32Floor = 0x8E;
    public const ushort F32Trunc = 0x8F;
    public const ushort F32Nearest = 0x90;
    public const ushort F32Sqrt = 0x91;
    public const ushort F32Add = 0x92;
    public const ushort F32Sub = 0x93;
    public const ushort F32Mul = 0x94;
    public const ushort F32Div = 0x95;
    public const ushort F32Min = 0x96;
    public const ushort F32Max = 0x97;
    public const ushort F32Copysign = 0x98;
    public const ushort F64Abs = 0x99;
    public const ushort F64Neg = 0x9A;
    public const ushort F64Ceil = 0x9B;
    public const ushort F64Floor = 0x9C;
    public const ushort F64Trunc = 0x9D;
    public const ushort F64Nearest = 0x9E;
    public const ushort F64Sqrt = 0x9F;
    public const ushort F64Add = 0xA0;
    public const ushort F64Sub = 0xA1;
    public const ushort F64Mul = 0xA2;
    public const ushort F64Div = 0xA3;
    public const ushort F64Min = 0xA4;
    public const ushort F64Max = 0xA5;
    public const ushort F64Copysign = 0xA6;
    public const ushort I32WrapI64 = 0xA7;
    public const ushort I32TruncF32S = 0xA8;
    public const ushort I32TruncF32U = 0xA9;
    public const ushort I32TruncF64S = 0xAA;
    public const ushort I32TruncF64U = 0xAB;
    public const ushort I64ExtendI32S = 0xAC;
    public const ushort I64ExtendI32U = 0xAD;
    public const ushort I64TruncF32S = 0xAE;
    public const ushort I64TruncF32U = 0xAF;
    public const ushort I64TruncF64S = 0xB0;
    public const ushort I64TruncF64U = 0xB1;
    public const ushort F32ConvertI32S = 0xB2;
    public const ushort F32ConvertI32U = 0xB3;
    public const ushort F32ConvertI64S = 0xB4;
    public const ushort F32ConvertI64U = 0xB5;
    public const ushort F32DemoteF64 = 0xB6;
    public const ushort F64ConvertI32S = 0xB7;
    public const ushort F64ConvertI32U = 0xB8;
    public const ushort F64ConvertI64S = 0xB9;
    public const ushort F64ConvertI64U = 0xBA;
    public const ushort F64PromoteF32 = 0xBB;
    public const ushort I32ReinterpretF32 = 0xBC;
    public const ushort I64ReinterpretF64 = 0xBD;
    public const ushort F32ReinterpretI32 = 0xBE;
    public const ushort F64ReinterpretI64 = 0xBF;
    public const ushort I32Extend8S = 0xC0;
    public const ushort I32Extend16S = 0xC1;
    public const ushort I64Extend8S = 0xC2;
    public const ushort I64Extend16S = 0xC3;
    public const ushort I64Extend32S = 0xC4;

    public const ushort RefNull = 0xD0;
    public const ushort RefIsNull = 0xD1;
    public const ushort RefFunc = 0xD2;
    public const ushort RefEq = 0xD3;
    public const ushort RefAsNonNull = 0xD4;
    public const ushort BrOnNull = 0xD5;
    public const ushort BrOnNonNull = 0xD6;

    // 0xFB: the GC instructions
    public const ushort StructNew = GcPrefix + 0;
    public const ushort StructNewDefault = GcPrefix + 1;
    public const ushort StructGet = GcPrefix + 2;
    public const ushort StructGetS = GcPrefix + 3;
    public const ushort StructGetU = GcPrefix + 4;
    public const ushort StructSet = GcPrefix + 5;
    public const ushort ArrayNew = GcPrefix + 6;
    public const ushort ArrayNewDefault = GcPrefix + 7;
    public const ushort ArrayNewFixed = GcPrefix + 8;
    public const ushort ArrayNewData = GcPrefix + 9;
    public const ushort ArrayNewElem = GcPrefix + 10;
    public const ushort ArrayGet = GcPrefix + 11;
    public const ushort ArrayGetS = GcPrefix + 12;
    public const ushort ArrayGetU = GcPrefix + 13;
    public const ushort ArraySet = GcPrefix + 14;
    public const ushort ArrayLen = GcPrefix + 15;
    public const ushort ArrayFill = GcPrefix + 16;
    public const ushort ArrayCopy = GcPrefix + 17;
    public const ushort ArrayInitData = GcPrefix + 18;
    public const ushort ArrayInitElem = GcPrefix + 19;
    public const ushort RefTest = GcPrefix + 20;
    public const ushort RefTestNull = GcPrefix + 21;
    public const ushort RefCast = GcPrefix + 22;
    public const ushort RefCastNull = GcPrefix + 23;
    public const ushort BrOnCast = GcPrefix + 24;
    public const ushort BrOnCastFail = GcPrefix + 25;
    public const ushort AnyConvertExtern = GcPrefix + 26;
    public const ushort ExternConvertAny = GcPrefix + 27;
    public const ushort RefI31 = GcPrefix + 28;
    public const ushort I31GetS = GcPrefix + 29;
    public const ushort I31GetU = GcPrefix + 30;

    // 0xFC
    public const ushort I32TruncSatF32S = MiscPrefix + 0;
    public const ushort I32TruncSatF32U = MiscPrefix + 1;
    public const ushort I32TruncSatF64S = MiscPrefix + 2;
    public const ushort I32TruncSatF64U = MiscPrefix + 3;
    public const ushort I64TruncSatF32S = MiscPrefix + 4;
    public const ushort I64TruncSatF32U = MiscPrefix + 5;
    public const ushort I64TruncSatF64S = MiscPrefix + 6;
    public const ushort I64TruncSatF64U = MiscPrefix + 7;
    public const ushort MemoryInit = MiscPrefix + 8;
    public const ushort DataDrop = MiscPrefix + 9;
    public const ushort MemoryCopy = MiscPrefix + 10;
    public const ushort MemoryFill = MiscPrefix + 11;
    public const ushort TableInit = MiscPrefix + 12;
    public const ushort ElemDrop = MiscPrefix + 13;
    public const ushort TableCopy = MiscPrefix + 14;
    public const ushort TableGrow = MiscPrefix + 15;
    public const ushort TableSize = MiscPrefix + 16;
    public const ushort TableFill = MiscPrefix + 17;
    public const ushort I64Add128 = MiscPrefix + 19;
    public const ushort I64Sub128 = MiscPrefix + 20;
    public const ushort I64MulWideS = MiscPrefix + 21;
    public const ushort I64MulWideU = MiscPrefix + 22;

    // 0xFE: the atomic instructions
    public const ushort MemoryAtomicNotify = AtomicPrefix + 0x00;
    public const ushort MemoryAtomicWait32 = AtomicPrefix + 0x01;
    public const ushort MemoryAtomicWait64 = AtomicPrefix + 0x02;
    public const ushort AtomicFence = AtomicPrefix + 0x03;
    public const ushort I32AtomicLoad = AtomicPrefix + 0x10;
    public const ushort I64AtomicLoad = AtomicPrefix + 0x11;
    public const ushort I32AtomicLoad8U = AtomicPrefix + 0x12;
    public const ushort I32AtomicLoad16U = AtomicPrefix + 0x13;
    public const ushort I64AtomicLoad8U = AtomicPrefix + 0x14;
    public const ushort I64AtomicLoad16U = AtomicPrefix + 0x15;
    public const ushort I64AtomicLoad32U = AtomicPrefix + 0x16;
    public const ushort I32AtomicStore = AtomicPrefix + 0x17;
    public const ushort I64AtomicStore = AtomicPrefix + 0x18;
    public const ushort I32AtomicStore8 = AtomicPrefix + 0x19;
    public const ushort I32AtomicStore16 = AtomicPrefix + 0x1A;
    public const ushort I64AtomicStore8 = AtomicPrefix + 0x1B;
    public const ushort I64AtomicStore16 = AtomicPrefix + 0x1C;
    public const ushort I64AtomicStore32 = AtomicPrefix + 0x1D;

    /// <summary>The first read-modify-write operation (i32.atomic.rmw.add): 7 operations per kind (add, sub, and, or, xor, xchg, cmpxchg).</summary>
    public const ushort AtomicRmwFirst = AtomicPrefix + 0x1E;
    public const ushort AtomicRmwLast = AtomicPrefix + 0x4E;

    // The operations of the compiler

    /// <summary>Jumps to I0.</summary>
    public const ushort Jump = 0x700;

    /// <summary>Pops an i32 and jumps to I0 when it is zero (the condition of an if).</summary>
    public const ushort JumpIfZero = 0x701;

    /// <summary>Keeps the I1 top values at the height I2 and jumps to I0.</summary>
    public const ushort BrCopy = 0x702;

    /// <summary>Copies the value of the local I1 to the local I0 (not emitted yet).</summary>
    public const ushort LocalCopy = 0x703;

    /// <summary>The end of a function: returns its results (the I1 top values).</summary>
    public const ushort End = 0x704;

    // The fused operations of the interpreter (see InstructionFuser): an operation replaces the first instruction of a
    // sequence, the next instructions of the sequence stay in place (a branch to one of them runs them one by one). They are
    // after the operations of the prefix 0xFB, so that the switch of the interpreter has one jump table for them and the
    // single-byte opcodes (the operations of the compiler, from 0x700, are reached after a few comparisons)

    /// <summary>The first fused operation.</summary>
    public const int FusedFirst = 0x130;

    /// <summary><c>local.get I0</c>, <c>local.get I1</c>.</summary>
    public const ushort LocalGetLocalGet = 0x130;

    /// <summary><c>local.get I0</c>, then a constant L0 (<c>i32.const</c>, <c>i64.const</c>, <c>f32.const</c> or <c>f64.const</c>).</summary>
    public const ushort LocalGetConst = 0x131;

    /// <summary><c>local.get I0</c>, <c>i32.const L0</c>, <c>i32.add</c>.</summary>
    public const ushort LocalGetI32ConstI32Add = 0x132;

    /// <summary><c>i32.const L0</c>, <c>i32.add</c>.</summary>
    public const ushort I32ConstI32Add = 0x133;

    /// <summary><c>local.get I0</c>, <c>i32.load</c> of the memory I1 at the offset L0.</summary>
    public const ushort LocalGetI32Load = 0x134;

    /// <summary><c>local.set I0</c>, <c>local.get I1</c>.</summary>
    public const ushort LocalSetLocalGet = 0x135;

    /// <summary><c>local.tee I0</c>, <c>local.get I1</c>.</summary>
    public const ushort LocalTeeLocalGet = 0x136;

    /// <summary>
    /// The first of the 11 operations <c>i32.eqz</c>, <c>i32.eq</c>, ... <c>i32.ge_u</c> followed by <c>br_if</c> (in the
    /// order of their opcodes): the operation has the operands of the <c>br_if</c>.
    /// </summary>
    public const ushort I32CompareBrIfFirst = 0x137;

    /// <summary>The first of the 11 operations <c>i32.eqz</c>, <c>i32.eq</c>, ... <c>i32.ge_u</c> followed by the jump of an <c>if</c> (<see cref="JumpIfZero"/>, whose target is I0).</summary>
    public const ushort I32CompareJumpIfZeroFirst = 0x142;

    /// <summary>
    /// The first of the 13 operations <c>local.get I0</c>, <c>local.get I1</c> followed by <c>i32.add</c>, <c>i32.sub</c>,
    /// <c>i32.mul</c>, <c>i32.and</c>, <c>i32.or</c>, <c>i32.xor</c>, <c>i64.add</c>, <c>i64.sub</c>, <c>i64.mul</c>,
    /// <c>f64.add</c>, <c>f64.sub</c>, <c>f64.mul</c> or <c>f64.div</c> (see <see cref="InstructionFuser"/>).
    /// </summary>
    public const ushort LocalGetLocalGetBinaryFirst = 0x14D;

    /// <summary>
    /// The first of the 10 operations <c>local.get I1</c>, <c>local.get I2</c>, <c>i32.eq</c> ... <c>i32.ge_u</c> (in the
    /// order of their opcodes), <c>br_if</c> to I0 (a br_if that keeps no value).
    /// </summary>
    public const ushort LocalGetLocalGetCompareBrIfFirst = 0x15A;

    /// <summary>The first of the 10 operations <c>local.get I1</c>, <c>i32.const L1</c>, <c>i32.eq</c> ... <c>i32.ge_u</c>, <c>br_if</c> to I0 (a br_if that keeps no value).</summary>
    public const ushort LocalGetConstCompareBrIfFirst = 0x164;

    /// <summary>The first of the 10 operations <c>local.get I1</c>, <c>local.get I2</c>, <c>i32.eq</c> ... <c>i32.ge_u</c>, then the jump to I0 of an <c>if</c>.</summary>
    public const ushort LocalGetLocalGetCompareJumpIfZeroFirst = 0x16E;

    /// <summary>The first of the 10 operations <c>local.get I1</c>, <c>i32.const L1</c>, <c>i32.eq</c> ... <c>i32.ge_u</c>, then the jump to I0 of an <c>if</c>.</summary>
    public const ushort LocalGetConstCompareJumpIfZeroFirst = 0x178;

    /// <summary>The last fused operation.</summary>
    public const int FusedLast = LocalGetConstCompareJumpIfZeroFirst + 9;

    public static int Simd(int opcode) => SimdPrefix + opcode;

    public static bool IsSimd(int op) => op is >= SimdPrefix and < AtomicPrefix;
}

#pragma warning restore CA1707

internal static class OpNames
{
    public static string GetName(int op) => op switch
    {
        >= Op.Jump => "internal:0x" + op.ToString("X", CultureInfo.InvariantCulture),
        >= Op.FusedFirst and <= Op.FusedLast => "fused:0x" + op.ToString("X", CultureInfo.InvariantCulture),
        >= Op.AtomicPrefix => "0xFE 0x" + (op - Op.AtomicPrefix).ToString("X2", CultureInfo.InvariantCulture),
        >= Op.SimdPrefix => "0xFD 0x" + (op - Op.SimdPrefix).ToString("X2", CultureInfo.InvariantCulture),
        >= Op.MiscPrefix => "0xFC 0x" + (op - Op.MiscPrefix).ToString("X2", CultureInfo.InvariantCulture),
        >= Op.GcPrefix => "0xFB 0x" + (op - Op.GcPrefix).ToString("X2", CultureInfo.InvariantCulture),
        _ => "0x" + op.ToString("X2", CultureInfo.InvariantCulture),
    };
}
