using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>The patterns of the CommonMark constructs, from the reference implementation (commonmark.js).</summary>
internal static partial class Scanners
{
    private const string TagName = "[A-Za-z][A-Za-z0-9-]*";
    private const string AttributeName = "[a-zA-Z_:][a-zA-Z0-9:._-]*";
    private const string UnquotedValue = "[^\"'=<>`\\x00-\\x20]+";
    private const string SingleQuotedValue = "'[^']*'";
    private const string DoubleQuotedValue = "\"[^\"]*\"";
    private const string AttributeValue = "(?:" + UnquotedValue + "|" + SingleQuotedValue + "|" + DoubleQuotedValue + ")";
    private const string AttributeValueSpec = "(?:[ \\t\\n\\v\\f\\r]*=[ \\t\\n\\v\\f\\r]*" + AttributeValue + ")";
    private const string Attribute = "(?:[ \\t\\n\\v\\f\\r]+" + AttributeName + AttributeValueSpec + "?)";
    private const string OpenTag = "<" + TagName + Attribute + "*[ \\t\\n\\v\\f\\r]*/?>";
    private const string CloseTag = "</" + TagName + "[ \\t\\n\\v\\f\\r]*[>]";
    private const string HtmlComment = "<!-->|<!--->|<!--[\\s\\S]*?-->";
    private const string ProcessingInstruction = "[<][?][\\s\\S]*?[?][>]";
    private const string Declaration = "<![A-Za-z]+[^>]*>";
    private const string CData = "<!\\[CDATA\\[[\\s\\S]*?\\]\\]>";
    private const string HtmlTag = "(?:" + OpenTag + "|" + CloseTag + "|" + HtmlComment + "|" + ProcessingInstruction + "|" + Declaration + "|" + CData + ")";

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture;

    [GeneratedRegex("^" + HtmlTag, Options)]
    public static partial Regex HtmlTagRegex();

    [GeneratedRegex("^&(?:#[xX][a-fA-F0-9]{1,6}|#[0-9]{1,7}|[a-zA-Z][a-zA-Z0-9]{1,31});", Options)]
    public static partial Regex EntityRegex();

    [GeneratedRegex("^<[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*>", Options)]
    public static partial Regex EmailAutolinkRegex();

    // NUL is U+FFFD for CommonMark, which the patterns accept as any other character
    [GeneratedRegex("^<[A-Za-z][A-Za-z0-9.+-]{1,31}:[^<>\\x01-\\x20]*>", Options)]
    public static partial Regex AutolinkRegex();

    [GeneratedRegex("^(?:\"(?:\\\\[\\s\\S]|[^\\\\\"])*\"|'(?:\\\\[\\s\\S]|[^\\\\'])*'|\\((?:\\\\[\\s\\S]|[^\\\\()])*\\))", Options)]
    public static partial Regex LinkTitleRegex();

    [GeneratedRegex("^<(?:[^<>\\n\\\\]|\\\\.)*>", Options)]
    public static partial Regex LinkDestinationBracesRegex();

    // The start conditions of the HTML blocks, by type
    [GeneratedRegex("^<(?:script|pre|textarea|style)(?:[ \\t\\n\\v\\f\\r]|>|$)", Options | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockOpen1();

    [GeneratedRegex("^(?:" + OpenTag + "|" + CloseTag + ")[ \\t\\n\\v\\f\\r]*$", Options | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockOpen7();

    [GeneratedRegex("^</?(?:address|article|aside|base|basefont|blockquote|body|caption|center|col|colgroup|dd|details|dialog|dir|div|dl|dt|fieldset|figcaption|figure|footer|form|frame|frameset|h[123456]|head|header|hr|html|iframe|legend|li|link|main|menu|menuitem|nav|noframes|ol|optgroup|option|p|param|search|section|summary|table|tbody|td|tfoot|th|thead|title|tr|track|ul)(?:[ \\t\\n\\v\\f\\r]|/?>|$)", Options | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockOpen6();

    [GeneratedRegex("</(?:script|pre|textarea|style)>", Options | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockClose1();

    /// <summary>Gets the type (1 to 7) of the HTML block that the line starts, or 0.</summary>
    public static int GetHtmlBlockStart(string line, bool canInterruptParagraph)
    {
        if (HtmlBlockOpen1().IsMatch(line))
        {
            return 1;
        }

        if (line.StartsWith("<!--", StringComparison.Ordinal))
        {
            return 2;
        }

        if (line.StartsWith("<?", StringComparison.Ordinal))
        {
            return 3;
        }

        if (line.Length > 2 && line[1] == '!' && char.IsAsciiLetter(line[2]))
        {
            return 4;
        }

        if (line.StartsWith("<![CDATA[", StringComparison.Ordinal))
        {
            return 5;
        }

        if (HtmlBlockOpen6().IsMatch(line))
        {
            return 6;
        }

        if (!canInterruptParagraph && HtmlBlockOpen7().IsMatch(line))
        {
            return 7;
        }

        return 0;
    }

    /// <summary>Whether the line ends an HTML block of the type, which only the types 1 to 5 have.</summary>
    public static bool IsHtmlBlockEnd(int type, string line) => type switch
    {
        1 => HtmlBlockClose1().IsMatch(line),
        2 => line.Contains("-->", StringComparison.Ordinal),
        3 => line.Contains("?>", StringComparison.Ordinal),
        4 => line.Contains('>', StringComparison.Ordinal),
        5 => line.Contains("]]>", StringComparison.Ordinal),
        _ => false,
    };

    public static bool IsEscapable(char c) => c is '!' or '"' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or '-' or '.' or '/' or ':' or ';' or '<' or '=' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~';

    public static bool IsSpaceOrTab(char c) => c is ' ' or '\t';

    // A Unicode whitespace character: the Zs category, a tab, a line feed, a form feed or a carriage return
    public static bool IsUnicodeWhitespace(Rune rune) => rune.Value is '\t' or '\n' or '\f' or '\r' || Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;

    // A Unicode punctuation character: the categories P (punctuation) and S (symbol)
    public static bool IsUnicodePunctuation(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;

    /// <summary>Normalizes a link label for matching: the whitespace is collapsed and the case is folded.</summary>
    /// <summary>The label of an abbreviation, as Markdig reads it: without its escapes and the whitespace around it, and with one space for each run of whitespace.</summary>
    public static string NormalizeAbbreviationLabel(ReadOnlySpan<char> label)
    {
        var builder = new StringBuilder(label.Length);
        var pendingSpace = false;
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (c is ' ' or '\t' or '\n' or '\r' or '\v' or '\f')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            if (c == '\\' && i + 1 < label.Length && IsEscapable(label[i + 1]))
            {
                i++;
                c = label[i];
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    // The key that matches the labels: the runs of whitespace are one space, and the text is case folded with the Unicode full
    // case folding, as cmark does, so that [ﬀ] matches [FF] and [ẞ] matches [SS]
    public static string NormalizeLabel(string label)
    {
        const string Whitespaces = " \t\r\n";
        var builder = new StringBuilder(label.Length);
        var span = label.AsSpan().Trim(Whitespaces);
        while (true)
        {
            var index = span.IndexOfAny(Whitespaces);
            if (index < 0)
            {
                CaseFolding.AppendFolded(builder, span);
                break;
            }

            CaseFolding.AppendFolded(builder, span[..index]);
            builder.Append(' ');
            span = span[index..].TrimStart(Whitespaces);
        }

        return builder.ToString();
    }
}
