using System.Buffers.Binary;

namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>
/// The linear memory of the module: every access is checked, and an access outside of the memory fails with
/// <see cref="WasiErrno.Fault"/>. The addresses are 64-bit, so that the sums of the 32-bit pointers and offsets of the
/// module do not wrap around.
/// </summary>
internal readonly ref struct WasiMemory(Span<byte> bytes)
{
    private readonly Span<byte> _bytes = bytes;

    public int Length => _bytes.Length;

    public Span<byte> Slice(ulong address, ulong length)
    {
        var size = (ulong)_bytes.Length;
        if (address > size || length > size - address)
            throw new WasiException(WasiErrno.Fault);

        return _bytes.Slice((int)address, (int)length);
    }

    public byte ReadByte(ulong address) => Slice(address, 1)[0];

    public ushort ReadUInt16(ulong address) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(address, 2));

    public uint ReadUInt32(ulong address) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(address, 4));

    public ulong ReadUInt64(ulong address) => BinaryPrimitives.ReadUInt64LittleEndian(Slice(address, 8));

    public void WriteByte(ulong address, byte value) => Slice(address, 1)[0] = value;

    public void WriteUInt16(ulong address, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Slice(address, 2), value);

    public void WriteUInt32(ulong address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Slice(address, 4), value);

    public void WriteUInt64(ulong address, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Slice(address, 8), value);
}
