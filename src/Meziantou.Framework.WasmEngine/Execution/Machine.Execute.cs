using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WasmEngine;

internal sealed unsafe partial class Machine
{
#pragma warning disable MA0051 // Method is too long: the loop of the interpreter
    private void Execute(CodeFunction function, int fp, int entryFrames)
    {
        var code = function.Code;
        var instrs = code.Code;
        var instance = function.Instance;
        var sp = EnterFrame(code, fp);
        var ob = sp;
        var stack = _stack;
        var pc = 0;
        var arity = 0;
        var unwindCurrentFrame = false;
        ManagedFunction? callee = null;
        ManagedException? exception = null;
        CheckInterrupt();

        while (true)
        {
            ref readonly var ins = ref instrs[pc++];
            switch (ins.Op)
            {
                // ------------------------------------------------------------------------------------------------
                // Control
                // ------------------------------------------------------------------------------------------------
                case Op.Unreachable:
                    throw Traps.Unreachable();

                case Op.Nop:
                    break;

                case Op.Jump:
                    if (ins.I0 < pc)
                    {
                        pc = ins.I0;
                        goto BackEdge;
                    }

                    pc = ins.I0;
                    break;

                case Op.JumpIfZero:
                    sp--;
                    if ((int)stack[sp].A == 0)
                    {
                        pc = ins.I0;
                    }

                    break;

                case Op.BrCopy:
                    CopySlots(stack, sp - ins.I1, ob + ins.I2, ins.I1);
                    sp = ob + ins.I2 + ins.I1;
                    if (ins.I0 < pc)
                    {
                        pc = ins.I0;
                        goto BackEdge;
                    }

                    pc = ins.I0;
                    break;

                case Op.BrIf:
                    sp--;
                    if ((int)stack[sp].A != 0)
                    {
                        if (ins.L0 != 0)
                        {
                            CopySlots(stack, sp - ins.I1, ob + ins.I2, ins.I1);
                            sp = ob + ins.I2 + ins.I1;
                        }

                        if (ins.I0 < pc)
                        {
                            pc = ins.I0;
                            goto BackEdge;
                        }

                        pc = ins.I0;
                    }

                    break;

                case Op.BrTable:
                {
                    sp--;
                    var index = (uint)stack[sp].A;
                    var count = ins.I1;
                    var target = code.BranchTables[ins.I0 + (int)Math.Min(index, (uint)(count - 1))];
                    CopySlots(stack, sp - target.Arity, ob + target.Height, target.Arity);
                    sp = ob + target.Height + target.Arity;
                    if (target.Pc < pc)
                    {
                        pc = target.Pc;
                        goto BackEdge;
                    }

                    pc = target.Pc;
                    break;
                }

                case Op.Return:
                case Op.End:
                    arity = ins.I1;
                    goto DoReturn;

                case Op.Call:
                    callee = instance.Functions[ins.I0];
                    goto DoCall;

                case Op.CallIndirect:
                    sp--;
                    callee = GetIndirectFunction(instance.Tables[ins.I0], stack[sp].A, (DefinedType)ins.O!);
                    goto DoCall;

                case Op.CallRef:
                    sp--;
                    callee = (ManagedFunction?)stack[sp].R ?? throw Traps.NullFunctionReference();
                    goto DoCall;

                case Op.ReturnCall:
                    callee = instance.Functions[ins.I0];
                    goto DoTailCall;

                case Op.ReturnCallIndirect:
                    sp--;
                    callee = GetIndirectFunction(instance.Tables[ins.I0], stack[sp].A, (DefinedType)ins.O!);
                    goto DoTailCall;

                case Op.ReturnCallRef:
                    sp--;
                    callee = (ManagedFunction?)stack[sp].R ?? throw Traps.NullFunctionReference();
                    goto DoTailCall;

                case Op.Throw:
                {
                    var tag = instance.Tags[ins.I0];
                    var count = tag.FunctionType.Parameters.Length;
                    var payload = count == 0 ? [] : new Slot[count];
                    Array.Copy(stack, sp - count, payload, 0, count);
                    sp -= count;
                    exception = new ManagedException(tag, payload);
                    goto HandleException;
                }

                case Op.ThrowRef:
                    sp--;
                    exception = (ManagedException?)stack[sp].R ?? throw Traps.NullExceptionReference();
                    goto HandleException;

                case Op.Drop:
                    sp--;
                    break;

                case Op.Select:
                    sp -= 2;
                    if ((int)stack[sp + 1].A == 0)
                    {
                        stack[sp - 1] = stack[sp];
                    }

                    break;

                // ------------------------------------------------------------------------------------------------
                // Variables and tables
                // ------------------------------------------------------------------------------------------------
                case Op.LocalGet:
                    CopySlot(ref stack[fp + ins.I0], ref stack[sp++]);
                    break;

                case Op.LocalSet:
                    CopySlot(ref stack[--sp], ref stack[fp + ins.I0]);
                    break;

                case Op.LocalTee:
                    CopySlot(ref stack[sp - 1], ref stack[fp + ins.I0]);
                    break;

                case Op.GlobalGet:
                    CopySlot(ref instance.Globals[ins.I0].Value, ref stack[sp++]);
                    break;

                case Op.GlobalSet:
                    instance.Globals[ins.I0].Value = stack[--sp];
                    break;

                case Op.TableGet:
                {
                    var table = instance.Tables[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    var index = Address(table, slot.A);
                    if (index >= table.Size)
                        throw Traps.OutOfBoundsTable();

                    slot.R = table.Elements[(int)index];
                    break;
                }

                case Op.TableSet:
                {
                    sp -= 2;
                    var table = instance.Tables[ins.I0];
                    var index = Address(table, stack[sp].A);
                    if (index >= table.Size)
                        throw Traps.OutOfBoundsTable();

                    table.Elements[(int)index] = stack[sp + 1].R;
                    break;
                }

                // ------------------------------------------------------------------------------------------------
                // Memory
                // ------------------------------------------------------------------------------------------------
                case Op.I32Load:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<int>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 4));
                    break;
                }

                case Op.I64Load:
                case Op.F64Load:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<long>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 8));
                    break;
                }

                case Op.F32Load:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<int>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 4));
                    break;
                }

                case Op.I32Load8S:
                case Op.I64Load8S:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = *(sbyte*)memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 1);
                    break;
                }

                case Op.I32Load8U:
                case Op.I64Load8U:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = *memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 1);
                    break;
                }

                case Op.I32Load16S:
                case Op.I64Load16S:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<short>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 2));
                    break;
                }

                case Op.I32Load16U:
                case Op.I64Load16U:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<ushort>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 2));
                    break;
                }

                case Op.I64Load32S:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<int>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 4));
                    break;
                }

                case Op.I64Load32U:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    slot.A = Unsafe.ReadUnaligned<uint>(memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, 4));
                    break;
                }

                case Op.I32Store:
                case Op.F32Store:
                case Op.I64Store32:
                {
                    sp -= 2;
                    var memory = instance.Memories[ins.I0];
                    Unsafe.WriteUnaligned(memory.GetAddress(Address(memory, stack[sp].A), (ulong)ins.L0, 4), (int)stack[sp + 1].A);
                    break;
                }

                case Op.I64Store:
                case Op.F64Store:
                {
                    sp -= 2;
                    var memory = instance.Memories[ins.I0];
                    Unsafe.WriteUnaligned(memory.GetAddress(Address(memory, stack[sp].A), (ulong)ins.L0, 8), stack[sp + 1].A);
                    break;
                }

                case Op.I32Store8:
                case Op.I64Store8:
                {
                    sp -= 2;
                    var memory = instance.Memories[ins.I0];
                    *memory.GetAddress(Address(memory, stack[sp].A), (ulong)ins.L0, 1) = (byte)stack[sp + 1].A;
                    break;
                }

                case Op.I32Store16:
                case Op.I64Store16:
                {
                    sp -= 2;
                    var memory = instance.Memories[ins.I0];
                    Unsafe.WriteUnaligned(memory.GetAddress(Address(memory, stack[sp].A), (ulong)ins.L0, 2), (short)stack[sp + 1].A);
                    break;
                }

                case Op.MemorySize:
                    stack[sp++] = new Slot { A = (long)instance.Memories[ins.I0].Size };
                    break;

                case Op.MemoryGrow:
                {
                    var memory = instance.Memories[ins.I0];
                    ref var slot = ref stack[sp - 1];
                    var result = memory.TryGrow(Address(memory, slot.A));
                    slot.A = memory.Is64 ? result : (int)result;
                    break;
                }

                // ------------------------------------------------------------------------------------------------
                // Constants and numbers
                // ------------------------------------------------------------------------------------------------
                case Op.I32Const:
                case Op.I64Const:
                case Op.F32Const:
                case Op.F64Const:
                    SetNumber(ref stack[sp++], ins.L0);
                    break;

                case Op.I32Eqz: stack[sp - 1].A = (int)stack[sp - 1].A == 0 ? 1 : 0; break;
                case Op.I32Eq: sp--; stack[sp - 1].A = (int)stack[sp - 1].A == (int)stack[sp].A ? 1 : 0; break;
                case Op.I32Ne: sp--; stack[sp - 1].A = (int)stack[sp - 1].A != (int)stack[sp].A ? 1 : 0; break;
                case Op.I32LtS: sp--; stack[sp - 1].A = (int)stack[sp - 1].A < (int)stack[sp].A ? 1 : 0; break;
                case Op.I32LtU: sp--; stack[sp - 1].A = (uint)stack[sp - 1].A < (uint)stack[sp].A ? 1 : 0; break;
                case Op.I32GtS: sp--; stack[sp - 1].A = (int)stack[sp - 1].A > (int)stack[sp].A ? 1 : 0; break;
                case Op.I32GtU: sp--; stack[sp - 1].A = (uint)stack[sp - 1].A > (uint)stack[sp].A ? 1 : 0; break;
                case Op.I32LeS: sp--; stack[sp - 1].A = (int)stack[sp - 1].A <= (int)stack[sp].A ? 1 : 0; break;
                case Op.I32LeU: sp--; stack[sp - 1].A = (uint)stack[sp - 1].A <= (uint)stack[sp].A ? 1 : 0; break;
                case Op.I32GeS: sp--; stack[sp - 1].A = (int)stack[sp - 1].A >= (int)stack[sp].A ? 1 : 0; break;
                case Op.I32GeU: sp--; stack[sp - 1].A = (uint)stack[sp - 1].A >= (uint)stack[sp].A ? 1 : 0; break;
                case Op.I64Eqz: stack[sp - 1].A = stack[sp - 1].A == 0 ? 1 : 0; break;
                case Op.I64Eq: sp--; stack[sp - 1].A = stack[sp - 1].A == stack[sp].A ? 1 : 0; break;
                case Op.I64Ne: sp--; stack[sp - 1].A = stack[sp - 1].A != stack[sp].A ? 1 : 0; break;
                case Op.I64LtS: sp--; stack[sp - 1].A = stack[sp - 1].A < stack[sp].A ? 1 : 0; break;
                case Op.I64LtU: sp--; stack[sp - 1].A = (ulong)stack[sp - 1].A < (ulong)stack[sp].A ? 1 : 0; break;
                case Op.I64GtS: sp--; stack[sp - 1].A = stack[sp - 1].A > stack[sp].A ? 1 : 0; break;
                case Op.I64GtU: sp--; stack[sp - 1].A = (ulong)stack[sp - 1].A > (ulong)stack[sp].A ? 1 : 0; break;
                case Op.I64LeS: sp--; stack[sp - 1].A = stack[sp - 1].A <= stack[sp].A ? 1 : 0; break;
                case Op.I64LeU: sp--; stack[sp - 1].A = (ulong)stack[sp - 1].A <= (ulong)stack[sp].A ? 1 : 0; break;
                case Op.I64GeS: sp--; stack[sp - 1].A = stack[sp - 1].A >= stack[sp].A ? 1 : 0; break;
                case Op.I64GeU: sp--; stack[sp - 1].A = (ulong)stack[sp - 1].A >= (ulong)stack[sp].A ? 1 : 0; break;
                case Op.F32Eq: sp--; stack[sp - 1].A = Numerics.F32(stack[sp - 1].A) == Numerics.F32(stack[sp].A) ? 1 : 0; break;
                case Op.F32Ne: sp--; stack[sp - 1].A = Numerics.F32(stack[sp - 1].A) != Numerics.F32(stack[sp].A) ? 1 : 0; break;
                case Op.F32Lt: sp--; stack[sp - 1].A = Numerics.F32(stack[sp - 1].A) < Numerics.F32(stack[sp].A) ? 1 : 0; break;
                case Op.F32Gt: sp--; stack[sp - 1].A = Numerics.F32(stack[sp - 1].A) > Numerics.F32(stack[sp].A) ? 1 : 0; break;
                case Op.F32Le: sp--; stack[sp - 1].A = Numerics.F32(stack[sp - 1].A) <= Numerics.F32(stack[sp].A) ? 1 : 0; break;
                case Op.F32Ge: sp--; stack[sp - 1].A = Numerics.F32(stack[sp - 1].A) >= Numerics.F32(stack[sp].A) ? 1 : 0; break;
                case Op.F64Eq: sp--; stack[sp - 1].A = Numerics.F64(stack[sp - 1].A) == Numerics.F64(stack[sp].A) ? 1 : 0; break;
                case Op.F64Ne: sp--; stack[sp - 1].A = Numerics.F64(stack[sp - 1].A) != Numerics.F64(stack[sp].A) ? 1 : 0; break;
                case Op.F64Lt: sp--; stack[sp - 1].A = Numerics.F64(stack[sp - 1].A) < Numerics.F64(stack[sp].A) ? 1 : 0; break;
                case Op.F64Gt: sp--; stack[sp - 1].A = Numerics.F64(stack[sp - 1].A) > Numerics.F64(stack[sp].A) ? 1 : 0; break;
                case Op.F64Le: sp--; stack[sp - 1].A = Numerics.F64(stack[sp - 1].A) <= Numerics.F64(stack[sp].A) ? 1 : 0; break;
                case Op.F64Ge: sp--; stack[sp - 1].A = Numerics.F64(stack[sp - 1].A) >= Numerics.F64(stack[sp].A) ? 1 : 0; break;

                case Op.I32Clz: stack[sp - 1].A = BitOperations.LeadingZeroCount((uint)stack[sp - 1].A); break;
                case Op.I32Ctz: stack[sp - 1].A = (uint)stack[sp - 1].A == 0 ? 32 : BitOperations.TrailingZeroCount((uint)stack[sp - 1].A); break;
                case Op.I32Popcnt: stack[sp - 1].A = BitOperations.PopCount((uint)stack[sp - 1].A); break;
                case Op.I32Add: sp--; stack[sp - 1].A = (int)stack[sp - 1].A + (int)stack[sp].A; break;
                case Op.I32Sub: sp--; stack[sp - 1].A = (int)stack[sp - 1].A - (int)stack[sp].A; break;
                case Op.I32Mul: sp--; stack[sp - 1].A = (int)stack[sp - 1].A * (int)stack[sp].A; break;
                case Op.I32DivS: sp--; stack[sp - 1].A = Numerics.DivS((int)stack[sp - 1].A, (int)stack[sp].A); break;
                case Op.I32DivU: sp--; stack[sp - 1].A = (int)Numerics.DivU((uint)stack[sp - 1].A, (uint)stack[sp].A); break;
                case Op.I32RemS: sp--; stack[sp - 1].A = Numerics.RemS((int)stack[sp - 1].A, (int)stack[sp].A); break;
                case Op.I32RemU: sp--; stack[sp - 1].A = (int)Numerics.RemU((uint)stack[sp - 1].A, (uint)stack[sp].A); break;
                case Op.I32And: sp--; stack[sp - 1].A = (int)stack[sp - 1].A & (int)stack[sp].A; break;
                case Op.I32Or: sp--; stack[sp - 1].A = (int)stack[sp - 1].A | (int)stack[sp].A; break;
                case Op.I32Xor: sp--; stack[sp - 1].A = (int)stack[sp - 1].A ^ (int)stack[sp].A; break;
                case Op.I32Shl: sp--; stack[sp - 1].A = (int)stack[sp - 1].A << ((int)stack[sp].A & 31); break;
                case Op.I32ShrS: sp--; stack[sp - 1].A = (int)stack[sp - 1].A >> ((int)stack[sp].A & 31); break;
                case Op.I32ShrU: sp--; stack[sp - 1].A = (int)((uint)stack[sp - 1].A >> ((int)stack[sp].A & 31)); break;
                case Op.I32Rotl: sp--; stack[sp - 1].A = Numerics.Rotl((int)stack[sp - 1].A, (int)stack[sp].A); break;
                case Op.I32Rotr: sp--; stack[sp - 1].A = Numerics.Rotr((int)stack[sp - 1].A, (int)stack[sp].A); break;
                case Op.I64Clz: stack[sp - 1].A = BitOperations.LeadingZeroCount((ulong)stack[sp - 1].A); break;
                case Op.I64Ctz: stack[sp - 1].A = stack[sp - 1].A == 0 ? 64 : BitOperations.TrailingZeroCount(stack[sp - 1].A); break;
                case Op.I64Popcnt: stack[sp - 1].A = BitOperations.PopCount((ulong)stack[sp - 1].A); break;
                case Op.I64Add: sp--; stack[sp - 1].A += stack[sp].A; break;
                case Op.I64Sub: sp--; stack[sp - 1].A -= stack[sp].A; break;
                case Op.I64Mul: sp--; stack[sp - 1].A *= stack[sp].A; break;
                case Op.I64DivS: sp--; stack[sp - 1].A = Numerics.DivS(stack[sp - 1].A, stack[sp].A); break;
                case Op.I64DivU: sp--; stack[sp - 1].A = (long)Numerics.DivU((ulong)stack[sp - 1].A, (ulong)stack[sp].A); break;
                case Op.I64RemS: sp--; stack[sp - 1].A = Numerics.RemS(stack[sp - 1].A, stack[sp].A); break;
                case Op.I64RemU: sp--; stack[sp - 1].A = (long)Numerics.RemU((ulong)stack[sp - 1].A, (ulong)stack[sp].A); break;
                case Op.I64And: sp--; stack[sp - 1].A &= stack[sp].A; break;
                case Op.I64Or: sp--; stack[sp - 1].A |= stack[sp].A; break;
                case Op.I64Xor: sp--; stack[sp - 1].A ^= stack[sp].A; break;
                case Op.I64Shl: sp--; stack[sp - 1].A <<= (int)(stack[sp].A & 63); break;
                case Op.I64ShrS: sp--; stack[sp - 1].A >>= (int)(stack[sp].A & 63); break;
                case Op.I64ShrU: sp--; stack[sp - 1].A = (long)((ulong)stack[sp - 1].A >> (int)(stack[sp].A & 63)); break;
                case Op.I64Rotl: sp--; stack[sp - 1].A = Numerics.Rotl(stack[sp - 1].A, stack[sp].A); break;
                case Op.I64Rotr: sp--; stack[sp - 1].A = Numerics.Rotr(stack[sp - 1].A, stack[sp].A); break;

                case Op.F32Abs: stack[sp - 1].A = (int)((uint)stack[sp - 1].A & 0x7FFF_FFFF); break;
                case Op.F32Neg: stack[sp - 1].A = (int)((uint)stack[sp - 1].A ^ 0x8000_0000); break;
                case Op.F32Ceil: stack[sp - 1].A = Numerics.Bits(Numerics.Ceil(Numerics.F32(stack[sp - 1].A))); break;
                case Op.F32Floor: stack[sp - 1].A = Numerics.Bits(Numerics.Floor(Numerics.F32(stack[sp - 1].A))); break;
                case Op.F32Trunc: stack[sp - 1].A = Numerics.Bits(Numerics.Trunc(Numerics.F32(stack[sp - 1].A))); break;
                case Op.F32Nearest: stack[sp - 1].A = Numerics.Bits(Numerics.Nearest(Numerics.F32(stack[sp - 1].A))); break;
                case Op.F32Sqrt: stack[sp - 1].A = Numerics.Bits(MathF.Sqrt(Numerics.F32(stack[sp - 1].A))); break;
                case Op.F32Add: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F32(stack[sp - 1].A) + Numerics.F32(stack[sp].A)); break;
                case Op.F32Sub: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F32(stack[sp - 1].A) - Numerics.F32(stack[sp].A)); break;
                case Op.F32Mul: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F32(stack[sp - 1].A) * Numerics.F32(stack[sp].A)); break;
                case Op.F32Div: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F32(stack[sp - 1].A) / Numerics.F32(stack[sp].A)); break;
                case Op.F32Min: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.Min(Numerics.F32(stack[sp - 1].A), Numerics.F32(stack[sp].A))); break;
                case Op.F32Max: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.Max(Numerics.F32(stack[sp - 1].A), Numerics.F32(stack[sp].A))); break;
                case Op.F32Copysign: sp--; stack[sp - 1].A = (int)(((uint)stack[sp - 1].A & 0x7FFF_FFFF) | ((uint)stack[sp].A & 0x8000_0000)); break;
                case Op.F64Abs: stack[sp - 1].A &= 0x7FFF_FFFF_FFFF_FFFF; break;
                case Op.F64Neg: stack[sp - 1].A ^= unchecked((long)0x8000_0000_0000_0000); break;
                case Op.F64Ceil: stack[sp - 1].A = Numerics.Bits(Numerics.Ceil(Numerics.F64(stack[sp - 1].A))); break;
                case Op.F64Floor: stack[sp - 1].A = Numerics.Bits(Numerics.Floor(Numerics.F64(stack[sp - 1].A))); break;
                case Op.F64Trunc: stack[sp - 1].A = Numerics.Bits(Numerics.Trunc(Numerics.F64(stack[sp - 1].A))); break;
                case Op.F64Nearest: stack[sp - 1].A = Numerics.Bits(Numerics.Nearest(Numerics.F64(stack[sp - 1].A))); break;
                case Op.F64Sqrt: stack[sp - 1].A = Numerics.Bits(Math.Sqrt(Numerics.F64(stack[sp - 1].A))); break;
                case Op.F64Add: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F64(stack[sp - 1].A) + Numerics.F64(stack[sp].A)); break;
                case Op.F64Sub: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F64(stack[sp - 1].A) - Numerics.F64(stack[sp].A)); break;
                case Op.F64Mul: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F64(stack[sp - 1].A) * Numerics.F64(stack[sp].A)); break;
                case Op.F64Div: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.F64(stack[sp - 1].A) / Numerics.F64(stack[sp].A)); break;
                case Op.F64Min: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.Min(Numerics.F64(stack[sp - 1].A), Numerics.F64(stack[sp].A))); break;
                case Op.F64Max: sp--; stack[sp - 1].A = Numerics.Bits(Numerics.Max(Numerics.F64(stack[sp - 1].A), Numerics.F64(stack[sp].A))); break;
                case Op.F64Copysign: sp--; stack[sp - 1].A = (stack[sp - 1].A & 0x7FFF_FFFF_FFFF_FFFF) | (stack[sp].A & unchecked((long)0x8000_0000_0000_0000)); break;

                case Op.I32WrapI64: stack[sp - 1].A = (int)stack[sp - 1].A; break;
                case Op.I32TruncF32S: stack[sp - 1].A = Numerics.TruncI32(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I32TruncF32U: stack[sp - 1].A = (int)Numerics.TruncU32(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I32TruncF64S: stack[sp - 1].A = Numerics.TruncI32(Numerics.F64(stack[sp - 1].A)); break;
                case Op.I32TruncF64U: stack[sp - 1].A = (int)Numerics.TruncU32(Numerics.F64(stack[sp - 1].A)); break;
                case Op.I64ExtendI32S: stack[sp - 1].A = (int)stack[sp - 1].A; break;
                case Op.I64ExtendI32U: stack[sp - 1].A = (uint)stack[sp - 1].A; break;
                case Op.I64TruncF32S: stack[sp - 1].A = Numerics.TruncI64(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I64TruncF32U: stack[sp - 1].A = (long)Numerics.TruncU64(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I64TruncF64S: stack[sp - 1].A = Numerics.TruncI64(Numerics.F64(stack[sp - 1].A)); break;
                case Op.I64TruncF64U: stack[sp - 1].A = (long)Numerics.TruncU64(Numerics.F64(stack[sp - 1].A)); break;
                case Op.F32ConvertI32S: stack[sp - 1].A = Numerics.Bits((float)(int)stack[sp - 1].A); break;
                case Op.F32ConvertI32U: stack[sp - 1].A = Numerics.Bits((float)(uint)stack[sp - 1].A); break;
                case Op.F32ConvertI64S: stack[sp - 1].A = Numerics.Bits(Numerics.ConvertI64ToF32(stack[sp - 1].A)); break;
                case Op.F32ConvertI64U: stack[sp - 1].A = Numerics.Bits(Numerics.ConvertU64ToF32((ulong)stack[sp - 1].A)); break;
                case Op.F32DemoteF64: stack[sp - 1].A = Numerics.Bits(Numerics.Demote(Numerics.F64(stack[sp - 1].A))); break;
                case Op.F64ConvertI32S: stack[sp - 1].A = Numerics.Bits((double)(int)stack[sp - 1].A); break;
                case Op.F64ConvertI32U: stack[sp - 1].A = Numerics.Bits((double)(uint)stack[sp - 1].A); break;
                case Op.F64ConvertI64S: stack[sp - 1].A = Numerics.Bits((double)stack[sp - 1].A); break;
                case Op.F64ConvertI64U: stack[sp - 1].A = Numerics.Bits(Numerics.ConvertU64ToF64((ulong)stack[sp - 1].A)); break;
                case Op.F64PromoteF32: stack[sp - 1].A = Numerics.Bits(Numerics.Promote(Numerics.F32(stack[sp - 1].A))); break;
                case Op.I32ReinterpretF32:
                case Op.F32ReinterpretI32:
                    stack[sp - 1].A = (int)stack[sp - 1].A;
                    break;
                case Op.I64ReinterpretF64:
                case Op.F64ReinterpretI64:
                    break;
                case Op.I32Extend8S: stack[sp - 1].A = (sbyte)stack[sp - 1].A; break;
                case Op.I32Extend16S: stack[sp - 1].A = (short)stack[sp - 1].A; break;
                case Op.I64Extend8S: stack[sp - 1].A = (sbyte)stack[sp - 1].A; break;
                case Op.I64Extend16S: stack[sp - 1].A = (short)stack[sp - 1].A; break;
                case Op.I64Extend32S: stack[sp - 1].A = (int)stack[sp - 1].A; break;

                case Op.I32TruncSatF32S: stack[sp - 1].A = Numerics.TruncSatI32(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I32TruncSatF32U: stack[sp - 1].A = (int)Numerics.TruncSatU32(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I32TruncSatF64S: stack[sp - 1].A = Numerics.TruncSatI32(Numerics.F64(stack[sp - 1].A)); break;
                case Op.I32TruncSatF64U: stack[sp - 1].A = (int)Numerics.TruncSatU32(Numerics.F64(stack[sp - 1].A)); break;
                case Op.I64TruncSatF32S: stack[sp - 1].A = Numerics.TruncSatI64(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I64TruncSatF32U: stack[sp - 1].A = (long)Numerics.TruncSatU64(Numerics.F32(stack[sp - 1].A)); break;
                case Op.I64TruncSatF64S: stack[sp - 1].A = Numerics.TruncSatI64(Numerics.F64(stack[sp - 1].A)); break;
                case Op.I64TruncSatF64U: stack[sp - 1].A = (long)Numerics.TruncSatU64(Numerics.F64(stack[sp - 1].A)); break;

                case Op.I64Add128:
                {
                    sp -= 2;
                    var (low, high) = Numerics.Add128(stack[sp - 2].A, stack[sp - 1].A, stack[sp].A, stack[sp + 1].A);
                    stack[sp - 2].A = low;
                    stack[sp - 1].A = high;
                    break;
                }

                case Op.I64Sub128:
                {
                    sp -= 2;
                    var (low, high) = Numerics.Sub128(stack[sp - 2].A, stack[sp - 1].A, stack[sp].A, stack[sp + 1].A);
                    stack[sp - 2].A = low;
                    stack[sp - 1].A = high;
                    break;
                }

                case Op.I64MulWideS:
                {
                    var (low, high) = Numerics.MulWideS(stack[sp - 2].A, stack[sp - 1].A);
                    stack[sp - 2].A = low;
                    stack[sp - 1].A = high;
                    break;
                }

                case Op.I64MulWideU:
                {
                    var (low, high) = Numerics.MulWideU(stack[sp - 2].A, stack[sp - 1].A);
                    stack[sp - 2].A = low;
                    stack[sp - 1].A = high;
                    break;
                }

                // ------------------------------------------------------------------------------------------------
                // References
                // ------------------------------------------------------------------------------------------------
                case Op.RefNull:
                    stack[sp++] = default;
                    break;

                case Op.RefIsNull:
                {
                    ref var slot = ref stack[sp - 1];
                    slot.A = slot.R is null ? 1 : 0;
                    slot.R = null;
                    break;
                }

                case Op.RefFunc:
                    stack[sp++] = new Slot { R = instance.Functions[ins.I0] };
                    break;

                case Op.RefEq:
                    sp--;
                    stack[sp - 1] = new Slot { A = RuntimeTypes.RefEquals(stack[sp - 1].R, stack[sp].R) ? 1 : 0 };
                    break;

                case Op.RefAsNonNull:
                    if (stack[sp - 1].R is null)
                        throw Traps.NullReference();

                    break;

                case Op.BrOnNull:
                    sp--;
                    if (stack[sp].R is null)
                    {
                        if (ins.L0 != 0)
                        {
                            CopySlots(stack, sp - ins.I1, ob + ins.I2, ins.I1);
                            sp = ob + ins.I2 + ins.I1;
                        }

                        if (ins.I0 < pc)
                        {
                            pc = ins.I0;
                            goto BackEdge;
                        }

                        pc = ins.I0;
                    }
                    else
                    {
                        sp++;
                    }

                    break;

                case Op.BrOnNonNull:
                    if (stack[sp - 1].R is not null)
                    {
                        if (ins.L0 != 0)
                        {
                            CopySlots(stack, sp - ins.I1, ob + ins.I2, ins.I1);
                            sp = ob + ins.I2 + ins.I1;
                        }

                        if (ins.I0 < pc)
                        {
                            pc = ins.I0;
                            goto BackEdge;
                        }

                        pc = ins.I0;
                    }
                    else
                    {
                        sp--;
                    }

                    break;

                case Op.BrOnCast:
                case Op.BrOnCastFail:
                {
                    var matches = RuntimeTypes.Matches(stack[sp - 1].R, (WasmHeapKind)(ins.L1 >> 1), (DefinedType?)ins.O, (ins.L1 & 1) != 0);
                    if (matches == (ins.Op == Op.BrOnCast))
                    {
                        if (ins.L0 != 0)
                        {
                            CopySlots(stack, sp - ins.I1, ob + ins.I2, ins.I1);
                            sp = ob + ins.I2 + ins.I1;
                        }

                        if (ins.I0 < pc)
                        {
                            pc = ins.I0;
                            goto BackEdge;
                        }

                        pc = ins.I0;
                    }

                    break;
                }

                case Op.RefTest:
                case Op.RefTestNull:
                {
                    ref var slot = ref stack[sp - 1];
                    slot.A = RuntimeTypes.Matches(slot.R, (WasmHeapKind)ins.I0, (DefinedType?)ins.O, ins.I1 != 0) ? 1 : 0;
                    slot.R = null;
                    break;
                }

                case Op.RefCast:
                case Op.RefCastNull:
                    if (!RuntimeTypes.Matches(stack[sp - 1].R, (WasmHeapKind)ins.I0, (DefinedType?)ins.O, ins.I1 != 0))
                        throw Traps.CastFailure();

                    break;

                case Op.AnyConvertExtern:
                case Op.ExternConvertAny:
                    break;

                case Op.RefI31:
                {
                    ref var slot = ref stack[sp - 1];
                    slot.R = new WasmI31Reference((int)(slot.A << 33 >> 33));
                    break;
                }

                case Op.I31GetS:
                case Op.I31GetU:
                {
                    ref var slot = ref stack[sp - 1];
                    var value = ((WasmI31Reference?)slot.R ?? throw Traps.NullI31Reference()).Value;
                    slot.A = ins.Op == Op.I31GetS ? value : value & 0x7FFF_FFFF;
                    slot.R = null;
                    break;
                }

                default:
                {
                    // A copy of sp is passed by reference: the address of sp would make the JIT keep it in memory in the whole loop
                    var extendedSp = sp;
                    ExecuteExtended(in ins, instance, stack, ref extendedSp);
                    sp = extendedSp;
                    stack = _stack;
                    break;
                }
            }

            continue;

        BackEdge:
            // A loop: the host can interrupt it, and a loop that runs many times continues in .NET code (OSR)
            CheckInterrupt();
            if (++code.BackEdgeCount >= _osrThreshold && TryOsr(function, fp, pc, out exception))
            {
                stack = _stack;
                if (exception is not null)
                {
                    // The compiled code did not catch the exception: the callers handle it
                    unwindCurrentFrame = true;
                    goto HandleException;
                }

                arity = code.Results.Length;
                sp = fp + arity;
                goto DoReturn;
            }

            continue;

        DoCall:
            if (callee is CodeFunction interpreted && interpreted.Code.Compiled is null && !(ShouldCompile(interpreted.Code) && CompileForCall(interpreted)))
            {
                PushFrame(function, pc, fp);
                fp = sp - interpreted.DefinedType.Parameters.Length;
                function = interpreted;
                code = interpreted.Code;
                instrs = code.Code;
                instance = interpreted.Instance;
                sp = EnterFrame(code, fp);
                ob = sp;
                stack = _stack;
                pc = 0;
                CheckInterrupt();
                continue;
            }

            {
                var calleeFp = sp - callee!.DefinedType.Parameters.Length;
                exception = CallExternal(callee, calleeFp, sp);
                stack = _stack;
                if (exception is not null)
                    goto HandleException;

                sp = calleeFp + callee.DefinedType.Results.Length;
                continue;
            }

        DoTailCall:
            {
                var parameterCount = callee!.DefinedType.Parameters.Length;
                CopySlots(stack, sp - parameterCount, fp, parameterCount);
                if (callee is CodeFunction tailInterpreted && tailInterpreted.Code.Compiled is null && !(ShouldCompile(tailInterpreted.Code) && CompileForCall(tailInterpreted)))
                {
                    function = tailInterpreted;
                    code = tailInterpreted.Code;
                    instrs = code.Code;
                    instance = tailInterpreted.Instance;
                    sp = EnterFrame(code, fp);
                    ob = sp;
                    stack = _stack;
                    pc = 0;
                    CheckInterrupt();
                    continue;
                }

                exception = CallExternal(callee, fp, fp + parameterCount);
                stack = _stack;
                if (exception is not null)
                {
                    // The frame of the tail call is gone: the handlers of the caller handle the exception
                    unwindCurrentFrame = true;
                    goto HandleException;
                }

                arity = callee.DefinedType.Results.Length;
                sp = fp + arity;
                goto DoReturn;
            }

        DoReturn:
            CopySlots(stack, sp - arity, fp, arity);
            sp = fp + arity;
            if (_frameCount == entryFrames)
                return;

            {
                ref var frame = ref _frames[--_frameCount];
                function = frame.Function;
                frame.Function = null!;
                code = function.Code;
                instrs = code.Code;
                instance = function.Instance;
                pc = frame.Pc;
                fp = frame.Fp;
                ob = fp + code.Locals.Length;
                continue;
            }

        HandleException:
            while (true)
            {
                if (!unwindCurrentFrame && FindHandler(code, instance, pc - 1, exception!, out var clause))
                {
                    sp = ob + clause.Height;
                    var payload = exception!.Payload;
                    switch (clause.Kind)
                    {
                        case CatchKind.Catch:
                            Array.Copy(payload, 0, stack, sp, payload.Length);
                            sp += payload.Length;
                            break;
                        case CatchKind.CatchRef:
                            Array.Copy(payload, 0, stack, sp, payload.Length);
                            sp += payload.Length;
                            stack[sp++] = new Slot { R = exception };
                            break;
                        case CatchKind.CatchAllRef:
                            stack[sp++] = new Slot { R = exception };
                            break;
                    }

                    pc = clause.Pc;
                    exception = null;
                    break;
                }

                unwindCurrentFrame = false;
                if (_frameCount == entryFrames)
                    throw new WasmThrowException(exception!);

                ref var frame = ref _frames[--_frameCount];
                function = frame.Function;
                frame.Function = null!;
                code = function.Code;
                instrs = code.Code;
                instance = function.Instance;
                pc = frame.Pc;
                fp = frame.Fp;
                ob = fp + code.Locals.Length;
            }
        }
    }
#pragma warning restore MA0051

    /// <summary>Compiles a function that is called: whether it is compiled (it is called as .NET code).</summary>
    private static bool CompileForCall(CodeFunction function)
    {
        TryCompile(function);
        return function.Code.Compiled is not null;
    }

    internal static bool FindHandler(CompiledCode code, ManagedInstance instance, int pc, ManagedException exception, out CatchClause clause) => FindClause(code, instance, pc, exception, out clause) >= 0;

    /// <summary>
    /// Finds the catch clause that handles an exception thrown at <paramref name="pc"/> (the innermost handler first), and
    /// returns its index in the clauses of the handlers in their order, or -1.
    /// </summary>
    internal static int FindClause(CompiledCode code, ManagedInstance instance, int pc, ManagedException exception, out CatchClause clause)
    {
        var handlers = code.Handlers;
        var index = 0;
        foreach (var handler in handlers)
        {
            index += handler.Catches.Length;
        }

        for (var i = handlers.Length - 1; i >= 0; i--)
        {
            var handler = handlers[i];
            index -= handler.Catches.Length;
            if (pc < handler.Start || pc >= handler.End)
                continue;

            for (var c = 0; c < handler.Catches.Length; c++)
            {
                var candidate = handler.Catches[c];
                if (candidate.Kind is CatchKind.CatchAll or CatchKind.CatchAllRef || ReferenceEquals(instance.Tags[candidate.Tag], exception.ManagedTag))
                {
                    clause = candidate;
                    return index + c;
                }
            }
        }

        clause = default;
        return -1;
    }

    internal static ManagedFunction GetIndirectFunction(ManagedTable table, long indexValue, DefinedType type)
    {
        var index = Address(table, indexValue);
        if (index >= table.Size)
            throw Traps.UndefinedElement();

        var function = (ManagedFunction?)table.Elements[(int)index] ?? throw Traps.UninitializedElement();
        if (!function.DefinedType.IsSubtypeOf(type))
            throw Traps.IndirectCallTypeMismatch();

        return function;
    }
}
