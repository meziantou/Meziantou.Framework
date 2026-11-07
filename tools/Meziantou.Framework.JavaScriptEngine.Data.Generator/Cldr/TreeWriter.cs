using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>A table of a data tree being built: the values are strings or tables.</summary>
internal sealed class TreeTable
{
    public SortedDictionary<string, object> Entries { get; } = new(StringComparer.Ordinal);

    public int Count => Entries.Count;

    public object this[string key]
    {
        get => Entries[key];
        set
        {
            if (key.Contains('/', StringComparison.Ordinal) || key.Length == 0)
                throw new InvalidDataException("Invalid key: '" + key + "'");

            Entries[key] = value;
        }
    }

    public TreeTable GetOrAddTable(string key)
    {
        if (Entries.TryGetValue(key, out var value))
            return (TreeTable)value;

        var result = new TreeTable();
        this[key] = result;
        return result;
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out object? value) => Entries.TryGetValue(key, out value);

    public bool Remove(string key) => Entries.Remove(key);

    public object? GetPath(string path)
    {
        object current = this;
        foreach (var segment in path.Split('/'))
        {
            if (current is not TreeTable table || !table.TryGetValue(segment, out var next))
                return null;

            current = next;
        }

        return current;
    }

    /// <summary>Enumerates the leaves as (path, value), the path segments separated by '/'.</summary>
    public IEnumerable<(string Path, string Value)> EnumerateLeaves(string prefix = "")
    {
        foreach (var (key, value) in Entries)
        {
            var path = prefix.Length == 0 ? key : prefix + "/" + key;
            if (value is TreeTable table)
            {
                foreach (var leaf in table.EnumerateLeaves(path))
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return (path, (string)value);
            }
        }
    }

    public void CollectKeys(HashSet<string> keys)
    {
        foreach (var (key, value) in Entries)
        {
            keys.Add(key);
            if (value is TreeTable table)
            {
                table.CollectKeys(keys);
            }
        }
    }

    public TreeTable Clone()
    {
        var result = new TreeTable();
        foreach (var (key, value) in Entries)
        {
            result.Entries[key] = value is TreeTable table ? table.Clone() : value;
        }

        return result;
    }

    /// <summary>Returns the entries of this table whose value differs from the one in <paramref name="parent"/>, or null when there is none.</summary>
    public TreeTable? Delta(TreeTable? parent)
    {
        var result = new TreeTable();
        foreach (var (key, value) in Entries)
        {
            object? parentValue = null;
            parent?.TryGetValue(key, out parentValue);
            if (value is TreeTable table)
            {
                var delta = table.Delta(parentValue as TreeTable);
                if (delta is not null)
                {
                    result.Entries[key] = delta;
                }
            }
            else if (parentValue is not string parentString || !string.Equals(parentString, (string)value, StringComparison.Ordinal))
            {
                result.Entries[key] = value;
            }
        }

        return result.Count == 0 ? null : result;
    }
}

/// <summary>Little-endian binary output with LEB128 varints and UTF-8 strings prefixed by their byte length.</summary>
internal sealed class BinaryBuffer
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public int Length => _buffer.WrittenCount;

    public void WriteByte(byte value) => _buffer.Write([value]);

    public void WriteBytes(ReadOnlySpan<byte> value) => _buffer.Write(value);

    public void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _buffer.Write(bytes);
    }

    public void WriteVarUInt(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    public void WriteVarUInt(int value) => WriteVarUInt(checked((ulong)value));

    public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarUInt(bytes.Length);
        WriteBytes(bytes);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
}

/// <summary>Writes the key table shared by the chunks of a file (read by CldrKeys).</summary>
internal sealed class TreeKeys
{
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    public TreeKeys(IEnumerable<string> keys)
    {
        Names = keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < Names.Length; i++)
        {
            _ids.Add(Names[i], i);
        }
    }

    public string[] Names { get; }

    public int GetId(string key) => _ids[key];

    public void Write(BinaryBuffer output)
    {
        output.WriteVarUInt(Names.Length);
        foreach (var name in Names)
        {
            output.WriteString(name);
        }
    }
}

/// <summary>Builds a data chunk (read by CldrTreeChunk): identical strings and identical tables are stored once.</summary>
internal sealed class TreeChunkWriter(TreeKeys keys)
{
    private readonly List<string> _strings = [];
    private readonly Dictionary<string, int> _stringIds = new(StringComparer.Ordinal);
    private readonly List<byte[]> _tables = [];
    private readonly Dictionary<byte[], int> _tableIds = new(ByteArrayComparer.Instance);

    public bool IsEmpty => _tables.Count == 0;

    /// <summary>Adds a table and its descendants, and returns the index of the table in the chunk.</summary>
    public int AddTable(TreeTable table)
    {
        var entries = new List<(int Key, int Value)>(table.Count);
        foreach (var (key, value) in table.Entries)
        {
            var code = value switch
            {
                TreeTable child => (AddTable(child) << 1) | 1,
                string text => AddString(text) << 1,
                _ => throw new InvalidDataException("Unexpected value: " + value),
            };
            entries.Add((keys.GetId(key), code));
        }

        entries.Sort((a, b) => a.Key.CompareTo(b.Key));
        var output = new BinaryBuffer();
        output.WriteVarUInt(entries.Count);
        var previousKey = 0;
        foreach (var (key, value) in entries)
        {
            output.WriteVarUInt(key - previousKey);
            output.WriteVarUInt(value);
            previousKey = key;
        }

        var bytes = output.ToArray();
        if (!_tableIds.TryGetValue(bytes, out var index))
        {
            index = _tables.Count;
            _tables.Add(bytes);
            _tableIds.Add(bytes, index);
        }

        return index;
    }

    private int AddString(string value)
    {
        if (!_stringIds.TryGetValue(value, out var index))
        {
            index = _strings.Count;
            _strings.Add(value);
            _stringIds.Add(value, index);
        }

        return index;
    }

    public byte[] ToArray()
    {
        var output = new BinaryBuffer();
        output.WriteVarUInt(_strings.Count);
        foreach (var value in _strings)
        {
            output.WriteString(value);
        }

        output.WriteVarUInt(_tables.Count);
        foreach (var table in _tables)
        {
            output.WriteBytes(table);
        }

        return output.ToArray();
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}

/// <summary>Writes a file read by CldrChunkedFile: each chunk is compressed independently.</summary>
internal static class ChunkedFileWriter
{
    public static byte[] Write(IReadOnlyList<byte[]> chunks)
    {
        var compressed = chunks.Select(Compress).ToList();
        var output = new BinaryBuffer();
        output.WriteBytes("MFCD"u8);
        output.WriteByte(CldrChunkedFile.FormatVersion);
        output.WriteBytes([0, 0, 0]);
        output.WriteUInt32((uint)chunks.Count);
        var offset = 12 + ((chunks.Count + 1) * 4);
        foreach (var chunk in compressed)
        {
            output.WriteUInt32((uint)offset);
            offset += chunk.Length;
        }

        output.WriteUInt32((uint)offset);
        foreach (var chunk in compressed)
        {
            output.WriteBytes(chunk);
        }

        return output.ToArray();
    }

    /// <summary>Gets whether a file contains the same chunks once decompressed (the compressed bytes depend on the version of Brotli).</summary>
    public static bool IsSameContent(string path, IReadOnlyList<byte[]> chunks)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            var file = CldrChunkedFile.FromBytes(File.ReadAllBytes(path));
            if (file.ChunkCount != chunks.Count)
                return false;

            for (var i = 0; i < chunks.Count; i++)
            {
                if (!file.GetChunk(i).AsSpan().SequenceEqual(chunks[i]))
                    return false;
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static byte[] Compress(byte[] content)
    {
        var compressed = new byte[4 + BrotliEncoder.GetMaxCompressedLength(content.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(compressed, content.Length);
        if (!BrotliEncoder.TryCompress(content, compressed.AsSpan(4), out var written, quality: 11, window: 24))
            throw new InvalidOperationException("Cannot compress a chunk");

        return compressed.AsSpan(0, 4 + written).ToArray();
    }
}
