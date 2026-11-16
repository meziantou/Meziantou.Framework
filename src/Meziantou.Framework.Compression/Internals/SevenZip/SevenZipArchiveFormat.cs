using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Reads the database of a 7z archive (the signature header and the headers), and writes the headers of a new one.</summary>
internal static class SevenZipArchiveFormat
{
    private const long MaxFileTime = 2650467743999999999; // DateTime.MaxValue

    public static bool IsSignature(ReadOnlySpan<byte> data) => data.StartsWith(SevenZipConstants.Signature);

    public static SevenZipDatabase ReadDatabase(Stream stream, SevenZipDecoderContext context)
    {
        var streamLength = stream.Length;
        Span<byte> signatureHeader = stackalloc byte[SevenZipConstants.SignatureHeaderSize];
        stream.Position = 0;
        if (stream.ReadAtLeast(signatureHeader, signatureHeader.Length, throwOnEndOfStream: false) != signatureHeader.Length)
            throw new InvalidDataException("The data are not a 7z archive: they are too short.");

        if (!IsSignature(signatureHeader))
            throw new InvalidDataException("The data are not a 7z archive: the signature does not match.");

        if (signatureHeader[6] is not 0)
            throw new NotSupportedException($"The version {signatureHeader[6]}.{signatureHeader[7]} of the 7z format is not supported.");

        var startHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(signatureHeader[8..]);
        if (System.IO.Hashing.Crc32.HashToUInt32(signatureHeader.Slice(12, 20)) != startHeaderCrc)
            throw new InvalidDataException("The CRC-32 of the start header of the 7z archive does not match.");

        var nextHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(signatureHeader[12..]);
        var nextHeaderSize = BinaryPrimitives.ReadUInt64LittleEndian(signatureHeader[20..]);
        var nextHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(signatureHeader[28..]);
        if (nextHeaderSize is 0)
            return new SevenZipDatabase();

        var dataOffset = (long)SevenZipConstants.SignatureHeaderSize;
        var available = (ulong)(streamLength - dataOffset);
        if (nextHeaderOffset > available || nextHeaderSize > available - nextHeaderOffset)
            throw new InvalidDataException("The 7z archive is truncated: its headers end after its end.");

        if (nextHeaderSize > (ulong)Array.MaxLength)
            throw new InvalidDataException("The headers of the 7z archive are too large.");

        var header = new byte[(int)nextHeaderSize];
        stream.Position = dataOffset + (long)nextHeaderOffset;
        stream.ReadExactlyOrThrow(header);
        if (System.IO.Hashing.Crc32.HashToUInt32(header) != nextHeaderCrc)
            throw new InvalidDataException("The CRC-32 of the headers of the 7z archive does not match.");

        var encrypted = false;
        for (var depth = 0; ; depth++)
        {
            try
            {
                var reader = new SevenZipHeaderReader(header);
                var kind = reader.ReadHeaderKind();
                if (kind == SevenZipPropertyId.Header)
                    return reader.ReadHeader(dataOffset, streamLength);

                if (kind != SevenZipPropertyId.EncodedHeader)
                    throw new InvalidDataException($"The headers of the 7z archive start with an unknown property, 0x{kind:X2}.");

                if (depth >= SevenZipConstants.MaxEncodedHeaderDepth)
                    throw new InvalidDataException("The headers of the 7z archive are encoded too many times.");

                var streams = reader.ReadEncodedHeader();
                SevenZipHeaderReader.Locate(streams, dataOffset, streamLength);
                if (streams.Folders.Count is 0)
                    throw new InvalidDataException("The encoded headers of the 7z archive have no folder.");

                var folder = streams.Folders[0];
                encrypted |= folder.IsEncrypted;
                header = DecodeHeader(stream, folder, context);
            }
            catch (InvalidDataException ex) when (encrypted)
            {
                throw new ArchivePasswordException("The encrypted headers of the 7z archive are invalid. The password may be wrong.", ex);
            }
        }
    }

    private static byte[] DecodeHeader(Stream stream, SevenZipFolder folder, SevenZipDecoderContext context)
    {
        var size = folder.UnpackSize;
        if (size > Array.MaxLength || size > context.MaxMemoryUsage)
            throw new InvalidDataException("The headers of the 7z archive are too large.");

        // The size is not trusted for the allocation: the buffer grows with the decoded data
        using var decoder = SevenZipFolderDecoder.Open(stream, folder, context);
        using var output = new MemoryStream((int)Math.Min(size, 1024 * 1024));
        decoder.CopyTo(output);
        if (output.Length != size)
            throw new InvalidDataException("The encoded headers of the 7z archive are truncated.");

        var header = output.ToArray();
        if (folder.UnpackCrc is { } crc && System.IO.Hashing.Crc32.HashToUInt32(header) != crc)
            throw new InvalidDataException("The CRC-32 of the encoded headers of the 7z archive does not match.");

        return header;
    }

    /// <summary>Writes the zeros of the signature header, which <see cref="WriteHeaders"/> overwrites.</summary>
    public static void WriteSignaturePlaceholder(Stream output)
    {
        output.Write(new byte[SevenZipConstants.SignatureHeaderSize]);
    }

    /// <summary>Writes the headers after the packed streams, then the signature header at <paramref name="baseOffset"/>.</summary>
    public static void WriteHeaders(Stream output, long baseOffset, SevenZipWriterSettings settings, IReadOnlyList<SevenZipFolder> folders, IReadOnlyList<SevenZipFileItem> files)
    {
        var dataOffset = baseOffset + SevenZipConstants.SignatureHeaderSize;
        ulong nextHeaderOffset = 0;
        ulong nextHeaderSize = 0;
        uint nextHeaderCrc = 0;
        if (files.Count > 0)
        {
            var packPosition = folders.Count > 0 ? folders[0].PackOffsets[0] - dataOffset : 0;
            var header = SevenZipHeaderWriter.WriteHeader(packPosition, folders, files);
            var options = settings.Options;
            if (options.CompressHeaders || options.EncryptHeaders)
            {
                var chain = new SevenZipEncoderChain(output, settings.GetHeaderSpecs(header.Length));
                chain.Input.Write(header);
                var folder = chain.Complete();
                folder.UnpackCrc = System.IO.Hashing.Crc32.HashToUInt32(header);
                header = SevenZipHeaderWriter.WriteEncodedHeader(folder.PackOffsets[0] - dataOffset, folder);
            }

            nextHeaderOffset = (ulong)(output.Position - dataOffset);
            nextHeaderSize = (ulong)header.Length;
            nextHeaderCrc = System.IO.Hashing.Crc32.HashToUInt32(header);
            output.Write(header);
        }

        var end = output.Position;
        Span<byte> signatureHeader = stackalloc byte[SevenZipConstants.SignatureHeaderSize];
        SevenZipConstants.Signature.CopyTo(signatureHeader);
        signatureHeader[6] = 0;
        signatureHeader[7] = 4;
        BinaryPrimitives.WriteUInt64LittleEndian(signatureHeader[12..], nextHeaderOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(signatureHeader[20..], nextHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(signatureHeader[28..], nextHeaderCrc);
        BinaryPrimitives.WriteUInt32LittleEndian(signatureHeader[8..], System.IO.Hashing.Crc32.HashToUInt32(signatureHeader.Slice(12, 20)));
        output.Position = baseOffset;
        output.Write(signatureHeader);
        output.Position = end;
        output.Flush();
    }

    /// <summary>
    /// Orders the files of a new archive: the files without content keep their place, and the files with content take the
    /// places of the files with content in the order of the folders.
    /// </summary>
    public static List<SevenZipFileItem> OrderFiles(IEnumerable<SevenZipFileItem> files, IReadOnlyList<SevenZipFileItem> streamFiles)
    {
        var result = new List<SevenZipFileItem>();
        var next = 0;
        foreach (var file in files)
        {
            result.Add(file.HasStream ? streamFiles[next++] : file);
        }

        return result;
    }

    public static DateTimeOffset? ToDateTimeOffset(long? fileTime)
    {
        if (fileTime is not { } value || value < 0 || value > MaxFileTime)
            return null;

        return new DateTimeOffset(DateTime.FromFileTimeUtc(value));
    }

    public static long? ToFileTime(DateTimeOffset? value)
    {
        if (value is not { } time)
            return null;

        var utc = time.UtcDateTime;
        if (utc.Year < 1601)
            return 0;

        return utc.ToFileTimeUtc();
    }
}
