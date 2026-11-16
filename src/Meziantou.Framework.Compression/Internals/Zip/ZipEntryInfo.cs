using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>The fields of a central directory record or of a local header, once the extra fields are applied.</summary>
internal sealed class ZipEntryInfo
{
    public const int CentralHeaderLength = 46;
    public const int LocalHeaderLength = 30;

    public const uint LocalHeaderSignature = 0x04034B50;
    public const uint CentralHeaderSignature = 0x02014B50;
    public const uint DataDescriptorSignature = 0x08074B50;

    public const ushort FlagEncrypted = 0x0001;
    public const ushort FlagDataDescriptor = 0x0008;
    public const ushort FlagStrongEncryption = 0x0040;
    public const ushort FlagUtf8 = 0x0800;
    public const ushort FlagMaskedHeaders = 0x2000;

    public const ushort AesMethod = 99;

    public ushort VersionMadeBy { get; set; }

    public ushort VersionNeeded { get; set; }

    public ushort InternalAttributes { get; set; }

    /// <summary>Gets or sets the central directory record the entry was read from, which the Update mode copies.</summary>
    public byte[]? RawCentralRecord { get; set; }

    public ushort Flags { get; set; }

    /// <summary>Gets or sets the method of the header (99 for AES).</summary>
    public ushort RawMethod { get; set; }

    /// <summary>Gets or sets the compression method (the one of the AES extra field for AES).</summary>
    public ushort Method { get; set; }

    public ushort DosTime { get; set; }

    public ushort DosDate { get; set; }

    public uint Crc { get; set; }

    public long CompressedSize { get; set; }

    public long UncompressedSize { get; set; }

    public long LocalHeaderOffset { get; set; }

    public uint DiskNumber { get; set; }

    public int ExternalAttributes { get; set; }

    public string FullName { get; set; } = "";

    public string Comment { get; set; } = "";

    /// <summary>Gets or sets the encoding of the name, used to decode the target of a symbolic link.</summary>
    public Encoding? NameEncoding { get; set; }

    public ZipEncryption Encryption { get; set; }

    /// <summary>Gets or sets the AES vendor version: 1 (AE-1, the CRC is checked) or 2 (AE-2, the CRC is not stored).</summary>
    public int AesVendorVersion { get; set; }

    public bool HasAesExtra { get; set; }

    public bool HasZip64Extra { get; set; }

    public DateTimeOffset? LastWriteTime { get; set; }

    public DateTimeOffset? LastAccessTime { get; set; }

    public DateTimeOffset? CreationTime { get; set; }

    public bool HasDataDescriptor => (Flags & FlagDataDescriptor) != 0;

    public bool IsEncrypted => (Flags & FlagEncrypted) != 0;

    /// <summary>Gets the operating system that created the entry (the high byte of "version made by").</summary>
    public int HostSystem => VersionMadeBy >> 8;

    /// <summary>Gets a value indicating whether the external attributes are MS-DOS attributes (FAT, OS/2 HPFS, NTFS, VFAT).</summary>
    public bool HasDosAttributes => HostSystem is 0 or 6 or 10 or 14;

    /// <summary>Gets a value indicating whether the high 16 bits of the external attributes are a Unix mode (Unix, OS X).</summary>
    public bool HasUnixAttributes => HostSystem is 3 or 19;

    /// <summary>Gets the CRC the content is checked against, or <see langword="null"/> when there is none (AE-2).</summary>
    public uint? ExpectedCrc => HasAesExtra && AesVendorVersion is 2 ? null : Crc;

    /// <summary>Parses a central directory record.</summary>
    /// <returns>The length of the record.</returns>
    public static ZipEntryInfo ParseCentral(ReadOnlySpan<byte> data, ArchiveReaderOptions options, out int length)
    {
        if (data.Length < CentralHeaderLength || BinaryPrimitives.ReadUInt32LittleEndian(data) != CentralHeaderSignature)
            throw new InvalidDataException("The central directory of the zip archive is corrupted.");

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(data[30..]);
        var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(data[32..]);
        length = CentralHeaderLength + nameLength + extraLength + commentLength;
        if (length > data.Length)
            throw new InvalidDataException("The central directory of the zip archive is truncated.");

        var info = new ZipEntryInfo
        {
            VersionMadeBy = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
            VersionNeeded = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]),
            InternalAttributes = BinaryPrimitives.ReadUInt16LittleEndian(data[36..]),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
            RawMethod = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
            DosTime = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            DosDate = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            Crc = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]),
            CompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
            UncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
            DiskNumber = BinaryPrimitives.ReadUInt16LittleEndian(data[34..]),
            ExternalAttributes = BinaryPrimitives.ReadInt32LittleEndian(data[38..]),
            LocalHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[42..]),
        };

        var name = data.Slice(CentralHeaderLength, nameLength);
        var extra = data.Slice(CentralHeaderLength + nameLength, extraLength);
        var comment = data.Slice(CentralHeaderLength + nameLength + extraLength, commentLength);
        info.Apply(name, extra, comment, options, central: true);
        info.RawCentralRecord = data[..length].ToArray();
        return info;
    }

    /// <summary>Parses a local header: <paramref name="header"/> is its fixed part, after the signature (26 bytes).</summary>
    public static ZipEntryInfo ParseLocal(ReadOnlySpan<byte> header, ReadOnlySpan<byte> name, ReadOnlySpan<byte> extra, ArchiveReaderOptions options)
    {
        var info = new ZipEntryInfo
        {
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]),
            RawMethod = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]),
            DosTime = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]),
            DosDate = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]),
            Crc = BinaryPrimitives.ReadUInt32LittleEndian(header[10..]),
            CompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]),
            UncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[18..]),
        };

        info.Apply(name, extra, [], options, central: false);
        return info;
    }

    private void Apply(ReadOnlySpan<byte> name, ReadOnlySpan<byte> extra, ReadOnlySpan<byte> comment, ArchiveReaderOptions options, bool central)
    {
        var utf8 = (Flags & FlagUtf8) != 0;
        NameEncoding = utf8 ? Encoding.UTF8 : options.EntryNameEncoding;
        var fullName = Decode(name, NameEncoding);
        var fullComment = Decode(comment, NameEncoding);
        Method = RawMethod;
        DateTimeOffset? ntfsWrite = null;
        DateTimeOffset? unixWrite = null;

        var usizeInZip64 = UncompressedSize == uint.MaxValue;
        var csizeInZip64 = CompressedSize == uint.MaxValue;
        var offsetInZip64 = central && LocalHeaderOffset == uint.MaxValue;
        var diskInZip64 = central && DiskNumber == ushort.MaxValue;

        var remaining = extra;
        while (remaining.Length >= 4)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(remaining);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(remaining[2..]);
            if (size > remaining.Length - 4)
                break; // A truncated extra field: ignored, as Info-ZIP does

            var field = remaining.Slice(4, size);
            remaining = remaining[(4 + size)..];
            switch (id)
            {
                case 0x0001 when !HasZip64Extra:
                    HasZip64Extra = true;
                    ApplyZip64(field, usizeInZip64, csizeInZip64, offsetInZip64, diskInZip64);
                    break;

                case 0x000A:
                    ApplyNtfs(field, ref ntfsWrite);
                    break;

                case 0x5455:
                    ApplyExtendedTimestamp(field, central, ref unixWrite);
                    break;

                case 0x7075 when !utf8:
                    if (TryGetUnicodeExtra(field, name) is { } unicodeName)
                    {
                        fullName = unicodeName;
                    }

                    break;

                case 0x6375 when !utf8 && central:
                    if (TryGetUnicodeExtra(field, comment) is { } unicodeComment)
                    {
                        fullComment = unicodeComment;
                    }

                    break;

                case 0x9901 when field.Length >= 7 && !HasAesExtra:
                    HasAesExtra = true;
                    AesVendorVersion = BinaryPrimitives.ReadUInt16LittleEndian(field);
                    var strength = field[4];
                    var method = BinaryPrimitives.ReadUInt16LittleEndian(field[5..]);
                    if (RawMethod is AesMethod)
                    {
                        Method = method;
                        Encryption = strength switch
                        {
                            1 => ZipEncryption.Aes128,
                            2 => ZipEncryption.Aes192,
                            3 => ZipEncryption.Aes256,
                            _ => ZipEncryption.Unsupported,
                        };
                    }

                    break;
            }
        }

        if (IsEncrypted)
        {
            if ((Flags & FlagStrongEncryption) != 0 || (RawMethod is AesMethod && !HasAesExtra) || (HasAesExtra && AesVendorVersion is not (1 or 2)))
            {
                Encryption = ZipEncryption.Unsupported;
            }
            else if (RawMethod is not AesMethod)
            {
                Encryption = ZipEncryption.ZipCrypto;
            }
        }
        else
        {
            Encryption = ZipEncryption.None;
            Method = RawMethod;
        }

        if (!central || HasDosAttributes)
        {
            // MS-DOS and Windows tools may write backslashes; the forward-only reader does not know the host, and normalizes them too
            fullName = fullName.Replace('\\', '/');
        }

        FullName = fullName;
        Comment = fullComment;
        LastWriteTime = ntfsWrite ?? unixWrite ?? DecodeDosTime(DosDate, DosTime);
    }

    private static string Decode(ReadOnlySpan<byte> value, Encoding? encoding)
    {
        if (value.IsEmpty)
            return "";

        return encoding is null ? Cp437.GetString(value) : encoding.GetString(value);
    }

    private void ApplyZip64(ReadOnlySpan<byte> field, bool usize, bool csize, bool offset, bool disk)
    {
        if (usize)
        {
            UncompressedSize = ReadZip64Value(ref field);
        }

        if (csize)
        {
            CompressedSize = ReadZip64Value(ref field);
        }

        if (offset)
        {
            LocalHeaderOffset = ReadZip64Value(ref field);
        }

        if (disk)
        {
            if (field.Length < 4)
                throw new InvalidDataException("The zip64 extra field of an entry is truncated.");

            DiskNumber = BinaryPrimitives.ReadUInt32LittleEndian(field);
        }

        static long ReadZip64Value(ref ReadOnlySpan<byte> field)
        {
            if (field.Length < 8)
                throw new InvalidDataException("The zip64 extra field of an entry is truncated.");

            var value = BinaryPrimitives.ReadUInt64LittleEndian(field);
            if (value > long.MaxValue)
                throw new InvalidDataException("A size or an offset of the zip64 extra field of an entry is invalid.");

            field = field[8..];
            return (long)value;
        }
    }

    private void ApplyNtfs(ReadOnlySpan<byte> field, ref DateTimeOffset? lastWrite)
    {
        if (field.Length < 4)
            return;

        var attributes = field[4..];
        while (attributes.Length >= 4)
        {
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(attributes);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(attributes[2..]);
            if (size > attributes.Length - 4)
                return;

            if (tag is 1 && size >= 24)
            {
                lastWrite = FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(attributes[4..]));
                LastAccessTime = FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(attributes[12..]));
                CreationTime = FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(attributes[20..]));
                return;
            }

            attributes = attributes[(4 + size)..];
        }
    }

    private void ApplyExtendedTimestamp(ReadOnlySpan<byte> field, bool central, ref DateTimeOffset? lastWrite)
    {
        if (field.IsEmpty)
            return;

        var flags = field[0];
        var times = field[1..];
        if ((flags & 1) != 0 && times.Length >= 4)
        {
            lastWrite = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt32LittleEndian(times));
            times = times[4..];
        }

        // The central directory stores the modification time only
        if (central)
            return;

        if ((flags & 2) != 0 && times.Length >= 4)
        {
            LastAccessTime ??= DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt32LittleEndian(times));
            times = times[4..];
        }

        if ((flags & 4) != 0 && times.Length >= 4)
        {
            CreationTime ??= DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt32LittleEndian(times));
        }
    }

    private static string? TryGetUnicodeExtra(ReadOnlySpan<byte> field, ReadOnlySpan<byte> headerValue)
    {
        // Version 1, the CRC-32 of the value of the header, then the value in UTF-8
        if (field.Length < 5 || field[0] is not 1)
            return null;

        if (BinaryPrimitives.ReadUInt32LittleEndian(field[1..]) != System.IO.Hashing.Crc32.HashToUInt32(headerValue))
            return null;

        return Encoding.UTF8.GetString(field[5..]);
    }

    private static DateTimeOffset? FromFileTime(long value)
    {
        // 0 means that the time is not set
        if (value <= 0 || value > DateTime.MaxValue.ToFileTimeUtc())
            return null;

        return new DateTimeOffset(DateTime.FromFileTimeUtc(value), TimeSpan.Zero);
    }

    /// <summary>Decodes an MS-DOS date and time, a local time without a time zone, as a time of the local time zone (as <see cref="System.IO.Compression.ZipArchiveEntry"/> does).</summary>
    public static DateTimeOffset? DecodeDosTime(ushort date, ushort time)
    {
        var year = 1980 + (date >> 9);
        var month = (date >> 5) & 0xF;
        var day = date & 0x1F;
        var hour = time >> 11;
        var minute = (time >> 5) & 0x3F;
        var second = (time & 0x1F) * 2;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            return null;

        return new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified));
    }
}
