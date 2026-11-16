using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.Lzma;
using Meziantou.Framework.Compression.Internals.Ppmd;
using Meziantou.Framework.Compression.Internals.ZipLegacy;

using Meziantou.Framework.Compression.Internals.Zstandard;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>Builds the stream of the content of an entry: the decryption, the decompression, and the checks.</summary>
internal static class ZipEntryOpener
{
    /// <summary>Throws the exception that opening the entry throws before reading anything, when the entry cannot be read.</summary>
    public static void ThrowIfNotSupported(ZipEntryInfo info)
    {
        if ((info.Flags & ZipEntryInfo.FlagMaskedHeaders) != 0)
            throw new NotSupportedException($"The zip entry '{info.FullName}' is in an archive whose central directory is encrypted, which is not supported.");

        if (info.Encryption is ZipEncryption.Unsupported)
        {
            if (info.RawMethod is ZipEntryInfo.AesMethod && !info.HasAesExtra)
                throw new InvalidDataException($"The AES extra field of the zip entry '{info.FullName}' is missing.");

            throw new NotSupportedException($"The encryption of the zip entry '{info.FullName}' is not supported (PKWARE strong encryption, or an unknown AES version).");
        }

        if (!IsSupported(info.Method))
            throw new NotSupportedException($"The compression method {info.Method} of the zip entry '{info.FullName}' is not supported.");
    }

    /// <summary>Gets a value indicating whether a compression method is supported.</summary>
    public static bool IsSupported(ushort method) => (ZipCompressionMethod)method switch
    {
        ZipCompressionMethod.Stored or ZipCompressionMethod.Deflate or ZipCompressionMethod.Deflate64 or ZipCompressionMethod.BZip2 or ZipCompressionMethod.Ppmd or ZipCompressionMethod.Lzma or ZipCompressionMethod.Xz => true,
        ZipCompressionMethod.Shrink or ZipCompressionMethod.Reduce1 or ZipCompressionMethod.Reduce2 or ZipCompressionMethod.Reduce3 or ZipCompressionMethod.Reduce4 or ZipCompressionMethod.Implode => true,
        ZipCompressionMethod.Zstandard => true,
        _ => false,
    };

    /// <summary>Opens the content of an entry.</summary>
    /// <param name="info">The entry.</param>
    /// <param name="raw">The raw content (possibly encrypted). The returned stream owns it.</param>
    /// <param name="uncompressedSize">The length of the content, or -1 when it is not known yet.</param>
    /// <param name="options">The options of the archive.</param>
    /// <param name="onEnd">The action run when the content ends, which may return the length and the CRC-32 to check.</param>
    /// <param name="isValid">A function that returns <see langword="false"/> when the content can no longer be read.</param>
    /// <param name="createDecoder">Creates the decoder instead of <see cref="CreateDecoder"/> (the deflate decoder that finds the end of a streamed entry).</param>
    public static Stream Open(ZipEntryInfo info, Stream raw, long uncompressedSize, ArchiveReaderOptions options, Func<(long Length, uint? Crc)?>? onEnd, Func<bool>? isValid, Func<Stream, Stream>? createDecoder = null)
    {
        // The streams are disposed by the returned stream, or by the catch block
#pragma warning disable CA2000 // Dispose objects before losing scope
        Stream data = raw;
        try
        {
            ThrowIfNotSupported(info);
            WinZipAesDecryptStream? aes = null;
            switch (info.Encryption)
            {
                case ZipEncryption.ZipCrypto:
                    var checkByte = info.HasDataDescriptor ? (byte)(info.DosTime >> 8) : (byte)(info.Crc >> 24);
                    data = new ZipCryptoDecryptStream(raw, GetPassword(info, options), checkByte);
                    break;

                case ZipEncryption.Aes128 or ZipEncryption.Aes192 or ZipEncryption.Aes256:
                    aes = new WinZipAesDecryptStream(raw, GetPassword(info, options), info.Encryption);
                    data = aes;
                    break;
            }

            var decoder = createDecoder is not null ? createDecoder(data) : CreateDecoder(info, data, uncompressedSize, options);
            data = decoder;
            var end = aes is null ? onEnd : () =>
            {
                aes.VerifyAuthenticationCode();
                return onEnd?.Invoke();
            };

            return new ZipEntryStream(decoder, uncompressedSize, info.ExpectedCrc, info.Encryption is ZipEncryption.ZipCrypto, end, isValid);
        }
        catch
        {
            data.Dispose();
            raw.Dispose();
            throw;
        }
#pragma warning restore CA2000
    }

    /// <summary>
    /// LZMA (method 14): a 2-byte version, the 2-byte length of the properties (5), and the properties, then the LZMA data.
    /// General purpose flag bit 1 says that the data end with an end marker, which a streamed entry needs.
    /// </summary>
    private static LzmaDecoderStream CreateLzmaDecoder(ZipEntryInfo info, Stream input, long uncompressedSize, ArchiveReaderOptions options)
    {
        Span<byte> header = stackalloc byte[4];
        if (input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
            throw new InvalidDataException($"The LZMA header of the zip entry '{info.FullName}' is truncated.");

        var propertiesLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
        if (propertiesLength is not 5)
            throw new InvalidDataException($"The LZMA properties of the zip entry '{info.FullName}' are invalid.");

        Span<byte> properties = stackalloc byte[5];
        if (input.ReadAtLeast(properties, properties.Length, throwOnEndOfStream: false) != properties.Length)
            throw new InvalidDataException($"The LZMA properties of the zip entry '{info.FullName}' are truncated.");

        if (uncompressedSize < 0 && (info.Flags & 2) is 0)
            throw new NotSupportedException($"The LZMA data of the zip entry '{info.FullName}' have no end marker, and its length is not known before its data descriptor: read it from a seekable stream.");

        // The decoder allocates at most the dictionary, and at most the length of the content
        long dictionarySize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(properties[1..]);
        var window = uncompressedSize >= 0 ? Math.Min(dictionarySize, uncompressedSize) : dictionarySize;
        if (window > options.MaxMemoryUsage)
            throw new InvalidDataException($"The LZMA dictionary of the zip entry '{info.FullName}' ({dictionarySize} bytes) exceeds the maximum memory usage.");

        return new LzmaDecoderStream(input, properties, uncompressedSize, leaveOpen: false);
    }

    /// <summary>
    /// PPMd version I, revision 1 (method 98): 2 little-endian bytes of parameters (the order minus 1 in bits 0-3, the memory
    /// size in MiB minus 1 in bits 4-11, the restore method in bits 12-15), then the PPMd8 data. Without a known length, the
    /// decoder reads up to the end marker.
    /// </summary>
    private static Ppmd8DecoderStream CreatePpmdDecoder(ZipEntryInfo info, Stream input, long uncompressedSize, ArchiveReaderOptions options)
    {
        Span<byte> parameters = stackalloc byte[2];
        if (input.ReadAtLeast(parameters, 2, throwOnEndOfStream: false) != 2)
            throw new InvalidDataException($"The PPMd parameters of the zip entry '{info.FullName}' are truncated.");

        var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(parameters);
        var order = (value & 0xF) + 1;
        var memorySize = (uint)(((value >> 4) & 0xFF) + 1) << 20;
        var restoreMethod = value >> 12;
        if (order < 2)
            throw new InvalidDataException($"The PPMd order of the zip entry '{info.FullName}' is invalid.");

        return new Ppmd8DecoderStream(input, order, memorySize, restoreMethod, uncompressedSize, options.MaxMemoryUsage, leaveOpen: false);
    }

    /// <summary>The legacy methods do not mark the end of their data: they need the length of the content.</summary>
    private static long GetKnownLength(ZipEntryInfo info, long uncompressedSize)
    {
        if (uncompressedSize < 0)
            throw new NotSupportedException($"The zip entry '{info.FullName}' is compressed with the method {info.Method}, which does not mark the end of its data, and its length is not known before its data descriptor: read it from a seekable stream.");

        return uncompressedSize;
    }

    private static string GetPassword(ZipEntryInfo info, ArchiveReaderOptions options)
    {
        return options.Password ?? throw new ArchivePasswordException($"The zip entry '{info.FullName}' is encrypted: set the password in the options.");
    }

    private static Stream CreateDecoder(ZipEntryInfo info, Stream input, long uncompressedSize, ArchiveReaderOptions options)
    {
        return (ZipCompressionMethod)info.Method switch
        {
            ZipCompressionMethod.Ppmd => CreatePpmdDecoder(info, input, uncompressedSize, options),
            ZipCompressionMethod.Lzma => CreateLzmaDecoder(info, input, uncompressedSize, options),
            ZipCompressionMethod.Xz => new XzStream(input, leaveOpen: false, options.MaxMemoryUsage),
            ZipCompressionMethod.Stored => input,
            ZipCompressionMethod.Deflate => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false),
            ZipCompressionMethod.Deflate64 => new Deflate64DecoderStream(input, deflate64: true, leaveOpen: false),
            ZipCompressionMethod.BZip2 => new BZip2Stream(input, CompressionMode.Decompress, leaveOpen: false),
            ZipCompressionMethod.Shrink => new ZipShrinkDecoderStream(input, GetKnownLength(info, uncompressedSize), leaveOpen: false),
            ZipCompressionMethod.Reduce1 or ZipCompressionMethod.Reduce2 or ZipCompressionMethod.Reduce3 or ZipCompressionMethod.Reduce4 => new ZipReduceDecoderStream(input, info.Method - 1, GetKnownLength(info, uncompressedSize), leaveOpen: false),
            ZipCompressionMethod.Implode => new ZipImplodeDecoderStream(input, largeWindow: (info.Flags & 2) != 0, literalTree: (info.Flags & 4) != 0, GetKnownLength(info, uncompressedSize), leaveOpen: false),
            ZipCompressionMethod.Zstandard => ZstandardDecoderFactory.Create(input, uncompressedSize, options.MaxMemoryUsage, leaveOpen: false),
            _ => throw new NotSupportedException($"The compression method {info.Method} of the zip entry '{info.FullName}' is not supported."),
        };
    }
}
