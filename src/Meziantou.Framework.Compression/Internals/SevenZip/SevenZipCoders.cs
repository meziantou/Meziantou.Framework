using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Crypto;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Creates the decoder of each coder ID of 7z.</summary>
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
        switch (id)
        {
            case SevenZipMethodId.Copy:
                return input;

            case SevenZipMethodId.Deflate:
                return new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);

            case SevenZipMethodId.Aes:
                return CreateAesDecoder(input, properties, context);

            default:
                throw Unsupported(id);
        }
    }

    /// <summary>Gets the coder of the compressed headers: LZMA, as 7-Zip.</summary>
    public static SevenZipEncoderSpec CreateHeaderEncoderSpec(long headerLength)
    {
        _ = headerLength;
        return SevenZipEncoderSpec.Copy;
    }

    public static SevenZipEncoderSpec CreateFilterEncoderSpec(SevenZipFilter filter)
    {
        throw new NotSupportedException($"The filter {filter} is not supported yet.");
    }

    public static SevenZipEncoderSpec CreateMethodEncoderSpec(SevenZipCompressionMethod method, int level, int? dictionarySize)
    {
        _ = level;
        _ = dictionarySize;
        throw new NotSupportedException($"The method {method} is not supported yet.");
    }

    private static AesCbcDecryptStream CreateAesDecoder(Stream input, byte[] properties, SevenZipDecoderContext context)
    {
        var (cyclesPower, salt, iv) = SevenZipAes.ParseProperties(properties);
        var key = context.GetKey(cyclesPower, salt);
        return new AesCbcDecryptStream(input, key, iv, leaveOpen: false);
    }

    /// <summary>Gets the start offset of a branch filter: the optional 4-byte property.</summary>
    internal static uint GetFilterStartOffset(byte[] properties)
    {
        if (properties.Length is 0)
            return 0;

        if (properties.Length is not 4)
            throw new NotSupportedException("The properties of the branch filter of the 7z archive are not supported.");

        return BinaryPrimitives.ReadUInt32LittleEndian(properties);
    }

    private static bool IsKnown(ulong id) => id is SevenZipMethodId.Copy or SevenZipMethodId.Delta or SevenZipMethodId.X86 or SevenZipMethodId.PowerPC or SevenZipMethodId.IA64
        or SevenZipMethodId.Arm or SevenZipMethodId.ArmThumb or SevenZipMethodId.Sparc or SevenZipMethodId.Arm64 or SevenZipMethodId.RiscV or SevenZipMethodId.Lzma
        or SevenZipMethodId.Lzma2 or SevenZipMethodId.Ppmd or SevenZipMethodId.BZip2 or SevenZipMethodId.Deflate or SevenZipMethodId.Deflate64 or SevenZipMethodId.Bcj2 or SevenZipMethodId.Aes;

    private static NotSupportedException Unsupported(ulong id) => new($"The 7z archive uses the method {SevenZipMethodId.Format(id)} ({SevenZipMethodId.GetName(id)}), which is not supported.");
}
