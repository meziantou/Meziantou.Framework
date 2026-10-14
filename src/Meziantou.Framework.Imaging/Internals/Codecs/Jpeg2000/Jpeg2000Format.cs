namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Constants and error factories of the JPEG 2000 codec: the codestream of Rec. ITU-T T.800 | ISO/IEC 15444-1 (Annex A for
/// the marker segments), the JP2 file format of its Annex I, and the high-throughput block coder of Rec. ITU-T T.814 |
/// ISO/IEC 15444-15.
/// </summary>
internal static class Jpeg2000Format
{
    /// <summary>The length of a box header: the 32-bit size and the type.</summary>
    public const int BoxHeaderLength = 8;

    /// <summary>The length of a box header whose size is stored in the 64-bit field.</summary>
    public const int LargeBoxHeaderLength = 16;

    /// <summary>The <c>jP  </c> code: the signature box.</summary>
    public const uint SignatureBox = 0x6A502020;

    /// <summary>The <c>ftyp</c> code.</summary>
    public const uint FileTypeBox = 0x66747970;

    /// <summary>The <c>jp2h</c> code: the header superbox.</summary>
    public const uint HeaderBox = 0x6A703268;

    /// <summary>The <c>ihdr</c> code: the image header.</summary>
    public const uint ImageHeaderBox = 0x69686472;

    /// <summary>The <c>bpcc</c> code: the bit depth of each component.</summary>
    public const uint BitsPerComponentBox = 0x62706363;

    /// <summary>The <c>colr</c> code: a color specification.</summary>
    public const uint ColorSpecificationBox = 0x636F6C72;

    /// <summary>The <c>pclr</c> code: the palette.</summary>
    public const uint PaletteBox = 0x70636C72;

    /// <summary>The <c>cmap</c> code: the mapping of the components and of the palette columns to channels.</summary>
    public const uint ComponentMappingBox = 0x636D6170;

    /// <summary>The <c>cdef</c> code: the definition of the channels.</summary>
    public const uint ChannelDefinitionBox = 0x63646566;

    /// <summary>The <c>res </c> code: the resolution superbox.</summary>
    public const uint ResolutionBox = 0x72657320;

    /// <summary>The <c>resc</c> code: the resolution the image was captured at.</summary>
    public const uint CaptureResolutionBox = 0x72657363;

    /// <summary>The <c>resd</c> code: the resolution the image is displayed at by default.</summary>
    public const uint DisplayResolutionBox = 0x72657364;

    /// <summary>The <c>jp2c</c> code: the contiguous codestream.</summary>
    public const uint CodestreamBox = 0x6A703263;

    /// <summary>The <c>uuid</c> code: vendor data identified by a 16-byte identifier.</summary>
    public const uint UuidBox = 0x75756964;

    /// <summary>The <c>jpch</c> code: the codestream header superbox of a JPX file.</summary>
    public const uint CodestreamHeaderBox = 0x6A706368;

    /// <summary>The <c>jplh</c> code: the compositing layer header superbox of a JPX file.</summary>
    public const uint CompositingLayerHeaderBox = 0x6A706C68;

    /// <summary>The <c>ftbl</c> code: the fragment table of a JPX file.</summary>
    public const uint FragmentTableBox = 0x6674626C;

    /// <summary>The <c>jp2 </c> brand: a file that a JP2 reader can read.</summary>
    public const uint BrandJp2 = 0x6A703220;

    /// <summary>The <c>jpx </c> brand: a file with the extensions of ISO/IEC 15444-2.</summary>
    public const uint BrandJpx = 0x6A707820;

    /// <summary>The <c>jph </c> brand: a file whose codestream uses the high-throughput block coder.</summary>
    public const uint BrandJph = 0x6A706820;

    /// <summary>The <c>jpm </c> brand: a compound image file (ISO/IEC 15444-6).</summary>
    public const uint BrandJpm = 0x6A706D20;

    /// <summary>The <c>mjp2</c> brand: a Motion JPEG 2000 file (ISO/IEC 15444-3).</summary>
    public const uint BrandMj2 = 0x6D6A7032;

    /// <summary>The <c>mj2s</c> brand: a Motion JPEG 2000 file of the simple profile.</summary>
    public const uint BrandMj2Simple = 0x6D6A3273;

    /// <summary>Start of codestream.</summary>
    public const ushort MarkerSoc = 0xFF4F;

    /// <summary>Extended capabilities (ISO/IEC 15444-15 uses it to describe the high-throughput block coder).</summary>
    public const ushort MarkerCap = 0xFF50;

    /// <summary>Image and tile size.</summary>
    public const ushort MarkerSiz = 0xFF51;

    /// <summary>Coding style default.</summary>
    public const ushort MarkerCod = 0xFF52;

    /// <summary>Coding style of a component.</summary>
    public const ushort MarkerCoc = 0xFF53;

    /// <summary>Tile-part lengths.</summary>
    public const ushort MarkerTlm = 0xFF55;

    /// <summary>Packet lengths, main header.</summary>
    public const ushort MarkerPlm = 0xFF57;

    /// <summary>Packet lengths, tile-part header.</summary>
    public const ushort MarkerPlt = 0xFF58;

    /// <summary>Corresponding profile (ISO/IEC 15444-15).</summary>
    public const ushort MarkerCpf = 0xFF59;

    /// <summary>Quantization default.</summary>
    public const ushort MarkerQcd = 0xFF5C;

    /// <summary>Quantization of a component.</summary>
    public const ushort MarkerQcc = 0xFF5D;

    /// <summary>Region of interest.</summary>
    public const ushort MarkerRgn = 0xFF5E;

    /// <summary>Progression order change.</summary>
    public const ushort MarkerPoc = 0xFF5F;

    /// <summary>Packed packet headers, main header.</summary>
    public const ushort MarkerPpm = 0xFF60;

    /// <summary>Packed packet headers, tile-part header.</summary>
    public const ushort MarkerPpt = 0xFF61;

    /// <summary>Component registration.</summary>
    public const ushort MarkerCrg = 0xFF63;

    /// <summary>Comment.</summary>
    public const ushort MarkerCom = 0xFF64;

    /// <summary>Start of tile-part.</summary>
    public const ushort MarkerSot = 0xFF90;

    /// <summary>Start of packet.</summary>
    public const ushort MarkerSop = 0xFF91;

    /// <summary>End of packet header.</summary>
    public const ushort MarkerEph = 0xFF92;

    /// <summary>Start of data: the end of a tile-part header.</summary>
    public const ushort MarkerSod = 0xFF93;

    /// <summary>End of codestream.</summary>
    public const ushort MarkerEoc = 0xFFD9;

    /// <summary>The length of a SOT marker segment, with its marker.</summary>
    public const int SotSegmentLength = 12;

    /// <summary>The largest number of tiles: the index of a tile is stored in 16 bits and 65535 is not an index.</summary>
    public const int MaxTileCount = 65535;

    /// <summary>The largest number of decomposition levels (Table A.15).</summary>
    public const int MaxDecompositionLevels = 32;

    /// <summary>
    /// The largest number of magnitude bit-planes of a code-block that the decoder represents: a coefficient is held in 32
    /// bits with its sign and one bit below the binary point.
    /// </summary>
    public const int MaxBitPlanes = 30;

    /// <summary>The largest number of components of an image the library can hold: four inks and an opacity channel.</summary>
    public const int MaxSupportedComponents = 5;

    /// <summary>The first four bytes of a codestream: the SOC marker and the SIZ marker.</summary>
    public static ReadOnlySpan<byte> CodestreamSignature => [0xFF, 0x4F, 0xFF, 0x51];

    /// <summary>The first twelve bytes of a file: the signature box.</summary>
    public static ReadOnlySpan<byte> FileSignature => [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>The identifier of the <c>uuid</c> box that holds an XMP packet.</summary>
    public static ReadOnlySpan<byte> XmpUuid => [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    /// <summary>The identifier of the <c>uuid</c> box that holds EXIF data (<c>JpgTiffExif-&gt;JP2</c>): the payload starts with the TIFF header.</summary>
    public static ReadOnlySpan<byte> ExifUuid => "JpgTiffExif->JP2"u8;

    /// <summary>The file violates the specification.</summary>
    public static InvalidImageContentException Invalid(string message) => new("The JPEG 2000 file is invalid: " + message, ImageFormat.Jpeg2000);

    /// <summary>The file is valid but uses a feature that the decoder does not implement or that an image cannot hold.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Jpeg2000, feature);

    /// <summary>Returns the text of a four-character code, for messages.</summary>
    public static string ToText(uint code)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var value = (char)((code >> (24 - (i * 8))) & 0xFF);
            text[i] = value is >= ' ' and <= '~' ? value : '?';
        }

        return new string(text);
    }
}
