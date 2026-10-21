using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>The keys of the tables of a data file, sorted by ordinal: the id of a key is its index.</summary>
internal sealed class CldrKeys
{
    private readonly string[] _names;
    private readonly Dictionary<string, int> _ids;
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public CldrKeys(string[] names)
    {
        _names = names;
        _ids = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            _ids.Add(names[i], i);
        }

        _lookup = _ids.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public int Count => _names.Length;

    public string this[int id] => _names[id];

    public static CldrKeys Read(ref CldrBinaryReader reader)
    {
        var names = new string[reader.ReadVarInt32()];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = reader.ReadString();
        }

        return new CldrKeys(names);
    }

    public bool TryGetId(ReadOnlySpan<char> name, out int id) => _lookup.TryGetValue(name, out id);
}

/// <summary>
/// A decompressed data chunk: a string table and a table of tables, decoded on first use.
/// <code>
/// stringCount (utf8Length utf8)*
/// tableCount (entryCount (keyIdDelta value)*)*      value = index &lt;&lt; 1 | isTable
/// </code>
/// </summary>
internal sealed class CldrTreeChunk
{
    private readonly byte[] _data;
    private readonly int[] _stringOffsets;
    private readonly int[] _tableOffsets;
    private readonly string?[] _strings;
    private readonly CldrTable?[] _tables;

    public CldrTreeChunk(CldrKeys keys, byte[] data)
    {
        Keys = keys;
        _data = data;
        var reader = new CldrBinaryReader(data, 0);
        _stringOffsets = new int[reader.ReadVarInt32()];
        for (var i = 0; i < _stringOffsets.Length; i++)
        {
            _stringOffsets[i] = reader.Position;
            reader.SkipString();
        }

        _tableOffsets = new int[reader.ReadVarInt32()];
        for (var i = 0; i < _tableOffsets.Length; i++)
        {
            _tableOffsets[i] = reader.Position;
            var count = reader.ReadVarInt32();
            for (var j = 0; j < count * 2; j++)
            {
                reader.ReadVarUInt64();
            }
        }

        _strings = new string?[_stringOffsets.Length];
        _tables = new CldrTable?[_tableOffsets.Length];
    }

    public CldrKeys Keys { get; }

    public int StringCount => _strings.Length;

    public int TableCount => _tables.Length;

    public string GetString(int index)
    {
        var value = Volatile.Read(ref _strings[index]);
        if (value is null)
        {
            var reader = new CldrBinaryReader(_data, _stringOffsets[index]);
            value = reader.ReadString();
            value = Interlocked.CompareExchange(ref _strings[index], value, comparand: null) ?? value;
        }

        return value;
    }

    public CldrTable GetTable(int index)
    {
        var table = Volatile.Read(ref _tables[index]);
        if (table is null)
        {
            var reader = new CldrBinaryReader(_data, _tableOffsets[index]);
            var count = reader.ReadVarInt32();
            var keys = new int[count];
            var values = new int[count];
            var key = 0;
            for (var i = 0; i < count; i++)
            {
                key += reader.ReadVarInt32();
                keys[i] = key;
                values[i] = reader.ReadVarInt32();
            }

            table = new CldrTable(this, keys, values);
            table = Interlocked.CompareExchange(ref _tables[index], table, comparand: null) ?? table;
        }

        return table;
    }
}

/// <summary>A node of a data tree: a string (a leaf) or a table.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct CldrNode
{
    private readonly CldrTreeChunk? _chunk;
    private readonly int _value;

    internal CldrNode(CldrTreeChunk chunk, int value)
    {
        _chunk = chunk;
        _value = value;
    }

    public bool IsDefault => _chunk is null;

    public bool IsTable => _chunk is not null && (_value & 1) != 0;

    public bool IsString => _chunk is not null && (_value & 1) == 0;

    public string GetString() => IsString ? _chunk!.GetString(_value >> 1) : throw new InvalidOperationException("The node is not a string");

    public CldrTable GetTable() => IsTable ? _chunk!.GetTable(_value >> 1) : throw new InvalidOperationException("The node is not a table");
}

/// <summary>An immutable table of a data tree: its entries are sorted by key (ordinal).</summary>
internal sealed class CldrTable
{
    private readonly CldrTreeChunk _chunk;
    private readonly int[] _keys;
    private readonly int[] _values;

    internal CldrTable(CldrTreeChunk chunk, int[] keys, int[] values)
    {
        _chunk = chunk;
        _keys = keys;
        _values = values;
    }

    public int Count => _keys.Length;

    public string GetKey(int index) => _chunk.Keys[_keys[index]];

    public CldrNode GetValue(int index) => new(_chunk, _values[index]);

    public bool TryGetValue(ReadOnlySpan<char> key, out CldrNode node)
    {
        if (_chunk.Keys.TryGetId(key, out var id))
        {
            var index = Array.BinarySearch(_keys, id);
            if (index >= 0)
            {
                node = new CldrNode(_chunk, _values[index]);
                return true;
            }
        }

        node = default;
        return false;
    }

    public bool TryGetString(ReadOnlySpan<char> key, [NotNullWhen(true)] out string? value)
    {
        if (TryGetValue(key, out var node) && node.IsString)
        {
            value = node.GetString();
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Gets the node at a path of keys separated by '/' (e.g. "symbols-numberSystem-latn/decimal").</summary>
    public bool TryGetNode(ReadOnlySpan<char> path, out CldrNode node)
    {
        var table = this;
        while (true)
        {
            var separator = path.IndexOf('/');
            var segment = separator < 0 ? path : path[..separator];
            if (!table.TryGetValue(segment, out node))
                return false;

            if (separator < 0)
                return true;

            if (!node.IsTable)
            {
                node = default;
                return false;
            }

            table = node.GetTable();
            path = path[(separator + 1)..];
        }
    }
}
