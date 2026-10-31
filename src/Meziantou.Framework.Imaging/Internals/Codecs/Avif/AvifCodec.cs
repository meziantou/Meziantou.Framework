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
    /// prefix (18 bytes) then holds its first two characters only, and they decide, whatever the length of the prefix, so
    /// that the format does not depend on how many bytes the caller has read. No writer is known to do so.
    /// </remarks>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(prefix[4..]) is not HeifFormat.Ftyp)
            return false;

        // "av", "mi", "ms" or "MA"
        if (BinaryPrimitives.ReadUInt32BigEndian(prefix) is 1)
            return prefix.Length >= 18 && BinaryPrimitives.ReadUInt16BigEndian(prefix[16..]) is 0x6176 or 0x6D69 or 0x6D73 or 0x4D41;

        return IsCandidateBrand(BinaryPrimitives.ReadUInt32BigEndian(prefix[8..]));

        static bool IsCandidateBrand(uint brand) => brand is HeifFormat.Avif or HeifFormat.Avis or HeifFormat.Mif1 or HeifFormat.Msf1 or HeifFormat.Miaf or HeifFormat.MA1B or HeifFormat.MA1A;
    }

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    /// <summary>
    /// A file with a generic major brand has the format of its compatible brands, which the walker reads: when the prefix
    /// holds the whole file type box, they tell an AVIF file from an HEIC file, and from a file that is neither. A file type
    /// box that the prefix cuts is reported as AVIF.
    /// </summary>
    public override ImageFormat GetDetectedFormat(ReadOnlySpan<byte> prefix)
    {
        var size = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (size < 16 || size > (uint)prefix.Length || size % 4 is not 0 || BinaryPrimitives.ReadUInt32BigEndian(prefix[8..]) is not (HeifFormat.Mif1 or HeifFormat.Msf1 or HeifFormat.Miaf))
            return Format;

        var isAvif = false;
        var isHeic = false;
        for (var offset = 16; offset < (int)size; offset += 4)
        {
            var brand = BinaryPrimitives.ReadUInt32BigEndian(prefix[offset..]);
            isAvif |= brand is HeifFormat.Avif or HeifFormat.Avis;
            isHeic |= HeicBinding.IsBrand(brand);
        }

        if (isAvif)
            return Format;

        return isHeic ? ImageFormat.Heic : ImageFormat.Unknown;
    }

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new HeifStructureParser(context, AvifBinding.Instance, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = new HeifDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new HeifStructureParser(context, AvifBinding.Instance, StructureWalk.Decode, observer, request.HeifItemId), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }
}
