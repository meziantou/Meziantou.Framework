using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A face of a font file (TrueType, OpenType, WOFF, WOFF2 or a collection), for the texts of a scene. Add it to a
/// <see cref="SceneFontCollection"/>: the layout measures the texts with it and every renderer draws them with it.
/// </summary>
/// <remarks>
/// A face keeps the bytes of its file, so that each renderer reads the same font: the image renderer loads it with its
/// own font engine, and the SVG renderer can embed it (<see cref="SvgRenderOptions.EmbedFonts"/>). A face is immutable
/// and can be used by several threads at the same time.
/// </remarks>
public sealed class SceneFontFace
{
    private static readonly FontEnvironment Environment = new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024);

    private SceneFontFace(ReadOnlyMemory<byte> data, int index, int faceCount, FontFace face)
    {
        Data = data;
        Index = index;
        FaceCount = faceCount;
        Face = face;
    }

    /// <summary>Gets the bytes of the font file the face was loaded from.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the index of the face in its file: 0, except for the faces of a font collection (<c>.ttc</c>, <c>.otc</c>).</summary>
    public int Index { get; }

    /// <summary>Gets the name of the family of the face, such as <c>Liberation Sans</c>; empty when the font has no name.</summary>
    public string FamilyName => Face.FamilyName;

    /// <summary>Gets the weight of the face, from 1 to 1000: 400 is regular, 700 is bold.</summary>
    public int Weight => Face.Weight;

    /// <summary>Gets a value indicating whether the face is italic or oblique.</summary>
    public bool IsItalic => Face.IsItalic;

    /// <summary>Gets the top of the underline, in em, above the baseline: negative below it. The value of the font, or -0.075 when it has none.</summary>
    public double UnderlinePosition => Face.UnderlineThickness > 0 ? (double)Face.UnderlinePosition / Face.UnitsPerEm : DefaultUnderlinePosition;

    /// <summary>Gets the thickness of the underline, in em. The value of the font, or 0.05 when it has none.</summary>
    public double UnderlineThickness => Face.UnderlineThickness > 0 ? (double)Face.UnderlineThickness / Face.UnitsPerEm : DefaultLineThickness;

    /// <summary>Gets the top of the strikethrough, in em, above the baseline. The value of the font, or 0.3 when it has none.</summary>
    public double StrikeoutPosition => Face.StrikeoutSize > 0 ? (double)Face.StrikeoutPosition / Face.UnitsPerEm : DefaultStrikeoutPosition;

    /// <summary>Gets the thickness of the strikethrough, in em. The value of the font, or 0.05 when it has none.</summary>
    public double StrikeoutThickness => Face.StrikeoutSize > 0 ? (double)Face.StrikeoutSize / Face.UnitsPerEm : DefaultLineThickness;

    // The decorations of Helvetica: its underline (Helvetica.afm: centered 100 units below the baseline, 50 units thick),
    // and the strikeout of Arial, which has its widths
    internal const double DefaultUnderlinePosition = -0.075;
    internal const double DefaultStrikeoutPosition = 0.3;
    internal const double DefaultLineThickness = 0.05;

    /// <summary>Gets the number of faces of the file.</summary>
    internal int FaceCount { get; }

    /// <summary>Gets the face, read by the font engine of <c>Meziantou.Framework.Fonts</c>.</summary>
    internal FontFace Face { get; }

    /// <summary>Loads every face of a font file.</summary>
    /// <param name="data">The bytes of the file. They are copied.</param>
    /// <returns>The faces of the file: one for a font, one per font of a collection.</returns>
    /// <exception cref="FontException">The data is not a font, or the font is malformed, uses a feature that is not supported, or exceeds a limit.</exception>
    public static IReadOnlyList<SceneFontFace> Load(ReadOnlySpan<byte> data) => Create(data.ToArray());

    /// <summary>Loads every face of a font file from a stream, read from its current position to its end.</summary>
    /// <param name="stream">The stream. It is left open.</param>
    /// <returns>The faces of the file: one for a font, one per font of a collection.</returns>
    /// <exception cref="FontException">The data is not a font, or the font is malformed, uses a feature that is not supported, or exceeds a limit.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static IReadOnlyList<SceneFontFace> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Create(ReadBounded(stream, Environment.Limits.MaxEncodedBytes));
    }

    /// <summary>Loads every face of a font file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The faces of the file: one for a font, one per font of a collection.</returns>
    /// <exception cref="FontException">The file is not a font, or the font is malformed, uses a feature that is not supported, or exceeds a limit.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static IReadOnlyList<SceneFontFace> Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return Create(ReadBounded(stream, Environment.Limits.MaxEncodedBytes));
    }

    // Reads at most one byte more than the limit of the font engine: a longer file is refused by FontLoader with a
    // FontResourceLimitException, without reading the rest of it
    internal static byte[] ReadBounded(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length <= limit)
        {
            var read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, limit + 1 - buffer.Length));
            if (read is 0)
                break;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <inheritdoc/>
    public override string ToString() => FamilyName + " " + Weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + (IsItalic ? " italic" : "");

    private static SceneFontFace[] Create(byte[] data)
    {
        var faces = FontLoader.Load(data, Environment);
        var result = new SceneFontFace[faces.Length];
        for (var i = 0; i < faces.Length; i++)
        {
            result[i] = new SceneFontFace(data, i, faces.Length, faces[i]);
        }

        return result;
    }
}
