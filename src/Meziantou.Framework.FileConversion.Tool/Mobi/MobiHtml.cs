using System.Text.RegularExpressions;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// Converts the text of a MOBI book to an HTML document that a browser lays out: the images (<c>recindex</c>) get a URL, the
/// links to a position of the text (<c>filepos</c>) get an anchor, and the page breaks (<c>mbp:pagebreak</c>) break the page.
/// </summary>
internal static partial class MobiHtml
{
    public const string BaseUrl = "https://mobi.invalid/";

    public const string DocumentUrl = BaseUrl + "book.html";

    private const string ImagesPath = "images/";

    // How far before a position its anchor can move to the start of the tag it is in
    private const int MaxTagLength = 4096;

    public static string GetImageUrl(int index) => BaseUrl + ImagesPath + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the index of the image of a URL, or null when the URL is not the one of an image.</summary>
    public static int? GetImageIndex(string url)
    {
        if (!url.StartsWith(BaseUrl + ImagesPath, StringComparison.Ordinal))
            return null;

        return int.TryParse(RemoveQueryAndFragment(url.AsSpan((BaseUrl + ImagesPath).Length)), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
    }

    /// <summary>Gets the path of a URL without its query and its fragment.</summary>
    public static ReadOnlySpan<char> RemoveQueryAndFragment(ReadOnlySpan<char> path)
    {
        var end = path.IndexOfAny('?', '#');
        return end >= 0 ? path[..end] : path;
    }

    /// <summary>Gets the id of the anchor of a position of the text.</summary>
    public static string GetAnchorId(int position) => "filepos" + position.ToString(CultureInfo.InvariantCulture);

    /// <summary>Converts the text of a book to HTML, in the same encoding.</summary>
    /// <param name="text">The text of the book.</param>
    /// <param name="isUtf8">Whether the text is UTF-8, whose characters an anchor must not split.</param>
    /// <param name="imageIndexes">The indexes of the images the text shows.</param>
    public static byte[] Convert(ReadOnlySpan<byte> text, bool isUtf8, out IReadOnlySet<int> imageIndexes)
    {
        // Latin-1 maps each byte to one character: the positions of the text are the positions of the string, and the
        // bytes of the text, whatever its encoding, are kept as they are
        var source = Encoding.Latin1.GetString(text);
        var withAnchors = InsertAnchors(source, isUtf8);

        var images = new HashSet<int>();
        var result = TagRegex().Replace(withAnchors, match => ReplaceTag(match, images));
        result = FilePosRegex().Replace(result, match => TryParseNumber(match.Groups["value"].ValueSpan, out var position) ? $"href=\"#{GetAnchorId(position)}\"" : match.Value);

        imageIndexes = images;
        return Encoding.Latin1.GetBytes(result);
    }

    private static string InsertAnchors(string source, bool isUtf8)
    {
        // An anchor in the head would end the head: the anchors of the positions before the body go to its start
        var body = BodyRegex().Match(source);
        var bodyStart = body.Success ? body.Index + body.Length : 0;

        var anchors = new List<(int Position, int Target)>();
        var targets = new HashSet<int>();
        foreach (Match match in FilePosRegex().Matches(source))
        {
            if (TryParseNumber(match.Groups["value"].ValueSpan, out var target) && target <= source.Length && targets.Add(target))
            {
                anchors.Add((Math.Max(bodyStart, GetAnchorPosition(source, target, isUtf8)), target));
            }
        }

        if (anchors.Count is 0)
            return source;

        anchors.Sort();
        var sb = new StringBuilder(source.Length + (anchors.Count * 32));
        var last = 0;
        foreach (var (position, target) in anchors)
        {
            sb.Append(source, last, position - last);
            sb.Append("<a id=\"").Append(GetAnchorId(target)).Append("\"></a>");
            last = position;
        }

        sb.Append(source, last, source.Length - last);
        return sb.ToString();
    }

    /// <summary>Gets where the anchor of a position goes: before the tag the position is in, and not inside a UTF-8 character.</summary>
    private static int GetAnchorPosition(string source, int position, bool isUtf8)
    {
        var searchStart = Math.Max(0, position - MaxTagLength);
        var index = source.AsSpan(searchStart, position - searchStart).LastIndexOfAny('<', '>');
        if (index >= 0 && source[searchStart + index] is '<')
            return searchStart + index;

        if (isUtf8)
        {
            // A continuation byte (10xxxxxx) is inside a character, which has at most 3 of them
            for (var i = 0; i < 3 && position > 0 && position < source.Length && (source[position] & 0xC0) is 0x80; i++)
            {
                position--;
            }
        }

        return position;
    }

    private static string ReplaceTag(Match match, HashSet<int> images)
    {
        var tag = match.Groups["tag"].Value;
        if (tag.Equals("/mbp:pagebreak", StringComparison.OrdinalIgnoreCase))
            return "";

        if (tag.Equals("mbp:pagebreak", StringComparison.OrdinalIgnoreCase))
            return """<div style="break-before: page"></div>""";

        // An image gives its record in recindex, and some books in hirecindex or lorecindex
        int? image = null;
        var priority = int.MaxValue;
        foreach (Match attribute in RecIndexRegex().Matches(match.Groups["attributes"].Value))
        {
            var attributePriority = attribute.Groups["name"].ValueSpan.Length switch
            {
                8 => 0, // recindex
                _ when attribute.Groups["name"].ValueSpan.StartsWith("hi", StringComparison.OrdinalIgnoreCase) => 1,
                _ => 2,
            };

            if (attributePriority < priority && TryParseNumber(attribute.Groups["value"].ValueSpan, out var index))
            {
                image = index;
                priority = attributePriority;
            }
        }

        if (image is not { } imageIndex)
            return match.Value;

        images.Add(imageIndex);
        return match.Value.Insert(1 + tag.Length, $" src=\"{GetImageUrl(imageIndex)}\"");
    }

    private static bool TryParseNumber(ReadOnlySpan<char> value, out int result) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    [GeneratedRegex("""<(?<tag>img|/?mbp:pagebreak)\b(?<attributes>[^<>]*)>?""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex TagRegex();

    [GeneratedRegex("""<body\b[^<>]*>?""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex BodyRegex();

    [GeneratedRegex("""\bfilepos\s*=\s*(?:"\s*(?<value>[0-9]+)\s*"|'\s*(?<value>[0-9]+)\s*'|(?<value>[0-9]+))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex FilePosRegex();

    [GeneratedRegex("""\b(?<name>recindex|hirecindex|lorecindex)\s*=\s*["']?\s*(?<value>[0-9]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex RecIndexRegex();
}
