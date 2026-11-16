using System.Buffers.Binary;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// An entry written by a <see cref="ZipWriter"/>: the fields of its local header and of its central directory record. An
/// entry copied from an existing archive (Update mode) keeps the extra fields of its central directory record.
/// </summary>
internal sealed class ZipWriterRecord
{
    public const ushort Zip64ExtraId = 0x0001;
    public const ushort ExtendedTimestampExtraId = 0x5455;
    public const ushort AesExtraId = 0x9901;
    public const ushort FlagLzmaEndMarker = 0x0002;
    public const uint MaxClassicValue = 0xFFFFFFFF;

    private const int Zip64LocalExtraLength = 4 + 16;
    private const int AesExtraLength = 4 + 7;
    private const int TimestampExtraLength = 4 + 5;

    public required byte[] Name { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the copy of an existing entry writes <see cref="Name"/> and the UTF-8 flag in
    /// its local header too, instead of the bytes of the archive (the name is re-encoded as UTF-8 for a comment that needs it).
    /// </summary>
    public bool RewriteLocalName { get; set; }

    public byte[] Comment { get; set; } = [];

    public required ushort VersionMadeBy { get; set; }

    public ushort VersionNeeded { get; set; }

    public ushort Flags { get; set; }

    /// <summary>Gets the method of the headers: 99 for AES, otherwise the compression method.</summary>
    public required ushort RawMethod { get; set; }

    /// <summary>Gets the compression method.</summary>
    public required ushort Method { get; set; }

    public required ushort DosTime { get; init; }

    public required ushort DosDate { get; init; }

    /// <summary>Gets the modification time of the extended timestamp extra field, or <see langword="null"/> when it does not fit in 32 bits.</summary>
    public required int? UnixTime { get; init; }

    public required uint ExternalAttributes { get; set; }

    public ushort InternalAttributes { get; init; }

    /// <summary>Gets the extra fields of the central directory record of a copied entry, without its zip64 extra field, or <see langword="null"/> for a new entry.</summary>
    public byte[]? PreservedCentralExtra { get; init; }

    /// <summary>Gets or sets the number of bytes of the entry in the archive: its local header, its content and its data descriptor.</summary>
    public long TotalLength { get; set; }

    public required ZipEncryption Encryption { get; init; }

    /// <summary>Gets or sets the AES vendor version: 1 (AE-1, the CRC is stored) or 2 (AE-2, the CRC is 0).</summary>
    public ushort AesVendorVersion { get; set; }

    /// <summary>Gets a value indicating whether the local header has a zip64 extra field.</summary>
    public required bool LocalZip64 { get; set; }

    /// <summary>Gets a value indicating whether every zip64 field is written (<see cref="Zip64Mode.Always"/>).</summary>
    public required bool AlwaysZip64 { get; init; }

    public long LocalHeaderOffset { get; set; }

    public uint Crc { get; set; }

    public long CompressedSize { get; set; }

    public long UncompressedSize { get; set; }

    public bool IsAes => Encryption is ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256;

    public bool HasDataDescriptor => (Flags & ZipEntryInfo.FlagDataDescriptor) != 0;

    /// <summary>Gets the offset of the AES vendor version in the local header.</summary>
    public int LocalAesVersionOffset => ZipEntryInfo.LocalHeaderLength + Name.Length + (LocalZip64 ? Zip64LocalExtraLength : 0) + 4;

    /// <summary>Gets the offset of the values of the zip64 extra field in the local header.</summary>
    public int LocalZip64ValuesOffset => ZipEntryInfo.LocalHeaderLength + Name.Length + 4;

    /// <summary>Gets the CRC the headers store: 0 for AE-2.</summary>
    public uint StoredCrc => IsAes && AesVendorVersion is 2 ? 0 : Crc;

    /// <summary>Writes the local header, with the current CRC and sizes (or zeros, with a data descriptor).</summary>
    public byte[] BuildLocalHeader()
    {
        var extraLength = (LocalZip64 ? Zip64LocalExtraLength : 0) + (IsAes ? AesExtraLength : 0) + (UnixTime is null ? 0 : TimestampExtraLength);
        var header = new byte[ZipEntryInfo.LocalHeaderLength + Name.Length + extraLength];
        var span = header.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, ZipEntryInfo.LocalHeaderSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], VersionNeeded);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], RawMethod);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], DosTime);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], DosDate);
        WriteLocalCrcAndSizes(span[14..]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], (ushort)Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], (ushort)extraLength);
        Name.CopyTo(span[ZipEntryInfo.LocalHeaderLength..]);

        var extra = span[(ZipEntryInfo.LocalHeaderLength + Name.Length)..];
        if (LocalZip64)
        {
            // The local zip64 extra field has both sizes (APPNOTE 4.5.3), zeros when a data descriptor follows the content
            BinaryPrimitives.WriteUInt16LittleEndian(extra, Zip64ExtraId);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[2..], 16);
            WriteLocalZip64Values(extra[4..]);
            extra = extra[Zip64LocalExtraLength..];
        }

        WriteCommonExtraFields(extra);
        return header;
    }

    /// <summary>Writes the CRC and the sizes of the local header (12 bytes).</summary>
    public void WriteLocalCrcAndSizes(Span<byte> destination)
    {
        if (HasDataDescriptor)
        {
            destination[..12].Clear();
            if (LocalZip64)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], MaxClassicValue);
                BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], MaxClassicValue);
            }

            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, StoredCrc);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], LocalZip64 ? MaxClassicValue : (uint)CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], LocalZip64 ? MaxClassicValue : (uint)UncompressedSize);
    }

    /// <summary>Writes the values of the local zip64 extra field (16 bytes).</summary>
    public void WriteLocalZip64Values(Span<byte> destination)
    {
        var known = !HasDataDescriptor;
        BinaryPrimitives.WriteUInt64LittleEndian(destination, known ? (ulong)UncompressedSize : 0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], known ? (ulong)CompressedSize : 0);
    }

    /// <summary>Writes the data descriptor that follows the content: a signature, the CRC, and 4-byte or 8-byte sizes.</summary>
    public byte[] BuildDataDescriptor()
    {
        var wide = LocalZip64 || CompressedSize >= MaxClassicValue || UncompressedSize >= MaxClassicValue;
        var descriptor = new byte[wide ? 24 : 16];
        var span = descriptor.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, ZipEntryInfo.DataDescriptorSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], StoredCrc);
        if (wide)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[8..], (ulong)CompressedSize);
            BinaryPrimitives.WriteUInt64LittleEndian(span[16..], (ulong)UncompressedSize);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)CompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)UncompressedSize);
        }

        return descriptor;
    }

    /// <summary>Writes the central directory record.</summary>
    public void WriteCentralRecord(Stream output)
    {
        var usizeInZip64 = AlwaysZip64 || UncompressedSize >= MaxClassicValue;
        var csizeInZip64 = AlwaysZip64 || CompressedSize >= MaxClassicValue;
        var offsetInZip64 = AlwaysZip64 || LocalHeaderOffset >= MaxClassicValue;
        var zip64Length = (usizeInZip64 ? 8 : 0) + (csizeInZip64 ? 8 : 0) + (offsetInZip64 ? 8 : 0);
        var zip64ExtraLength = zip64Length > 0 ? 4 + zip64Length : 0;
        var extraLength = zip64ExtraLength + (PreservedCentralExtra?.Length ?? ((IsAes ? AesExtraLength : 0) + (UnixTime is null ? 0 : TimestampExtraLength)));

        var versionNeeded = VersionNeeded;
        if (zip64Length > 0 && versionNeeded < 45)
        {
            versionNeeded = 45;
        }

        var record = new byte[ZipEntryInfo.CentralHeaderLength + Name.Length + extraLength + Comment.Length];
        var span = record.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, ZipEntryInfo.CentralHeaderSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], VersionMadeBy);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], versionNeeded);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], RawMethod);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], DosTime);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], DosDate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], StoredCrc);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], csizeInZip64 ? MaxClassicValue : (uint)CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], usizeInZip64 ? MaxClassicValue : (uint)UncompressedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], (ushort)Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[30..], (ushort)extraLength);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], (ushort)Comment.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 0); // Disk number
        BinaryPrimitives.WriteUInt16LittleEndian(span[36..], InternalAttributes);
        BinaryPrimitives.WriteUInt32LittleEndian(span[38..], ExternalAttributes);
        BinaryPrimitives.WriteUInt32LittleEndian(span[42..], offsetInZip64 ? MaxClassicValue : (uint)LocalHeaderOffset);
        Name.CopyTo(span[ZipEntryInfo.CentralHeaderLength..]);

        var extra = span[(ZipEntryInfo.CentralHeaderLength + Name.Length)..];
        if (zip64Length > 0)
        {
            // Only the values whose field is 0xFFFFFFFF, in this order (APPNOTE 4.5.3)
            BinaryPrimitives.WriteUInt16LittleEndian(extra, Zip64ExtraId);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[2..], (ushort)zip64Length);
            var values = extra[4..];
            if (usizeInZip64)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(values, (ulong)UncompressedSize);
                values = values[8..];
            }

            if (csizeInZip64)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(values, (ulong)CompressedSize);
                values = values[8..];
            }

            if (offsetInZip64)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(values, (ulong)LocalHeaderOffset);
            }

            extra = extra[zip64ExtraLength..];
        }

        if (PreservedCentralExtra is not null)
        {
            PreservedCentralExtra.CopyTo(extra);
            extra = extra[PreservedCentralExtra.Length..];
        }
        else
        {
            extra = WriteCommonExtraFields(extra);
        }

        Comment.CopyTo(extra);
        output.Write(record);
    }

    /// <summary>Writes the AES and the extended timestamp extra fields, which are the same in both headers.</summary>
    private Span<byte> WriteCommonExtraFields(Span<byte> extra)
    {
        if (IsAes)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(extra, AesExtraId);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[2..], 7);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[4..], AesVendorVersion);
            extra[6] = (byte)'A';
            extra[7] = (byte)'E';
            extra[8] = WinZipAesCipher.GetStrength(Encryption);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[9..], Method);
            extra = extra[AesExtraLength..];
        }

        if (UnixTime is { } unixTime)
        {
            // The modification time only, in both headers
            BinaryPrimitives.WriteUInt16LittleEndian(extra, ExtendedTimestampExtraId);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[2..], 5);
            extra[4] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(extra[5..], unixTime);
            extra = extra[TimestampExtraLength..];
        }

        return extra;
    }
}
