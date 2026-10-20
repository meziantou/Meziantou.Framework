using SharedFontException = Meziantou.Framework.Fonts.FontException;
using SharedFontFace = Meziantou.Framework.Fonts.FontFace;
using SharedFontLoader = Meziantou.Framework.Fonts.FontLoader;
using SharedOutlineKind = Meziantou.Framework.Fonts.FontOutlineKind;
using SharedPooledFontEnvironment = Meziantou.Framework.Fonts.PooledFontEnvironment;
using SharedResourceLimits = Meziantou.Framework.Fonts.FontResourceLimits;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The format of a font file.</summary>
internal enum FontFileFormat
{
    /// <summary>An OpenType or TrueType font (https://learn.microsoft.com/typography/opentype/spec/otff).</summary>
    OpenType,

    /// <summary>A font collection, <c>ttcf</c> (https://learn.microsoft.com/typography/opentype/spec/otff#font-collections).</summary>
    Collection,

    /// <summary>A WOFF 1.0 file (https://www.w3.org/TR/WOFF/).</summary>
    Woff,

    /// <summary>A WOFF 2.0 file (https://www.w3.org/TR/WOFF2/).</summary>
    Woff2,
}

/// <summary>
/// A font file read by the font reader shared with Meziantou.Framework.Imaging: the sfnt data (an OpenType font or a font
/// collection, after the WOFF or WOFF2 decompression) and its font faces. A file whose structure is invalid is not loaded
/// (<see cref="Load(ReadOnlySpan{byte})"/> returns null), like the browsers that sanitize the web fonts before using them.
/// </summary>
/// <remarks>
/// The rasterizer of a rendering backend creates its typefaces from <see cref="Data"/> and the index of the face in the
/// collection (<see cref="OpenTypeFont.FaceIndex"/>), and draws the glyphs whose ids and positions are computed by the
/// managed shaper. The instances are immutable and can be shared by the documents and the threads.
/// </remarks>
internal sealed class FontFile
{
    /// <summary>The maximum size of the decoded data of a WOFF or WOFF2 file (the largest fonts are about 50 MB).</summary>
    internal const int MaxDecodedLength = 128 * 1024 * 1024;

    /// <summary>The maximum number of faces of a collection.</summary>
    private const int MaxFaceCount = 1024;

    private readonly OpenTypeFont[] _faces;

    private FontFile(FontFileFormat format, ReadOnlyMemory<byte> data, int faceCount)
    {
        Format = format;
        Data = data;
        _faces = new OpenTypeFont[faceCount];
    }

    /// <summary>
    /// The limits of the font reader: the decoded data of a file is bounded by <see cref="MaxDecodedLength"/>, and the
    /// temporary buffers of an operation (the expansion of a WOFF2 file, the decoding of an outline) by 256 MB.
    /// </summary>
    internal static SharedPooledFontEnvironment Environment { get; } = new(new SharedResourceLimits { MaxFontBytes = MaxDecodedLength }, maxLiveAllocationBytes: 256L * 1024 * 1024);

    /// <summary>The format of the file that was loaded.</summary>
    public FontFileFormat Format { get; }

    /// <summary>The sfnt data: an OpenType font, or a font collection when <see cref="IsCollection"/> is true.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Whether <see cref="Data"/> is a font collection (a <c>ttcf</c> file, or a WOFF2 file of a collection).</summary>
    public bool IsCollection => FontBinary.U32(Data.Span, 0) == OpenTypeTag.Collection;

    /// <summary>The font faces of the file (one, except for a collection).</summary>
    public IReadOnlyList<OpenTypeFont> Faces => _faces;

    /// <summary>Returns whether the data starts with the signature of a font file (it may still be invalid).</summary>
    public static bool HasFontSignature(ReadOnlySpan<byte> data) => FontBinary.U32(data, 0) is OpenTypeTag.TrueTypeVersion or OpenTypeTag.OpenTypeCff or OpenTypeTag.AppleTrueType or OpenTypeTag.Collection or OpenTypeTag.Woff or OpenTypeTag.Woff2;

    /// <summary>Loads a font file (OpenType, TrueType, a collection, WOFF or WOFF2); returns null when the data is not a valid font file.</summary>
    public static FontFile? Load(ReadOnlySpan<byte> data)
    {
        var format = FontBinary.U32(data, 0) switch
        {
            OpenTypeTag.Woff => FontFileFormat.Woff,
            OpenTypeTag.Woff2 => FontFileFormat.Woff2,
            OpenTypeTag.Collection => FontFileFormat.Collection,
            OpenTypeTag.TrueTypeVersion or OpenTypeTag.OpenTypeCff or OpenTypeTag.AppleTrueType => FontFileFormat.OpenType,
            _ => (FontFileFormat?)null,
        };

        if (format is not { } knownFormat)
            return null;

        SharedFontFace[] faces;
        try
        {
            faces = SharedFontLoader.Load(data, Environment);
        }
        catch (SharedFontException)
        {
            return null;
        }

        if (faces.Length > MaxFaceCount)
            return null;

        // A WOFF2 file can hold a collection
        var bytes = faces[0].Data.Bytes;
        if (knownFormat == FontFileFormat.OpenType && FontBinary.U32(bytes.Span, 0) == OpenTypeTag.Collection)
        {
            knownFormat = FontFileFormat.Collection;
        }

        var file = new FontFile(knownFormat, bytes, faces.Length);
        for (var i = 0; i < faces.Length; i++)
        {
            if (!HasOutlines(faces[i]))
                return null;

            file._faces[i] = new OpenTypeFont(file, i, faces[i]);
        }

        return file;
    }

    /// <summary>Loads a font file. The array must not be modified afterwards.</summary>
    public static FontFile? Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Load((ReadOnlySpan<byte>)data);
    }

    /// <summary>
    /// Returns whether a face has outlines (glyf and loca, CFF or CFF2), color bitmaps or SVG glyphs, as the OpenType
    /// sanitizer of the browsers requires them.
    /// </summary>
    private static bool HasOutlines(SharedFontFace face)
    {
        var tags = face.TableTags;
        return face.Outlines != SharedOutlineKind.None || tags.Contains(OpenTypeTag.Cbdt) || tags.Contains(OpenTypeTag.Sbix) || tags.Contains(OpenTypeTag.Svg);
    }
}
