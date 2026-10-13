namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The four-character codes of the boxes, item types and brands of an AVIF file (ISO/IEC 14496-12, ISO/IEC 23008-12 and the
/// AV1 Image File Format specification), as big-endian 32-bit values, and the exceptions of the AVIF container parser.
/// </summary>
internal static class HeifFormat
{
    /// <summary>The <c>ftyp</c> code.</summary>
    public const uint Ftyp = 0x66747970;

    /// <summary>The <c>meta</c> code.</summary>
    public const uint Meta = 0x6D657461;

    /// <summary>The <c>moov</c> code.</summary>
    public const uint Moov = 0x6D6F6F76;

    /// <summary>The <c>mdat</c> code.</summary>
    public const uint Mdat = 0x6D646174;

    /// <summary>The <c>free</c> code.</summary>
    public const uint Free = 0x66726565;

    /// <summary>The <c>skip</c> code.</summary>
    public const uint Skip = 0x736B6970;

    /// <summary>The <c>uuid</c> code.</summary>
    public const uint Uuid = 0x75756964;

    /// <summary>The <c>moof</c> code.</summary>
    public const uint Moof = 0x6D6F6F66;

    /// <summary>The <c>hdlr</c> code.</summary>
    public const uint Hdlr = 0x68646C72;

    /// <summary>The <c>pitm</c> code.</summary>
    public const uint Pitm = 0x7069746D;

    /// <summary>The <c>iloc</c> code.</summary>
    public const uint Iloc = 0x696C6F63;

    /// <summary>The <c>iinf</c> code.</summary>
    public const uint Iinf = 0x69696E66;

    /// <summary>The <c>infe</c> code.</summary>
    public const uint Infe = 0x696E6665;

    /// <summary>The <c>iref</c> code.</summary>
    public const uint Iref = 0x69726566;

    /// <summary>The <c>iprp</c> code.</summary>
    public const uint Iprp = 0x69707270;

    /// <summary>The <c>ipco</c> code.</summary>
    public const uint Ipco = 0x6970636F;

    /// <summary>The <c>ipma</c> code.</summary>
    public const uint Ipma = 0x69706D61;

    /// <summary>The <c>idat</c> code.</summary>
    public const uint Idat = 0x69646174;

    /// <summary>The <c>pict</c> code.</summary>
    public const uint Pict = 0x70696374;

    /// <summary>The <c>ispe</c> code.</summary>
    public const uint Ispe = 0x69737065;

    /// <summary>The <c>pixi</c> code.</summary>
    public const uint Pixi = 0x70697869;

    /// <summary>The <c>av1C</c> code.</summary>
    public const uint Av1C = 0x61763143;

    /// <summary>The <c>colr</c> code.</summary>
    public const uint Colr = 0x636F6C72;

    /// <summary>The <c>irot</c> code.</summary>
    public const uint Irot = 0x69726F74;

    /// <summary>The <c>imir</c> code.</summary>
    public const uint Imir = 0x696D6972;

    /// <summary>The <c>clap</c> code.</summary>
    public const uint Clap = 0x636C6170;

    /// <summary>The <c>pasp</c> code.</summary>
    public const uint Pasp = 0x70617370;

    /// <summary>The <c>auxC</c> code.</summary>
    public const uint AuxC = 0x61757843;

    /// <summary>The <c>a1op</c> code.</summary>
    public const uint A1op = 0x61316F70;

    /// <summary>The <c>lsel</c> code.</summary>
    public const uint Lsel = 0x6C73656C;

    /// <summary>The <c>a1lx</c> code.</summary>
    public const uint A1lx = 0x61316C78;

    /// <summary>The <c>clli</c> code.</summary>
    public const uint Clli = 0x636C6C69;

    /// <summary>The <c>mdcv</c> code.</summary>
    public const uint Mdcv = 0x6D646376;

    /// <summary>The <c>cclv</c> code.</summary>
    public const uint Cclv = 0x63636C76;

    /// <summary>The <c>nclx</c> code.</summary>
    public const uint Nclx = 0x6E636C78;

    /// <summary>The <c>prof</c> code.</summary>
    public const uint Prof = 0x70726F66;

    /// <summary>The <c>rICC</c> code.</summary>
    public const uint RICC = 0x72494343;

    /// <summary>The <c>av01</c> code.</summary>
    public const uint Av01 = 0x61763031;

    /// <summary>The <c>grid</c> code.</summary>
    public const uint Grid = 0x67726964;

    /// <summary>The <c>iden</c> code.</summary>
    public const uint Iden = 0x6964656E;

    /// <summary>The <c>iovl</c> code.</summary>
    public const uint Iovl = 0x696F766C;

    /// <summary>The <c>Exif</c> code.</summary>
    public const uint Exif = 0x45786966;

    /// <summary>The <c>mime</c> code.</summary>
    public const uint Mime = 0x6D696D65;

    /// <summary>The <c>dimg</c> code.</summary>
    public const uint Dimg = 0x64696D67;

    /// <summary>The <c>auxl</c> code.</summary>
    public const uint Auxl = 0x6175786C;

    /// <summary>The <c>cdsc</c> code.</summary>
    public const uint Cdsc = 0x63647363;

    /// <summary>The <c>prem</c> code.</summary>
    public const uint Prem = 0x7072656D;

    /// <summary>The <c>thmb</c> code.</summary>
    public const uint Thmb = 0x74686D62;

    /// <summary>The <c>avif</c> code.</summary>
    public const uint Avif = 0x61766966;

    /// <summary>The <c>avis</c> code.</summary>
    public const uint Avis = 0x61766973;

    /// <summary>The <c>mif1</c> code.</summary>
    public const uint Mif1 = 0x6D696631;

    /// <summary>The <c>msf1</c> code.</summary>
    public const uint Msf1 = 0x6D736631;

    /// <summary>The <c>miaf</c> code.</summary>
    public const uint Miaf = 0x6D696166;

    /// <summary>The <c>MA1B</c> code.</summary>
    public const uint MA1B = 0x4D413142;

    /// <summary>The <c>MA1A</c> code.</summary>
    public const uint MA1A = 0x4D413141;

    /// <summary>The <c>heic</c> code.</summary>
    public const uint Heic = 0x68656963;

    /// <summary>The <c>heix</c> code.</summary>
    public const uint Heix = 0x68656978;

    /// <summary>The <c>heim</c> code.</summary>
    public const uint Heim = 0x6865696D;

    /// <summary>The <c>heis</c> code.</summary>
    public const uint Heis = 0x68656973;

    /// <summary>The <c>hevc</c> code.</summary>
    public const uint Hevc = 0x68657663;

    /// <summary>The <c>hevx</c> code.</summary>
    public const uint Hevx = 0x68657678;

    /// <summary>The <c>hevm</c> code.</summary>
    public const uint Hevm = 0x6865766D;

    /// <summary>The <c>hevs</c> code.</summary>
    public const uint Hevs = 0x68657673;

    /// <summary>The <c>hvc1</c> code.</summary>
    public const uint Hvc1 = 0x68766331;

    /// <summary>The <c>hev1</c> code.</summary>
    public const uint Hev1 = 0x68657631;

    /// <summary>The <c>hvcC</c> code.</summary>
    public const uint HvcC = 0x68766343;

    /// <summary>The <c>mvhd</c> code.</summary>
    public const uint Mvhd = 0x6D766864;

    /// <summary>The <c>trak</c> code.</summary>
    public const uint Trak = 0x7472616B;

    /// <summary>The <c>tkhd</c> code.</summary>
    public const uint Tkhd = 0x746B6864;

    /// <summary>The <c>tref</c> code.</summary>
    public const uint Tref = 0x74726566;

    /// <summary>The <c>edts</c> code.</summary>
    public const uint Edts = 0x65647473;

    /// <summary>The <c>elst</c> code.</summary>
    public const uint Elst = 0x656C7374;

    /// <summary>The <c>mdia</c> code.</summary>
    public const uint Mdia = 0x6D646961;

    /// <summary>The <c>mdhd</c> code.</summary>
    public const uint Mdhd = 0x6D646864;

    /// <summary>The <c>minf</c> code.</summary>
    public const uint Minf = 0x6D696E66;

    /// <summary>The <c>stbl</c> code.</summary>
    public const uint Stbl = 0x7374626C;

    /// <summary>The <c>stsd</c> code.</summary>
    public const uint Stsd = 0x73747364;

    /// <summary>The <c>stts</c> code.</summary>
    public const uint Stts = 0x73747473;

    /// <summary>The <c>stsc</c> code.</summary>
    public const uint Stsc = 0x73747363;

    /// <summary>The <c>stsz</c> code.</summary>
    public const uint Stsz = 0x7374737A;

    /// <summary>The <c>stz2</c> code.</summary>
    public const uint Stz2 = 0x73747A32;

    /// <summary>The <c>stco</c> code.</summary>
    public const uint Stco = 0x7374636F;

    /// <summary>The <c>co64</c> code.</summary>
    public const uint Co64 = 0x636F3634;

    /// <summary>The <c>auxi</c> code.</summary>
    public const uint Auxi = 0x61757869;

    /// <summary>The <c>auxv</c> code.</summary>
    public const uint Auxv = 0x61757876;

    /// <summary>The <c>iso8</c> brand.</summary>
    public const uint Iso8 = 0x69736F38;

    /// <summary>The <c>vmhd</c> code.</summary>
    public const uint Vmhd = 0x766D6864;

    /// <summary>The <c>dinf</c> code.</summary>
    public const uint Dinf = 0x64696E66;

    /// <summary>The <c>dref</c> code.</summary>
    public const uint Dref = 0x64726566;

    /// <summary>The <c>url </c> code.</summary>
    public const uint Url = 0x75726C20;

    /// <summary>The <c>ctts</c> code.</summary>
    public const uint Ctts = 0x63747473;

    /// <summary>The <c>stss</c> code.</summary>
    public const uint Stss = 0x73747373;

    /// <summary>The <c>ccst</c> code.</summary>
    public const uint Ccst = 0x63637374;

    /// <summary>The <c>tmap</c> code.</summary>
    public const uint Tmap = 0x746D6170;

    /// <summary>The auxiliary type of an alpha plane (ISO/IEC 23008-12, section 6.6.2.1).</summary>
    public static ReadOnlySpan<byte> AlphaAuxiliaryType => "urn:mpeg:mpegB:cicp:systems:auxiliary:alpha"u8;

    /// <summary>The auxiliary type of an alpha plane coded with HEVC (ISO/IEC 23008-12, annex B.2.2).</summary>
    public static ReadOnlySpan<byte> HevcAlphaAuxiliaryType => "urn:mpeg:hevc:2015:auxid:1"u8;

    /// <summary>The content type of an XMP item.</summary>
    public static ReadOnlySpan<byte> XmpContentType => "application/rdf+xml"u8;

    /// <summary>Returns the text of a four-character code, for messages.</summary>
    public static string ToText(uint code)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((code >> (24 - (8 * i))) & 0xFF);
            text[i] = c is >= ' ' and < (char)127 ? c : '?';
        }

        return new string(text);
    }
}
