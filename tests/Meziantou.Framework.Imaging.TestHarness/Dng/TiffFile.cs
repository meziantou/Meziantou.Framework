using System.Buffers.Binary;
using System.Text;

#pragma warning disable CA1819 // Properties should not return arrays

namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>A minimal reader of classic TIFF directories (TIFF 6.0, section 2), independent of the library: entries and their values as numbers, bytes or strings.</summary>
public sealed class TiffFile
{
    public TiffFile(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        IsBigEndian = data[0] == (byte)'M';
        if (ReadUInt16(2) != 42)
            throw new InvalidDataException("Not a classic TIFF file.");

        FirstDirectory = ReadUInt32(4);
    }

    public byte[] Data { get; }

    public bool IsBigEndian { get; }

    public uint FirstDirectory { get; }

    public Directory ReadDirectory(uint offset)
    {
        var count = ReadUInt16((int)offset);
        var entries = new Dictionary<ushort, (ushort Type, int Count, byte[] Values)>();
        for (var i = 0; i < count; i++)
        {
            var entry = (int)offset + 2 + (i * 12);
            var tag = ReadUInt16(entry);
            var type = ReadUInt16(entry + 2);
            var valueCount = (int)ReadUInt32(entry + 4);
            var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 or 13 => 4, _ => 8 };
            var length = size * valueCount;
            var position = length <= 4 ? entry + 8 : (int)ReadUInt32(entry + 8);
            entries[tag] = (type, valueCount, Data.AsSpan(position, length).ToArray());
        }

        return new Directory(this, entries, ReadUInt32((int)offset + 2 + (count * 12)));
    }

    private ushort ReadUInt16(int offset) => IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset));

    private uint ReadUInt32(int offset) => IsBigEndian ? BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(offset));

    /// <summary>A directory: its entries by tag.</summary>
    public sealed class Directory
    {
        private readonly TiffFile _file;
        private readonly Dictionary<ushort, (ushort Type, int Count, byte[] Values)> _entries;

        internal Directory(TiffFile file, Dictionary<ushort, (ushort Type, int Count, byte[] Values)> entries, uint next)
        {
            _file = file;
            _entries = entries;
            Next = next;
        }

        public uint Next { get; }

        /// <summary>Gets the entries: their tag, type, count and the bytes of their values, in the byte order of the file.</summary>
        public IEnumerable<(ushort Tag, ushort Type, int Count, byte[] Values)> RawEntries => _entries.Select(entry => (entry.Key, entry.Value.Type, entry.Value.Count, entry.Value.Values));

        public bool Has(ushort tag) => _entries.ContainsKey(tag);

        public double Number(ushort tag) => Numbers(tag)[0];

        public double? TryNumber(ushort tag) => TryNumbers(tag)?[0];

        public double[] Numbers(ushort tag) => TryNumbers(tag) ?? throw new InvalidDataException($"The tag {tag} is missing.");

        public double[]? TryNumbers(ushort tag)
        {
            if (!_entries.TryGetValue(tag, out var entry))
                return null;

            var (type, count, values) = entry;
            var big = _file.IsBigEndian;
            var result = new double[count];
            for (var i = 0; i < count; i++)
            {
                var span = values.AsSpan();
                result[i] = type switch
                {
                    1 or 7 => span[i],
                    6 => (sbyte)span[i],
                    3 => big ? BinaryPrimitives.ReadUInt16BigEndian(span[(i * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(span[(i * 2)..]),
                    8 => big ? BinaryPrimitives.ReadInt16BigEndian(span[(i * 2)..]) : BinaryPrimitives.ReadInt16LittleEndian(span[(i * 2)..]),
                    4 or 13 => big ? BinaryPrimitives.ReadUInt32BigEndian(span[(i * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[(i * 4)..]),
                    9 => big ? BinaryPrimitives.ReadInt32BigEndian(span[(i * 4)..]) : BinaryPrimitives.ReadInt32LittleEndian(span[(i * 4)..]),
                    5 => (double)(big ? BinaryPrimitives.ReadUInt32BigEndian(span[(i * 8)..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[(i * 8)..])) / (big ? BinaryPrimitives.ReadUInt32BigEndian(span[((i * 8) + 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[((i * 8) + 4)..])),
                    10 => (double)(big ? BinaryPrimitives.ReadInt32BigEndian(span[(i * 8)..]) : BinaryPrimitives.ReadInt32LittleEndian(span[(i * 8)..])) / (big ? BinaryPrimitives.ReadInt32BigEndian(span[((i * 8) + 4)..]) : BinaryPrimitives.ReadInt32LittleEndian(span[((i * 8) + 4)..])),
                    11 => big ? BinaryPrimitives.ReadSingleBigEndian(span[(i * 4)..]) : BinaryPrimitives.ReadSingleLittleEndian(span[(i * 4)..]),
                    12 => big ? BinaryPrimitives.ReadDoubleBigEndian(span[(i * 8)..]) : BinaryPrimitives.ReadDoubleLittleEndian(span[(i * 8)..]),
                    _ => throw new InvalidDataException($"The tag {tag} is not numeric."),
                };
            }

            return result;
        }

        public byte[] Bytes(ushort tag) => TryBytes(tag) ?? throw new InvalidDataException($"The tag {tag} is missing.");

        public byte[]? TryBytes(ushort tag) => _entries.TryGetValue(tag, out var entry) ? entry.Values : null;

        public string? TryString(ushort tag)
        {
            if (TryBytes(tag) is not { } bytes)
                return null;

            var end = Array.IndexOf(bytes, (byte)0);
            var text = Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
            return text.Length == 0 ? null : text;
        }
    }
}
