using Meziantou.Framework.Compression.Internals.Crypto;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>Builds the stream of the content of a RAR entry: decryption, decompression, and verification of the CRC or the hash.</summary>
internal static class RarDecoding
{
    /// <summary>Creates the decoder of the compression of an entry.</summary>
    /// <exception cref="NotSupportedException">The compression is not supported.</exception>
    public static RarUnpacker CreateUnpacker(RarFileHeader header, long maxMemoryUsage)
    {
        return header.UnpackVersion switch
        {
            50 or 70 when header.Format is RarFormatVersion.Rar5 => new Rar5Unpacker(maxMemoryUsage),
            20 or 26 => new Rar2Unpacker(maxMemoryUsage),
            29 or 36 => new Rar3Unpacker(maxMemoryUsage),
            15 => throw new NotSupportedException("The compression of RAR 1.5 is not supported."),
            _ => throw new NotSupportedException($"The compression version {header.UnpackVersion} of the RAR entry is not supported."),
        };
    }

    /// <summary>Returns whether <paramref name="unpacker"/> decodes the compression of <paramref name="header"/>.</summary>
    public static bool IsCompatible(RarUnpacker unpacker, RarFileHeader header)
    {
        return header.UnpackVersion switch
        {
            50 or 70 => unpacker is Rar5Unpacker,
            20 or 26 => unpacker is Rar2Unpacker,
            29 or 36 => unpacker is Rar3Unpacker,
            _ => false,
        };
    }

    /// <summary>Checks that the method of an entry is supported before its data are read.</summary>
    public static void CheckSupported(RarFileHeader header)
    {
        if (header.Format is RarFormatVersion.Rar5 && header.UnpackVersion > 70)
            throw new NotSupportedException($"The compression version {header.UnpackVersion} of the RAR entry is not supported.");

        if (header.Method > 5)
            throw new NotSupportedException($"The compression method {header.Method} of the RAR entry is not supported.");

        if (header.IsEncrypted)
        {
            if (header.Encryption is null)
                throw new NotSupportedException("The encryption of the RAR entry is not supported.");

            if (header.Format is RarFormatVersion.Rar4 && header.UnpackVersion < 29)
                throw new NotSupportedException("The encryption of RAR 2.0 and older is not supported.");
        }
    }

    /// <summary>Decrypts the compressed data of an entry, checking the password when the archive stores a check value.</summary>
    /// <param name="packed">The compressed data, disposed with the result.</param>
    /// <param name="header">The header of the entry.</param>
    /// <param name="keys">The keys of the archive.</param>
    /// <param name="hashKey">The key that converts the CRC and the hash of the entry, when they depend on the key.</param>
    public static Stream Decrypt(Stream packed, RarFileHeader header, RarKeyStore keys, out byte[]? hashKey)
    {
        hashKey = null;
        var encryption = header.Encryption!;
        if (keys.Password is null)
            throw new ArchivePasswordException($"The entry '{header.Name}' is encrypted and no password is provided.");

        if (encryption.IsRar5)
        {
            var key = keys.GetRar5Key(encryption.Salt, encryption.KdfCount);
            if (encryption.PasswordCheck is { } check && !check.AsSpan().SequenceEqual(key.PasswordCheck))
                throw new ArchivePasswordException($"The password of the entry '{header.Name}' is wrong.");

            if (encryption.UseMac)
            {
                hashKey = key.HashKey;
            }

            return new AesCbcDecryptStream(packed, key.Key, encryption.Iv, leaveOpen: false);
        }

        var (rar3Key, iv) = keys.GetRar3Key(encryption.Salt);
        return new AesCbcDecryptStream(packed, rar3Key, iv, leaveOpen: false);
    }

    /// <summary>Opens the stream of the decoded content of an entry, without verification.</summary>
    /// <param name="packed">The compressed data, disposed with the result.</param>
    /// <param name="header">The header of the entry.</param>
    /// <param name="unpacker">The decoder (for a compressed entry), which may continue a solid stream.</param>
    /// <param name="solid">Whether the entry continues the solid stream of <paramref name="unpacker"/>.</param>
    public static Stream OpenDecoded(Stream packed, RarFileHeader header, RarUnpacker? unpacker, bool solid)
    {
        if (unpacker is null)
        {
            // Stored: the size is the size of the content, without the AES padding
            return header.UnpackedSize < 0 ? packed : new OwningBoundedStream(packed, header.UnpackedSize);
        }

        if (unpacker is Rar5Unpacker rar5)
        {
            rar5.ExtendedDistances = header.UnpackVersion is 70;
        }

        unpacker.StartFile(packed, header.UnpackedSize, header.DictionarySize, solid);
        return new RarUnpackStream(unpacker, packed);
    }

    /// <summary>Unpacks a small buffer compressed with a RAR 2.x or 2.9 method (the comments).</summary>
    public static byte[] Unpack(byte[] data, int version, int size, long dictionarySize, long maxMemoryUsage)
    {
        var header = new RarFileHeader { Format = RarFormatVersion.Rar4, UnpackVersion = version, UnpackedSize = size, DictionarySize = dictionarySize, Method = 3 };
        var unpacker = CreateUnpacker(header, maxMemoryUsage);
        using var stream = OpenDecoded(new MemoryStream(data, writable: false), header, unpacker, solid: false);
        var result = new byte[size];
        stream.ReadExactlyOrThrow(result);
        return result;
    }

    /// <summary>A view of the first bytes of a stream that disposes the stream.</summary>
    private sealed class OwningBoundedStream(Stream inner, long length) : ReadOnlyStreamBase
    {
        private long _remaining = length;

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0 || buffer.IsEmpty)
                return 0;

            if (buffer.Length > _remaining)
            {
                buffer = buffer[..(int)_remaining];
            }

            var read = inner.Read(buffer);
            _remaining -= read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
