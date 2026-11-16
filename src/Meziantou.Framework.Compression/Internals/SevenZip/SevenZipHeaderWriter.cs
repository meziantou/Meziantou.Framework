using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Writes the headers of a 7z archive.</summary>
internal sealed class SevenZipHeaderWriter
{
    private readonly ArrayBufferWriter<byte> _output = new();

    public byte[] ToArray() => _output.WrittenSpan.ToArray();

    /// <summary>
    /// Writes a header. The files with content must be in the order of the folders, and each folder must have
    /// <see cref="SevenZipFolder.UnpackStreamCount"/> of them.
    /// </summary>
    public static byte[] WriteHeader(long packPosition, IReadOnlyList<SevenZipFolder> folders, IReadOnlyList<SevenZipFileItem> files)
    {
        var writer = new SevenZipHeaderWriter();
        writer.WriteByte(SevenZipPropertyId.Header);
        if (folders.Count > 0)
        {
            var streamFiles = files.Where(file => file.HasStream).ToList();
            writer.WriteByte(SevenZipPropertyId.MainStreamsInfo);
            writer.WriteStreamsInfo(packPosition, folders, streamFiles);
        }

        if (files.Count > 0)
        {
            writer.WriteFilesInfo(files);
        }

        writer.WriteByte(SevenZipPropertyId.End);
        return writer.ToArray();
    }

    /// <summary>Writes the streams of an encoded header: one folder whose output is the header.</summary>
    public static byte[] WriteEncodedHeader(long packPosition, SevenZipFolder folder)
    {
        var writer = new SevenZipHeaderWriter();
        writer.WriteByte(SevenZipPropertyId.EncodedHeader);
        writer.WriteStreamsInfo(packPosition, [folder], files: null);
        return writer.ToArray();
    }

    private void WriteStreamsInfo(long packPosition, IReadOnlyList<SevenZipFolder> folders, List<SevenZipFileItem>? files)
    {
        WriteByte(SevenZipPropertyId.PackInfo);
        WriteNumber((ulong)packPosition);
        var packSizes = folders.SelectMany(folder => folder.PackSizes).ToList();
        WriteNumber((ulong)packSizes.Count);
        WriteByte(SevenZipPropertyId.Size);
        foreach (var size in packSizes)
        {
            WriteNumber((ulong)size);
        }

        WriteByte(SevenZipPropertyId.End);

        WriteByte(SevenZipPropertyId.UnpackInfo);
        WriteByte(SevenZipPropertyId.Folder);
        WriteNumber((ulong)folders.Count);
        WriteByte(0); // Not external
        foreach (var folder in folders)
        {
            WriteFolder(folder);
        }

        WriteByte(SevenZipPropertyId.CodersUnpackSize);
        foreach (var folder in folders)
        {
            foreach (var size in folder.UnpackSizes)
            {
                WriteNumber((ulong)size);
            }
        }

        var folderCrcs = folders.Select(folder => folder.UnpackCrc).ToList();
        if (folderCrcs.Exists(crc => crc is not null))
        {
            WriteByte(SevenZipPropertyId.Crc);
            WriteDigests(folderCrcs);
        }

        WriteByte(SevenZipPropertyId.End);

        if (files is not null)
        {
            WriteSubStreamsInfo(folders, files);
        }

        WriteByte(SevenZipPropertyId.End);
    }

    private void WriteFolder(SevenZipFolder folder)
    {
        WriteNumber((ulong)folder.Coders.Length);
        foreach (var coder in folder.Coders)
        {
            var idSize = 1;
            while (idSize < 8 && (coder.MethodId >> (8 * idSize)) is not 0)
            {
                idSize++;
            }

            var isComplex = coder.InStreamCount is not 1 || coder.OutStreamCount is not 1;
            var flags = (byte)(idSize | (isComplex ? 0x10 : 0) | (coder.Properties.Length > 0 ? 0x20 : 0));
            WriteByte(flags);
            for (var i = idSize - 1; i >= 0; i--)
            {
                WriteByte((byte)(coder.MethodId >> (8 * i)));
            }

            if (isComplex)
            {
                WriteNumber((ulong)coder.InStreamCount);
                WriteNumber((ulong)coder.OutStreamCount);
            }

            if (coder.Properties.Length > 0)
            {
                WriteNumber((ulong)coder.Properties.Length);
                WriteBytes(coder.Properties);
            }
        }

        foreach (var pair in folder.BindPairs)
        {
            WriteNumber((ulong)pair.InIndex);
            WriteNumber((ulong)pair.OutIndex);
        }

        if (folder.PackedStreams.Length > 1)
        {
            foreach (var index in folder.PackedStreams)
            {
                WriteNumber((ulong)index);
            }
        }
    }

    private void WriteSubStreamsInfo(IReadOnlyList<SevenZipFolder> folders, List<SevenZipFileItem> files)
    {
        WriteByte(SevenZipPropertyId.SubStreamsInfo);
        var anyMultiple = false;
        foreach (var folder in folders)
        {
            if (folder.UnpackStreamCount is not 1)
            {
                anyMultiple = true;
            }
        }

        if (anyMultiple)
        {
            WriteByte(SevenZipPropertyId.NumUnpackStream);
            foreach (var folder in folders)
            {
                WriteNumber((ulong)folder.UnpackStreamCount);
            }

            WriteByte(SevenZipPropertyId.Size);
            var index = 0;
            foreach (var folder in folders)
            {
                for (var i = 0; i < folder.UnpackStreamCount; i++)
                {
                    if (i < folder.UnpackStreamCount - 1)
                    {
                        WriteNumber((ulong)files[index].Size);
                    }

                    index++;
                }
            }
        }

        // The digests of the files whose CRC is not the CRC of their folder
        var digests = new List<uint?>();
        var fileIndex = 0;
        foreach (var folder in folders)
        {
            if (folder.UnpackStreamCount is 1 && folder.UnpackCrc is not null)
            {
                fileIndex++;
                continue;
            }

            for (var i = 0; i < folder.UnpackStreamCount; i++)
            {
                digests.Add(files[fileIndex++].Crc);
            }
        }

        if (digests.Exists(crc => crc is not null))
        {
            WriteByte(SevenZipPropertyId.Crc);
            WriteDigests(digests);
        }

        WriteByte(SevenZipPropertyId.End);
    }

    private void WriteFilesInfo(IReadOnlyList<SevenZipFileItem> files)
    {
        WriteByte(SevenZipPropertyId.FilesInfo);
        WriteNumber((ulong)files.Count);

        var emptyStreams = files.Select(file => !file.HasStream).ToArray();
        if (Array.IndexOf(emptyStreams, value: true) >= 0)
        {
            WriteProperty(SevenZipPropertyId.EmptyStream, writer => writer.WriteBitField(emptyStreams));

            var empty = files.Where(file => !file.HasStream).ToList();
            var emptyFiles = empty.Select(file => !file.IsDirectory).ToArray();
            if (Array.IndexOf(emptyFiles, value: true) >= 0)
            {
                WriteProperty(SevenZipPropertyId.EmptyFile, writer => writer.WriteBitField(emptyFiles));
            }

            var anti = empty.Select(file => file.IsAnti).ToArray();
            if (Array.IndexOf(anti, value: true) >= 0)
            {
                WriteProperty(SevenZipPropertyId.Anti, writer => writer.WriteBitField(anti));
            }
        }

        WriteProperty(SevenZipPropertyId.Name, writer =>
        {
            writer.WriteByte(0); // Not external
            foreach (var file in files)
            {
                writer.WriteBytes(Encoding.Unicode.GetBytes(file.Name));
                writer.WriteBytes([0, 0]);
            }
        });

        WriteTimes(SevenZipPropertyId.CTime, files.Select(file => file.CreationTime).ToArray());
        WriteTimes(SevenZipPropertyId.ATime, files.Select(file => file.LastAccessTime).ToArray());
        WriteTimes(SevenZipPropertyId.MTime, files.Select(file => file.LastWriteTime).ToArray());

        var attributes = files.Select(file => file.Attributes).ToArray();
        if (Array.Exists(attributes, value => value is not null))
        {
            WriteProperty(SevenZipPropertyId.WinAttributes, writer =>
            {
                writer.WriteDefinedVector(attributes.Select(value => value is not null).ToArray());
                writer.WriteByte(0); // Not external
                Span<byte> buffer = stackalloc byte[4];
                foreach (var value in attributes)
                {
                    if (value is { } v)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(buffer, v);
                        writer.WriteBytes(buffer);
                    }
                }
            });
        }

        WriteByte(SevenZipPropertyId.End);
    }

    private void WriteTimes(byte propertyId, long?[] times)
    {
        if (!Array.Exists(times, value => value is not null))
            return;

        WriteProperty(propertyId, writer =>
        {
            writer.WriteDefinedVector(times.Select(value => value is not null).ToArray());
            writer.WriteByte(0); // Not external
            Span<byte> buffer = stackalloc byte[8];
            foreach (var value in times)
            {
                if (value is { } v)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(buffer, v);
                    writer.WriteBytes(buffer);
                }
            }
        });
    }

    private void WriteProperty(byte propertyId, Action<SevenZipHeaderWriter> write)
    {
        var content = new SevenZipHeaderWriter();
        write(content);
        var bytes = content.ToArray();
        WriteByte(propertyId);
        WriteNumber((ulong)bytes.Length);
        WriteBytes(bytes);
    }

    private void WriteDigests(List<uint?> digests)
    {
        WriteDefinedVector(digests.Select(crc => crc is not null).ToArray());
        Span<byte> buffer = stackalloc byte[4];
        foreach (var crc in digests)
        {
            if (crc is { } value)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
                WriteBytes(buffer);
            }
        }
    }

    private void WriteDefinedVector(bool[] defined)
    {
        if (Array.TrueForAll(defined, value => value))
        {
            WriteByte(1);
            return;
        }

        WriteByte(0);
        WriteBitField(defined);
    }

    private void WriteBitField(bool[] values)
    {
        byte current = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                current |= (byte)(0x80 >> (i & 7));
            }

            if ((i & 7) is 7)
            {
                WriteByte(current);
                current = 0;
            }
        }

        if ((values.Length & 7) is not 0)
        {
            WriteByte(current);
        }
    }

    private void WriteByte(byte value) => _output.Write([value]);

    private void WriteBytes(ReadOnlySpan<byte> value) => _output.Write(value);

    /// <summary>Writes a number in the variable-length encoding of 7z.</summary>
    private void WriteNumber(ulong value)
    {
        byte first = 0;
        byte mask = 0x80;
        int i;
        for (i = 0; i < 8; i++)
        {
            if (value < (1UL << (7 * (i + 1))))
            {
                first |= (byte)(value >> (8 * i));
                break;
            }

            first |= mask;
            mask >>= 1;
        }

        WriteByte(first);
        for (; i > 0; i--)
        {
            WriteByte((byte)value);
            value >>= 8;
        }
    }
}
