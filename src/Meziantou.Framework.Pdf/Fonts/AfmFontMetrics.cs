using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>The metrics of an Adobe Font Metrics file (Adobe Technical Note #5004): the global metrics, the glyphs and the kerning pairs.</summary>
internal sealed class AfmFontMetrics
{
    private AfmFontMetrics()
    {
    }

    public string FontName { get; private set; } = "";

    public string FamilyName { get; private set; } = "";

    public string Weight { get; private set; } = "";

    public double ItalicAngle { get; private set; }

    public bool IsFixedPitch { get; private set; }

    public PdfRectangle FontBBox { get; private set; }

    public double CapHeight { get; private set; }

    public double XHeight { get; private set; }

    public double Ascender { get; private set; }

    public double Descender { get; private set; }

    public double StemV { get; private set; }

    /// <summary>Gets a value indicating whether the font has its own encoding (<c>FontSpecific</c>: Symbol and ZapfDingbats).</summary>
    public bool IsFontSpecific { get; private set; }

    public Dictionary<string, AfmGlyph> Glyphs { get; } = new(StringComparer.Ordinal);

    public Dictionary<(string Left, string Right), double> KerningPairs { get; } = [];

    public static AfmFontMetrics Parse(TextReader reader)
    {
        var metrics = new AfmFontMetrics();
        var hasCapHeight = false;
        var hasXHeight = false;
        var hasAscender = false;
        var hasDescender = false;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..].Trim();
            switch (key)
            {
                case "FontName":
                    metrics.FontName = value;
                    break;
                case "FamilyName":
                    metrics.FamilyName = value;
                    break;
                case "Weight":
                    metrics.Weight = value;
                    break;
                case "ItalicAngle":
                    metrics.ItalicAngle = ParseNumber(value);
                    break;
                case "IsFixedPitch":
                    metrics.IsFixedPitch = value == "true";
                    break;
                case "FontBBox":
                    var box = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    metrics.FontBBox = new PdfRectangle(ParseNumber(box[0]), ParseNumber(box[1]), ParseNumber(box[2]), ParseNumber(box[3]));
                    break;
                case "CapHeight":
                    metrics.CapHeight = ParseNumber(value);
                    hasCapHeight = true;
                    break;
                case "XHeight":
                    metrics.XHeight = ParseNumber(value);
                    hasXHeight = true;
                    break;
                case "Ascender":
                    metrics.Ascender = ParseNumber(value);
                    hasAscender = true;
                    break;
                case "Descender":
                    metrics.Descender = ParseNumber(value);
                    hasDescender = true;
                    break;
                case "StdVW":
                    metrics.StemV = ParseNumber(value);
                    break;
                case "EncodingScheme":
                    metrics.IsFontSpecific = value == "FontSpecific";
                    break;
                case "C":
                    ParseCharacter(metrics, line);
                    break;
                case "KPX":
                    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        metrics.KerningPairs[(parts[0], parts[1])] = ParseNumber(parts[2]);
                    }

                    break;
            }
        }

        // The fonts with their own encoding have no such metrics: the font box gives them (9.8.1)
        if (!hasAscender)
        {
            metrics.Ascender = metrics.FontBBox.Top;
        }

        if (!hasDescender)
        {
            metrics.Descender = metrics.FontBBox.Bottom;
        }

        if (!hasCapHeight)
        {
            metrics.CapHeight = metrics.FontBBox.Top;
        }

        if (!hasXHeight)
        {
            metrics.XHeight = metrics.CapHeight / 2;
        }

        return metrics;
    }

    public static AfmFontMetrics Load(string fontName)
    {
        using var stream = typeof(AfmFontMetrics).Assembly.GetManifestResourceStream("Meziantou.Framework.Pdf.Afm." + fontName + ".afm") ?? throw new InvalidOperationException("The metrics of " + fontName + " are missing");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return Parse(reader);
    }

    private static void ParseCharacter(AfmFontMetrics metrics, string line)
    {
        // C 32 ; WX 278 ; N space ; B 0 0 0 0 ;
        var code = -1;
        double width = 0;
        string? name = null;
        foreach (var part in line.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var space = part.IndexOf(' ', StringComparison.Ordinal);
            if (space < 0)
                continue;

            var value = part[(space + 1)..].Trim();
            switch (part[..space])
            {
                case "C":
                    code = (int)ParseNumber(value);
                    break;
                case "WX":
                    width = ParseNumber(value);
                    break;
                case "N":
                    name = value;
                    break;
            }
        }

        if (name is not null)
        {
            metrics.Glyphs[name] = new AfmGlyph(name, code, width);
        }
    }

    private static double ParseNumber(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
