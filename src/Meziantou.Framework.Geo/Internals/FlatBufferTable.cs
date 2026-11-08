using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>A table of a FlatBuffers buffer: its fields are read through the vtable, every offset checked against the buffer.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly ref struct FlatBufferTable
{
    private readonly ReadOnlySpan<byte> _buffer;
    private readonly int _position;
    private readonly int _vtable;
    private readonly int _vtableLength;

    private FlatBufferTable(ReadOnlySpan<byte> buffer, int position)
    {
        var span = buffer;
        CheckRange(span, position, 4);
        _buffer = buffer;
        _position = position;
        var vtable = (long)position - BinaryPrimitives.ReadInt32LittleEndian(span[position..]);
        if (vtable < 0 || vtable > span.Length - 4)
            throw InvalidData();

        _vtable = (int)vtable;
        _vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(span[_vtable..]);
        if (_vtableLength < 4 || (_vtableLength & 1) != 0 || _vtable + _vtableLength > span.Length)
            throw InvalidData();
    }

    /// <summary>Gets the root table of a buffer, whose first four bytes are the offset of the table.</summary>
    public static FlatBufferTable GetRoot(ReadOnlySpan<byte> buffer)
    {
        CheckRange(buffer, 0, 4);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if (offset > (uint)(buffer.Length - 4))
            throw InvalidData();

        return new FlatBufferTable(buffer, (int)offset);
    }

    public bool Has(int field) => GetFieldPosition(field) != 0;

    public byte GetByte(int field, byte defaultValue = 0) => GetFieldPosition(field) is var p && p != 0 ? Read(p, 1)[0] : defaultValue;

    public bool GetBool(int field, bool defaultValue = false) => GetFieldPosition(field) is var p && p != 0 ? Read(p, 1)[0] != 0 : defaultValue;

    public ushort GetUInt16(int field, ushort defaultValue = 0) => GetFieldPosition(field) is var p && p != 0 ? BinaryPrimitives.ReadUInt16LittleEndian(Read(p, 2)) : defaultValue;

    public int GetInt32(int field, int defaultValue = 0) => GetFieldPosition(field) is var p && p != 0 ? BinaryPrimitives.ReadInt32LittleEndian(Read(p, 4)) : defaultValue;

    public ulong GetUInt64(int field, ulong defaultValue = 0) => GetFieldPosition(field) is var p && p != 0 ? BinaryPrimitives.ReadUInt64LittleEndian(Read(p, 8)) : defaultValue;

    public string? GetString(int field)
    {
        if (GetVectorRange(field, elementSize: 1) is not { } range)
            return null;

        return Encoding.UTF8.GetString(_buffer.Slice(range.Start, range.Count));
    }

    public bool TryGetTable(int field, out FlatBufferTable table)
    {
        var position = GetFieldPosition(field);
        if (position == 0)
        {
            table = default;
            return false;
        }

        table = new FlatBufferTable(_buffer, Follow(position));
        return true;
    }

    /// <summary>Gets the bytes of a vector of scalars, or an empty span when the field is absent.</summary>
    public ReadOnlySpan<byte> GetVectorBytes(int field, int elementSize, out int count)
    {
        if (GetVectorRange(field, elementSize) is not { } range)
        {
            count = 0;
            return default;
        }

        count = range.Count;
        return _buffer.Slice(range.Start, range.Count * elementSize);
    }

    public double[] GetDoubleVector(int field)
    {
        var bytes = GetVectorBytes(field, 8, out var count);
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(i * 8)..]);
        }

        return result;
    }

    public uint[] GetUInt32Vector(int field)
    {
        var bytes = GetVectorBytes(field, 4, out var count);
        var result = new uint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
        }

        return result;
    }

    public TableVector GetTableVector(int field)
    {
        if (GetVectorRange(field, elementSize: 4) is not { } range)
            return default;

        return new TableVector(this, range.Start, range.Count);
    }

    private (int Start, int Count)? GetVectorRange(int field, int elementSize)
    {
        var position = GetFieldPosition(field);
        if (position == 0)
            return null;

        var vector = Follow(position);
        var span = _buffer;
        CheckRange(span, vector, 4);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[vector..]);
        if (count > (uint)(span.Length - vector - 4) / (uint)elementSize)
            throw InvalidData();

        return (vector + 4, (int)count);
    }

    /// <summary>Gets the position of a field in the buffer, or 0 when the table does not have it.</summary>
    private int GetFieldPosition(int field)
    {
        var slot = 4 + (field * 2);
        if (slot + 2 > _vtableLength)
            return 0;

        var offset = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[(_vtable + slot)..]);
        if (offset == 0)
            return 0;

        return _position + offset;
    }

    /// <summary>Follows the unsigned offset stored at a position.</summary>
    private int Follow(int position)
    {
        var span = _buffer;
        CheckRange(span, position, 4);
        var target = (long)position + BinaryPrimitives.ReadUInt32LittleEndian(span[position..]);
        if (target >= span.Length)
            throw InvalidData();

        return (int)target;
    }

    private ReadOnlySpan<byte> Read(int position, int length)
    {
        CheckRange(_buffer, position, length);
        return _buffer.Slice(position, length);
    }

    private static void CheckRange(ReadOnlySpan<byte> span, int position, int length)
    {
        if (position < 0 || position > span.Length - length)
            throw InvalidData();
    }

    private static FormatException InvalidData() => new("The FlatGeobuf data is not valid: an offset is outside of its buffer.");

    /// <summary>A vector of tables: each table is read when it is accessed.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly ref struct TableVector
    {
        private readonly FlatBufferTable _table;
        private readonly int _start;

        internal TableVector(FlatBufferTable table, int start, int count)
        {
            _table = table;
            _start = start;
            Count = count;
        }

        public int Count { get; }

        public FlatBufferTable this[int index] => new(_table._buffer, _table.Follow(_start + (index * 4)));

        public Enumerator GetEnumerator() => new(this);

        [StructLayout(LayoutKind.Auto)]
        public ref struct Enumerator
        {
            private readonly TableVector _vector;
            private int _index;

            internal Enumerator(TableVector vector)
            {
                _vector = vector;
                _index = -1;
            }

            public readonly FlatBufferTable Current => _vector[_index];

            public bool MoveNext() => ++_index < _vector.Count;
        }
    }
}
