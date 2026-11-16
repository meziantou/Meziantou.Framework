using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>
/// Parses the headers of a 7z archive. Every count is checked against the bytes that remain before anything is allocated
/// for it, so that a small corrupted header cannot allocate much memory.
/// </summary>
internal sealed class SevenZipHeaderReader
{
    private readonly byte[] _data;
    private int _position;
    private int _end;

    public SevenZipHeaderReader(byte[] data)
    {
        _data = data;
        _end = data.Length;
    }

    private int Remaining => _end - _position;

    /// <summary>The streams of the headers: the packed streams, the folders, and the files of the folders.</summary>
    public sealed class StreamsInfo
    {
        public long PackPosition { get; set; }

        public long[] PackSizes { get; set; } = [];

        public List<SevenZipFolder> Folders { get; } = [];

        public long[] SubStreamSizes { get; set; } = [];

        public uint?[] SubStreamCrcs { get; set; } = [];
    }

    public byte ReadByte()
    {
        if (_position >= _end)
            throw Truncated();

        return _data[_position++];
    }

    public ulong ReadNumber()
    {
        var first = ReadByte();
        byte mask = 0x80;
        ulong value = 0;
        for (var i = 0; i < 8; i++)
        {
            if ((first & mask) is 0)
            {
                var high = (ulong)(first & (mask - 1));
                value |= high << (8 * i);
                return value;
            }

            value |= (ulong)ReadByte() << (8 * i);
            mask >>= 1;
        }

        return value;
    }

    /// <summary>Reads a size: a number that fits in a <see cref="long"/>.</summary>
    public long ReadSize()
    {
        var value = ReadNumber();
        if (value > long.MaxValue)
            throw new InvalidDataException("A size of the headers of the 7z archive is too large.");

        return (long)value;
    }

    /// <summary>Reads a count, which must be at most <paramref name="max"/>.</summary>
    public int ReadCount(long max)
    {
        var value = ReadNumber();
        if (value > (ulong)Math.Min(max, Array.MaxLength))
            throw new InvalidDataException("A count of the headers of the 7z archive is too large.");

        return (int)value;
    }

    private uint ReadUInt32()
    {
        if (Remaining < 4)
            throw Truncated();

        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    private ulong ReadUInt64()
    {
        if (Remaining < 8)
            throw Truncated();

        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_position));
        _position += 8;
        return value;
    }

    private void Expect(byte propertyId)
    {
        var id = ReadNumber();
        if (id != propertyId)
            throw new InvalidDataException($"The headers of the 7z archive are corrupted: property 0x{propertyId:X2} expected, 0x{id:X2} found.");
    }

    private void SkipData()
    {
        var size = ReadNumber();
        if (size > (ulong)Remaining)
            throw Truncated();

        _position += (int)size;
    }

    private bool[] ReadBitField(int count)
    {
        if ((count + 7L) / 8 > Remaining)
            throw Truncated();

        var result = new bool[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = (_data[_position + (i >> 3)] & (0x80 >> (i & 7))) is not 0;
        }

        _position += (count + 7) / 8;
        return result;
    }

    /// <summary>Reads the "AllAreDefined" byte and, when it is 0, the bit field of the defined items.</summary>
    private bool[] ReadDefinedVector(int count)
    {
        var allDefined = ReadByte();
        if (allDefined is 0)
            return ReadBitField(count);

        // Without the bit field, the vector takes no bytes: bound it by the bytes of its values instead
        if (count > Remaining)
            throw Truncated();

        var result = new bool[count];
        Array.Fill(result, value: true);
        return result;
    }

    private uint?[] ReadDigests(int count)
    {
        var defined = ReadDefinedVector(count);
        var result = new uint?[count];
        for (var i = 0; i < count; i++)
        {
            if (defined[i])
            {
                result[i] = ReadUInt32();
            }
        }

        return result;
    }

    /// <summary>Reads the property ID of the start of the headers: <see cref="SevenZipPropertyId.Header"/> or <see cref="SevenZipPropertyId.EncodedHeader"/>.</summary>
    public ulong ReadHeaderKind() => ReadNumber();

    /// <summary>Reads the streams of an encoded header (after <see cref="SevenZipPropertyId.EncodedHeader"/>).</summary>
    public StreamsInfo ReadEncodedHeader() => ReadStreamsInfo();

    /// <summary>Reads a header (after <see cref="SevenZipPropertyId.Header"/>).</summary>
    public SevenZipDatabase ReadHeader(long dataOffset, long streamLength)
    {
        var id = ReadNumber();
        if (id == SevenZipPropertyId.ArchiveProperties)
        {
            while (true)
            {
                var type = ReadNumber();
                if (type is 0)
                    break;

                SkipData();
            }

            id = ReadNumber();
        }

        if (id == SevenZipPropertyId.AdditionalStreamsInfo)
        {
            // The data of the "external" properties, which this reader does not support: they are rejected where they are used
            _ = ReadStreamsInfo();
            id = ReadNumber();
        }

        var database = new SevenZipDatabase();
        StreamsInfo? streams = null;
        if (id == SevenZipPropertyId.MainStreamsInfo)
        {
            streams = ReadStreamsInfo();
            Locate(streams, dataOffset, streamLength);
            database.Folders.AddRange(streams.Folders);
            id = ReadNumber();
        }

        if (id == SevenZipPropertyId.FilesInfo)
        {
            ReadFilesInfo(database, streams);
            id = ReadNumber();
        }
        else if (streams is not null && streams.SubStreamSizes.Length > 0)
        {
            throw new InvalidDataException("The headers of the 7z archive have content but no files.");
        }

        if (id != SevenZipPropertyId.End)
            throw new InvalidDataException($"The headers of the 7z archive are corrupted: unexpected property 0x{id:X2}.");

        return database;
    }

    /// <summary>Computes the offset of each packed stream of the folders, and checks that they are inside the archive.</summary>
    public static void Locate(StreamsInfo streams, long dataOffset, long streamLength)
    {
        var offset = dataOffset + streams.PackPosition;
        if (streams.PackPosition > streamLength || offset > streamLength)
            throw new InvalidDataException("The 7z archive is truncated: its packed streams are after its end.");

        var packIndex = 0;
        foreach (var folder in streams.Folders)
        {
            folder.FirstPackStreamIndex = packIndex;
            var count = folder.PackedStreams.Length;
            if (packIndex + count > streams.PackSizes.Length)
                throw new InvalidDataException("The folders of the 7z archive have more packed streams than the archive.");

            folder.PackOffsets = new long[count];
            folder.PackSizes = new long[count];
            for (var i = 0; i < count; i++)
            {
                var size = streams.PackSizes[packIndex];
                if (size > streamLength - offset)
                    throw new InvalidDataException("The 7z archive is truncated: a packed stream ends after its end.");

                folder.PackOffsets[i] = offset;
                folder.PackSizes[i] = size;
                offset += size;
                packIndex++;
            }
        }
    }

    private StreamsInfo ReadStreamsInfo()
    {
        var result = new StreamsInfo();
        var id = ReadNumber();
        if (id == SevenZipPropertyId.PackInfo)
        {
            ReadPackInfo(result);
            id = ReadNumber();
        }

        if (id == SevenZipPropertyId.UnpackInfo)
        {
            ReadUnpackInfo(result);
            id = ReadNumber();
        }

        // Without SubStreamsInfo, each folder has one file
        result.SubStreamSizes = result.Folders.ConvertAll(folder => folder.UnpackSize).ToArray();
        result.SubStreamCrcs = result.Folders.ConvertAll(folder => folder.UnpackCrc).ToArray();
        if (id == SevenZipPropertyId.SubStreamsInfo)
        {
            ReadSubStreamsInfo(result);
            id = ReadNumber();
        }

        if (id != SevenZipPropertyId.End)
            throw new InvalidDataException($"The streams of the 7z archive are corrupted: unexpected property 0x{id:X2}.");

        var packedStreamCount = 0L;
        foreach (var folder in result.Folders)
        {
            packedStreamCount += folder.PackedStreams.Length;
        }

        if (packedStreamCount > result.PackSizes.Length)
            throw new InvalidDataException("The folders of the 7z archive have more packed streams than the archive.");

        return result;
    }

    private void ReadPackInfo(StreamsInfo result)
    {
        result.PackPosition = ReadSize();
        var count = ReadCount(Remaining);
        var sizesRead = false;
        while (true)
        {
            var id = ReadNumber();
            if (id == SevenZipPropertyId.End)
                break;

            if (id == SevenZipPropertyId.Size)
            {
                var sizes = new long[count];
                long total = 0;
                for (var i = 0; i < count; i++)
                {
                    sizes[i] = ReadSize();
                    total += sizes[i];
                    if (total < 0)
                        throw new InvalidDataException("The packed streams of the 7z archive are too large.");
                }

                result.PackSizes = sizes;
                sizesRead = true;
            }
            else if (id == SevenZipPropertyId.Crc)
            {
                // The CRCs of the packed streams are not checked: the CRCs of the content are
                _ = ReadDigests(count);
            }
            else
            {
                SkipData();
            }
        }

        if (!sizesRead && count > 0)
            throw new InvalidDataException("The packed streams of the 7z archive have no sizes.");
    }

    private void ReadUnpackInfo(StreamsInfo result)
    {
        Expect(SevenZipPropertyId.Folder);
        var count = ReadCount(Remaining);
        var external = ReadByte();
        if (external is not 0)
            throw new NotSupportedException("The folders of the 7z archive are stored in an additional stream, which is not supported.");

        for (var i = 0; i < count; i++)
        {
            result.Folders.Add(ReadFolder());
        }

        Expect(SevenZipPropertyId.CodersUnpackSize);
        foreach (var folder in result.Folders)
        {
            var sizes = new long[folder.Coders.Length];
            for (var i = 0; i < sizes.Length; i++)
            {
                sizes[i] = ReadSize();
            }

            folder.UnpackSizes = sizes;
        }

        while (true)
        {
            var id = ReadNumber();
            if (id == SevenZipPropertyId.End)
                break;

            if (id == SevenZipPropertyId.Crc)
            {
                var crcs = ReadDigests(count);
                for (var i = 0; i < count; i++)
                {
                    result.Folders[i].UnpackCrc = crcs[i];
                }
            }
            else
            {
                SkipData();
            }
        }
    }

    private SevenZipFolder ReadFolder()
    {
        var coderCount = ReadCount(SevenZipConstants.MaxCoders);
        if (coderCount is 0)
            throw new InvalidDataException("A folder of the 7z archive has no coders.");

        var coders = new SevenZipCoder[coderCount];
        var inStreamCount = 0;
        for (var i = 0; i < coderCount; i++)
        {
            var flags = ReadByte();
            var idSize = flags & 0x0F;
            if (idSize > 8)
                throw new NotSupportedException($"A coder of the 7z archive has an ID of {idSize} bytes, which is not supported.");

            if ((flags & 0x80) is not 0)
                throw new NotSupportedException("A coder of the 7z archive has alternative methods, which is not supported.");

            ulong methodId = 0;
            for (var j = 0; j < idSize; j++)
            {
                methodId = (methodId << 8) | ReadByte();
            }

            var coderInStreams = 1;
            var coderOutStreams = 1;
            if ((flags & 0x10) is not 0)
            {
                coderInStreams = ReadCount(SevenZipConstants.MaxCoders);
                coderOutStreams = ReadCount(SevenZipConstants.MaxCoders);
            }

            if (coderOutStreams is not 1)
                throw new NotSupportedException($"The coder {SevenZipMethodId.Format(methodId)} of the 7z archive has {coderOutStreams} output streams, which is not supported.");

            if (coderInStreams is 0)
                throw new InvalidDataException("A coder of the 7z archive has no input streams.");

            byte[] properties = [];
            if ((flags & 0x20) is not 0)
            {
                var size = ReadCount(Remaining);
                properties = _data.AsSpan(_position, size).ToArray();
                _position += size;
            }

            inStreamCount += coderInStreams;
            if (inStreamCount > SevenZipConstants.MaxCoders)
                throw new NotSupportedException("A folder of the 7z archive has too many streams.");

            coders[i] = new SevenZipCoder(methodId, properties, coderInStreams, coderOutStreams);
        }

        var bindPairs = new SevenZipBindPair[coderCount - 1];
        for (var i = 0; i < bindPairs.Length; i++)
        {
            var inIndex = ReadCount(inStreamCount - 1);
            var outIndex = ReadCount(coderCount - 1);
            bindPairs[i] = new SevenZipBindPair(inIndex, outIndex);
        }

        var packedCount = inStreamCount - bindPairs.Length;
        if (packedCount < 1)
            throw new InvalidDataException("A folder of the 7z archive has no packed streams.");

        var packedStreams = new int[packedCount];
        if (packedCount is 1)
        {
            packedStreams[0] = -1;
            for (var i = 0; i < inStreamCount; i++)
            {
                if (!Array.Exists(bindPairs, pair => pair.InIndex == i))
                {
                    packedStreams[0] = i;
                    break;
                }
            }
        }
        else
        {
            for (var i = 0; i < packedCount; i++)
            {
                packedStreams[i] = ReadCount(inStreamCount - 1);
            }
        }

        var folder = new SevenZipFolder { Coders = coders, BindPairs = bindPairs, PackedStreams = packedStreams };
        Validate(folder, inStreamCount);
        return folder;
    }

    /// <summary>Checks that the coders of a folder form a tree whose root is the only unbound output.</summary>
    private static void Validate(SevenZipFolder folder, int inStreamCount)
    {
        var boundIn = new bool[inStreamCount];
        var boundOut = new bool[folder.Coders.Length];
        foreach (var pair in folder.BindPairs)
        {
            if (boundIn[pair.InIndex] || boundOut[pair.OutIndex])
                throw new InvalidDataException("A stream of a folder of the 7z archive is bound twice.");

            boundIn[pair.InIndex] = true;
            boundOut[pair.OutIndex] = true;
        }

        foreach (var index in folder.PackedStreams)
        {
            if (index < 0 || boundIn[index])
                throw new InvalidDataException("A packed stream of a folder of the 7z archive is bound to a coder.");

            boundIn[index] = true;
        }

        if (Array.IndexOf(boundIn, false) >= 0)
            throw new InvalidDataException("An input of a coder of a folder of the 7z archive is not bound.");

        // Every coder is reached once from the main output: there is no cycle
        var visited = new bool[folder.Coders.Length];
        var pending = new Stack<int>();
        pending.Push(folder.MainOutStream);
        var count = 0;
        while (pending.Count > 0)
        {
            var coder = pending.Pop();
            if (coder < 0 || visited[coder])
                throw new InvalidDataException("The coders of a folder of the 7z archive form a cycle.");

            visited[coder] = true;
            count++;
            var first = folder.GetFirstInStream(coder);
            for (var i = 0; i < folder.Coders[coder].InStreamCount; i++)
            {
                foreach (var pair in folder.BindPairs)
                {
                    if (pair.InIndex == first + i)
                    {
                        pending.Push(pair.OutIndex);
                    }
                }
            }
        }

        if (count != folder.Coders.Length)
            throw new InvalidDataException("A coder of a folder of the 7z archive is not used.");
    }

    private void ReadSubStreamsInfo(StreamsInfo result)
    {
        var folders = result.Folders;
        var counts = new int[folders.Count];
        Array.Fill(counts, 1);
        ulong id;
        while (true)
        {
            id = ReadNumber();
            if (id == SevenZipPropertyId.NumUnpackStream)
            {
                long total = 0;
                for (var i = 0; i < counts.Length; i++)
                {
                    // Each file after the first of a folder has a size in the headers: the counts are bounded by the headers
                    counts[i] = ReadCount(Remaining + 1);
                    total += counts[i];
                    if (total > Remaining + folders.Count)
                        throw new InvalidDataException("The folders of the 7z archive have more files than their headers can describe.");
                }

                continue;
            }

            if (id == SevenZipPropertyId.Crc || id == SevenZipPropertyId.Size || id == SevenZipPropertyId.End)
                break;

            SkipData();
        }

        var sizes = new List<long>();
        for (var i = 0; i < folders.Count; i++)
        {
            var count = counts[i];
            if (count is 0)
                continue;

            if (count > 1 && id != SevenZipPropertyId.Size)
                throw new InvalidDataException("The files of a folder of the 7z archive have no sizes.");

            var folderSize = folders[i].UnpackSize;
            long sum = 0;
            for (var j = 1; j < count; j++)
            {
                var size = ReadSize();
                sum += size;
                if (sum < 0 || sum > folderSize)
                    throw new InvalidDataException("The files of a folder of the 7z archive are larger than the folder.");

                sizes.Add(size);
            }

            sizes.Add(folderSize - sum);
        }

        if (id == SevenZipPropertyId.Size)
        {
            id = ReadNumber();
        }

        var crcs = new uint?[sizes.Count];
        while (id != SevenZipPropertyId.End)
        {
            if (id == SevenZipPropertyId.Crc)
            {
                var unknown = 0;
                for (var i = 0; i < folders.Count; i++)
                {
                    if (counts[i] is not 1 || folders[i].UnpackCrc is null)
                    {
                        unknown += counts[i];
                    }
                }

                var digests = ReadDigests(unknown);
                var next = 0;
                var index = 0;
                for (var i = 0; i < folders.Count; i++)
                {
                    if (counts[i] is 1 && folders[i].UnpackCrc is { } crc)
                    {
                        crcs[index++] = crc;
                        continue;
                    }

                    for (var j = 0; j < counts[i]; j++)
                    {
                        crcs[index++] = digests[next++];
                    }
                }
            }
            else
            {
                SkipData();
            }

            id = ReadNumber();
        }

        // Without digests, the file of a folder that has one file has the CRC of the folder
        var position = 0;
        for (var i = 0; i < folders.Count; i++)
        {
            if (counts[i] is 1 && crcs[position] is null)
            {
                crcs[position] = folders[i].UnpackCrc;
            }

            position += counts[i];
            folders[i].UnpackStreamCount = counts[i];
        }

        result.SubStreamSizes = [.. sizes];
        result.SubStreamCrcs = crcs;
    }

    private void ReadFilesInfo(SevenZipDatabase database, StreamsInfo? streams)
    {
        var streamCount = streams?.SubStreamSizes.Length ?? 0;
        var fileCount = ReadCount(streamCount + (8L * Remaining));
        bool[]? emptyStream = null;
        bool[]? emptyFile = null;
        bool[]? anti = null;
        string[]? names = null;
        long?[]? creationTimes = null;
        long?[]? accessTimes = null;
        long?[]? writeTimes = null;
        uint?[]? attributes = null;
        var emptyStreamCount = 0;

        while (true)
        {
            var type = ReadNumber();
            if (type == SevenZipPropertyId.End)
                break;

            var size = ReadNumber();
            if (size > (ulong)Remaining)
                throw Truncated();

            var end = _position + (int)size;
            var savedEnd = _end;
            _end = end;
            try
            {
                switch (type)
                {
                    case SevenZipPropertyId.EmptyStream:
                        emptyStream = ReadBitField(fileCount);
                        emptyStreamCount = 0;
                        foreach (var value in emptyStream)
                        {
                            if (value)
                            {
                                emptyStreamCount++;
                            }
                        }

                        break;

                    case SevenZipPropertyId.EmptyFile:
                        emptyFile = ReadBitField(emptyStreamCount);
                        break;

                    case SevenZipPropertyId.Anti:
                        anti = ReadBitField(emptyStreamCount);
                        break;

                    case SevenZipPropertyId.Name:
                        names = ReadNames(fileCount);
                        break;

                    case SevenZipPropertyId.CTime:
                        creationTimes = ReadTimes(fileCount);
                        break;

                    case SevenZipPropertyId.ATime:
                        accessTimes = ReadTimes(fileCount);
                        break;

                    case SevenZipPropertyId.MTime:
                        writeTimes = ReadTimes(fileCount);
                        break;

                    case SevenZipPropertyId.WinAttributes:
                        attributes = ReadAttributes(fileCount);
                        break;

                    // kStartPos, kDummy (padding), kComment and the unknown properties are skipped
                }
            }
            finally
            {
                _end = savedEnd;
            }

            _position = end;
        }

        if (fileCount - emptyStreamCount != streamCount)
            throw new InvalidDataException($"The 7z archive has {fileCount - emptyStreamCount} files with content, but its folders have {streamCount}.");

        var folderIndex = 0;
        var indexInFolder = 0;
        long offsetInFolder = 0;
        var streamIndex = 0;
        var emptyIndex = 0;
        for (var i = 0; i < fileCount; i++)
        {
            var item = new SevenZipFileItem
            {
                Name = names is not null ? names[i] : "",
                CreationTime = creationTimes?[i],
                LastAccessTime = accessTimes?[i],
                LastWriteTime = writeTimes?[i],
                Attributes = attributes?[i],
            };

            if (emptyStream is not null && emptyStream[i])
            {
                var isEmptyFile = emptyFile is not null && emptyFile[emptyIndex];
                item.IsDirectory = !isEmptyFile;
                item.IsAnti = anti is not null && anti[emptyIndex];
                emptyIndex++;
            }
            else
            {
                while (database.Folders[folderIndex].UnpackStreamCount == indexInFolder)
                {
                    folderIndex++;
                    indexInFolder = 0;
                    offsetInFolder = 0;
                }

                item.HasStream = true;
                item.FolderIndex = folderIndex;
                item.OffsetInFolder = offsetInFolder;
                item.Size = streams!.SubStreamSizes[streamIndex];
                item.Crc = streams.SubStreamCrcs[streamIndex];
                offsetInFolder += item.Size;
                indexInFolder++;
                streamIndex++;
            }

            database.Files.Add(item);
        }
    }

    private string[] ReadNames(int count)
    {
        var external = ReadByte();
        if (external is not 0)
            throw new NotSupportedException("The names of the files of the 7z archive are stored in an additional stream, which is not supported.");

        if (count > Remaining / 2)
            throw Truncated();

        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            var start = _position;
            while (true)
            {
                if (Remaining < 2)
                    throw Truncated();

                var c = _data[_position] | (_data[_position + 1] << 8);
                _position += 2;
                if (c is 0)
                    break;
            }

            var name = Encoding.Unicode.GetString(_data, start, _position - start - 2);

            // 7-Zip on Windows may store the paths with backslashes
            names[i] = name.Replace('\\', '/');
        }

        return names;
    }

    private long?[] ReadTimes(int count)
    {
        var defined = ReadDefinedVector(count);
        var external = ReadByte();
        if (external is not 0)
            throw new NotSupportedException("The times of the files of the 7z archive are stored in an additional stream, which is not supported.");

        var result = new long?[count];
        for (var i = 0; i < count; i++)
        {
            if (defined[i])
            {
                var value = ReadUInt64();
                result[i] = value <= long.MaxValue ? (long)value : null;
            }
        }

        return result;
    }

    private uint?[] ReadAttributes(int count)
    {
        var defined = ReadDefinedVector(count);
        var external = ReadByte();
        if (external is not 0)
            throw new NotSupportedException("The attributes of the files of the 7z archive are stored in an additional stream, which is not supported.");

        var result = new uint?[count];
        for (var i = 0; i < count; i++)
        {
            if (defined[i])
            {
                result[i] = ReadUInt32();
            }
        }

        return result;
    }

    private static InvalidDataException Truncated() => new("The headers of the 7z archive are truncated.");
}
