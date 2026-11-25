using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// Reads the headers of a volume of a RAR archive: the signature (after an SFX module), the main header, then the file,
/// service and end headers, decrypting them when the archive encrypts its headers. The data that follow a header are
/// skipped when the next header is read, unless they are read first.
/// </summary>
internal sealed class RarHeaderReader
{
    /// <summary>The largest SFX module before the signature that is searched.</summary>
    public const int MaxSfxSize = 1024 * 1024;

    private const int MaxRar5HeaderSize = 2 * 1024 * 1024;

    private readonly RarInput _input;
    private readonly ArchiveReaderOptions _options;
    private readonly RarKeyStore _keys;
    private long _nextHeaderPosition;
    private byte[]? _rar5HeaderKey;
    private bool _rar4HeadersEncrypted;
    private bool _ended;
    private Exception? _error;

    public RarHeaderReader(RarInput input, ArchiveReaderOptions options, RarKeyStore keys)
    {
        _input = input;
        _options = options;
        _keys = keys;
    }

    public RarFormatVersion Format { get; private set; }

    public RarMainHeader MainHeader { get; private set; } = null!;

    public RarInput Input => _input;

    /// <summary>Reads the signature and the main header of the volume.</summary>
    /// <exception cref="InvalidDataException">The stream is not a RAR archive.</exception>
    public void ReadSignatureAndMainHeader()
    {
        if (_input.StartsWithOldSignature())
            throw new NotSupportedException("The archives of RAR 1.4 and older are not supported.");

        Format = _input.FindSignature(MaxSfxSize) ?? throw new InvalidDataException("The data are not a RAR archive.");
        _nextHeaderPosition = _input.Position;
        while (true)
        {
            var header = ReadNext() ?? throw new InvalidDataException("The RAR archive has no main header.");
            if (header is RarMainHeader main)
            {
                MainHeader = main;
                return;
            }

            if (header.Kind is not RarHeaderKind.Other)
                throw new InvalidDataException("The RAR archive has no main header.");
        }
    }

    /// <summary>Reads the next header, skipping the data of the previous one. Returns <see langword="null"/> at the end of the volume.</summary>
    public RarHeader? ReadNext()
    {
        if (_ended)
            return null;

        // A header that could not be read may have been read in part: the position of the next one is unknown
        if (_error is not null)
        {
            var message = "A previous header of the RAR archive could not be read. " + _error.Message;
            throw _error switch
            {
                NotSupportedException => new NotSupportedException(message, _error),
                ArchivePasswordException => new ArchivePasswordException(message, _error),
                _ => new InvalidDataException(message, _error),
            };
        }

        try
        {
            return ReadNextCore();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArchivePasswordException or NotSupportedException)
        {
            _error = ex;
            throw;
        }
    }

    private RarHeader? ReadNextCore()
    {
        var position = _input.Position;
        if (position < _nextHeaderPosition)
        {
            if (!_input.Skip(_nextHeaderPosition - position))
            {
                _ended = true;
                return null;
            }
        }
        else if (position > _nextHeaderPosition)
        {
            throw new InvalidOperationException("The data of the previous header were read too far.");
        }

        var header = Format is RarFormatVersion.Rar5 ? ReadRar5Header() : ReadRar4Header();
        if (header is null)
        {
            _ended = true;
            return null;
        }

        header.DataOffset = _input.Position;
        if (header.DataSize < 0 || header.DataSize > long.MaxValue - header.DataOffset)
            throw new InvalidDataException("A header of the RAR archive has an invalid data size.");

        _nextHeaderPosition = header.DataOffset + header.DataSize;
        if (header is RarEndHeader)
        {
            _ended = true;
        }

        return header;
    }

    private Exception HeaderError(string message)
    {
        return _rar5HeaderKey is not null || _rar4HeadersEncrypted
            ? new ArchivePasswordException(message + " The password may be wrong.")
            : new InvalidDataException(message);
    }

    private RarHeader? ReadRar5Header()
    {
        byte[] header;
        int vintLength;
        uint crc;
        if (_rar5HeaderKey is not null)
        {
            // AES is not available on the browser (WebAssembly)
            if (OperatingSystem.IsBrowser())
                throw new PlatformNotSupportedException("The encrypted headers of RAR archives are not supported on the browser.");

            Span<byte> iv = stackalloc byte[16];
            var read = _input.ReadAtMost(iv);
            if (read is 0)
                return null;

            if (read < 16)
                throw new InvalidDataException("The RAR archive is truncated.");

            var first = new byte[16];
            _input.ReadExactly(first);
            using var aes = Aes.Create();
            aes.Key = _rar5HeaderKey;
            var plainFirst = aes.DecryptCbc(first, iv, PaddingMode.None);
            crc = BinaryPrimitives.ReadUInt32LittleEndian(plainFirst);
            var reader = new RarSpanReader(plainFirst.AsSpan(4));
            var size = reader.ReadVInt();
            vintLength = reader.Position;
            if (size is 0 || size > MaxRar5HeaderSize)
                throw HeaderError("A header of the RAR archive has an invalid size.");

            var total = 4 + vintLength + (int)size;
            var padded = (total + 15) & ~15;
            var encrypted = new byte[padded];
            first.CopyTo(encrypted, 0);
            if (_input.ReadAtMost(encrypted.AsSpan(16)) != padded - 16)
                throw HeaderError("The RAR archive is truncated.");

            var plain = aes.DecryptCbc(encrypted, iv, PaddingMode.None);
            header = plain.AsSpan(4, vintLength + (int)size).ToArray();
        }
        else
        {
            Span<byte> crcBytes = stackalloc byte[4];
            var read = _input.ReadAtMost(crcBytes);
            if (read is 0)
                return null;

            if (read < 4)
                throw new InvalidDataException("The RAR archive is truncated.");

            crc = BinaryPrimitives.ReadUInt32LittleEndian(crcBytes);
            Span<byte> vint = stackalloc byte[10];
            vintLength = 0;
            ulong size = 0;
            while (true)
            {
                if (vintLength == vint.Length)
                    throw new InvalidDataException("A header of the RAR archive has an invalid size.");

                var value = _input.ReadByte();
                if (value < 0)
                    throw new InvalidDataException("The RAR archive is truncated.");

                vint[vintLength] = (byte)value;
                size |= (ulong)(value & 0x7F) << (7 * vintLength);
                vintLength++;
                if ((value & 0x80) == 0)
                    break;
            }

            if (size is 0 || size > MaxRar5HeaderSize)
                throw new InvalidDataException("A header of the RAR archive has an invalid size.");

            header = new byte[vintLength + (int)size];
            vint[..vintLength].CopyTo(header);
            _input.ReadExactly(header.AsSpan(vintLength));
        }

        if (System.IO.Hashing.Crc32.HashToUInt32(header) != crc)
            throw HeaderError("The CRC of a header of the RAR archive does not match.");

        return ParseRar5Header(header.AsSpan(vintLength));
    }

    private RarHeader ParseRar5Header(ReadOnlySpan<byte> header)
    {
        var reader = new RarSpanReader(header);
        var type = reader.ReadVInt();
        var flags = reader.ReadVInt();
        ulong extraSize = 0;
        ulong dataSize = 0;
        if ((flags & 0x1) != 0)
        {
            extraSize = reader.ReadVInt();
        }

        if ((flags & 0x2) != 0)
        {
            dataSize = reader.ReadVInt();
        }

        if (extraSize > (ulong)header.Length || dataSize > long.MaxValue)
            throw new InvalidDataException("A header of the RAR archive is invalid.");

        var extra = header[(header.Length - (int)extraSize)..];
        switch (type)
        {
            case 1:
                {
                    var archiveFlags = reader.ReadVInt();
                    return new RarMainHeader
                    {
                        Kind = RarHeaderKind.Main,
                        IsVolume = (archiveFlags & 0x1) != 0,
                        IsSolid = (archiveFlags & 0x4) != 0,
                        IsFirstVolume = (archiveFlags & 0x2) == 0 || ReadVolumeNumber(ref reader) is 0,
                        NewVolumeNaming = true,
                        DataSize = (long)dataSize,
                    };
                }

            case 2:
            case 3:
                return ParseRar5File(ref reader, extra, flags, (long)dataSize, isService: type is 3);

            case 4:
                ParseRar5Encryption(ref reader);
                return new RarHeader { Kind = RarHeaderKind.Other, DataSize = (long)dataSize };

            case 5:
                {
                    var endFlags = reader.ReadVInt();
                    return new RarEndHeader { Kind = RarHeaderKind.End, HasNextVolume = (endFlags & 0x1) != 0 };
                }

            default:
                return new RarHeader { Kind = RarHeaderKind.Other, DataSize = (long)dataSize };
        }

        static ulong ReadVolumeNumber(ref RarSpanReader reader) => reader.ReadVInt();
    }

    private void ParseRar5Encryption(ref RarSpanReader reader)
    {
        var version = reader.ReadVInt();
        if (version is not 0)
            throw new NotSupportedException($"The encryption version {version} of the RAR archive is not supported.");

        var flags = reader.ReadVInt();
        var kdfCount = reader.ReadByte();
        var salt = reader.ReadBytes(16).ToArray();
        byte[]? check = null;
        if ((flags & 0x1) != 0)
        {
            check = reader.ReadBytes(12).ToArray();
        }

        if (_keys.Password is null)
            throw new ArchivePasswordException("The headers of the RAR archive are encrypted and no password is provided.");

        var key = _keys.GetRar5Key(salt, kdfCount);
        if (check is not null && RarKeyStore.IsPasswordCheckValid(check) && !check.AsSpan(0, 8).SequenceEqual(key.PasswordCheck))
            throw new ArchivePasswordException("The password of the RAR archive is wrong.");

        _rar5HeaderKey = key.Key;
    }

    private static RarFileHeader ParseRar5File(ref RarSpanReader reader, ReadOnlySpan<byte> extra, ulong headerFlags, long dataSize, bool isService)
    {
        var fileFlags = reader.ReadVInt();
        var unpackedSize = reader.ReadVInt();
        var attributes = reader.ReadVInt();
        DateTimeOffset? lastWriteTime = null;
        if ((fileFlags & 0x2) != 0)
        {
            lastWriteTime = DateTimeOffset.FromUnixTimeSeconds(reader.ReadUInt32());
        }

        uint? crc = null;
        if ((fileFlags & 0x4) != 0)
        {
            crc = reader.ReadUInt32();
        }

        var compression = reader.ReadVInt();
        var hostOs = reader.ReadVInt();
        var nameLength = reader.ReadVInt();
        if (nameLength > (ulong)reader.Remaining)
            throw new InvalidDataException("A header of the RAR archive is invalid.");

        var name = DecodeUtf8(reader.ReadBytes((int)nameLength));
        var algorithm = (int)(compression & 0x3F);
        var dictionaryBits = (int)((compression >> 10) & 0x1F);
        var dictionarySize = 0x20000L << Math.Min(dictionaryBits, 31);
        if (algorithm is 1)
        {
            dictionarySize += dictionarySize / 32 * (long)((compression >> 15) & 0x1F);
        }

        var file = new RarFileHeader
        {
            Kind = isService ? RarHeaderKind.Service : RarHeaderKind.File,
            Format = RarFormatVersion.Rar5,
            DataSize = dataSize,
            SplitBefore = (headerFlags & 0x8) != 0,
            SplitAfter = (headerFlags & 0x10) != 0,
            IsDirectory = (fileFlags & 0x1) != 0,
            UnpackedSize = (fileFlags & 0x8) != 0 || unpackedSize > long.MaxValue ? -1 : (long)unpackedSize,
            Attributes = (uint)attributes,
            LastWriteTime = lastWriteTime,
            Crc32 = crc,
            IsSolid = (compression & 0x40) != 0,
            Method = (int)((compression >> 7) & 0x7),
            UnpackVersion = algorithm switch
            {
                0 => 50,
                1 when (compression & 0x100000) != 0 => 50,
                1 => 70,
                _ => 50 + (algorithm * 20),
            },
            DictionarySize = dictionarySize,
            HostOperatingSystem = hostOs switch
            {
                0 => RarHostOperatingSystem.Windows,
                1 => RarHostOperatingSystem.Unix,
                _ => RarHostOperatingSystem.Unknown,
            },
        };

        ParseRar5Extra(extra, file);
        if (file.HostOperatingSystem is RarHostOperatingSystem.Windows)
        {
            name = name.Replace('\\', '/');
        }

        if (file.FileVersion > 0)
        {
            name += ";" + file.FileVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        file.Name = name;
        return file;
    }

    private static void ParseRar5Extra(ReadOnlySpan<byte> extra, RarFileHeader file)
    {
        var reader = new RarSpanReader(extra);
        while (reader.Remaining > 0)
        {
            var size = reader.ReadVInt();
            if (size is 0 || size > (ulong)reader.Remaining)
                throw new InvalidDataException("An extra record of a header of the RAR archive is invalid.");

            var record = new RarSpanReader(reader.ReadBytes((int)size));
            var type = record.ReadVInt();
            switch (type)
            {
                case 1:
                    {
                        var version = record.ReadVInt();
                        var flags = record.ReadVInt();
                        var kdfCount = record.ReadByte();
                        var salt = record.ReadBytes(16).ToArray();
                        var iv = record.ReadBytes(16).ToArray();
                        byte[]? check = null;
                        if ((flags & 0x1) != 0)
                        {
                            var value = record.ReadBytes(12);
                            if (RarKeyStore.IsPasswordCheckValid(value))
                            {
                                check = value[..8].ToArray();
                            }
                        }

                        file.IsEncrypted = true;
                        file.Encryption = version is 0
                            ? new RarEncryption { IsRar5 = true, Salt = salt, Iv = iv, KdfCount = kdfCount, PasswordCheck = check, UseMac = (flags & 0x2) != 0 }
                            : null;
                        break;
                    }

                case 2:
                    {
                        var hashType = record.ReadVInt();
                        if (hashType is 0)
                        {
                            file.Blake2 = record.ReadBytes(32).ToArray();
                        }

                        break;
                    }

                case 3:
                    {
                        var flags = record.ReadVInt();
                        var unix = (flags & 0x1) != 0;
                        var times = new long[3];
                        var present = new bool[3];
                        for (var i = 0; i < 3; i++)
                        {
                            if ((flags & (0x2u << i)) != 0)
                            {
                                present[i] = true;
                                times[i] = unix ? record.ReadUInt32() : (long)record.ReadUInt64();
                            }
                        }

                        var nanoseconds = new uint[3];
                        if (unix && (flags & 0x10) != 0)
                        {
                            for (var i = 0; i < 3; i++)
                            {
                                if (present[i])
                                {
                                    nanoseconds[i] = record.ReadUInt32();
                                }
                            }
                        }

                        for (var i = 0; i < 3; i++)
                        {
                            if (!present[i])
                                continue;

                            var time = unix ? FromUnixTime(times[i], nanoseconds[i]) : FromFileTime(times[i]);
                            switch (i)
                            {
                                case 0: file.LastWriteTime = time; break;
                                case 1: file.CreationTime = time; break;
                                default: file.LastAccessTime = time; break;
                            }
                        }

                        break;
                    }

                case 4:
                    {
                        record.ReadVInt();
                        var version = record.ReadVInt();
                        file.FileVersion = version > long.MaxValue ? long.MaxValue : (long)version;
                        break;
                    }

                case 5:
                    {
                        var redirectionType = record.ReadVInt();
                        record.ReadVInt();
                        var nameLength = record.ReadVInt();
                        if (nameLength > (ulong)record.Remaining)
                            throw new InvalidDataException("A redirection record of the RAR archive is invalid.");

                        var target = DecodeUtf8(record.ReadBytes((int)nameLength));
                        if (redirectionType is >= 1 and <= 5)
                        {
                            file.RedirectionType = (RarRedirectionType)redirectionType;
                            file.RedirectionTarget = redirectionType is 1 ? target : target.Replace('\\', '/');
                        }

                        break;
                    }
            }
        }
    }

    private static DateTimeOffset? FromUnixTime(long seconds, uint nanoseconds)
    {
        if (nanoseconds >= 1_000_000_000)
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);
    }

    private static DateTimeOffset? FromFileTime(long fileTime)
    {
        if (fileTime < 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc())
            return null;

        return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    private RarHeader? ReadRar4Header()
    {
        byte[] header;
        if (_rar4HeadersEncrypted)
        {
            // AES is not available on the browser (WebAssembly)
            if (OperatingSystem.IsBrowser())
                throw new PlatformNotSupportedException("The encrypted headers of RAR archives are not supported on the browser.");

            Span<byte> salt = stackalloc byte[8];
            var read = _input.ReadAtMost(salt);
            if (read < 8)
                return null;

            var (key, iv) = _keys.GetRar3Key(salt);
            var first = new byte[16];
            if (_input.ReadAtMost(first) < 16)
                return null;

            using var aes = Aes.Create();
            aes.Key = key;
            var plainFirst = aes.DecryptCbc(first, iv, PaddingMode.None);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(plainFirst.AsSpan(5));
            if (size < 7)
                throw HeaderError("A header of the RAR archive has an invalid size.");

            var padded = (size + 15) & ~15;
            var encrypted = new byte[padded];
            first.CopyTo(encrypted, 0);
            if (_input.ReadAtMost(encrypted.AsSpan(16)) != padded - 16)
                throw HeaderError("The RAR archive is truncated.");

            header = aes.DecryptCbc(encrypted, iv, PaddingMode.None).AsSpan(0, size).ToArray();
        }
        else
        {
            var start = new byte[7];
            var read = _input.ReadAtMost(start);
            if (read is 0)
                return null;

            if (read < 7)
                throw new InvalidDataException("The RAR archive is truncated.");

            var size = BinaryPrimitives.ReadUInt16LittleEndian(start.AsSpan(5));
            if (size < 7)
                throw new InvalidDataException("A header of the RAR archive has an invalid size.");

            header = new byte[size];
            start.CopyTo(header, 0);
            _input.ReadExactly(header.AsSpan(7));
        }

        var type = header[2];
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(3));
        var expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (type is 0x73 or 0x74 or 0x7A or 0x7B && (ushort)System.IO.Hashing.Crc32.HashToUInt32(header.AsSpan(2, GetRar4CrcLength(header, type, flags) - 2)) != expectedCrc)
        {
            // The main header of the old archives may have a CRC of its fixed part only
            if (type is not 0x73 || header.Length < 13 || (ushort)System.IO.Hashing.Crc32.HashToUInt32(header.AsSpan(2, 11)) != expectedCrc)
                throw HeaderError("The CRC of a header of the RAR archive does not match.");
        }

        var reader = new RarSpanReader(header.AsSpan(7));
        switch (type)
        {
            case 0x73:
                {
                    var main = new RarMainHeader
                    {
                        Kind = RarHeaderKind.Main,
                        IsVolume = (flags & 0x0001) != 0,
                        IsSolid = (flags & 0x0008) != 0,
                        NewVolumeNaming = (flags & 0x0010) != 0,
                        IsFirstVolume = (flags & 0x0100) != 0 || (flags & 0x0001) == 0,
                        HeadersEncrypted = (flags & 0x0080) != 0,
                    };

                    if ((flags & 0x0002) != 0 && header.Length > 13)
                    {
                        main.OldComment = ReadOldComment(header.AsSpan(13));
                    }

                    if (main.HeadersEncrypted)
                    {
                        if (_keys.Password is null)
                            throw new ArchivePasswordException("The headers of the RAR archive are encrypted and no password is provided.");

                        _rar4HeadersEncrypted = true;
                    }

                    return main;
                }

            case 0x74:
            case 0x7A:
                return ParseRar4File(header, flags, isService: type is 0x7A);

            case 0x7B:
                return new RarEndHeader { Kind = RarHeaderKind.End, HasNextVolume = (flags & 0x0001) != 0 };

            default:
                {
                    long dataSize = 0;
                    if ((flags & 0x8000) != 0)
                    {
                        dataSize = reader.ReadUInt32();
                    }

                    return new RarHeader { Kind = RarHeaderKind.Other, DataSize = dataSize };
                }
        }
    }

    /// <summary>Gets the length of the part of a header its CRC covers: the file headers of RAR 1.5-2.x exclude the comment they contain.</summary>
    private static int GetRar4CrcLength(byte[] header, byte type, ushort flags)
    {
        if (type is 0x74 && (flags & 0x0008) != 0 && header.Length >= 32)
        {
            var length = 32 + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) + ((flags & 0x0100) != 0 ? 8 : 0);
            return Math.Min(length, header.Length);
        }

        return header.Length;
    }

    private RarFileHeader ParseRar4File(byte[] header, ushort flags, bool isService)
    {
        var reader = new RarSpanReader(header.AsSpan(7));
        long packedSize = reader.ReadUInt32();
        long unpackedSize = reader.ReadUInt32();
        var hostOs = reader.ReadByte();
        var crc = reader.ReadUInt32();
        var dosTime = reader.ReadUInt32();
        var unpackVersion = reader.ReadByte();
        var method = reader.ReadByte();
        var nameSize = reader.ReadUInt16();
        var attributes = reader.ReadUInt32();
        var unknownSize = unpackedSize == 0xFFFFFFFF;
        if ((flags & 0x0100) != 0)
        {
            var highPacked = reader.ReadUInt32();
            var highUnpacked = reader.ReadUInt32();
            packedSize |= (long)highPacked << 32;
            unknownSize &= highUnpacked == 0xFFFFFFFF;
            unpackedSize |= (long)highUnpacked << 32;
        }

        if (unpackedSize < 0 && !unknownSize)
            throw new InvalidDataException("A header of the RAR archive has an invalid size.");

        var nameBytes = reader.ReadBytes(nameSize);
        byte[]? salt = null;
        if ((flags & 0x0400) != 0)
        {
            salt = reader.ReadBytes(8).ToArray();
        }

        var lastWriteTime = FromDosTime(dosTime);
        DateTimeOffset? creationTime = null;
        DateTimeOffset? lastAccessTime = null;
        if ((flags & 0x1000) != 0 && reader.Remaining >= 2)
        {
            var timeFlags = reader.ReadUInt16();
            for (var i = 0; i < 4; i++)
            {
                var mode = (timeFlags >> ((3 - i) * 4)) & 0xF;
                if ((mode & 8) == 0)
                    continue;

                var time = i is 0 ? lastWriteTime : FromDosTime(reader.ReadUInt32());
                var count = mode & 3;
                var remainder = 0;
                for (var j = 0; j < count; j++)
                {
                    remainder |= reader.ReadByte() << ((j + 3 - count) * 8);
                }

                if (time is { } value)
                {
                    value = value.AddTicks(remainder);
                    if ((mode & 4) != 0)
                    {
                        value = value.AddSeconds(1);
                    }

                    time = value;
                }

                switch (i)
                {
                    case 0: lastWriteTime = time; break;
                    case 1: creationTime = time; break;
                    case 2: lastAccessTime = time; break;
                }
            }
        }

        var hostOperatingSystem = hostOs switch
        {
            0 => RarHostOperatingSystem.MSDos,
            1 => RarHostOperatingSystem.OS2,
            2 => RarHostOperatingSystem.Windows,
            3 => RarHostOperatingSystem.Unix,
            4 => RarHostOperatingSystem.MacOS,
            5 => RarHostOperatingSystem.BeOS,
            _ => RarHostOperatingSystem.Unknown,
        };

        var isDirectory = (flags & 0x00E0) == 0x00E0;
        var file = new RarFileHeader
        {
            Kind = isService ? RarHeaderKind.Service : RarHeaderKind.File,
            Format = RarFormatVersion.Rar4,
            Name = DecodeRar4Name(nameBytes, (flags & 0x0200) != 0, isService),
            DataSize = packedSize,
            UnpackedSize = unknownSize ? -1 : unpackedSize,
            SplitBefore = (flags & 0x0001) != 0,
            SplitAfter = (flags & 0x0002) != 0,
            IsEncrypted = (flags & 0x0004) != 0,
            IsSolid = (flags & 0x0010) != 0,
            IsDirectory = isDirectory || (hostOperatingSystem is RarHostOperatingSystem.Unix ? (attributes & 0xF000) == 0x4000 : (attributes & 0x10) != 0),
            DictionarySize = isDirectory ? 0 : 0x10000L << ((flags >> 5) & 7),
            HostOperatingSystem = hostOperatingSystem,
            Attributes = attributes,
            Crc32 = crc,
            LastWriteTime = lastWriteTime,
            CreationTime = creationTime,
            LastAccessTime = lastAccessTime,
            UnpackVersion = unpackVersion,
            Method = method is >= 0x30 and <= 0x35 ? method - 0x30 : method,
        };

        if (file.IsEncrypted)
        {
            file.Encryption = new RarEncryption { IsRar5 = false, Salt = salt ?? [] };
        }

        return file;
    }

    private string DecodeRar4Name(ReadOnlySpan<byte> bytes, bool unicode, bool isService)
    {
        string name;
        if (unicode)
        {
            var zero = bytes.IndexOf((byte)0);
            name = zero < 0 ? Encoding.UTF8.GetString(bytes) : RarUnicodeName.Decode(bytes[..zero], bytes[(zero + 1)..]) ?? DecodeOem(bytes[..zero]);
        }
        else
        {
            name = isService ? Encoding.Latin1.GetString(bytes) : DecodeOem(bytes);
        }

        return name.Replace('\\', '/');
    }

    private string DecodeOem(ReadOnlySpan<byte> bytes)
    {
        if (_options.EntryNameEncoding is { } encoding)
            return encoding.GetString(bytes);

        return System.Text.Unicode.Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
    }

    private static DateTimeOffset? FromDosTime(uint value)
    {
        var second = (int)(value & 0x1F) * 2;
        var minute = (int)(value >> 5) & 0x3F;
        var hour = (int)(value >> 11) & 0x1F;
        var day = (int)(value >> 16) & 0x1F;
        var month = (int)(value >> 21) & 0xF;
        var year = (int)(value >> 25) + 1980;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            return null;

        // The times of RAR 1.5-4.x are local times
        var time = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time));
    }

    /// <summary>Decodes the comment of RAR 2.x stored in the main header (a comment block), or returns <see langword="null"/>.</summary>
    private static string? ReadOldComment(ReadOnlySpan<byte> block)
    {
        // HEAD_CRC, HEAD_TYPE (0x75), HEAD_FLAGS, HEAD_SIZE, UNP_SIZE, UNP_VER, METHOD, COMM_CRC
        if (block.Length < 13 || block[2] is not 0x75)
            return null;

        var headSize = BinaryPrimitives.ReadUInt16LittleEndian(block[5..]);
        if (headSize < 13 || headSize > block.Length)
            return null;

        var unpackedSize = BinaryPrimitives.ReadUInt16LittleEndian(block[7..]);
        var version = block[9];
        var method = block[10];
        var crc = BinaryPrimitives.ReadUInt16LittleEndian(block[11..]);
        var data = block[13..headSize].ToArray();
        try
        {
            byte[] content;
            if (method is 0x30)
            {
                content = data.AsSpan(0, Math.Min(data.Length, (int)unpackedSize)).ToArray();
            }
            else
            {
                content = RarDecoding.Unpack(data, version, unpackedSize, 0x10000, maxMemoryUsage: 16 * 1024 * 1024);
            }

            if ((ushort)System.IO.Hashing.Crc32.HashToUInt32(content) != crc)
                return null;

            return Encoding.Latin1.GetString(content);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
