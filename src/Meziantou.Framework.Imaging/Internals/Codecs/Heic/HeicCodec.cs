using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The HEIC decoder registration: still images and image sequences coded with HEVC in an ISO base media (HEIF) container.
/// </summary>
/// <remarks>
/// A file whose major brand is a generic HEIF brand (<c>mif1</c>, <c>msf1</c>, <c>miaf</c>) is claimed by
/// <see cref="AvifCodec"/>; the walker both codecs share then reads the compatible brands and decodes the file as the format
/// that they declare.
/// </remarks>
internal sealed class HeicCodec : ImageCodec
{
    private HeicCodec()
    {
    }

    public static HeicCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Heic;

    /// <summary>Determines whether the data start with a file type box whose major brand is an HEIC brand (12 bytes).</summary>
    /// <remarks>
    /// A file type box may store its size in the 64-bit field, which moves the major brand to offset 16: the detection
    /// prefix (18 bytes) then holds its first two characters only, and they decide, whatever the length of the prefix, so
    /// that the format does not depend on how many bytes the caller has read.
    /// </remarks>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(prefix[4..]) is not HeifFormat.Ftyp)
            return false;

        // "he"
        if (BinaryPrimitives.ReadUInt32BigEndian(prefix) is 1)
            return prefix.Length >= 18 && BinaryPrimitives.ReadUInt16BigEndian(prefix[16..]) is 0x6865;

        return HeicBinding.IsBrand(BinaryPrimitives.ReadUInt32BigEndian(prefix[8..]));
    }

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => MatchesSignature(prefix);

    public override ImageParser<ImageInfo> CreateIdentifyParser(ImageIdentifyMode mode, ImageCodecContext context)
        => new HeifStructureParser(context, HeicBinding.Instance, mode == ImageIdentifyMode.FullScan ? StructureWalk.FullScan : StructureWalk.Header);

    public override ImageParser<Image> CreateDecodeParser(ImageDecodeRequest request, ImageCodecContext context)
    {
        var observer = new HeifDecoder(request, context);
        try
        {
            return new StructureDecodeParser(new HeifStructureParser(context, HeicBinding.Instance, StructureWalk.Decode, observer), observer);
        }
        catch
        {
            observer.Dispose();
            throw;
        }
    }
}
