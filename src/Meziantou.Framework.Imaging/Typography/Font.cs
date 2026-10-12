using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// A font face loaded from a TrueType or OpenType file (<c>.ttf</c>, <c>.otf</c>), from a WOFF or WOFF2 file, or from
/// a font collection (<c>.ttc</c>, <c>.otc</c>).
/// </summary>
/// <remarks>
/// <para>
/// A font is immutable and can be used by several threads at the same time. It keeps the bytes of its file in memory
/// for its whole lifetime, within <see cref="ImageResourceLimits.MaxFontBytes"/>, and reads its tables from them: it
/// holds no other resource and needs no disposal. The faces of a collection share the bytes.
/// </para>
/// <para>
/// A font file is untrusted input. What every use of the font needs is validated when it is loaded; the data of a
/// glyph is validated when the glyph is first needed, and the work to produce one glyph is bounded by
/// <see cref="ImageResourceLimits.MaxGlyphComplexity"/>.
/// </para>
/// </remarks>
public sealed class Font
{
    /// <summary>The greatest font size, in pixels per em.</summary>
    internal const double MaxSize = 65536;

    private readonly FontVariationAxis[] _axes;
    private readonly FontNamedInstance[] _namedInstances;
    private readonly FontVariationSetting[] _variations;

    internal Font(FontFace face)
        : this(face, CreateAxes(face), namedInstances: null)
    {
    }

    private Font(FontFace face, FontVariationAxis[] axes, FontNamedInstance[]? namedInstances)
    {
        Face = face;
        Metrics = CreateMetrics(face);
        _axes = axes;
        _namedInstances = namedInstances ?? CreateNamedInstances(face, axes);

        // The value of each axis: the ones of the instance, or the defaults.
        _variations = axes.Length == 0 ? [] : new FontVariationSetting[axes.Length];
        for (var i = 0; i < _variations.Length; i++)
        {
            _variations[i] = new FontVariationSetting(axes[i].Tag, face.Instance is { } instance ? instance.UserCoordinates[i] / 65536.0 : axes[i].Default);
        }
    }

    internal FontFace Face { get; }

    /// <summary>Gets the name of the family of the font, such as <c>Noto Sans</c>; empty when the font has no name.</summary>
    /// <remarks>The typographic family name is used when the font has one: it groups every weight and style of the design.</remarks>
    public string FamilyName => Face.FamilyName;

    /// <summary>Gets the name of the style of the font within its family, such as <c>Bold Italic</c>; empty when the font has no name.</summary>
    public string SubfamilyName => Face.SubfamilyName;

    /// <summary>Gets the PostScript name of the font, such as <c>NotoSans-Bold</c>, or <see langword="null"/> when the font has none.</summary>
    public string? PostScriptName => Face.PostScriptName;

    /// <summary>Gets the weight of the font, from 1 to 1000: 400 is regular, 700 is bold.</summary>
    /// <remarks>For an instance of a variable font that has a weight axis (<c>wght</c>), the value of that axis, rounded.</remarks>
    public int Weight => Face.Weight;

    /// <summary>Gets a value indicating whether the font is italic or oblique.</summary>
    public bool IsItalic => Face.IsItalic;

    /// <summary>Gets a value indicating whether the font declares that all its glyphs have the same advance.</summary>
    public bool IsMonospaced => Face.IsMonospaced;

    /// <summary>Gets the number of font units per em: the scale of the coordinates of the font.</summary>
    public int UnitsPerEm => Face.UnitsPerEm;

    /// <summary>Gets the number of glyphs of the font.</summary>
    public int GlyphCount => Face.GlyphCount;

    /// <summary>Gets the vertical metrics of the font, in font units.</summary>
    /// <remarks>
    /// <para>
    /// The ascent, the descent and the line gap are the typographic values of the font when it asks for them to be
    /// used (the <c>USE_TYPO_METRICS</c> flag); else the values of its horizontal header; else its typographic values;
    /// else its Windows values.
    /// </para>
    /// <para>
    /// When the font does not declare an underline, it is 1/16 em thick with its top 1/8 em below the baseline; when it
    /// does not declare a strikethrough line, it is 1/16 em thick with its top 5/16 em above the baseline.
    /// </para>
    /// </remarks>
    public FontMetrics Metrics { get; }

    /// <summary>Gets the axes of a variable font: what can vary, and in what range. Empty for a font that does not vary.</summary>
    public IReadOnlyList<FontVariationAxis> VariationAxes => _axes;

    /// <summary>Gets the instances a variable font names, such as "Light" or "Bold Condensed". Empty for a font that names none.</summary>
    public IReadOnlyList<FontNamedInstance> NamedInstances => _namedInstances;

    /// <summary>
    /// Gets the value of each axis of a variable font for this font, in the order of <see cref="VariationAxes"/>: the
    /// defaults for a font as it is loaded, the values of the instance for a font <see cref="WithVariations(ReadOnlySpan{FontVariationSetting})"/>
    /// returned. Empty for a font that does not vary.
    /// </summary>
    public IReadOnlyList<FontVariationSetting> Variations => _variations;

    /// <summary>Gets an instance of a variable font: the same font with other values of its axes.</summary>
    /// <param name="settings">
    /// The values of the axes. An axis that is not named has its default value, whatever its value in this font; a value
    /// outside the range of its axis is brought to the nearest end of the range; a tag that is not one of the axes of
    /// the font is ignored; when an axis is named several times, its last value is used.
    /// </param>
    /// <returns>
    /// A font that shares the data of this one: its outlines, its advances, its metrics and the positions of its
    /// layout tables are the ones of the instance. For a font that does not vary, the font itself.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Creating an instance reads no glyph: it normalizes the values (with the <c>avar</c> table of the font) and
    /// computes how much of each region of the variation data applies. The glyphs are varied when they are needed:
    /// their points are not rounded, so an instance is as smooth as the default one. The advances and the metrics of
    /// an instance are whole font units.
    /// </para>
    /// <para>The instances of a font are different fonts for text layout: a text that uses two of them is shaped in two runs.</para>
    /// </remarks>
    public Font WithVariations(params ReadOnlySpan<FontVariationSetting> settings)
    {
        if (_axes.Length == 0)
            return this;

        Span<int> coordinates = _axes.Length <= 64 ? stackalloc int[_axes.Length] : new int[_axes.Length];
        var axes = Face.Axes;
        for (var i = 0; i < coordinates.Length; i++)
        {
            coordinates[i] = axes[i].Default;
        }

        foreach (var setting in settings)
        {
            var tag = setting.TagValue;
            for (var i = 0; i < coordinates.Length; i++)
            {
                if (axes[i].Tag == tag)
                {
                    // A 16.16 fixed-point number; the face brings it into the range of the axis.
                    coordinates[i] = (int)Math.Clamp(Math.Round(setting.Value * 65536, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
                }
            }
        }

        return new Font(Face.WithCoordinates(coordinates), _axes, _namedInstances);
    }

    /// <summary>Gets a named instance of a variable font.</summary>
    /// <param name="instance">An instance of <see cref="NamedInstances"/>.</param>
    /// <returns>The font of the instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instance"/> is <see langword="null"/>.</exception>
    public Font WithVariations(FontNamedInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var settings = instance.Settings;
        var values = new FontVariationSetting[settings.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = settings[i];
        }

        return WithVariations(values);
    }

    /// <summary>Loads a font from a file. For a font collection, the first face is loaded.</summary>
    /// <param name="path">The path of the file. The file is closed before the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The file is not a font.</exception>
    /// <exception cref="InvalidImageContentException">The font is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The font uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static Font Load(string path, FontLoadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new Font(FontLoader.Load(path, (options ?? FontLoadOptions.Default).Configuration)[0]);
    }

    /// <summary>Loads a font from a stream, read from its current position to its end. For a font collection, the first face is loaded.</summary>
    /// <param name="stream">The readable source stream. It is left open and is not retained after the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The data is not a font.</exception>
    /// <exception cref="InvalidImageContentException">The font is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The font uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static Font Load(Stream stream, FontLoadOptions? options = null)
    {
        ValidateReadableStream(stream);
        return new Font(FontLoader.Load(stream, (options ?? FontLoadOptions.Default).Configuration)[0]);
    }

    /// <summary>Loads a font from the bytes of a font file. For a font collection, the first face is loaded.</summary>
    /// <param name="data">The bytes of the file. They are copied: the font does not refer to the span after the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The font.</returns>
    /// <exception cref="UnknownImageFormatException">The data is not a font.</exception>
    /// <exception cref="InvalidImageContentException">The font is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The font uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static Font Load(ReadOnlySpan<byte> data, FontLoadOptions? options = null)
        => new(FontLoader.Load(data, (options ?? FontLoadOptions.Default).Configuration)[0]);

    /// <summary>Asynchronously loads a font from a file, using asynchronous file I/O. For a font collection, the first face is loaded.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<Font> LoadAsync(string path, FontLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return LoadCoreAsync(FontLoader.LoadAsync(path, (options ?? FontLoadOptions.Default).Configuration, cancellationToken));
    }

    /// <summary>Asynchronously loads a font from a stream, read from its current position to its end. For a font collection, the first face is loaded.</summary>
    /// <param name="stream">The readable source stream. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<Font> LoadAsync(Stream stream, FontLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateReadableStream(stream);
        return LoadCoreAsync(FontLoader.LoadAsync(stream, (options ?? FontLoadOptions.Default).Configuration, cancellationToken));
    }

    /// <summary>Gets the vertical metrics of the font at a size, in pixels.</summary>
    /// <param name="size">The size of the font, in pixels per em.</param>
    /// <returns>The metrics of <see cref="Metrics"/> multiplied by <paramref name="size"/> / <see cref="UnitsPerEm"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a positive finite number, or is greater than 65536.</exception>
    public FontMetrics GetMetrics(double size)
    {
        ValidateSize(size, nameof(size));
        return Metrics.Scale(size / UnitsPerEm);
    }

    /// <summary>Determines whether the font has a glyph for a character.</summary>
    /// <param name="codePoint">The character.</param>
    /// <returns><see langword="true"/> when the character map of the font gives the character a glyph other than the missing glyph.</returns>
    public bool ContainsGlyph(Rune codePoint) => Face.MapCodePoint(codePoint.Value) != 0;

    internal static void ValidateSize(double size, string parameterName)
    {
        if (!(size > 0) || size > MaxSize)
            throw new ArgumentOutOfRangeException(parameterName, size, "The size of a font is a positive number of pixels per em, up to 65536.");
    }

    internal static void ValidateReadableStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream is not readable.", nameof(stream));
    }

    private static async Task<Font> LoadCoreAsync(Task<FontFace[]> faces) => new((await faces.ConfigureAwait(false))[0]);

    private static FontVariationAxis[] CreateAxes(FontFace face)
    {
        var records = face.Axes;
        if (records.IsEmpty)
            return [];

        var axes = new FontVariationAxis[records.Length];
        for (var i = 0; i < axes.Length; i++)
        {
            var tag = FontTags.Format(records[i].Tag);
            axes[i] = new FontVariationAxis(tag, face.GetName(records[i].NameId) ?? tag, records[i].Minimum / 65536.0, records[i].Default / 65536.0, records[i].Maximum / 65536.0, records[i].IsHidden);
        }

        return axes;
    }

    private static FontNamedInstance[] CreateNamedInstances(FontFace face, FontVariationAxis[] axes)
    {
        var records = face.NamedInstances;
        if (records.IsEmpty)
            return [];

        var instances = new FontNamedInstance[records.Length];
        for (var i = 0; i < instances.Length; i++)
        {
            var record = records[i];
            var settings = new FontVariationSetting[axes.Length];
            for (var axis = 0; axis < settings.Length; axis++)
            {
                settings[axis] = new FontVariationSetting(axes[axis].Tag, record.Coordinates[axis] / 65536.0);
            }

            // The identifiers 0xFFFF and 0 say that the instance has no PostScript name.
            var postScriptName = record.PostScriptNameId is 0 or 0xFFFF ? null : face.GetName(record.PostScriptNameId);
            instances[i] = new FontNamedInstance(face.GetName(record.NameId) ?? "", postScriptName, settings);
        }

        return instances;
    }

    private static FontMetrics CreateMetrics(FontFace face)
    {
        int ascender;
        int descender;
        int lineGap;
        if (face.HasOs2 && face.UseTypoMetrics)
        {
            (ascender, descender, lineGap) = (face.TypoAscender, face.TypoDescender, face.TypoLineGap);
        }
        else if (face.HorizontalAscender != 0 || face.HorizontalDescender != 0)
        {
            (ascender, descender, lineGap) = (face.HorizontalAscender, face.HorizontalDescender, face.HorizontalLineGap);
        }
        else if (face.HasOs2 && (face.TypoAscender != 0 || face.TypoDescender != 0))
        {
            (ascender, descender, lineGap) = (face.TypoAscender, face.TypoDescender, face.TypoLineGap);
        }
        else
        {
            // The Windows descent is a distance, the others are coordinates.
            (ascender, descender, lineGap) = (face.WinAscent, -face.WinDescent, 0);
        }

        // The defaults of the lines a font does not declare are fractions of the em.
        double unitsPerEm = face.UnitsPerEm;
        var hasUnderline = face.HasPost && face.UnderlineThickness > 0;
        var hasStrikethrough = face.HasOs2 && face.StrikeoutSize > 0;
        return new FontMetrics(
            ascent: ascender,
            descent: -descender,
            lineGap: lineGap,
            capHeight: face.CapHeight,
            xHeight: face.XHeight,
            underlinePosition: hasUnderline ? -face.UnderlinePosition : unitsPerEm / 8,
            underlineThickness: hasUnderline ? face.UnderlineThickness : unitsPerEm / 16,
            strikethroughPosition: hasStrikethrough ? face.StrikeoutPosition : unitsPerEm * 5 / 16,
            strikethroughThickness: hasStrikethrough ? face.StrikeoutSize : unitsPerEm / 16);
    }
}
