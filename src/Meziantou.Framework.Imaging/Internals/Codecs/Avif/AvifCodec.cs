using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The AVIF decoder registration: still images and image sequences coded with AV1 in an ISO base media (HEIF) container.
/// <see cref="AvifEncoderCodec"/> is the encoder.
/// </summary>
internal sealed class AvifCodec : ImageCodec
{
    private AvifCodec()
    {
    }

    public static AvifCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Avif;

    /// <summary>
    /// Determines whether the data start with a file type box whose major brand is an AVIF or a generic HEIF brand (12 bytes).
    /// A file with a generic major brand is an AVIF file only if its compatible brands, which the parser reads, include an
    /// AVIF brand.
    /// </summary>
    /// <remarks>
    /// A file type box may store its size in the 64-bit field, which moves the major brand to offset 16: the detection
    /// prefix (18 bytes) then holds its first two characters only, and they decide. No writer is known to do so.
    /// </remarks>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(prefix[4..]) is not AvifFormat.Ftyp)
            return false;

        if (BinaryPrimitives.ReadUInt32BigEndian(prefix) is 1)
        {
            if (prefix.Length >= 20)
                return IsCandidateBrand(BinaryPrimitives.ReadUInt32BigEndian(prefix[16..]));

            return prefix.Length >= 18 && BinaryPrimitives.ReadUInt16BigEndian(prefix[16..]) is 0x6176 or 0x6D69 or 0x6D73 or 0x4D41;
        }

        return IsCandidateBrand(BinaryPrimitives.ReadUInt32BigEndian(prefix[8..]));

        static bool IsCandidateBrand(uint brand) => brand is AvifFormat.Avif or AvifFormat.Avis or AvifFormat.Mif1 or AvifFormat.Msf1 or AvifFormat.Miaf or AvifFormat.MA1B or AvifFormat.MA1A;
    }

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new AvifStructureParser(context, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = new AvifDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new AvifStructureParser(context, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }
}
