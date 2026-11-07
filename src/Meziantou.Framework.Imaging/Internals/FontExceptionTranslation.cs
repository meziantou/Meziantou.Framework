namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the exceptions of the font code into the exceptions the library documents: a font is not an image, so its
/// errors are the content exceptions of the library with <see cref="ImageFormat.Unknown"/>, as for the ICC profiles.
/// </summary>
/// <remarks>Every public member that reads a font translates the exceptions of the font code with this class.</remarks>
internal static class FontExceptionTranslation
{
    public static ImageException Translate(FontException exception) => exception switch
    {
        FontResourceLimitException limit => new ImageResourceLimitException(ToImageKind(limit.Kind), limit.Limit, limit.Requested),
        UnsupportedFontFeatureException unsupported => new UnsupportedImageFeatureException(unsupported.Message, ImageFormat.Unknown, "Font: " + unsupported.Feature),
        UnknownFontFormatException => new UnknownImageFormatException(exception.Message, exception),
        _ => new InvalidImageContentException(exception.Message, ImageFormat.Unknown, exception),
    };

    private static ImageResourceLimitKind ToImageKind(FontResourceLimitKind kind) => kind switch
    {
        FontResourceLimitKind.EncodedBytes => ImageResourceLimitKind.EncodedBytes,
        FontResourceLimitKind.FontBytes => ImageResourceLimitKind.FontBytes,
        FontResourceLimitKind.GlyphComplexity => ImageResourceLimitKind.GlyphComplexity,
        FontResourceLimitKind.TextGlyphs => ImageResourceLimitKind.TextGlyphs,
        FontResourceLimitKind.LiveAllocationBytes => ImageResourceLimitKind.LiveAllocationBytes,
        FontResourceLimitKind.CollectionFaces => ImageResourceLimitKind.FontCollectionFaces,
        _ => ImageResourceLimitKind.Unknown,
    };
}
