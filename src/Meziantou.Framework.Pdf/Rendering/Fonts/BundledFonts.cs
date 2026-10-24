using System.Collections.Concurrent;
using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// The fonts the library bundles to replace the fonts that are not embedded: Liberation Sans, Serif and Mono (SIL Open
/// Font License 1.1), whose glyphs have the widths of Helvetica, Times and Courier, subset to the characters of the
/// Latin encodings (<c>eng/pdf/update-substitution-fonts.cs</c>).
/// </summary>
internal static class BundledFonts
{
    private static readonly ConcurrentDictionary<string, FontFace?> Faces = new(StringComparer.Ordinal);

    public static FontFace? Get(PdfFontRequest request, RenderCache cache)
    {
        // The symbolic standard fonts have glyphs the Latin fonts do not have
        if (request.StandardName is "Symbol" or "ZapfDingbats")
            return null;

        var family = request.IsFixedPitch ? "LiberationMono" : request.IsSerif ? "LiberationSerif" : "LiberationSans";
        var style = (request.IsBold, request.IsItalic) switch
        {
            (true, true) => "BoldItalic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular",
        };

        return Faces.GetOrAdd(family + "-" + style, name =>
        {
            using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream("Meziantou.Framework.Pdf.Rendering.Fonts." + name + ".woff2");
            if (stream is null)
                return null;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var faces = FontLoader.Load(buffer.ToArray(), cache.FontEnvironment);
            return faces.Length > 0 ? faces[0] : null;
        });
    }
}
