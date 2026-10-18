using System.Text;

namespace SpecGen;

/// <summary>
/// Converts the names of the specifications (HTMLElement, innerHTML, toJSON...) to .NET names (HtmlElement, InnerHtml, ToJson...).
/// Acronyms of 3 letters or more are PascalCased, acronyms of 2 letters stay upper case (UIEvent, HtmlBRElement, SvgFEBlendElement), following the .NET naming guidelines.
/// </summary>
internal static class ClrNames
{
    // Longest acronyms first, so that the segmentation of "SVGFE" or "HTMLLI" is unambiguous
    private static readonly (string Acronym, string Replacement)[] Acronyms =
    [
        ("CDATA", "CData"),
        ("HTML", "Html"),
        ("JSON", "Json"),
        ("BOM", "Bom"),
        ("CSS", "Css"),
        ("CTM", "Ctm"),
        ("DOM", "Dom"),
        ("IDL", "Idl"),
        ("SVG", "Svg"),
        ("URI", "Uri"),
        ("URL", "Url"),
        ("XML", "Xml"),
        ("ID", "Id"),
    ];

    /// <summary>Converts a type or member name (the first letter is kept as is).</summary>
    public static string ToClrName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var index = 0;
        while (index < name.Length)
        {
            if (!char.IsAsciiLetterUpper(name[index]))
            {
                builder.Append(name[index++]);
                continue;
            }

            var start = index;
            while (index < name.Length && char.IsAsciiLetterUpper(name[index]))
            {
                index++;
            }

            var run = name[start..index];

            // The last upper case letter of a run followed by a lower case letter starts the next word (HTMLElement → HTML + Element),
            // except for the plural of an acronym (URLs)
            if (run.Length > 1 && index < name.Length && char.IsAsciiLetterLower(name[index]) && !IsPluralAcronym(name, run, index))
            {
                run = run[..^1];
                index--;
            }

            AppendAcronyms(builder, run);
        }

        return builder.Replace("Javascript", "JavaScript").ToString();
    }

    private static bool IsPluralAcronym(string name, string run, int index)
        => name[index] == 's' && (index + 1 == name.Length || !char.IsAsciiLetterLower(name[index + 1])) && Array.Exists(Acronyms, a => a.Acronym == run);

    // Splits a run of upper case letters into the known acronyms, the other letters are kept as is
    private static void AppendAcronyms(StringBuilder builder, string run)
    {
        var index = 0;
        while (index < run.Length)
        {
            var match = Array.Find(Acronyms, a => string.CompareOrdinal(run, index, a.Acronym, 0, a.Acronym.Length) == 0);
            if (match.Acronym is null)
            {
                builder.Append(run[index++]);
                continue;
            }

            builder.Append(match.Replacement);
            index += match.Acronym.Length;
        }
    }
}
