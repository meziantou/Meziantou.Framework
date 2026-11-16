// A port of the branch/call/jump (BCJ) filters of xz-utils (liblzma/simple), which are in the public domain.
using System.Buffers.Binary;

namespace Meziantou.Framework.Compression.Internals.Filters;

/// <summary>
/// The BCJ filters of xz-utils: they convert the relative addresses of the branch instructions of an architecture to
/// absolute addresses (encoding) and back (decoding), so that repeated calls to the same function compress better. A call
/// converts the instructions that start in the buffer and end before its end, and returns the number of bytes it is done
/// with: the caller keeps the others, and converts them with the next data or leaves them unchanged at the end of the stream.
/// </summary>
internal sealed class BranchFilter
{
    private static readonly bool[] X86MaskToAllowedStatus = [true, true, true, false, true, false, false, false];
    private static readonly uint[] X86MaskToBitNumber = [0, 1, 2, 2, 3, 3, 3, 3];
    private static readonly uint[] IA64BranchTable =
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        4, 4, 6, 6, 0, 0, 7, 7,
        4, 4, 0, 0, 4, 4, 0, 0,
    ];

    private readonly BranchFilterKind _kind;
    private readonly bool _isEncoder;
    private uint _position;
    private uint _x86PrevMask;
    private uint _x86PrevPosition;

    public BranchFilter(BranchFilterKind kind, uint startOffset, bool isEncoder)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The filter is not supported.");

        _kind = kind;
        _isEncoder = isEncoder;
        _position = startOffset;
        _x86PrevPosition = unchecked(startOffset - 5);
    }

    /// <summary>Gets the largest number of bytes a call can leave unconverted at the end of the buffer.</summary>
    public static int MaxUnprocessed => 16;

    /// <summary>Converts the instructions of <paramref name="buffer"/> in place.</summary>
    /// <returns>The number of bytes at the start of <paramref name="buffer"/> that are done.</returns>
    public int Convert(Span<byte> buffer)
    {
        var processed = _kind switch
        {
            BranchFilterKind.X86 => X86(buffer),
            BranchFilterKind.PowerPC => PowerPC(buffer),
            BranchFilterKind.IA64 => IA64(buffer),
            BranchFilterKind.Arm => Arm(buffer),
            BranchFilterKind.ArmThumb => ArmThumb(buffer),
            BranchFilterKind.Sparc => Sparc(buffer),
            BranchFilterKind.Arm64 => Arm64(buffer),
            BranchFilterKind.RiscV => _isEncoder ? RiscVEncode(buffer) : RiscVDecode(buffer),
            _ => throw new InvalidOperationException(),
        };

        _position += (uint)processed;
        return processed;
    }

    private static bool Test86MSByte(byte b) => b is 0 or 0xFF;

    private int X86(Span<byte> buffer)
    {
        if (buffer.Length < 5)
            return 0;

        var nowPos = _position;
        var prevMask = _x86PrevMask;
        var prevPos = _x86PrevPosition;
        if (nowPos - prevPos > 5)
        {
            prevPos = nowPos - 5;
        }

        var limit = buffer.Length - 5;
        var bufferPos = 0;
        while (bufferPos <= limit)
        {
            var b = buffer[bufferPos];
            if (b is not 0xE8 and not 0xE9)
            {
                bufferPos++;
                continue;
            }

            var offset = nowPos + (uint)bufferPos - prevPos;
            prevPos = nowPos + (uint)bufferPos;
            if (offset > 5)
            {
                prevMask = 0;
            }
            else
            {
                for (uint i = 0; i < offset; i++)
                {
                    prevMask &= 0x77;
                    prevMask <<= 1;
                }
            }

            b = buffer[bufferPos + 4];
            if (Test86MSByte(b) && X86MaskToAllowedStatus[(prevMask >> 1) & 0x7] && (prevMask >> 1) < 0x10)
            {
                var src = ((uint)b << 24) | ((uint)buffer[bufferPos + 3] << 16) | ((uint)buffer[bufferPos + 2] << 8) | buffer[bufferPos + 1];
                uint dest;
                while (true)
                {
                    dest = _isEncoder ? src + (nowPos + (uint)bufferPos + 5) : src - (nowPos + (uint)bufferPos + 5);
                    if (prevMask == 0)
                        break;

                    var index = X86MaskToBitNumber[prevMask >> 1];
                    b = (byte)(dest >> (int)(24 - (index * 8)));
                    if (!Test86MSByte(b))
                        break;

                    src = dest ^ ((1u << (int)(32 - (index * 8))) - 1);
                }

                buffer[bufferPos + 4] = (byte)~(((dest >> 24) & 1) - 1);
                buffer[bufferPos + 3] = (byte)(dest >> 16);
                buffer[bufferPos + 2] = (byte)(dest >> 8);
                buffer[bufferPos + 1] = (byte)dest;
                bufferPos += 5;
                prevMask = 0;
            }
            else
            {
                bufferPos++;
                prevMask |= 1;
                if (Test86MSByte(b))
                {
                    prevMask |= 0x10;
                }
            }
        }

        _x86PrevMask = prevMask;
        _x86PrevPosition = prevPos;
        return bufferPos;
    }

    private int PowerPC(Span<byte> buffer)
    {
        var size = buffer.Length & ~3;
        var nowPos = _position;
        int i;
        for (i = 0; i < size; i += 4)
        {
            if ((buffer[i] >> 2) == 0x12 && (buffer[i + 3] & 3) == 1)
            {
                var src = (((uint)buffer[i] & 3) << 24) | ((uint)buffer[i + 1] << 16) | ((uint)buffer[i + 2] << 8) | ((uint)buffer[i + 3] & ~3u);
                var dest = _isEncoder ? nowPos + (uint)i + src : src - (nowPos + (uint)i);
                buffer[i] = (byte)(0x48 | ((dest >> 24) & 0x03));
                buffer[i + 1] = (byte)(dest >> 16);
                buffer[i + 2] = (byte)(dest >> 8);
                buffer[i + 3] = (byte)((buffer[i + 3] & 0x03u) | (dest & ~3u));
            }
        }

        return i;
    }

    private int IA64(Span<byte> buffer)
    {
        var size = buffer.Length & ~15;
        var nowPos = _position;
        int i;
        for (i = 0; i < size; i += 16)
        {
            var mask = IA64BranchTable[buffer[i] & 0x1F];
            var bitPos = 5;
            for (var slot = 0; slot < 3; slot++, bitPos += 41)
            {
                if (((mask >> slot) & 1) == 0)
                    continue;

                var bytePos = bitPos >> 3;
                var bitRes = bitPos & 0x7;
                ulong instruction = 0;
                for (var j = 0; j < 6; j++)
                {
                    instruction |= (ulong)buffer[i + j + bytePos] << (8 * j);
                }

                var instNorm = instruction >> bitRes;
                if (((instNorm >> 37) & 0xF) == 0x5 && ((instNorm >> 9) & 0x7) == 0)
                {
                    var src = (uint)((instNorm >> 13) & 0xFFFFF);
                    src |= (uint)((instNorm >> 36) & 1) << 20;
                    src <<= 4;
                    var dest = _isEncoder ? nowPos + (uint)i + src : src - (nowPos + (uint)i);
                    dest >>= 4;
                    instNorm &= ~((ulong)0x8FFFFF << 13);
                    instNorm |= (ulong)(dest & 0xFFFFF) << 13;
                    instNorm |= (ulong)(dest & 0x100000) << (36 - 20);
                    instruction &= (1u << bitRes) - 1;
                    instruction |= instNorm << bitRes;
                    for (var j = 0; j < 6; j++)
                    {
                        buffer[i + j + bytePos] = (byte)(instruction >> (8 * j));
                    }
                }
            }
        }

        return i;
    }

    private int Arm(Span<byte> buffer)
    {
        var size = buffer.Length & ~3;
        var nowPos = _position;
        int i;
        for (i = 0; i < size; i += 4)
        {
            if (buffer[i + 3] == 0xEB)
            {
                var src = ((uint)buffer[i + 2] << 16) | ((uint)buffer[i + 1] << 8) | buffer[i];
                src <<= 2;
                var dest = _isEncoder ? nowPos + (uint)i + 8 + src : src - (nowPos + (uint)i + 8);
                dest >>= 2;
                buffer[i + 2] = (byte)(dest >> 16);
                buffer[i + 1] = (byte)(dest >> 8);
                buffer[i] = (byte)dest;
            }
        }

        return i;
    }

    private int ArmThumb(Span<byte> buffer)
    {
        if (buffer.Length < 4)
            return 0;

        var size = buffer.Length - 4;
        var nowPos = _position;
        int i;
        for (i = 0; i <= size; i += 2)
        {
            if ((buffer[i + 1] & 0xF8) == 0xF0 && (buffer[i + 3] & 0xF8) == 0xF8)
            {
                var src = (((uint)buffer[i + 1] & 7) << 19) | ((uint)buffer[i] << 11) | (((uint)buffer[i + 3] & 7) << 8) | buffer[i + 2];
                src <<= 1;
                var dest = _isEncoder ? nowPos + (uint)i + 4 + src : src - (nowPos + (uint)i + 4);
                dest >>= 1;
                buffer[i + 1] = (byte)(0xF0 | ((dest >> 19) & 0x7));
                buffer[i] = (byte)(dest >> 11);
                buffer[i + 3] = (byte)(0xF8 | ((dest >> 8) & 0x7));
                buffer[i + 2] = (byte)dest;
                i += 2;
            }
        }

        return i;
    }

    private int Sparc(Span<byte> buffer)
    {
        var size = buffer.Length & ~3;
        var nowPos = _position;
        int i;
        for (i = 0; i < size; i += 4)
        {
            if ((buffer[i] == 0x40 && (buffer[i + 1] & 0xC0) == 0x00) || (buffer[i] == 0x7F && (buffer[i + 1] & 0xC0) == 0xC0))
            {
                var src = BinaryPrimitives.ReadUInt32BigEndian(buffer[i..]);
                src <<= 2;
                var dest = _isEncoder ? nowPos + (uint)i + src : src - (nowPos + (uint)i);
                dest >>= 2;
                dest = (((0 - ((dest >> 22) & 1)) << 22) & 0x3FFFFFFF) | (dest & 0x3FFFFF) | 0x40000000;
                BinaryPrimitives.WriteUInt32BigEndian(buffer[i..], dest);
            }
        }

        return i;
    }

    private int Arm64(Span<byte> buffer)
    {
        var nowPos = _position;
        int i;
        for (i = 0; i + 4 <= buffer.Length; i += 4)
        {
            var pc = nowPos + (uint)i;
            var instruction = BinaryPrimitives.ReadUInt32LittleEndian(buffer[i..]);
            if ((instruction >> 26) == 0x25)
            {
                // BL
                var src = instruction;
                instruction = 0x94000000;
                pc >>= 2;
                if (!_isEncoder)
                {
                    pc = 0 - pc;
                }

                instruction |= (src + pc) & 0x03FFFFFF;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[i..], instruction);
            }
            else if ((instruction & 0x9F000000) == 0x90000000)
            {
                // ADRP
                var src = ((instruction >> 29) & 3) | ((instruction >> 3) & 0x001FFFFC);
                if (((src + 0x00020000) & 0x001C0000) != 0)
                    continue;

                instruction &= 0x9000001F;
                pc >>= 12;
                if (!_isEncoder)
                {
                    pc = 0 - pc;
                }

                var dest = src + pc;
                instruction |= (dest & 3) << 29;
                instruction |= (dest & 0x0003FFFC) << 3;
                instruction |= (0 - (dest & 0x00020000)) & 0x00E00000;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[i..], instruction);
            }
        }

        return i;
    }

    // AUIPC rd == inst2 rs1, and the lowest two bits of the opcode of inst2 are set
    private static bool IsNotAuipcPair(uint auipc, uint inst2) => (((auipc << 8) ^ (inst2 - 3)) & 0xF8003) != 0;

    // AUIPC rd == x2, bits 12 and 13 are set, and inst2 rs1 is neither x0 nor x2
    private static bool IsNotSpecialAuipc(uint auipc, uint inst2Rs1) => (uint)(auipc - 0x3117) << 18 >= (inst2Rs1 & 0x1D);

    private int RiscVEncode(Span<byte> buffer)
    {
        if (buffer.Length < 8)
            return 0;

        var size = buffer.Length - 8;
        var nowPos = _position;
        int i;
        for (i = 0; i <= size; i += 2)
        {
            uint inst = buffer[i];
            if (inst == 0xEF)
            {
                // JAL
                uint b1 = buffer[i + 1];
                if ((b1 & 0x0D) != 0)
                    continue;

                uint b2 = buffer[i + 2];
                uint b3 = buffer[i + 3];
                var pc = nowPos + (uint)i;
                var addr = ((b1 & 0xF0) << 8) | ((b2 & 0x0F) << 16) | ((b2 & 0x10) << 7) | ((b2 & 0xE0) >> 4) | ((b3 & 0x7F) << 4) | ((b3 & 0x80) << 13);
                addr += pc;
                buffer[i + 1] = (byte)((b1 & 0x0F) | ((addr >> 13) & 0xF0));
                buffer[i + 2] = (byte)(addr >> 9);
                buffer[i + 3] = (byte)(addr >> 1);
                i += 4 - 2;
            }
            else if ((inst & 0x7F) == 0x17)
            {
                // AUIPC
                inst |= (uint)buffer[i + 1] << 8;
                inst |= (uint)buffer[i + 2] << 16;
                inst |= (uint)buffer[i + 3] << 24;
                if ((inst & 0xE80) != 0)
                {
                    // AUIPC's rd is neither x0 nor x2
                    var inst2 = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(i + 4)..]);
                    if (IsNotAuipcPair(inst, inst2))
                    {
                        i += 6 - 2;
                        continue;
                    }

                    var addr = inst & 0xFFFFF000;
                    addr += (inst2 >> 20) - ((inst2 >> 19) & 0x1000);
                    addr += nowPos + (uint)i;
                    inst = 0x17 | (2 << 7) | (inst2 << 12);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer[i..], inst);
                    BinaryPrimitives.WriteUInt32BigEndian(buffer[(i + 4)..], addr);
                }
                else
                {
                    // AUIPC's rd is x0 or x2: a "fake" pair keeps the filter bijective
                    var fakeRs1 = inst >> 27;
                    if (IsNotSpecialAuipc(inst, fakeRs1))
                    {
                        i += 4 - 2;
                        continue;
                    }

                    var fakeAddr = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(i + 4)..]);
                    var fakeInst2 = (inst >> 12) | (fakeAddr << 20);
                    inst = 0x17 | (fakeRs1 << 7) | (fakeAddr & 0xFFFFF000);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer[i..], inst);
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer[(i + 4)..], fakeInst2);
                }

                i += 8 - 2;
            }
        }

        return i;
    }

    private int RiscVDecode(Span<byte> buffer)
    {
        if (buffer.Length < 8)
            return 0;

        var size = buffer.Length - 8;
        var nowPos = _position;
        int i;
        for (i = 0; i <= size; i += 2)
        {
            uint inst = buffer[i];
            if (inst == 0xEF)
            {
                // JAL
                uint b1 = buffer[i + 1];
                if ((b1 & 0x0D) != 0)
                    continue;

                uint b2 = buffer[i + 2];
                uint b3 = buffer[i + 3];
                var pc = nowPos + (uint)i;
                var addr = ((b1 & 0xF0) << 13) | (b2 << 9) | (b3 << 1);
                addr -= pc;
                buffer[i + 1] = (byte)((b1 & 0x0F) | ((addr >> 8) & 0xF0));
                buffer[i + 2] = (byte)(((addr >> 16) & 0x0F) | ((addr >> 7) & 0x10) | ((addr << 4) & 0xE0));
                buffer[i + 3] = (byte)(((addr >> 4) & 0x7F) | ((addr >> 13) & 0x80));
                i += 4 - 2;
            }
            else if ((inst & 0x7F) == 0x17)
            {
                // AUIPC
                uint inst2;
                inst |= (uint)buffer[i + 1] << 8;
                inst |= (uint)buffer[i + 2] << 16;
                inst |= (uint)buffer[i + 3] << 24;
                if ((inst & 0xE80) != 0)
                {
                    // AUIPC's rd is neither x0 nor x2: a "fake" pair
                    inst2 = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(i + 4)..]);
                    if (IsNotAuipcPair(inst, inst2))
                    {
                        i += 6 - 2;
                        continue;
                    }

                    var addr = inst & 0xFFFFF000;
                    addr += inst2 >> 20;
                    inst = 0x17 | (2 << 7) | (inst2 << 12);
                    inst2 = addr;
                }
                else
                {
                    // AUIPC's rd is x0 or x2: a "real" pair
                    var inst2Rs1 = inst >> 27;
                    if (IsNotSpecialAuipc(inst, inst2Rs1))
                    {
                        i += 4 - 2;
                        continue;
                    }

                    var addr = BinaryPrimitives.ReadUInt32BigEndian(buffer[(i + 4)..]);
                    addr -= nowPos + (uint)i;
                    inst2 = (inst >> 12) | (addr << 20);
                    inst = 0x17 | (inst2Rs1 << 7) | ((addr + 0x800) & 0xFFFFF000);
                }

                BinaryPrimitives.WriteUInt32LittleEndian(buffer[i..], inst);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[(i + 4)..], inst2);
                i += 8 - 2;
            }
        }

        return i;
    }
}
