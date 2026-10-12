namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Creates the exceptions of the font code. A font is not an image: its errors are the content exceptions of the
/// library with <see cref="ImageFormat.Unknown"/>, as for the ICC profiles.
/// </summary>
internal static class FontErrors
{
    public static InvalidImageContentException Invalid(string message) => new("The font is invalid: " + message, ImageFormat.Unknown);

    public static InvalidImageContentException InvalidTable(uint tag, string message)
        => new($"The font is invalid: the '{FontTags.Format(tag)}' table {message}", ImageFormat.Unknown);

    public static InvalidImageContentException InvalidTable(uint tag, string message, Exception innerException)
        => new($"The font is invalid: the '{FontTags.Format(tag)}' table {message}", ImageFormat.Unknown, innerException);

    public static InvalidImageContentException Truncated()
        => new("The font is invalid: a table is truncated or refers to data outside its bounds.", ImageFormat.Unknown);

    /// <summary>
    /// The largest coordinate a glyph outline can have, in font units: a thousand times the largest em. The formats can
    /// describe more (nested transformations, arithmetic in charstrings), up to values that are not numbers; nothing
    /// that can be drawn is that large.
    /// </summary>
    public const double MaxCoordinate = 1 << 24;

    /// <summary>Checks a coordinate of a glyph outline.</summary>
    public static void EnsureCoordinates(double x, double y)
    {
        // Written so that a value that is not a number fails the test.
        if (!(Math.Abs(x) <= MaxCoordinate && Math.Abs(y) <= MaxCoordinate))
            throw Invalid("a glyph has a point outside the coordinates an outline can have.");
    }

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Unknown, "Font: " + feature);
}
