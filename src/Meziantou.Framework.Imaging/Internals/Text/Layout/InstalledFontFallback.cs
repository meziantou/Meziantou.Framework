using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Finds, among the installed fonts, a font for a grapheme cluster that no font of a stack has: the fallback of
/// <see cref="FontStack.WithInstalledFallback(SystemFontCollection?, InstalledFontFallbackOptions?)"/>.
/// </summary>
/// <remarks>
/// <para>The candidates, in order, for the first character of the cluster that needs a glyph:</para>
/// <list type="number">
/// <item><description>the families of <see cref="InstalledFontFallbackOptions.PreferredFamilies"/>;</description></item>
/// <item><description>for a cluster presented as an emoji (UTS #51), the color emoji fonts of the platforms;</description></item>
/// <item><description>the families the platforms usually use for the script of the character (<see cref="InstalledFontFallbackFamilies"/>);</description></item>
/// <item><description>every family that has the character: the ones whose closest face has the style of the primary font first, then by family name and by file.</description></item>
/// </list>
/// <para>
/// In a family, the face is the one closest to the style of the primary font (<see cref="SystemFontCollection.Match"/>),
/// at the instance of that style when it is a variable font. A face is used when its loaded font has every character of
/// the cluster; else the first one that has its first character is kept for when no font has the whole cluster.
/// </para>
/// <para>
/// The fallback is shared by the layouts of the stack and can be used by several threads at the same time. The font of
/// a cluster is kept, so that a layout loads each font once and the same cluster always gets the same font; the fonts
/// it loaded live as long as the stack.
/// </para>
/// </remarks>
internal sealed class InstalledFontFallback
{
    // The clusters whose font is kept: when there are more, they are forgotten and looked for again.
    private const int MaxCachedClusters = 4096;

    private readonly SystemFontCollection _fonts;
    private readonly InstalledFontFallbackOptions _options;
    private readonly int _weight;
    private readonly bool _isItalic;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Result> _clusters = new(StringComparer.Ordinal);
    private readonly Dictionary<SystemFontDescriptor, Font?> _loaded = new(ReferenceEqualityComparer.Instance);
    private volatile bool _hasReadAllCoverages;

    public InstalledFontFallback(SystemFontCollection fonts, InstalledFontFallbackOptions options, Font primary)
    {
        _fonts = fonts;
        _options = options;
        _weight = Math.Clamp(primary.Weight, 1, 1000);
        _isItalic = primary.IsItalic;
    }

    public SystemFontCollection Fonts => _fonts;

    /// <summary>Finds the font of a cluster.</summary>
    /// <param name="cluster">The grapheme cluster.</param>
    /// <param name="isEmoji">Whether the cluster is presented as an emoji.</param>
    /// <param name="withFirst">A font that has the first character of the cluster that needs a glyph, when no font has the whole cluster.</param>
    /// <returns>A font that has every character of the cluster, or <see langword="null"/>.</returns>
    public Font? Find(ReadOnlySpan<char> cluster, bool isEmoji, out Font? withFirst)
    {
        lock (_lock)
        {
            if (_clusters.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(cluster, out var cached))
            {
                withFirst = cached.WithFirst;
                return cached.Font;
            }
        }

        var result = Search(cluster, isEmoji);
        lock (_lock)
        {
            if (_clusters.Count >= MaxCachedClusters)
            {
                _clusters.Clear();
            }

            _clusters.TryAdd(cluster.ToString(), result);
        }

        withFirst = result.WithFirst;
        return result.Font;
    }

    private Result Search(ReadOnlySpan<char> cluster, bool isEmoji)
    {
        if (!TryGetFirstCharacter(cluster, out var first))
            return default;

        var tried = new HashSet<SystemFontDescriptor>(ReferenceEqualityComparer.Instance);
        Font? withFirst = null;
        foreach (var family in _options.PreferredFamilies)
        {
            if (TryFamily(family, cluster, first, tried, ref withFirst) is { } font)
                return new Result(font, null);
        }

        foreach (var family in InstalledFontFallbackFamilies.Get(first, isEmoji))
        {
            if (TryFamily(family, cluster, first, tried, ref withFirst) is { } font)
                return new Result(font, null);
        }

        // The first search of every face reads the character maps of all of them: in parallel, it takes a tenth of the
        // time (810 faces of macOS: 80 ms instead of 715 ms).
        if (!_hasReadAllCoverages)
        {
            Parallel.ForEach(_fonts.Fonts, face => face.Covers(first));
            _hasReadAllCoverages = true;
        }

        // Every family that has the character, in the order of the collection (by family name, then by file): the ones
        // whose closest face has the style of the primary font first.
        var families = new List<(string Family, SystemFontDescriptor Face)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var face in _fonts.Fonts)
        {
            if (face.FamilyName.Length == 0 || seen.Contains(face.FamilyName) || !face.Covers(first))
                continue;

            seen.Add(face.FamilyName);
            families.Add((face.FamilyName, _fonts.Match(face.FamilyName, _weight, _isItalic) ?? face));
        }

        foreach (var hasStyle in (ReadOnlySpan<bool>)[true, false])
        {
            foreach (var (family, face) in families)
            {
                if (HasStyle(face) == hasStyle && TryFamily(family, cluster, first, tried, ref withFirst) is { } font)
                    return new Result(font, null);
            }
        }

        // Faces without a family name, which no family gives.
        foreach (var face in _fonts.Fonts)
        {
            if (face.FamilyName.Length == 0 && TryFace(face, cluster, first, tried, ref withFirst) is { } font)
                return new Result(font, null);
        }

        return new Result(null, withFirst);
    }

    /// <summary>Tries the face of a family closest to the style of the primary font, then its other faces that have the character.</summary>
    private Font? TryFamily(string family, ReadOnlySpan<char> cluster, Rune first, HashSet<SystemFontDescriptor> tried, ref Font? withFirst)
    {
        if (_fonts.Match(family, _weight, _isItalic) is not { } closest)
            return null;

        if (TryFace(closest, cluster, first, tried, ref withFirst) is { } font)
            return font;

        foreach (var face in _fonts.GetFamily(family))
        {
            if (TryFace(face, cluster, first, tried, ref withFirst) is { } other)
                return other;
        }

        return null;
    }

    private Font? TryFace(SystemFontDescriptor face, ReadOnlySpan<char> cluster, Rune first, HashSet<SystemFontDescriptor> tried, ref Font? withFirst)
    {
        if (!tried.Add(face) || !face.Covers(first) || Load(face) is not { } font)
            return null;

        if (TextLayoutEngine.HasCluster(font.Face, cluster, out var hasFirst))
            return font;

        if (hasFirst)
        {
            withFirst ??= font;
        }

        return null;
    }

    /// <summary>Loads a face at the style of the primary font, once: a file that cannot be loaded is skipped.</summary>
    private Font? Load(SystemFontDescriptor face)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(face, out var cached))
                return cached;
        }

        Font? font;
        try
        {
            font = InstalledFontFiles.ApplyStyle(InstalledFontFiles.Load(face, _options.LoadOptions), _weight, _isItalic);
        }
        catch (Exception exception) when (exception is ImageException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            font = null;
        }

        // The first font loaded for a face is kept, so that a face always gives the same font.
        lock (_lock)
        {
            return _loaded.TryAdd(face, font) ? font : _loaded[face];
        }
    }

    /// <summary>Determines whether a face has the weight and the italic of the primary font.</summary>
    private bool HasStyle(SystemFontDescriptor face)
    {
        var (minimum, maximum) = (face.Weight, face.Weight);
        var hasItalicAxis = false;
        var supportsItalic = face.IsItalic;
        foreach (var axis in face.Axes)
        {
            if (axis.Tag == FontTags.WeightAxis)
            {
                (minimum, maximum) = ((int)Math.Floor(axis.Minimum / 65536.0), (int)Math.Ceiling(axis.Maximum / 65536.0));
            }
            else if (axis.Tag == FontTags.ItalicAxis)
            {
                hasItalicAxis = true;
                supportsItalic = _isItalic ? axis.Maximum >= 65536 : axis.Minimum <= 0;
            }
        }

        var italic = hasItalicAxis ? supportsItalic : face.IsItalic == _isItalic;
        return italic && minimum <= _weight && _weight <= maximum;
    }

    /// <summary>Gets the first character of a cluster that needs a glyph: neither a control nor a default ignorable character.</summary>
    private static bool TryGetFirstCharacter(ReadOnlySpan<char> cluster, out Rune first)
    {
        foreach (var rune in cluster.EnumerateRunes())
        {
            if (TextLayoutEngine.NeedsGlyph(rune.Value))
            {
                first = rune;
                return true;
            }
        }

        first = default;
        return false;
    }

    private readonly record struct Result(Font? Font, Font? WithFirst);
}
