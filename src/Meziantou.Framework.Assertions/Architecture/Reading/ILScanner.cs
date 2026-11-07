#pragma warning disable MA0012 // Invalid metadata is reported with BadImageFormatException, like System.Reflection.Metadata does
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// Walks the instructions of a method body and returns the metadata tokens they refer to
internal static class ILScanner
{
    // The "no." prefix, which ILOpCode does not define
    private const ILOpCode NoPrefix = (ILOpCode)0xFE19;

    public static List<ILTokenReference> Scan(BlobReader reader)
    {
        var result = new List<ILTokenReference>();
        while (reader.RemainingBytes > 0)
        {
            var offset = reader.Offset;
            var opCode = ReadOpCode(ref reader);
            switch (GetOperand(opCode))
            {
                case OperandKind.None:
                    break;

                case OperandKind.Int8:
                    reader.Offset += 1;
                    break;

                case OperandKind.Int16:
                    reader.Offset += 2;
                    break;

                case OperandKind.Int32:
                    reader.Offset += 4;
                    break;

                case OperandKind.Int64:
                    reader.Offset += 8;
                    break;

                case OperandKind.Switch:
                    var count = reader.ReadUInt32();
                    if (count > (uint)reader.RemainingBytes / 4)
                        throw new BadImageFormatException("The switch instruction is truncated.");

                    reader.Offset += (int)count * 4;
                    break;

                case OperandKind.Token:
                    var token = reader.ReadInt32();
                    if (GetTokenKind(opCode) is { } kind)
                    {
                        result.Add(new ILTokenReference(offset, kind, MetadataTokens.EntityHandle(token)));
                    }

                    break;
            }
        }

        return result;
    }

    private static ILOpCode ReadOpCode(ref BlobReader reader)
    {
        var value = reader.ReadByte();
        return value == 0xFE ? (ILOpCode)(0xFE00 | reader.ReadByte()) : (ILOpCode)value;
    }

    private static ILTokenKind? GetTokenKind(ILOpCode opCode)
    {
        return opCode switch
        {
            ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp => ILTokenKind.Method,
            ILOpCode.Newobj => ILTokenKind.ObjectCreation,
            ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld => ILTokenKind.Field,
            ILOpCode.Ldtoken => ILTokenKind.Token,
            ILOpCode.Calli => ILTokenKind.Signature,
            ILOpCode.Ldstr => null,
            _ => ILTokenKind.Type,
        };
    }

    private static OperandKind GetOperand(ILOpCode opCode)
    {
        return opCode switch
        {
            ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
                or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
                or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
                or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned or NoPrefix => OperandKind.Int8,

            ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => OperandKind.Int16,

            ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave
                or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 => OperandKind.Int32,

            ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => OperandKind.Int64,

            ILOpCode.Switch => OperandKind.Switch,

            ILOpCode.Ldstr or ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
                or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Newarr
                or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Cpobj or ILOpCode.Initobj
                or ILOpCode.Sizeof or ILOpCode.Mkrefany or ILOpCode.Refanyval or ILOpCode.Ldtoken or ILOpCode.Constrained => OperandKind.Token,

            _ => OperandKind.None,
        };
    }

    private enum OperandKind
    {
        None,
        Int8,
        Int16,
        Int32,
        Int64,
        Switch,
        Token,
    }
}
