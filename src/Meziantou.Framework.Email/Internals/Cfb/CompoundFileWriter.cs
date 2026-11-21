using System.Buffers.Binary;

namespace Meziantou.Framework.Email.Internals.Cfb;

/// <summary>Writes a compound file ([MS-CFB]) of version 3: 512-byte sectors, the streams of less than 4096 bytes in the mini stream.</summary>
internal static class CompoundFileWriter
{
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;
    private const int EntriesPerFatSector = SectorSize / 4;
    private const int HeaderDifatEntries = 109;

    public static void Write(CfbWriterStorage root, Stream output)
    {
        // The directory: the root first, then the entries of each storage
        var entries = new List<Entry> { new("Root Entry", CfbEntryType.Root, Data: null) };
        AddChildren(entries, 0, root);

        // The streams: the small ones in the mini stream, the others in sectors
        using var miniStream = new MemoryStream();
        var miniFat = new List<uint>();
        var largeSectorCount = 0L;
        foreach (var entry in entries)
        {
            if (entry.Data is not { Length: > 0 } data)
            {
                continue;
            }

            if (data.Length < MiniStreamCutoff)
            {
                entry.Start = (uint)miniFat.Count;
                var count = (data.Length + MiniSectorSize - 1) / MiniSectorSize;
                for (var i = 0; i < count; i++)
                {
                    miniFat.Add(i == count - 1 ? CompoundFile.EndOfChain : (uint)(miniFat.Count + 1));
                }

                miniStream.Write(data);
                miniStream.Write(new byte[(count * MiniSectorSize) - data.Length]);
            }
            else
            {
                entry.IsLarge = true;
                largeSectorCount += (data.Length + SectorSize - 1) / SectorSize;
            }
        }

        var directorySectors = ((entries.Count * 128) + SectorSize - 1) / SectorSize;
        var miniFatSectors = ((miniFat.Count * 4) + SectorSize - 1) / SectorSize;
        var miniStreamSectors = (int)((miniStream.Length + SectorSize - 1) / SectorSize);
        var dataSectors = directorySectors + miniFatSectors + miniStreamSectors + largeSectorCount;

        // The FAT covers its own sectors and the DIFAT sectors
        var fatSectors = 1L;
        long difatSectors;
        while (true)
        {
            difatSectors = fatSectors <= HeaderDifatEntries ? 0 : (fatSectors - HeaderDifatEntries + EntriesPerFatSector - 2) / (EntriesPerFatSector - 1);
            if (fatSectors * EntriesPerFatSector >= dataSectors + fatSectors + difatSectors)
            {
                break;
            }

            fatSectors++;
        }

        var totalSectors = fatSectors + difatSectors + dataSectors;
        if (totalSectors > CompoundFile.MaxRegularSector)
        {
            throw new InvalidOperationException("The content is too large for a compound file.");
        }

        var fat = new uint[fatSectors * EntriesPerFatSector];
        Array.Fill(fat, CompoundFile.FreeSector);
        uint next = 0;
        for (var i = 0; i < fatSectors; i++)
        {
            fat[next++] = CompoundFile.FatSector;
        }

        var firstDifat = difatSectors > 0 ? next : CompoundFile.EndOfChain;
        for (var i = 0; i < difatSectors; i++)
        {
            fat[next++] = CompoundFile.DifatSector;
        }

        uint Chain(long count)
        {
            if (count == 0)
            {
                return CompoundFile.EndOfChain;
            }

            var start = next;
            for (var i = 0; i < count; i++)
            {
                fat[next] = i == count - 1 ? CompoundFile.EndOfChain : next + 1;
                next++;
            }

            return start;
        }

        var firstDirectory = Chain(directorySectors);
        var firstMiniFat = Chain(miniFatSectors);
        entries[0].Start = Chain(miniStreamSectors);
        entries[0].Size = miniStream.Length;
        foreach (var entry in entries)
        {
            if (entry.IsLarge)
            {
                entry.Start = Chain((entry.Data!.Length + SectorSize - 1) / SectorSize);
            }
        }

        // The header
        var header = new byte[SectorSize];
        CompoundFile.Signature.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), (uint)fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), firstDirectory);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(56), MiniStreamCutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), firstMiniFat);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), (uint)miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), firstDifat);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), (uint)difatSectors);
        for (var i = 0; i < HeaderDifatEntries; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76 + (i * 4)), i < fatSectors ? (uint)i : CompoundFile.FreeSector);
        }

        output.Write(header);

        // The FAT, then the DIFAT: the FAT sectors after the 109 of the header, and the next DIFAT sector at the end of each
        var sector = new byte[SectorSize];
        for (var i = 0; i < fat.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan((i % EntriesPerFatSector) * 4), fat[i]);
            if (i % EntriesPerFatSector == EntriesPerFatSector - 1)
            {
                output.Write(sector);
            }
        }

        var fatIndex = (long)HeaderDifatEntries;
        for (var i = 0; i < difatSectors; i++)
        {
            Array.Fill(sector, (byte)0xFF);
            for (var j = 0; j < EntriesPerFatSector - 1 && fatIndex < fatSectors; j++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(j * 4), (uint)fatIndex++);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(SectorSize - 4), i == difatSectors - 1 ? CompoundFile.EndOfChain : (uint)(firstDifat + i + 1));
            output.Write(sector);
        }

        // The directory
        var directory = new byte[directorySectors * SectorSize];
        for (var i = 0; i < directorySectors * SectorSize / 128; i++)
        {
            var span = directory.AsSpan(i * 128, 128);
            if (i >= entries.Count)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[68..], CompoundFile.NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(span[72..], CompoundFile.NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(span[76..], CompoundFile.NoStream);
                continue;
            }

            var entry = entries[i];
            for (var c = 0; c < entry.Name.Length; c++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span[(c * 2)..], entry.Name[c]);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(span[64..], (ushort)((entry.Name.Length + 1) * 2));
            span[66] = (byte)entry.Type;
            span[67] = entry.IsRed ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteInt32LittleEndian(span[68..], entry.Left);
            BinaryPrimitives.WriteInt32LittleEndian(span[72..], entry.Right);
            BinaryPrimitives.WriteInt32LittleEndian(span[76..], entry.Child);
            var start = entry.Type == CfbEntryType.Storage ? 0u : entry.Data is { Length: 0 } ? CompoundFile.EndOfChain : entry.Start;
            BinaryPrimitives.WriteUInt32LittleEndian(span[116..], start);
            BinaryPrimitives.WriteInt64LittleEndian(span[120..], entry.Type == CfbEntryType.Root ? entry.Size : entry.Data?.Length ?? 0);
        }

        output.Write(directory);

        // The mini FAT, the mini stream, then the large streams
        var miniFatBytes = new byte[miniFatSectors * SectorSize];
        miniFatBytes.AsSpan().Fill(0xFF);
        for (var i = 0; i < miniFat.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(miniFatBytes.AsSpan(i * 4), miniFat[i]);
        }

        output.Write(miniFatBytes);
        WritePadded(output, miniStream.GetBuffer().AsSpan(0, (int)miniStream.Length));
        foreach (var entry in entries)
        {
            if (entry.IsLarge)
            {
                WritePadded(output, entry.Data);
            }
        }
    }

    private static void WritePadded(Stream output, ReadOnlySpan<byte> data)
    {
        output.Write(data);
        var padding = (SectorSize - (data.Length % SectorSize)) % SectorSize;
        if (padding > 0)
        {
            output.Write(new byte[padding]);
        }
    }

    /// <summary>Adds the entries of a storage and sets the tree of its children: a balanced binary search tree, colored as a red-black tree.</summary>
    private static void AddChildren(List<Entry> entries, int parent, CfbWriterStorage storage)
    {
        var children = new List<(int Id, CfbWriterStorage? Storage)>();
        foreach (var child in storage.Storages)
        {
            children.Add((entries.Count, child));
            entries.Add(new Entry(child.Name, CfbEntryType.Storage, Data: null));
        }

        foreach (var (name, data) in storage.Streams)
        {
            children.Add((entries.Count, null));
            entries.Add(new Entry(name, CfbEntryType.Stream, data));
        }

        children.Sort((a, b) => CompareNames(entries[a.Id].Name, entries[b.Id].Name));
        for (var i = 1; i < children.Count; i++)
        {
            if (CompareNames(entries[children[i - 1].Id].Name, entries[children[i].Id].Name) == 0)
            {
                throw new InvalidOperationException($"The storage '{storage.Name}' has two entries named '{entries[children[i].Id].Name}'.");
            }
        }

        var ids = children.Select(child => child.Id).ToArray();
        var perfect = (ids.Length & (ids.Length + 1)) == 0;
        var bottom = ids.Length == 0 ? 0 : System.Numerics.BitOperations.Log2((uint)ids.Length + 1) - (perfect ? 1 : 0);
        entries[parent].Child = Build(entries, ids, 0, ids.Length - 1, depth: 0, redDepth: perfect ? -1 : bottom);

        foreach (var (id, child) in children)
        {
            if (child is not null)
            {
                AddChildren(entries, id, child);
            }
        }
    }

    private static int Build(List<Entry> entries, int[] ids, int low, int high, int depth, int redDepth)
    {
        if (low > high)
        {
            return unchecked((int)CompoundFile.NoStream);
        }

        var middle = low + ((high - low + 1) / 2);
        var entry = entries[ids[middle]];
        entry.Left = Build(entries, ids, low, middle - 1, depth + 1, redDepth);
        entry.Right = Build(entries, ids, middle + 1, high, depth + 1, redDepth);
        entry.IsRed = depth == redDepth;
        return ids[middle];
    }

    /// <summary>Compares the names of entries as [MS-CFB] 2.6.4 requires: the shorter name first, then the upper-case characters.</summary>
    internal static int CompareNames(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return a.Length.CompareTo(b.Length);
        }

        for (var i = 0; i < a.Length; i++)
        {
            var result = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[i]));
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private sealed record Entry(string Name, CfbEntryType Type, byte[]? Data)
    {
        public int Left { get; set; } = unchecked((int)CompoundFile.NoStream);

        public int Right { get; set; } = unchecked((int)CompoundFile.NoStream);

        public int Child { get; set; } = unchecked((int)CompoundFile.NoStream);

        public bool IsRed { get; set; }

        public bool IsLarge { get; set; }

        public uint Start { get; set; } = CompoundFile.EndOfChain;

        public long Size { get; set; }
    }
}
