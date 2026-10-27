namespace Meziantou.Framework.Fonts;

/// <summary>
/// The fonts installed on the machine, or the fonts of chosen directories: an index of their faces by family and by
/// name, read without loading the fonts.
/// </summary>
/// <remarks>
/// <para>
/// A collection is immutable and can be used by several threads at the same time. It is a snapshot of the files when
/// it is created: a font installed or removed later is not seen. Creating it reads the names and the styles of the
/// faces, not their characters: <see cref="SystemFontDescriptor.Covers(Rune)"/> reads them when they are first asked
/// for, and the collection keeps them within a bounded amount of memory.
/// </para>
/// <para>
/// The installed fonts are found in the font directories of the operating system:
/// </para>
/// <list type="bullet">
/// <item>Windows: the fonts directory of Windows, the fonts installed for the current user, and the files the registry lists.</item>
/// <item>macOS: the <c>Library/Fonts</c> directories of the user, of the computer, of the network and of the system, and the fonts the system downloads on demand.</item>
/// <item>Linux and the other systems: the directories of the configuration of fontconfig, and the font directories of the XDG Base Directory Specification.</item>
/// </list>
/// </remarks>
public sealed class SystemFontCollection
{
    private static readonly Lazy<SystemFontCollection> DefaultCollection = new(() => Create(SystemFontDirectories.GetDefault(Environment.GetEnvironmentVariable)), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly SystemFontDescriptor[] _fonts;
    private readonly Dictionary<string, SystemFontDescriptor[]> _families;
    private readonly Dictionary<string, SystemFontDescriptor> _names;

    private SystemFontCollection(SystemFontDescriptor[] fonts, long coverageBudget)
    {
        _fonts = fonts;
        CoverageCache = new SystemFontCoverageCache(coverageBudget);
        var families = new Dictionary<string, List<SystemFontDescriptor>>(StringComparer.OrdinalIgnoreCase);
        _names = new Dictionary<string, SystemFontDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var font in fonts)
        {
            font.CoverageCache = CoverageCache;
            foreach (var familyName in font.FamilyNames)
            {
                if (!families.TryGetValue(familyName, out var family))
                {
                    family = [];
                    families.Add(familyName, family);
                }

                family.Add(font);
            }

            // The first face of a name, in the order of the collection, is the one found by the name.
            if (font.FullName is { Length: > 0 } fullName)
            {
                _names.TryAdd(fullName, font);
            }

            if (font.PostScriptName is { Length: > 0 } postScriptName)
            {
                _names.TryAdd(postScriptName, font);
            }
        }

        _families = new Dictionary<string, SystemFontDescriptor[]>(families.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, family) in families)
        {
            _families.Add(name, [.. family]);
        }
    }

    /// <summary>Gets the fonts installed on the machine, scanned when the property is first read.</summary>
    /// <remarks>The scan reads the directories of the fonts of the operating system once per process; a font installed later is not seen.</remarks>
    public static SystemFontCollection Default => DefaultCollection.Value;

    /// <summary>Gets the cache of the characters of the faces (<see cref="SystemFontDescriptor.Covers(Rune)"/>).</summary>
    internal SystemFontCoverageCache CoverageCache { get; }

    /// <summary>Gets the faces, ordered by family, then by file, then by index in the file.</summary>
    public IReadOnlyList<SystemFontDescriptor> Fonts => _fonts;

    /// <summary>Creates the collection of the fonts of directories and of font files.</summary>
    /// <param name="paths">The directories, whose subdirectories are scanned too, and the font files. A path that does not exist is ignored.</param>
    /// <returns>The faces of the <c>.ttf</c>, <c>.otf</c>, <c>.ttc</c>, <c>.otc</c>, <c>.woff</c> and <c>.woff2</c> files the font code loads; the other files are ignored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is <see langword="null"/>.</exception>
    public static SystemFontCollection Create(IEnumerable<string> paths) => Create(paths, SystemFontCoverageCache.DefaultBudget);

    /// <summary>Creates the collection of the fonts of directories, whose characters are kept within <paramref name="coverageBudget"/> bytes.</summary>
    internal static SystemFontCollection Create(IEnumerable<string> paths, long coverageBudget)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SystemFontCollection(SystemFontScanner.Scan(paths), coverageBudget);
    }

    /// <summary>Gets the faces of a family.</summary>
    /// <param name="familyName">A typographic family name or a family name, in any language of the fonts, compared without case.</param>
    /// <returns>The faces of the family, in the order of <see cref="Fonts"/>; empty when no font has the family.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="familyName"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<SystemFontDescriptor> GetFamily(string familyName)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        return _families.TryGetValue(familyName, out var family) ? family : [];
    }

    /// <summary>Finds a face by its full name or its PostScript name, as the <c>local()</c> function of CSS does.</summary>
    /// <param name="name">The full name or the PostScript name, compared without case.</param>
    /// <returns>The first face of <see cref="Fonts"/> with the name, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public SystemFontDescriptor? FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _names.GetValueOrDefault(name);
    }

    /// <summary>
    /// Finds the face of a family that is the closest to a style, with the font matching of CSS (CSS Fonts Module Level
    /// 4, §5.2): the normal width first, then the style, then the weight.
    /// </summary>
    /// <param name="familyName">A typographic family name or a family name, in any language of the fonts, compared without case.</param>
    /// <param name="weight">The weight, from 1 to 1000: 400 is normal, 700 is bold.</param>
    /// <param name="isItalic">Whether an italic face is preferred; an oblique face, then an upright face, are used when the family has none.</param>
    /// <returns>The face, or <see langword="null"/> when no font has the family.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="familyName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is not between 1 and 1000.</exception>
    /// <remarks>
    /// A variable font matches the weights and the widths of its <c>wght</c> and <c>wdth</c> axes, and the italic of its
    /// <c>ital</c> axis. Selecting the instance of the variable font is left to the caller.
    /// </remarks>
    public SystemFontDescriptor? Match(string familyName, int weight = 400, bool isItalic = false)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, 1000);
        return SystemFontMatcher.Match(GetFamily(familyName), weight, isItalic);
    }
}
