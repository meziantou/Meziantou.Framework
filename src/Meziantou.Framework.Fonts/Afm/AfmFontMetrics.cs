namespace Meziantou.Framework.Fonts;

/// <summary>
/// The metrics of a PostScript font read from an AFM file (Adobe Font Metrics, Adobe Technical Note #5004, version
/// 4.1): the global metrics of the font, the widths, bounding boxes and ligatures of its characters, its kerning pairs
/// and tracks, and its composite characters.
/// </summary>
/// <remarks>
/// <para>
/// An AFM file has no glyphs: it gives what text layout needs without the font program, such as the metrics of the 14
/// standard fonts of PDF. Its values are in units of 1/1000 of the point size, except the kerning amounts of the
/// tracks, which are in points.
/// </para>
/// <para>
/// The keys the reader does not know are ignored, as the specification requires. The multiple master (AMFM) and the
/// composite (ACFM) metrics files are not supported. The object is immutable and can be used from several threads.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs" ref:region="AfmFontMetrics_Load" ref:dedent="true">
/// // An AFM file gives the metrics of a PostScript font without its glyphs, here two characters of Helvetica.
/// var metrics = AfmFontMetrics.Parse("""
///     StartFontMetrics 4.1
///     FontName Helvetica
///     StartCharMetrics 2
///     C 65 ; WX 667 ; N A ; B 14 0 654 718 ;
///     C 86 ; WX 667 ; N V ; B 20 0 647 718 ;
///     EndCharMetrics
///     StartKernData
///     StartKernPairs 1
///     KPX A V -70
///     EndKernPairs
///     EndKernData
///     EndFontMetrics
///     """);
///
/// // The metrics are in 1/1000 of the point size: the width of "AV" at 12 points, with its kerning.
/// var units = metrics.GetWidth("A")!.Value.X + metrics.GetKerning("A", "V").X + metrics.GetWidth("V")!.Value.X;
/// var width = units * 12 / 1000;
/// Assert.Equal(15.168, width);
/// </code>
/// </example>
public sealed class AfmFontMetrics
{
    private readonly Dictionary<string, AfmCharacterMetrics> _charactersByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, AfmCharacterMetrics> _charactersByCode = [];
    private readonly Dictionary<(string First, string Second), AfmVector> _kerningByName = [];
    private readonly Dictionary<(int First, int Second), AfmVector> _kerningByCode = [];
    private readonly Dictionary<(string First, string Second), AfmVector> _verticalKerningByName = [];
    private readonly Dictionary<(int First, int Second), AfmVector> _verticalKerningByCode = [];

    internal AfmFontMetrics(string formatVersion, IReadOnlyList<AfmCharacterMetrics> characterMetrics, IReadOnlyList<AfmKerningPair> kerningPairs, IReadOnlyList<AfmKerningPair> verticalKerningPairs)
    {
        FormatVersion = formatVersion;
        CharacterMetrics = characterMetrics;

        // A name or a code that two characters have is the one of the first character: the later ones stay in
        // CharacterMetrics only.
        foreach (var character in characterMetrics)
        {
            if (character.Name is not null)
                _charactersByName.TryAdd(character.Name, character);

            if (character.Code >= 0)
                _charactersByCode.TryAdd(character.Code, character);
        }

        KerningPairs = Resolve(kerningPairs, _kerningByName, _kerningByCode);
        VerticalKerningPairs = Resolve(verticalKerningPairs, _verticalKerningByName, _verticalKerningByCode);
    }

    /// <summary>Gets the version of the AFM specification the file follows (the value of <c>StartFontMetrics</c>, for example "4.1").</summary>
    public string FormatVersion { get; }

    /// <summary>Gets the name of the font program, as given to the PostScript <c>findfont</c> operator (<c>FontName</c>).</summary>
    public string? FontName { get; internal init; }

    /// <summary>Gets the full name of the font (<c>FullName</c>).</summary>
    public string? FullName { get; internal init; }

    /// <summary>Gets the name of the family of the font (<c>FamilyName</c>).</summary>
    public string? FamilyName { get; internal init; }

    /// <summary>Gets the weight of the font, for example "Bold" (<c>Weight</c>).</summary>
    public string? Weight { get; internal init; }

    /// <summary>Gets the version of the font program (<c>Version</c>).</summary>
    public string? Version { get; internal init; }

    /// <summary>Gets the trademark or copyright notice of the font (<c>Notice</c>).</summary>
    public string? Notice { get; internal init; }

    /// <summary>Gets the default encoding of the font, for example "AdobeStandardEncoding" or "FontSpecific" (<c>EncodingScheme</c>).</summary>
    public string? EncodingScheme { get; internal init; }

    /// <summary>Gets the character set of the font, for example "ExtendedRoman" (<c>CharacterSet</c>).</summary>
    public string? CharacterSet { get; internal init; }

    /// <summary>Gets the comments of the file (<c>Comment</c>), in the order of the file.</summary>
    public IReadOnlyList<string> Comments { get; internal init; } = [];

    /// <summary>Gets the mapping scheme of a composite font (<c>MappingScheme</c>).</summary>
    public int? MappingScheme { get; internal init; }

    /// <summary>Gets the escape character of a font with the escape mapping scheme (<c>EscChar</c>).</summary>
    public int? EscChar { get; internal init; }

    /// <summary>Gets the number of characters of the font program (<c>Characters</c>).</summary>
    public int? CharacterCount { get; internal init; }

    /// <summary>Gets a value indicating whether the font is a base font rather than a composite font (<c>IsBaseFont</c>, <see langword="true"/> when absent).</summary>
    public bool IsBaseFont { get; internal init; } = true;

    /// <summary>Gets a value indicating whether the font is a CID-keyed font, whose character codes are CIDs (<c>IsCIDFont</c>).</summary>
    public bool IsCIDFont { get; internal init; }

    /// <summary>Gets the vector from the origin of writing direction 0 to the origin of writing direction 1, when every character has the same one (<c>VVector</c>).</summary>
    public AfmVector? VVector { get; internal init; }

    /// <summary>Gets a value indicating whether every character has the same <see cref="VVector"/> (<c>IsFixedV</c>; when absent, whether <see cref="VVector"/> is present).</summary>
    public bool IsFixedV { get; internal init; }

    /// <summary>Gets the bounding box of all the characters of the font (<c>FontBBox</c>).</summary>
    public AfmBox? FontBBox { get; internal init; }

    /// <summary>Gets the top of the capital H (<c>CapHeight</c>).</summary>
    public double? CapHeight { get; internal init; }

    /// <summary>Gets the top of the lowercase x (<c>XHeight</c>).</summary>
    public double? XHeight { get; internal init; }

    /// <summary>Gets the top of the lowercase d (<c>Ascender</c>).</summary>
    public double? Ascender { get; internal init; }

    /// <summary>Gets the bottom of the lowercase p (<c>Descender</c>).</summary>
    public double? Descender { get; internal init; }

    /// <summary>Gets the dominant width of the horizontal stems (<c>StdHW</c>).</summary>
    public double? StdHW { get; internal init; }

    /// <summary>Gets the dominant width of the vertical stems (<c>StdVW</c>).</summary>
    public double? StdVW { get; internal init; }

    /// <summary>Gets the writing directions the file describes: 0 for direction 0 only, 1 for direction 1 only, 2 for both (<c>MetricsSets</c>).</summary>
    public int MetricsSets { get; internal init; }

    /// <summary>Gets the metrics of writing direction 0 (horizontal), or <see langword="null"/> when the file does not describe it.</summary>
    public AfmWritingDirection? Horizontal { get; internal init; }

    /// <summary>Gets the metrics of writing direction 1 (vertical), or <see langword="null"/> when the file does not describe it.</summary>
    public AfmWritingDirection? Vertical { get; internal init; }

    /// <summary>Gets the metrics of the characters, in the order of the file.</summary>
    public IReadOnlyList<AfmCharacterMetrics> CharacterMetrics { get; }

    /// <summary>Gets the kerning pairs of writing direction 0 (<c>StartKernPairs</c> or <c>StartKernPairs0</c>), in the order of the file.</summary>
    public IReadOnlyList<AfmKerningPair> KerningPairs { get; }

    /// <summary>Gets the kerning pairs of writing direction 1 (<c>StartKernPairs1</c>), in the order of the file.</summary>
    public IReadOnlyList<AfmKerningPair> VerticalKerningPairs { get; }

    /// <summary>Gets the tracks of the font (<c>StartTrackKern</c>), in the order of the file.</summary>
    public IReadOnlyList<AfmTrackKerning> TrackKerning { get; internal init; } = [];

    /// <summary>Gets the composite characters of the font (<c>StartComposites</c>), in the order of the file.</summary>
    public IReadOnlyList<AfmCompositeCharacter> Composites { get; internal init; } = [];

    /// <summary>Reads an AFM file from its text.</summary>
    /// <param name="text">The text of the file.</param>
    /// <returns>The metrics of the file.</returns>
    /// <exception cref="InvalidFontException">The text is not a valid AFM file.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The text is a multiple master (AMFM) or a composite (ACFM) metrics file.</exception>
    public static AfmFontMetrics Parse(ReadOnlySpan<char> text) => AfmParser.Parse(text);

    /// <summary>Reads an AFM file from its bytes.</summary>
    /// <param name="data">The bytes of the file. The file is ASCII; the bytes above 127 found in some files are read as ISO-8859-1.</param>
    /// <param name="limits">The limits of the font code, or <see langword="null"/> for <see cref="FontResourceLimits.Default"/>: the file is bounded by <see cref="FontResourceLimits.MaxEncodedBytes"/>.</param>
    /// <returns>The metrics of the file.</returns>
    /// <exception cref="InvalidFontException">The data is not a valid AFM file.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The data is a multiple master (AMFM) or a composite (ACFM) metrics file.</exception>
    /// <exception cref="FontResourceLimitException">The data is larger than <see cref="FontResourceLimits.MaxEncodedBytes"/>.</exception>
    public static AfmFontMetrics Load(ReadOnlySpan<byte> data, FontResourceLimits? limits = null)
    {
        FontStreamReader.EnsureEncodedBytes(limits ?? FontResourceLimits.Default, data.Length);
        return AfmParser.Parse(data);
    }

    /// <summary>Reads an AFM file from a stream.</summary>
    /// <param name="stream">The stream of the file, read to its end. The file is ASCII; the bytes above 127 found in some files are read as ISO-8859-1.</param>
    /// <param name="limits">The limits of the font code, or <see langword="null"/> for <see cref="FontResourceLimits.Default"/>: the file is bounded by <see cref="FontResourceLimits.MaxEncodedBytes"/>.</param>
    /// <returns>The metrics of the file.</returns>
    /// <exception cref="InvalidFontException">The data is not a valid AFM file.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The data is a multiple master (AMFM) or a composite (ACFM) metrics file.</exception>
    /// <exception cref="FontResourceLimitException">The stream has more bytes than <see cref="FontResourceLimits.MaxEncodedBytes"/>.</exception>
    public static AfmFontMetrics Load(Stream stream, FontResourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return AfmParser.Parse(FontStreamReader.ReadAll(stream, limits ?? FontResourceLimits.Default));
    }

    /// <summary>Reads an AFM file from a stream, asynchronously.</summary>
    /// <param name="stream">The stream of the file, read to its end. The file is ASCII; the bytes above 127 found in some files are read as ISO-8859-1.</param>
    /// <param name="limits">The limits of the font code, or <see langword="null"/> for <see cref="FontResourceLimits.Default"/>: the file is bounded by <see cref="FontResourceLimits.MaxEncodedBytes"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The metrics of the file.</returns>
    /// <exception cref="InvalidFontException">The data is not a valid AFM file.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The data is a multiple master (AMFM) or a composite (ACFM) metrics file.</exception>
    /// <exception cref="FontResourceLimitException">The stream has more bytes than <see cref="FontResourceLimits.MaxEncodedBytes"/>.</exception>
    public static async Task<AfmFontMetrics> LoadAsync(Stream stream, FontResourceLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = await FontStreamReader.ReadAllAsync(stream, limits ?? FontResourceLimits.Default, cancellationToken).ConfigureAwait(false);
        return AfmParser.Parse(bytes);
    }

    /// <summary>Gets the metrics of the character that has a name.</summary>
    /// <param name="name">The PostScript name of the character.</param>
    /// <returns>The metrics of the first character of the file with that name, or <see langword="null"/> when there is none.</returns>
    public AfmCharacterMetrics? GetCharacter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _charactersByName.GetValueOrDefault(name);
    }

    /// <summary>Gets the metrics of the character that has a code in the default encoding of the font.</summary>
    /// <param name="code">The code of the character (the CID in a CID-keyed font).</param>
    /// <returns>The metrics of the first character of the file with that code, or <see langword="null"/> when there is none.</returns>
    public AfmCharacterMetrics? GetCharacterByCode(int code) => _charactersByCode.GetValueOrDefault(code);

    /// <summary>Gets the width vector of a character in writing direction 0.</summary>
    /// <param name="name">The PostScript name of the character.</param>
    /// <returns>The width of the character, the <see cref="AfmWritingDirection.CharWidth"/> of the direction when the character has none, or <see langword="null"/> when the font has no such character or no width for it.</returns>
    public AfmVector? GetWidth(string name) => GetCharacter(name) is { } character ? GetWidth(character) : null;

    /// <summary>Gets the width vector of a character in writing direction 0.</summary>
    /// <param name="character">The metrics of a character of this font.</param>
    /// <returns>The width of the character, the <see cref="AfmWritingDirection.CharWidth"/> of the direction when the character has none, or <see langword="null"/> when there is neither.</returns>
    public AfmVector? GetWidth(AfmCharacterMetrics character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.Width ?? Horizontal?.CharWidth;
    }

    /// <summary>Gets the width vector of a character in writing direction 1.</summary>
    /// <param name="name">The PostScript name of the character.</param>
    /// <returns>The vertical width of the character, the <see cref="AfmWritingDirection.CharWidth"/> of the direction when the character has none, or <see langword="null"/> when the font has no such character or no width for it.</returns>
    public AfmVector? GetVerticalWidth(string name) => GetCharacter(name) is { } character ? GetVerticalWidth(character) : null;

    /// <summary>Gets the width vector of a character in writing direction 1.</summary>
    /// <param name="character">The metrics of a character of this font.</param>
    /// <returns>The vertical width of the character, the <see cref="AfmWritingDirection.CharWidth"/> of the direction when the character has none, or <see langword="null"/> when there is neither.</returns>
    public AfmVector? GetVerticalWidth(AfmCharacterMetrics character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.VerticalWidth ?? Vertical?.CharWidth;
    }

    /// <summary>Gets the kerning of a pair of characters in writing direction 0.</summary>
    /// <param name="first">The name of the first character.</param>
    /// <param name="second">The name of the second character.</param>
    /// <returns>The vector by which the second character moves, or zero when the pair has no kerning. When the file has the pair twice, the first one applies.</returns>
    public AfmVector GetKerning(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return _kerningByName.GetValueOrDefault((first, second));
    }

    /// <summary>Gets the kerning of a pair of characters in writing direction 0.</summary>
    /// <param name="firstCode">The code of the first character.</param>
    /// <param name="secondCode">The code of the second character.</param>
    /// <returns>The vector by which the second character moves, or zero when the pair has no kerning. When the file has the pair twice, the first one applies.</returns>
    public AfmVector GetKerning(int firstCode, int secondCode) => _kerningByCode.GetValueOrDefault((firstCode, secondCode));

    /// <summary>Gets the kerning of a pair of characters in writing direction 1.</summary>
    /// <param name="first">The name of the first character.</param>
    /// <param name="second">The name of the second character.</param>
    /// <returns>The vector by which the second character moves, or zero when the pair has no kerning. When the file has the pair twice, the first one applies.</returns>
    public AfmVector GetVerticalKerning(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return _verticalKerningByName.GetValueOrDefault((first, second));
    }

    /// <summary>Gets the kerning of a pair of characters in writing direction 1.</summary>
    /// <param name="firstCode">The code of the first character.</param>
    /// <param name="secondCode">The code of the second character.</param>
    /// <returns>The vector by which the second character moves, or zero when the pair has no kerning. When the file has the pair twice, the first one applies.</returns>
    public AfmVector GetVerticalKerning(int firstCode, int secondCode) => _verticalKerningByCode.GetValueOrDefault((firstCode, secondCode));

    /// <summary>Gets the track kerning of a degree of tightness at a point size.</summary>
    /// <param name="degree">The degree of tightness of the track.</param>
    /// <param name="pointSize">The point size of the font.</param>
    /// <returns>The amount, in points, to add to the space between characters, or zero when the font has no track of that degree. When the file has the degree twice, the first track applies.</returns>
    public double GetTrackKerning(int degree, double pointSize)
    {
        foreach (var track in TrackKerning)
        {
            if (track.Degree == degree)
                return track.GetKerning(pointSize);
        }

        return 0;
    }

    // Completes each pair with the identifier of its characters that the file does not give, and indexes the pairs.
    private List<AfmKerningPair> Resolve(IReadOnlyList<AfmKerningPair> pairs, Dictionary<(string First, string Second), AfmVector> byName, Dictionary<(int First, int Second), AfmVector> byCode)
    {
        var result = new List<AfmKerningPair>(pairs.Count);
        foreach (var pair in pairs)
        {
            var resolved = pair with
            {
                First = pair.First ?? GetNameOfCode(pair.FirstCode),
                Second = pair.Second ?? GetNameOfCode(pair.SecondCode),
                FirstCode = pair.FirstCode ?? GetCodeOfName(pair.First),
                SecondCode = pair.SecondCode ?? GetCodeOfName(pair.Second),
            };

            result.Add(resolved);
            var vector = new AfmVector(resolved.X, resolved.Y);
            if (resolved is { First: not null, Second: not null })
                byName.TryAdd((resolved.First, resolved.Second), vector);

            if (resolved is { FirstCode: not null, SecondCode: not null })
                byCode.TryAdd((resolved.FirstCode.Value, resolved.SecondCode.Value), vector);
        }

        return result;
    }

    private string? GetNameOfCode(int? code) => code is { } value ? _charactersByCode.GetValueOrDefault(value)?.Name : null;

    private int? GetCodeOfName(string? name) => name is not null && _charactersByName.TryGetValue(name, out var character) && character.Code >= 0 ? character.Code : null;
}
