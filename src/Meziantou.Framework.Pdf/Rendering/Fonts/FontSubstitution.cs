using System.Collections.Concurrent;
using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// Finds the font that replaces a font the document does not embed (ISO 32000-2, 9.6.2.2 and 9.8): the font the
/// resolver of the options gives; for a standard font, the bundled font of its kind, which has its widths; else a font
/// of the system of the same family, else a font of the system of the same kind (sans serif, serif, monospaced); else
/// the bundled font of its kind.
/// </summary>
internal static class FontSubstitution
{
    // The families of the system that look like the standard fonts, by order of preference
    private static readonly string[] SansSerif = ["Arial", "Helvetica", "Liberation Sans", "Nimbus Sans", "Arimo", "DejaVu Sans", "FreeSans", "Noto Sans", "Segoe UI"];
    private static readonly string[] Serif = ["Times New Roman", "Times", "Liberation Serif", "Nimbus Roman", "Tinos", "DejaVu Serif", "FreeSerif", "Noto Serif"];
    private static readonly string[] Monospaced = ["Courier New", "Courier", "Liberation Mono", "Nimbus Mono PS", "Cousine", "DejaVu Sans Mono", "FreeMono", "Noto Sans Mono", "Consolas"];
    private static readonly string[] Dingbats = ["Dingbats", "DejaVu Sans", "Segoe UI Symbol", "Noto Sans Symbols 2"];

    private static readonly ConcurrentDictionary<(string Path, int Index), FontFace?> SystemFaces = new();

    public static FontFace? Resolve(PdfFontRequest request, RenderCache cache)
    {
        if (cache.Options.FontResolver?.Invoke(request) is { } resolved)
            return resolved;

        if (request.StandardName is not null && BundledFonts.Get(request, cache) is { } standard)
            return standard;

        return (cache.Options.UseSystemFonts ? FindSystemFont(request, cache) : null) ?? BundledFonts.Get(request, cache);
    }

    private static FontFace? FindSystemFont(PdfFontRequest request, RenderCache cache)
    {
        SystemFontCollection collection;
        try
        {
            collection = SystemFontCollection.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidFontException)
        {
            return null;
        }

        var weight = request.IsBold ? 700 : 400;
        foreach (var family in GetFamilies(request))
        {
            if (collection.Match(family, weight, request.IsItalic) is { } descriptor && Load(descriptor, cache) is { } face)
                return face;
        }

        return null;
    }

    private static IEnumerable<string> GetFamilies(PdfFontRequest request)
    {
        if (request.StandardName is null)
        {
            // The family of the name: "Garamond-BoldItalic" or "Garamond,Bold" is "Garamond"
            var name = request.Name;
            var end = name.IndexOfAny(['-', ',']);
            var family = end > 0 ? name[..end] : name;
            if (family.EndsWith("MT", StringComparison.Ordinal) || family.EndsWith("PS", StringComparison.Ordinal))
            {
                family = family[..^2];
            }

            if (family.Length > 0)
                yield return family;
        }

        var kind = request.StandardName == "ZapfDingbats" ? Dingbats : request.IsFixedPitch ? Monospaced : request.IsSerif ? Serif : SansSerif;
        foreach (var family in kind)
        {
            yield return family;
        }

        if (kind != SansSerif)
        {
            foreach (var family in SansSerif)
            {
                yield return family;
            }
        }
    }

    private static FontFace? Load(SystemFontDescriptor descriptor, RenderCache cache)
    {
        return SystemFaces.GetOrAdd((descriptor.FilePath, descriptor.FaceIndex), key =>
        {
            try
            {
                var faces = FontLoader.Load(File.ReadAllBytes(key.Path), cache.FontEnvironment);
                return key.Index < faces.Length ? faces[key.Index] : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidFontException or UnsupportedFontFeatureException or FontResourceLimitException)
            {
                return null;
            }
        });
    }
}
