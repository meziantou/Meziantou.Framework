using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Crypto;
using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.Filters;
using Meziantou.Framework.Compression.Internals.Lzma;
using Meziantou.Framework.Compression.Internals.Ppmd;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Creates the decoder of each coder ID of 7z, and the encoders of the methods the writer supports.</summary>
internal static class SevenZipCoders
{
    /// <summary>Creates the decoder of a coder. The decoder owns its inputs.</summary>
    public static Stream CreateDecoder(SevenZipCoder coder, Stream[] inputs, long unpackSize, SevenZipDecoderContext context)
    {
        var id = coder.MethodId;
        var expectedInputs = id == SevenZipMethodId.Bcj2 ? 4 : 1;
        if (inputs.Length != expectedInputs)
        {
            if (IsKnown(id))
                throw new InvalidDataException($"The coder {SevenZipMethodId.GetName(id)} of the 7z archive has {inputs.Length} input streams instead of {expectedInputs}.");

            throw Unsupported(id);
        }

        var input = inputs[0];
        var properties = coder.Properties;
        return id switch
        {
            SevenZipMethodId.Copy => input,
            SevenZipMethodId.Deflate => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false),
            SevenZipMethodId.Deflate64 => new Deflate64DecoderStream(input, deflate64: true, leaveOpen: false),
            SevenZipMethodId.BZip2 => new BZip2Stream(input, CompressionMode.Decompress, leaveOpen: false),
            SevenZipMethodId.Lzma => CreateLzmaDecoder(input, properties, unpackSize, context),
            SevenZipMethodId.Lzma2 => CreateLzma2Decoder(input, properties, unpackSize, context),
            SevenZipMethodId.Ppmd => CreatePpmdDecoder(input, properties, unpackSize, context),
            SevenZipMethodId.Delta => new DeltaDecoderStream(input, properties.Length > 0 ? properties[0] + 1 : 1, leaveOpen: false),
            SevenZipMethodId.Bcj2 => new Bcj2DecoderStream(inputs[0], inputs[1], inputs[2], inputs[3], unpackSize, leaveOpen: false),
            SevenZipMethodId.Aes => CreateAesDecoder(input, properties, context),
            _ when GetBranchFilterKind(id) is { } kind => new BranchFilterDecoderStream(input, kind, GetFilterStartOffset(properties), leaveOpen: false),
            _ => throw Unsupported(id),
        };
    }

    private static LzmaDecoderStream CreateLzmaDecoder(Stream input, byte[] properties, long unpackSize, SevenZipDecoderContext context)
    {
        if (properties.Length < 5)
            throw new InvalidDataException("The LZMA properties of the 7z archive are truncated.");

        CheckDictionarySize(BinaryPrimitives.ReadUInt32LittleEndian(properties.AsSpan(1)), unpackSize, context);
        return new LzmaDecoderStream(input, properties, unpackSize, leaveOpen: false);
    }

    private static Lzma2DecoderStream CreateLzma2Decoder(Stream input, byte[] properties, long unpackSize, SevenZipDecoderContext context)
    {
        if (properties.Length < 1)
            throw new InvalidDataException("The LZMA2 properties of the 7z archive are missing.");

        var property = properties[0];
        if (property > 40)
            throw new InvalidDataException("The LZMA2 dictionary size of the 7z archive is invalid.");

        var dictionarySize = property is 40 ? uint.MaxValue : (2u | (property & 1u)) << ((property / 2) + 11);
        CheckDictionarySize(dictionarySize, unpackSize, context);
        return new Lzma2DecoderStream(input, property, leaveOpen: false);
    }

    private static Ppmd7DecoderStream CreatePpmdDecoder(Stream input, byte[] properties, long unpackSize, SevenZipDecoderContext context)
    {
        if (properties.Length < 5)
            throw new InvalidDataException("The PPMd properties of the 7z archive are truncated.");

        return new Ppmd7DecoderStream(input, properties[0], BinaryPrimitives.ReadUInt32LittleEndian(properties.AsSpan(1)), unpackSize, context.MaxMemoryUsage, leaveOpen: false);
    }

    /// <summary>
    /// Rejects a dictionary larger than <see cref="ArchiveReaderOptions.MaxMemoryUsage"/>. The decoders allocate the dictionary
    /// as the output grows, so a dictionary larger than the output only uses the size of the output.
    /// </summary>
    private static void CheckDictionarySize(uint dictionarySize, long unpackSize, SevenZipDecoderContext context)
    {
        if (Math.Min(dictionarySize, unpackSize) > context.MaxMemoryUsage)
            throw new InvalidDataException($"The 7z archive needs a dictionary of {dictionarySize} bytes, more than the {context.MaxMemoryUsage} bytes allowed by MaxMemoryUsage.");
    }

    private static AesCbcDecryptStream CreateAesDecoder(Stream input, byte[] properties, SevenZipDecoderContext context)
    {
        var (cyclesPower, salt, iv) = SevenZipAes.ParseProperties(properties);
        var key = context.GetKey(cyclesPower, salt);
        return new AesCbcDecryptStream(input, key, iv, leaveOpen: false);
    }

    /// <summary>Gets the coder of the compressed headers: LZMA with a dictionary that fits the headers, as 7-Zip.</summary>
    public static SevenZipEncoderSpec CreateHeaderEncoderSpec(long headerLength)
    {
        var settings = LzmaEncoderSettings.FromPreset(5);
        var dictionarySize = 1 << 12;
        while (dictionarySize < headerLength && dictionarySize < settings.DictionarySize)
        {
            dictionarySize <<= 1;
        }

        settings.DictionarySize = dictionarySize;
        return CreateLzmaSpec(settings);
    }

    public static SevenZipEncoderSpec CreateFilterEncoderSpec(SevenZipFilter filter)
    {
        var (id, kind) = filter switch
        {
            SevenZipFilter.X86 => (SevenZipMethodId.X86, BranchFilterKind.X86),
            SevenZipFilter.PowerPC => (SevenZipMethodId.PowerPC, BranchFilterKind.PowerPC),
            SevenZipFilter.IA64 => (SevenZipMethodId.IA64, BranchFilterKind.IA64),
            SevenZipFilter.Arm => (SevenZipMethodId.Arm, BranchFilterKind.Arm),
            SevenZipFilter.ArmThumb => (SevenZipMethodId.ArmThumb, BranchFilterKind.ArmThumb),
            SevenZipFilter.Sparc => (SevenZipMethodId.Sparc, BranchFilterKind.Sparc),
            SevenZipFilter.Arm64 => (SevenZipMethodId.Arm64, BranchFilterKind.Arm64),
            SevenZipFilter.RiscV => (SevenZipMethodId.RiscV, BranchFilterKind.RiscV),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        return new SevenZipEncoderSpec(id, output => (new BranchFilterEncoderStream(output, kind, startOffset: 0, leaveOpen: false), () => []));
    }

    public static SevenZipEncoderSpec CreateMethodEncoderSpec(SevenZipCompressionMethod method, int level, int? dictionarySize)
    {
        switch (method)
        {
            case SevenZipCompressionMethod.Lzma2:
                {
                    var settings = GetLzmaSettings(level, dictionarySize);
                    return new SevenZipEncoderSpec(SevenZipMethodId.Lzma2, output =>
                    {
                        var encoder = new Lzma2EncoderStream(output, settings, leaveOpen: false);
                        return (encoder, () => [encoder.DictionaryProperty]);
                    });
                }

            case SevenZipCompressionMethod.Lzma:
                return CreateLzmaSpec(GetLzmaSettings(level, dictionarySize));

            case SevenZipCompressionMethod.BZip2:
                {
                    // As 7-Zip: blocks of 100,000 bytes at level 1, 500,000 at level 3, and 900,000 from level 5
                    var blockSize = dictionarySize is { } size ? Math.Clamp(size / 100_000, 1, 9) : level switch { <= 2 => 1, <= 4 => 5, _ => 9 };
                    return new SevenZipEncoderSpec(SevenZipMethodId.BZip2, output => (new BZip2Stream(output, new BZip2CompressionOptions { BlockSize = blockSize }, leaveOpen: false), () => []));
                }

            case SevenZipCompressionMethod.Ppmd:
                {
                    var (order, memorySize) = Ppmd7EncoderStream.GetDefaultParameters(level);
                    if (dictionarySize is { } size)
                    {
                        memorySize = (uint)size;
                    }

                    var properties = new byte[5];
                    properties[0] = (byte)order;
                    BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(1), memorySize);
                    return new SevenZipEncoderSpec(SevenZipMethodId.Ppmd, output => (new Ppmd7EncoderStream(output, order, memorySize, leaveOpen: false), () => properties));
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }
    }

    private static LzmaEncoderSettings GetLzmaSettings(int level, int? dictionarySize)
    {
        var settings = LzmaEncoderSettings.FromPreset(level);
        if (dictionarySize is { } size)
        {
            settings.DictionarySize = size;
        }

        return settings;
    }

    private static SevenZipEncoderSpec CreateLzmaSpec(LzmaEncoderSettings settings)
    {
        return new SevenZipEncoderSpec(SevenZipMethodId.Lzma, output =>
        {
            // Without an end marker, as 7-Zip: the decoder stops at the unpack size
            var encoder = new LzmaEncoderStream(output, settings, writeEndMarker: false, leaveOpen: false);
            return (encoder, () => encoder.Properties);
        });
    }

    private static BranchFilterKind? GetBranchFilterKind(ulong id) => id switch
    {
        SevenZipMethodId.X86 => BranchFilterKind.X86,
        SevenZipMethodId.PowerPC => BranchFilterKind.PowerPC,
        SevenZipMethodId.IA64 => BranchFilterKind.IA64,
        SevenZipMethodId.Arm => BranchFilterKind.Arm,
        SevenZipMethodId.ArmThumb => BranchFilterKind.ArmThumb,
        SevenZipMethodId.Sparc => BranchFilterKind.Sparc,
        SevenZipMethodId.Arm64 => BranchFilterKind.Arm64,
        SevenZipMethodId.RiscV => BranchFilterKind.RiscV,
        _ => null,
    };

    /// <summary>Gets the start offset of a branch filter: the optional 4-byte property.</summary>
    internal static uint GetFilterStartOffset(byte[] properties)
    {
        if (properties.Length is 0)
            return 0;

        if (properties.Length is not 4)
            throw new NotSupportedException("The properties of the branch filter of the 7z archive are not supported.");

        return BinaryPrimitives.ReadUInt32LittleEndian(properties);
    }

    private static bool IsKnown(ulong id) => id is SevenZipMethodId.Copy or SevenZipMethodId.Delta or SevenZipMethodId.Lzma or SevenZipMethodId.Lzma2 or SevenZipMethodId.Ppmd
        or SevenZipMethodId.BZip2 or SevenZipMethodId.Deflate or SevenZipMethodId.Deflate64 or SevenZipMethodId.Bcj2 or SevenZipMethodId.Aes || GetBranchFilterKind(id) is not null;

    private static NotSupportedException Unsupported(ulong id) => new($"The 7z archive uses the method {SevenZipMethodId.Format(id)} ({SevenZipMethodId.GetName(id)}), which is not supported.");
}
