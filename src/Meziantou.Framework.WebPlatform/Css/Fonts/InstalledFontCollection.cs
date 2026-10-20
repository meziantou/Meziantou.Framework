using System.Text;
using SystemFontCollection = Meziantou.Framework.Fonts.SystemFontCollection;
using SystemFontDescriptor = Meziantou.Framework.Fonts.SystemFontDescriptor;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// A font face installed by the host (<see cref="InstalledFontCollection"/>): a face of a font file the host added, or a face
/// of a font installed on the machine, read from its file on first use.
/// </summary>
internal sealed class InstalledFont
{
    // Not nullable fields assigned on first use: a collection can be shared by the browsing contexts of several threads,
    // and a struct is not written atomically (another thread could read a partially written value)
    private readonly Lazy<OpenTypeFont?> _font;
    private readonly Lazy<FontSelectionCapabilities> _capabilities;

    internal InstalledFont(OpenTypeFont font)
    {
        _font = new Lazy<OpenTypeFont?>(font);
        _capabilities = new Lazy<FontSelectionCapabilities>(() => FontSelectionCapabilities.FromFont(font), LazyThreadSafetyMode.ExecutionAndPublication);
        FullName = font.FullName;
        PostScriptName = font.PostScriptName;
    }

    /// <summary>A face of a font installed on the machine: its capabilities come from the system font index, its file is read when the face is used.</summary>
    internal InstalledFont(SystemFontDescriptor descriptor, Lazy<FontFile?> file)
    {
        _font = new Lazy<OpenTypeFont?>(() => file.Value is { } loaded && descriptor.FaceIndex < loaded.Faces.Count ? loaded.Faces[descriptor.FaceIndex] : null, LazyThreadSafetyMode.ExecutionAndPublication);
        _capabilities = new Lazy<FontSelectionCapabilities>(FontSelectionCapabilities.FromSystemFont(descriptor));
        FullName = descriptor.FullName ?? "";
        PostScriptName = descriptor.PostScriptName ?? "";
    }

    /// <summary>
    /// The font, or null when it is a font installed on the machine whose file can no longer be loaded (it was removed or
    /// changed since the fonts of the machine were scanned).
    /// </summary>
    public OpenTypeFont? Font => _font.Value;

    /// <summary>The full name of the font (used by <c>local()</c>).</summary>
    public string FullName { get; }

    /// <summary>The PostScript name of the font (used by <c>local()</c>).</summary>
    public string PostScriptName { get; }

    /// <summary>The styles of the font (its weight, width and style, or the ranges of its variation axes), for the font matching.</summary>
    public FontSelectionCapabilities Capabilities => _capabilities.Value;

    /// <inheritdoc />
    public override string ToString() => FullName;
}

/// <summary>
/// The fonts that a host installs in addition to the bundled fonts (<see cref="BundledFonts"/>), for the documents of a
/// browsing context (<see cref="Dom.BrowsingContextOptions.InstalledFonts"/>):
/// <list type="bullet">
/// <item>the installed families (<see cref="Add"/>), which a <c>font-family</c> and a <c>local()</c> source can name
/// (https://drafts.csswg.org/css-fonts-4/#font-family-prop): a bundled family hides an installed family of the same
/// name;</item>
/// <item>the fonts of the installed font fallback (<see cref="AddFallback"/>, https://drafts.csswg.org/css-fonts-4/#installed-font-fallback),
/// searched after the bundled fonts for the characters of their ranges that no other font supports. They cannot be named.</item>
/// </list>
/// The bundled fonts are always searched first: the collection changes neither the default font nor the fonts of the
/// characters that the bundled fonts support. <see cref="System"/> is the collection of the fonts installed on the machine
/// (<see cref="Dom.BrowsingContextOptions.UseSystemFonts"/>).
/// </summary>
/// <remarks>
/// The collection is filled before it is given to a browsing context: it must not change afterwards (the font matching
/// caches its results). The fonts are immutable: a collection can be shared by the browsing contexts of several threads.
/// </remarks>
internal sealed class InstalledFontCollection
{
    private static readonly Lazy<InstalledFontCollection> SystemCollection = new(() => FromSystemFonts(SystemFontCollection.Default), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, List<InstalledFont>> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InstalledFont> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InstalledFont> _fonts = [];
    private readonly List<FallbackFont> _fallbacks = [];

    /// <summary>A font of the installed font fallback: it is only used for the characters of its ranges.</summary>
    internal readonly record struct FallbackFont(InstalledFont Font, UnicodeRangeSet Ranges);

    /// <summary>
    /// The fonts installed on the machine (<see cref="SystemFontCollection.Default"/>), scanned when the property is first
    /// read: their families can be named, and their files are read when a face is used. They have no installed font fallback.
    /// </summary>
    public static InstalledFontCollection System => SystemCollection.Value;

    /// <summary>The fonts of the installed families, in the order they were added.</summary>
    public IReadOnlyList<InstalledFont> Fonts => _fonts;

    /// <summary>The fonts of the installed font fallback, in the order they are searched.</summary>
    public IReadOnlyList<FallbackFont> Fallbacks => _fallbacks;

    /// <summary>
    /// Installs the faces of a font file (OpenType, TrueType, a collection, WOFF or WOFF2). A face belongs to the families of
    /// all its family names (https://drafts.csswg.org/css-fonts-4/#localized-name-matching: the localized names match too).
    /// </summary>
    /// <returns>false when the data is not a valid font file.</returns>
    public bool Add(ReadOnlySpan<byte> data)
    {
        if (FontFile.Load(data) is not { } file)
            return false;

        foreach (var face in file.Faces)
        {
            AddFont(new InstalledFont(face), face.GetFamilyNames());
        }

        return true;
    }

    /// <summary>
    /// Creates the collection of fonts installed on the machine, or in directories (<see cref="SystemFontCollection.Create"/>).
    /// A face belongs to the families of all its typographic family names and family names, in all the languages: the
    /// browsers find an installed face by its legacy family name too (e.g. <c>Arial Black</c>). The faces of a file share
    /// the file, read once when one of them is used.
    /// </summary>
    public static InstalledFontCollection FromSystemFonts(SystemFontCollection fonts)
    {
        var collection = new InstalledFontCollection();
        var files = new Dictionary<string, Lazy<FontFile?>>(StringComparer.Ordinal);
        foreach (var descriptor in fonts.Fonts)
        {
            if (!files.TryGetValue(descriptor.FilePath, out var file))
            {
                var path = descriptor.FilePath;
                file = new Lazy<FontFile?>(() => LoadFile(path), LazyThreadSafetyMode.ExecutionAndPublication);
                files.Add(path, file);
            }

            collection.AddFont(new InstalledFont(descriptor, file), descriptor.FamilyNames);
        }

        return collection;
    }

    /// <summary>Reads a font file of the machine, or returns null when it can no longer be read or loaded.</summary>
    private static FontFile? LoadFile(string path)
    {
        try
        {
            // A file larger than the limit of the font reader is not read to be rejected
            if (new FileInfo(path).Length > FontFile.MaxDecodedLength)
                return null;

            return FontFile.Load(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or global::System.Security.SecurityException)
        {
            return null;
        }
    }

    private void AddFont(InstalledFont font, IReadOnlyList<string> familyNames)
    {
        _fonts.Add(font);
        foreach (var family in familyNames)
        {
            if (!_families.TryGetValue(family, out var fonts))
            {
                fonts = [];
                _families.Add(family, fonts);
            }

            // A face is in a family once, whatever the number of its names in that family
            if (!fonts.Contains(font))
            {
                fonts.Add(font);
            }
        }

        // The first font with a name is the one local() finds
        if (font.FullName.Length != 0)
        {
            _names.TryAdd(font.FullName, font);
        }

        if (font.PostScriptName.Length != 0)
        {
            _names.TryAdd(font.PostScriptName, font);
        }
    }

    /// <summary>Returns the installed font whose full name or PostScript name is the name (case-insensitive), or null.</summary>
    public InstalledFont? FindByName(string name) => _names.GetValueOrDefault(name);

    /// <summary>
    /// Adds the first face of a font file to the installed font fallback, for the characters of a list of ranges (the syntax
    /// of the <c>unicode-range</c> descriptor; all the characters when it is null).
    /// </summary>
    /// <returns>false when the data is not a valid font file.</returns>
    public bool AddFallback(ReadOnlySpan<byte> data, string? unicodeRange = null)
    {
        if (FontFile.Load(data) is not { Faces: [var face, ..] })
            return false;

        _fallbacks.Add(new FallbackFont(new InstalledFont(face), unicodeRange is null ? UnicodeRangeSet.All : UnicodeRangeSet.Parse(unicodeRange)));
        return true;
    }

    /// <summary>The fonts of an installed family (case-insensitive); empty when the family is not installed.</summary>
    public IReadOnlyList<InstalledFont> GetFamilyFonts(string family)
    {
        if (_families.TryGetValue(family, out var fonts))
            return fonts;

        // The names that are not ASCII are compared with the case folding of the font matching
        if (!Ascii.IsValid(family))
        {
            foreach (var (name, candidates) in _families)
            {
                if (FontSelector.FamilyNamesEqual(name, family))
                    return candidates;
            }
        }

        return [];
    }
}
