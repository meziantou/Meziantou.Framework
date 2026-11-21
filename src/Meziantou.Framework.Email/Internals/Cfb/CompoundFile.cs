using System.Buffers.Binary;

namespace Meziantou.Framework.Email.Internals.Cfb;

/// <summary>Reads a compound file ([MS-CFB]), the container of .msg files: a file system of storages and streams in a file.</summary>
internal sealed class CompoundFile
{
    public const uint FreeSector = 0xFFFFFFFF;
    public const uint EndOfChain = 0xFFFFFFFE;
    public const uint FatSector = 0xFFFFFFFD;
    public const uint DifatSector = 0xFFFFFFFC;
    public const uint MaxRegularSector = 0xFFFFFFFA;
    public const uint NoStream = 0xFFFFFFFF;

    public static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly uint _miniStreamCutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly List<CfbEntry> _entries = [];
    private readonly long _maxStreamSize;
    private byte[]? _miniStream;

    private CompoundFile(ReadOnlyMemory<byte> data, long maxStreamSize)
    {
        _data = data;
        _maxStreamSize = maxStreamSize;
        var header = data.Span;
        if (header.Length < 512 || !header[..8].SequenceEqual(Signature))
        {
            throw new InvalidEmailDataException("The file is not a compound file: the signature is missing.");
        }

        var majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
        var miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
        if (majorVersion is not (3 or 4) || sectorShift is not (9 or 12) || miniSectorShift != 6)
        {
            throw new InvalidEmailDataException($"The compound file has an unsupported version ({majorVersion}) or sector size (2^{sectorShift}).");
        }

        _sectorSize = 1 << sectorShift;
        _miniSectorSize = 1 << miniSectorShift;
        _miniStreamCutoff = BinaryPrimitives.ReadUInt32LittleEndian(header[56..]);
        var fatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);
        var firstDirectorySector = BinaryPrimitives.ReadUInt32LittleEndian(header[48..]);
        var firstMiniFatSector = BinaryPrimitives.ReadUInt32LittleEndian(header[60..]);
        var firstDifatSector = BinaryPrimitives.ReadUInt32LittleEndian(header[68..]);

        // The sectors of the FAT: 109 in the header, the others in the chain of DIFAT sectors
        var sectorCount = SectorCount;
        if (fatSectorCount > sectorCount)
        {
            throw new InvalidEmailDataException("The compound file has more FAT sectors than sectors.");
        }

        var fatSectors = new List<uint>((int)fatSectorCount);
        for (var i = 0; i < 109 && fatSectors.Count < fatSectorCount; i++)
        {
            fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(header[(76 + (i * 4))..]));
        }

        var difat = firstDifatSector;
        var visited = 0L;
        while (fatSectors.Count < fatSectorCount && difat <= MaxRegularSector)
        {
            if (++visited > sectorCount)
            {
                throw new InvalidEmailDataException("The DIFAT of the compound file has a loop.");
            }

            var sector = GetSector(difat);
            var entriesPerSector = (_sectorSize / 4) - 1;
            for (var i = 0; i < entriesPerSector && fatSectors.Count < fatSectorCount; i++)
            {
                fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector[(i * 4)..]));
            }

            difat = BinaryPrimitives.ReadUInt32LittleEndian(sector[(_sectorSize - 4)..]);
        }

        _fat = new uint[fatSectors.Count * (_sectorSize / 4)];
        for (var i = 0; i < fatSectors.Count; i++)
        {
            var sector = GetSector(fatSectors[i]);
            for (var j = 0; j < _sectorSize / 4; j++)
            {
                _fat[(i * (_sectorSize / 4)) + j] = BinaryPrimitives.ReadUInt32LittleEndian(sector[(j * 4)..]);
            }
        }

        var miniFat = ReadChain(firstMiniFatSector, long.MaxValue);
        _miniFat = new uint[miniFat.Length / 4];
        for (var i = 0; i < _miniFat.Length; i++)
        {
            _miniFat[i] = BinaryPrimitives.ReadUInt32LittleEndian(miniFat.AsSpan(i * 4));
        }

        var directory = ReadChain(firstDirectorySector, long.MaxValue);
        for (var offset = 0; offset + 128 <= directory.Length; offset += 128)
        {
            _entries.Add(ReadEntry(directory.AsSpan(offset, 128), _entries.Count, majorVersion));
        }

        if (_entries.Count == 0 || _entries[0].Type != CfbEntryType.Root)
        {
            throw new InvalidEmailDataException("The compound file has no root storage.");
        }
    }

    public CfbEntry Root => _entries[0];

    private long SectorCount => Math.Max(0, (_data.Length / _sectorSize) - 1);

    public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>Reads a compound file. Its streams are read when they are accessed.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="maxStreamSize">The size of the largest stream that can be read.</param>
    public static CompoundFile Read(ReadOnlyMemory<byte> data, long maxStreamSize) => new(data, maxStreamSize);

    public byte[] ReadStream(CfbEntry entry)
    {
        if (!entry.IsStream || entry.Size == 0)
        {
            return [];
        }

        if (entry.Size > _maxStreamSize)
        {
            throw new EmailResourceLimitException($"The stream '{entry.Name}' is larger than the limit of {_maxStreamSize} bytes.");
        }

        if (entry.Size < _miniStreamCutoff)
        {
            _miniStream ??= ReadChain(Root.StartSector, Root.Size);
            return ReadMiniChain(entry.StartSector, entry.Size);
        }

        return ReadChain(entry.StartSector, entry.Size);
    }

    internal Dictionary<string, CfbEntry> GetChildren(CfbEntry storage)
    {
        var result = new Dictionary<string, CfbEntry>(StringComparer.OrdinalIgnoreCase);
        if (!storage.IsStorage)
        {
            return result;
        }

        // The children of a storage are a binary tree: walk it, ignoring the cycles of a malformed file
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(storage.Child);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id < 0 || id >= _entries.Count || id == 0 || !visited.Add(id))
            {
                continue;
            }

            var entry = _entries[id];
            if (entry.Type is CfbEntryType.Storage or CfbEntryType.Stream)
            {
                result.TryAdd(entry.Name, entry);
            }

            pending.Push(entry.Left);
            pending.Push(entry.Right);
        }

        return result;
    }

    private CfbEntry ReadEntry(ReadOnlySpan<byte> data, int id, int majorVersion)
    {
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[64..]);
        var chars = Math.Clamp((nameLength / 2) - 1, 0, 31);
        var name = System.Text.Encoding.Unicode.GetString(data[..(chars * 2)]);

        var type = (CfbEntryType)data[66];
        var left = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[68..]);
        var right = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[72..]);
        var child = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[76..]);
        var start = BinaryPrimitives.ReadUInt32LittleEndian(data[116..]);
        var size = BinaryPrimitives.ReadInt64LittleEndian(data[120..]);
        if (majorVersion == 3)
        {
            // Version 3: only the low 32 bits of the size are valid
            size &= 0xFFFFFFFF;
        }

        if (size < 0)
        {
            size = 0;
        }

        return new CfbEntry(this, id, name, type, start, size, left, right, child, new Guid(data[80..96]));
    }

    private ReadOnlySpan<byte> GetSector(uint sector)
    {
        var offset = ((long)sector + 1) * _sectorSize;
        if (sector > MaxRegularSector || offset + _sectorSize > _data.Length)
        {
            // The last sector can be truncated: read what the file has, padded with zeros by the callers
            if (sector <= MaxRegularSector && offset < _data.Length)
            {
                var partial = new byte[_sectorSize];
                _data.Span[(int)offset..].CopyTo(partial);
                return partial;
            }

            throw new InvalidEmailDataException($"The sector {sector} is outside the compound file.");
        }

        return _data.Span.Slice((int)offset, _sectorSize);
    }

    /// <summary>Reads a chain of sectors of the FAT, up to a size.</summary>
    private byte[] ReadChain(uint start, long size)
    {
        if (start > MaxRegularSector)
        {
            return [];
        }

        var maxSectors = SectorCount + 1;
        if (size != long.MaxValue && (size + _sectorSize - 1) / _sectorSize > maxSectors)
        {
            throw new InvalidEmailDataException("A stream of the compound file is larger than the file.");
        }

        using var result = new MemoryStream();
        var sector = start;
        var count = 0L;
        while (sector <= MaxRegularSector && result.Length < size)
        {
            if (++count > maxSectors)
            {
                throw new InvalidEmailDataException("A chain of sectors of the compound file has a loop.");
            }

            var data = GetSector(sector);
            var length = (int)Math.Min(_sectorSize, size - result.Length);
            result.Write(data[..length]);
            sector = sector < _fat.Length ? _fat[sector] : throw new InvalidEmailDataException($"The sector {sector} is not in the FAT of the compound file.");
        }

        if (size != long.MaxValue && result.Length < size)
        {
            throw new InvalidEmailDataException("A stream of the compound file is shorter than its size.");
        }

        return result.ToArray();
    }

    private byte[] ReadMiniChain(uint start, long size)
    {
        var miniStream = _miniStream!;
        var result = new byte[size];
        var position = 0;
        var sector = start;
        var count = 0;
        while (position < size)
        {
            if (sector > MaxRegularSector || ++count > _miniFat.Length + 1)
            {
                throw new InvalidEmailDataException("A chain of mini sectors of the compound file is shorter than its stream, or has a loop.");
            }

            var offset = (long)sector * _miniSectorSize;
            var length = (int)Math.Min(_miniSectorSize, size - position);
            if (offset + length > miniStream.Length)
            {
                throw new InvalidEmailDataException($"The mini sector {sector} is outside the mini stream of the compound file.");
            }

            miniStream.AsSpan((int)offset, length).CopyTo(result.AsSpan(position));
            position += length;
            sector = sector < _miniFat.Length ? _miniFat[sector] : EndOfChain;
        }

        return result;
    }
}
