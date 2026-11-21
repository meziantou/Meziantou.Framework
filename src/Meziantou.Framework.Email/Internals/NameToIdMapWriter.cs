using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Assigns the identifiers of the named properties a writer uses, and writes the name-to-ID map ([MS-PST] 2.4.7).</summary>
internal sealed class NameToIdMapWriter
{
    public const int BucketCount = 251;

    private readonly Dictionary<MapiNamedProperty, ushort> _ids = [];
    private readonly List<MapiNamedProperty> _names = [];

    public int Count => _names.Count;

    /// <summary>Returns the name of an identifier the map gave.</summary>
    public MapiNamedProperty GetName(ushort id) => _names[id - 0x8000];

    /// <summary>Returns the identifier of a named property, and assigns one the first time.</summary>
    /// <exception cref="InvalidOperationException">The file already maps 32,767 named properties.</exception>
    public ushort GetId(MapiNamedProperty name)
    {
        if (_ids.TryGetValue(name, out var id))
        {
            return id;
        }

        if (_names.Count >= 0x7FFF)
        {
            throw new InvalidOperationException("A file cannot have more than 32,767 named properties.");
        }

        id = (ushort)(0x8000 + _names.Count);
        _ids.Add(name, id);
        _names.Add(name);
        return id;
    }

    /// <summary>Returns the properties of the map: the bucket count, the streams and the hash buckets.</summary>
    /// <param name="bucketCount">The number of hash buckets: 251 in an Outlook data file, 31 in an .msg file.</param>
    public List<(MapiPropertyTag Tag, byte[] Value)> CreateProperties(int bucketCount = BucketCount)
    {
        using var guids = new MemoryStream();
        using var entries = new MemoryStream();
        using var strings = new MemoryStream();
        var guidIndexes = new Dictionary<Guid, int>();
        var buckets = new List<byte>[bucketCount];
        Span<byte> entry = stackalloc byte[8];
        Span<byte> length = stackalloc byte[4];
        for (var i = 0; i < _names.Count; i++)
        {
            var name = _names[i];
            int guidIndex;
            if (name.PropertySet == Guid.Empty)
            {
                guidIndex = 0;
            }
            else if (name.PropertySet == MapiPropertySets.Mapi)
            {
                guidIndex = 1;
            }
            else if (name.PropertySet == MapiPropertySets.PublicStrings)
            {
                guidIndex = 2;
            }
            else if (!guidIndexes.TryGetValue(name.PropertySet, out guidIndex))
            {
                guidIndex = 3 + guidIndexes.Count;
                guidIndexes.Add(name.PropertySet, guidIndex);
                guids.Write(name.PropertySet.ToByteArray());
            }

            uint value;
            if (name.Name is not null)
            {
                value = (uint)strings.Position;
                var chars = name.Name;
                BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)(chars.Length * 2));
                strings.Write(length);
                foreach (var c in chars)
                {
                    strings.WriteByte((byte)c);
                    strings.WriteByte((byte)(c >> 8));
                }

                while (strings.Position % 4 != 0)
                {
                    strings.WriteByte(0);
                }
            }
            else
            {
                value = (uint)name.Id!.Value;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(entry, value);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], (ushort)((guidIndex << 1) | (name.Name is null ? 0 : 1)));
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], (ushort)i);
            entries.Write(entry);

            var record = CreateBucketRecord(entry, strings.GetBuffer().AsSpan(0, (int)strings.Length));
            var bucket = GetBucket(record, bucketCount);
            (buckets[bucket] ??= []).AddRange(record);
        }

        var result = new List<(MapiPropertyTag Tag, byte[] Value)>
        {
            (new MapiPropertyTag(NameToIdMap.BucketCountId, MapiPropertyType.Integer32), BitConverter.GetBytes(bucketCount)),
            (new MapiPropertyTag(NameToIdMap.GuidStreamId, MapiPropertyType.Binary), guids.ToArray()),
            (new MapiPropertyTag(NameToIdMap.EntryStreamId, MapiPropertyType.Binary), entries.ToArray()),
            (new MapiPropertyTag(NameToIdMap.StringStreamId, MapiPropertyType.Binary), strings.ToArray()),
        };

        for (var i = 0; i < bucketCount; i++)
        {
            result.Add((new MapiPropertyTag((ushort)(0x1000 + i), MapiPropertyType.Binary), buckets[i]?.ToArray() ?? []));
        }

        return result;
    }

    /// <summary>Returns the record of an entry in a hash bucket: the entry, with the CRC of the name instead of its offset for a string name.</summary>
    public static byte[] CreateBucketRecord(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> strings)
    {
        var record = entry[..8].ToArray();
        if ((record[4] & 1) != 0)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(record);
            if (offset <= strings.Length - 4)
            {
                var length = BinaryPrimitives.ReadUInt32LittleEndian(strings[(int)offset..]);
                if (length <= strings.Length - offset - 4)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(record, PstCrc.Compute(strings.Slice((int)offset + 4, (int)length)));
                }
            }
        }

        return record;
    }

    /// <summary>Returns the hash bucket of a record ([MS-PST] 2.4.7.5).</summary>
    public static int GetBucket(ReadOnlySpan<byte> record, int bucketCount)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(record);
        var guidAndKind = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        return (int)((value ^ guidAndKind) % (uint)bucketCount);
    }
}
