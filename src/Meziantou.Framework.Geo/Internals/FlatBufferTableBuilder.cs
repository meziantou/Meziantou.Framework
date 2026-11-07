using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>The fields of a FlatBuffers table to write: scalars, and strings, vectors and tables it refers to.</summary>
internal sealed class FlatBufferTableBuilder
{
    private readonly List<Field> _fields = [];

    public void AddByte(int index, byte value) => _fields.Add(new Field(index, [value], Child: null));

    public void AddBool(int index, bool value) => AddByte(index, value ? (byte)1 : (byte)0);

    public void AddUInt16(int index, ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _fields.Add(new Field(index, bytes, Child: null));
    }

    public void AddInt32(int index, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _fields.Add(new Field(index, bytes, Child: null));
    }

    public void AddUInt64(int index, ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _fields.Add(new Field(index, bytes, Child: null));
    }

    public void AddString(int index, string value) => AddChild(index, builder => builder.WriteString(value));

    public void AddBytes(int index, byte[] value) => AddChild(index, builder => builder.WriteVector(value, elementSize: 1, value.Length));

    public void AddUInt32s(int index, uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        }

        AddChild(index, builder => builder.WriteVector(bytes, elementSize: 4, values.Length));
    }

    public void AddDoubles(int index, double[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * 8), values[i]);
        }

        AddChild(index, builder => builder.WriteVector(bytes, elementSize: 8, values.Length));
    }

    public void AddTable(int index, FlatBufferTableBuilder table) => AddChild(index, table.Write);

    public void AddTables(int index, IReadOnlyList<FlatBufferTableBuilder> tables) => AddChild(index, builder =>
    {
        builder.Align(4);
        var position = builder.Length;
        var span = builder.Allocate(4 + (tables.Count * 4));
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)tables.Count);
        for (var i = 0; i < tables.Count; i++)
        {
            var element = position + 4 + (i * 4);
            var table = tables[i].Write(builder);
            builder.PatchUInt32(element, (uint)(table - element));
        }

        return position;
    });

    /// <summary>Writes the vtable, the table and what it refers to, and returns the position of the table.</summary>
    public int Write(FlatBufferBuilder builder)
    {
        // The inline fields from the largest to the smallest, each aligned on its size after the offset of the vtable
        var ordered = _fields.OrderByDescending(field => field.Size).ThenBy(field => field.Index).ToList();
        var offsets = new int[ordered.Count];
        var size = 4;
        var alignment = 4;
        for (var i = 0; i < ordered.Count; i++)
        {
            var fieldSize = ordered[i].Size;
            size = (size + fieldSize - 1) / fieldSize * fieldSize;
            offsets[i] = size;
            size += fieldSize;
            alignment = Math.Max(alignment, fieldSize);
        }

        var slots = _fields.Count == 0 ? 0 : _fields.Max(field => field.Index) + 1;
        builder.Align(2);
        var vtable = builder.Length;
        var vtableSpan = builder.Allocate(4 + (slots * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(vtableSpan, (ushort)(4 + (slots * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(vtableSpan[2..], (ushort)size);
        for (var i = 0; i < ordered.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(vtableSpan[(4 + (ordered[i].Index * 2))..], (ushort)offsets[i]);
        }

        builder.Align(alignment);
        var table = builder.Length;
        builder.Allocate(size);
        builder.PatchInt32(table, table - vtable);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Inline is { } inline)
            {
                inline.CopyTo(builder.GetSpan(table + offsets[i], inline.Length));
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Child is { } child)
            {
                var position = table + offsets[i];
                var target = child(builder);
                builder.PatchUInt32(position, (uint)(target - position));
            }
        }

        return table;
    }

    private void AddChild(int index, Func<FlatBufferBuilder, int> child) => _fields.Add(new Field(index, Inline: null, child));

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Field(int Index, byte[]? Inline, Func<FlatBufferBuilder, int>? Child)
    {
        public int Size => Inline?.Length ?? 4;
    }
}
