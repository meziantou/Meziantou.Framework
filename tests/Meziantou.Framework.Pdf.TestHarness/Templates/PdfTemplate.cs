using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Pdf.TestHarness.Templates;

/// <summary>
/// Expands the PDF templates of the PDFium test suite (<c>testing/resources/**/*.in</c>), also used for the hand-authored
/// fixtures of this repository: the text of a file with directives in double braces that compute what depends on byte
/// offsets.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>{{header}}</c>: the header and a comment of binary bytes.</description></item>
/// <item><description><c>{{object N G}}</c>: <c>N G obj</c>, and the offset of the object for the cross-reference table.</description></item>
/// <item><description><c>{{streamlen}}</c>: <c>/Length L</c>, the length of the data of the next stream.</description></item>
/// <item><description><c>{{xref}}</c>: a cross-reference table of the objects written so far.</description></item>
/// <item><description><c>{{xref shift N}}</c> (this repository only): the same table, every offset increased by N.</description></item>
/// <item><description><c>{{xref update}}</c> (this repository only): a cross-reference section of the objects written since the previous one, for an incremental update; <c>{{prev}}</c> writes its <c>/Prev</c> entry.</description></item>
/// <item><description><c>{{trailer}}</c>, <c>{{trailersize}}</c>: a trailer with <c>/Root 1 0 R</c> and <c>/Size</c>, or the <c>/Size</c> entry alone.</description></item>
/// <item><description><c>{{startxref}}</c>: <c>startxref</c> and the offset of the table.</description></item>
/// <item><description><c>{{include path}}</c>: the text of another file, relative to the template.</description></item>
/// </list>
/// The text is read and written as ISO-8859-1: a byte of the template is a byte of the file.
/// </remarks>
public static partial class PdfTemplate
{
    private const int MaxIncludeDepth = 8;

    /// <summary>Expands a template file.</summary>
    /// <param name="path">The path of the template.</param>
    /// <returns>The PDF file.</returns>
    public static byte[] ExpandFile(string path) => Expand(ReadWithIncludes(path, depth: 0), version: "1.7");

    /// <summary>Expands the text of a template that has no include.</summary>
    /// <param name="template">The text.</param>
    /// <param name="version">The version written by <c>{{header}}</c>.</param>
    /// <returns>The PDF file.</returns>
    public static byte[] Expand(string template, string version = "1.7")
    {
        var output = new StringBuilder(template.Length + 1024);
        var offsets = new SortedDictionary<int, (int Generation, int Offset)>();
        var xrefOffset = -1;
        var previousXrefOffset = -1;
        var sinceLastXref = new SortedSet<int>();
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf("{{", position, StringComparison.Ordinal);
            if (start < 0)
            {
                output.Append(template, position, template.Length - position);
                break;
            }

            output.Append(template, position, start - position);
            var end = template.IndexOf("}}", start, StringComparison.Ordinal);
            if (end < 0)
            {
                output.Append(template, start, template.Length - start);
                break;
            }

            var directive = template[(start + 2)..end].Trim();
            if (!DirectiveRegex().IsMatch(directive))
            {
                // Braces of the content (a script of a form), not a directive
                output.Append("{{");
                position = start + 2;
                continue;
            }

            position = end + 2;
            var parts = directive.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "header":
                    output.Append("%PDF-").Append(version).Append("\n%\u00A0\u00F2\u00A4\u00F4");
                    break;
                case "object" when parts.Length >= 3:
                    var number = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    var generation = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    offsets[number] = (generation, output.Length);
                    sinceLastXref.Add(number);
                    output.Append(CultureInfo.InvariantCulture, $"{number} {generation} obj");
                    break;
                case "streamlen":
                    output.Append(CultureInfo.InvariantCulture, $"/Length {GetNextStreamLength(template, position)}");
                    break;
                case "xref" when parts.Length > 1 && parts[1] == "update":
                    previousXrefOffset = xrefOffset;
                    xrefOffset = output.Length;
                    output.Append("xref\n");
                    foreach (var run in GetRuns(sinceLastXref))
                    {
                        output.Append(CultureInfo.InvariantCulture, $"{run.Start} {run.Count}\n");
                        for (var i = run.Start; i < run.Start + run.Count; i++)
                        {
                            output.Append(CultureInfo.InvariantCulture, $"{offsets[i].Offset:D10} {offsets[i].Generation:D5} n \n");
                        }
                    }

                    sinceLastXref.Clear();
                    break;
                case "prev":
                    output.Append(CultureInfo.InvariantCulture, $"/Prev {Math.Max(0, previousXrefOffset)}");
                    break;
                case "xref":
                    // {{xref shift N}} (this repository only) adds N to every offset, to make the table wrong
                    var shift = parts.Length > 2 && parts[1] == "shift" ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
                    previousXrefOffset = xrefOffset;
                    sinceLastXref.Clear();
                    xrefOffset = output.Length;
                    var size = offsets.Count == 0 ? 1 : offsets.Keys.Max() + 1;
                    output.Append(CultureInfo.InvariantCulture, $"xref\n0 {size}\n0000000000 65535 f \n");
                    for (var i = 1; i < size; i++)
                    {
                        output.Append(offsets.TryGetValue(i, out var entry) ? string.Create(CultureInfo.InvariantCulture, $"{entry.Offset + shift:D10} {entry.Generation:D5} n \n") : "0000000000 65535 f \n");
                    }

                    break;
                case "trailer":
                    output.Append(CultureInfo.InvariantCulture, $"trailer <<\n  /Root 1 0 R\n  /Size {GetSize(offsets)}\n>>");
                    break;
                case "trailersize":
                    output.Append(CultureInfo.InvariantCulture, $"/Size {GetSize(offsets)}");
                    break;
                case "startxref":
                    output.Append(CultureInfo.InvariantCulture, $"startxref\n{Math.Max(0, xrefOffset)}");
                    break;
                default:
                    // An unknown directive is kept: the file is then invalid, as the template intends or not
                    output.Append(template, start, end + 2 - start);
                    break;
            }
        }

        return Encoding.Latin1.GetBytes(output.ToString());
    }

    private static int GetSize(SortedDictionary<int, (int Generation, int Offset)> offsets) => offsets.Count == 0 ? 1 : offsets.Keys.Max() + 1;

    private static List<(int Start, int Count)> GetRuns(SortedSet<int> numbers)
    {
        var runs = new List<(int Start, int Count)>();
        foreach (var number in numbers)
        {
            if (runs.Count > 0 && runs[^1].Start + runs[^1].Count == number)
            {
                runs[^1] = (runs[^1].Start, runs[^1].Count + 1);
            }
            else
            {
                runs.Add((number, 1));
            }
        }

        return runs;
    }

    /// <summary>Gets the length of the data of the next stream of the template: between the end of line after <c>stream</c> and the end of line before <c>endstream</c>.</summary>
    private static int GetNextStreamLength(string template, int position)
    {
        var stream = StreamKeywordRegex().Match(template, position);
        if (!stream.Success)
            return 0;

        var dataStart = stream.Index + stream.Length;
        var endstream = template.IndexOf("endstream", dataStart, StringComparison.Ordinal);
        if (endstream < 0)
            return template.Length - dataStart;

        var dataEnd = endstream;
        if (dataEnd > dataStart && template[dataEnd - 1] == '\n')
        {
            dataEnd--;
        }

        if (dataEnd > dataStart && template[dataEnd - 1] == '\r')
        {
            dataEnd--;
        }

        return dataEnd - dataStart;
    }

    private static string ReadWithIncludes(string path, int depth)
    {
        var text = File.ReadAllText(path, Encoding.Latin1);
        if (depth >= MaxIncludeDepth)
            return text;

        var directory = Path.GetDirectoryName(path)!;
        return IncludeRegex().Replace(text, match => ReadWithIncludes(Path.Combine(directory, match.Groups["path"].Value), depth + 1));
    }

    [GeneratedRegex(@"\{\{include (?<path>[^}]+)\}\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IncludeRegex();

    [GeneratedRegex(@"\bstream\r?\n", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StreamKeywordRegex();

    [GeneratedRegex(@"^(header|object \d+ \d+|streamlen|xref|xref shift -?\d+|xref update|prev|trailer|trailersize|startxref)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DirectiveRegex();
}
