namespace Meziantou.Framework.Fonts;

/// <summary>Creates the exceptions of the font code.</summary>
internal static class FontErrors
{
    public static InvalidFontException Invalid(string message) => new("The font is invalid: " + message);

    public static InvalidFontException InvalidTable(uint tag, string message)
        => new($"The font is invalid: the '{FontTags.Format(tag)}' table {message}");

    public static InvalidFontException InvalidTable(uint tag, string message, Exception innerException)
        => new($"The font is invalid: the '{FontTags.Format(tag)}' table {message}", innerException);

    public static InvalidFontException Truncated()
        => new("The font is invalid: a table is truncated or refers to data outside its bounds.");

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

    public static UnsupportedFontFeatureException Unsupported(string message, string feature) => new(message, feature);
}
