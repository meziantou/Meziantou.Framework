using System.Globalization;
using System.Net;
using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Sanitizers.Fuzz;

/// <summary>
/// The output of the sanitizer is read the way a browser tokenizes HTML (WHATWG tokenizer: tags, attributes, comments,
/// character references in attribute values), not with the parser of the library, and must be safe: no element outside
/// the allowed ones, no attribute outside the allowed ones, no URL attribute and no srcset candidate whose decoded URL
/// has a script scheme (javascript:, vbscript:, a data: URL that is not an image, a video or an audio), no comment
/// unless they are allowed, nothing left open at the end of the output (a tag, a quoted value, a comment, a lone
/// <c>&lt;</c>) and no <c>&lt;</c> in text. The output is also stable: sanitizing it again changes nothing, so a browser
/// reads it back as the same elements.
/// </summary>
public static class HtmlSanitizerFuzzTargets
{
    // The elements the configuration of html-sanitizer-config can add to ValidElements (5 bytes of mask): the ones the
    // parser handles in a special way (raw text, foreign content, template, form, select, table, the document elements)
    private static readonly string[] ConfigurableElements =
    [
        "style", "script", "svg", "math", "mi", "mtext", "textarea", "title",
        "noscript", "template", "iframe", "xmp", "noembed", "noframes", "plaintext", "select",
        "option", "optgroup", "form", "input", "button", "object", "embed", "base",
        "meta", "link", "image", "listing", "frameset", "body", "html", "head",
        "details", "summary", "dialog", "marquee", "slot", "custom-element", "foreignobject", "desc",
    ];

    // The attributes the configuration of html-sanitizer-config can add to ValidAttributes, UriAttributes and
    // SrcsetAttributes (2 bytes of mask each): event handlers, style, URL attributes that are not in the defaults, ...
    private static readonly string[] ConfigurableAttributes =
    [
        "onclick", "onerror", "style", "id", "name", "data-url", "data-srcset", "formaction",
        "action", "poster", "is", "srcdoc", "xmlns", "imagesrcset", "href", "srcset",
    ];

    private const int ConfigurationLength = 12;

    // The library removes these elements with their content, or unwraps them, whatever the configuration, so a start tag
    // of one of them in the output is a failure: the tokenizer reads their content with other rules (raw text, foreign
    // content), which the checks of the output do not follow.
    private static readonly HashSet<string> NeverKeptElements = new(["iframe", "noembed", "noframes", "noscript", "plaintext", "script", "style", "textarea", "title", "xmp", "svg", "math"], StringComparer.Ordinal);

    // The HTML5 named character references that decode to ASCII, which WebUtility.HtmlDecode (HTML 4 names) does not know
    private static readonly Dictionary<string, string> AsciiCharacterReferences = new(StringComparer.Ordinal)
    {
        ["Tab"] = "\t", ["NewLine"] = "\n", ["excl"] = "!", ["quot"] = "\"", ["QUOT"] = "\"", ["num"] = "#", ["dollar"] = "$", ["percnt"] = "%", ["amp"] = "&", ["AMP"] = "&",
        ["apos"] = "'", ["lpar"] = "(", ["rpar"] = ")", ["ast"] = "*", ["midast"] = "*", ["plus"] = "+", ["comma"] = ",", ["period"] = ".", ["sol"] = "/", ["colon"] = ":",
        ["semi"] = ";", ["lt"] = "<", ["LT"] = "<", ["equals"] = "=", ["gt"] = ">", ["GT"] = ">", ["quest"] = "?", ["commat"] = "@", ["lsqb"] = "[", ["lbrack"] = "[",
        ["bsol"] = "\\", ["rsqb"] = "]", ["rbrack"] = "]", ["Hat"] = "^", ["lowbar"] = "_", ["UnderBar"] = "_", ["grave"] = "`", ["DiacriticalGrave"] = "`", ["lcub"] = "{",
        ["lbrace"] = "{", ["verbar"] = "|", ["vert"] = "|", ["VerticalLine"] = "|", ["rcub"] = "}", ["rbrace"] = "}",
    };

    private static readonly string[] HtmlTokens =
    [
        "<", ">", "</", "/>", "<!--", "-->", "--!>", "<!", "<?", "<![CDATA[", "]]>", "=", "\"", "'", "`", " ", "\t", "\n", "\r", "\f", "\0", "&", "&amp;", "&lt;", "&gt;", "&quot;", "&#", "&#x", ";",
        "&colon;", "&Tab;", "&NewLine;", "&comma;", "&#58;", "&#x3A;", "&#106;", "&#x6a", "&#44;", "&#9;", "&#x20;",
        "<p>", "</p>", "<a href=", "<img src=", "<img srcset=", " srcset=", " href=", " src=", " xlink:href=", " cite=", " background=", " longdesc=", " class=", " title=", " onclick=", " onerror=", " style=", " aria-label=", " role=",
        "javascript:", "JaVaScRiPt:", "vbscript:", "data:", "data:text/html,", "data:image/png;base64,", "data:image/svg+xml;base64,", "http://", "https:", "mailto:", "//", "#", "?", ",", " 1x", " 2x", " 300w",
        "<script>", "</script>", "<style>", "</style>", "<svg>", "</svg>", "<math>", "<mtext>", "<mglyph>", "<table>", "</table>", "<td>", "<tr>", "<li>", "<form>", "</form>", "<b>", "</b>", "<div>", "</div>",
        "<span>", "<title>", "</title>", "<textarea>", "<noscript>", "</noscript>", "<plaintext>", "<xmp>", "<iframe>", "<noembed>", "<noframes>", "<template>", "<select>", "<option>", "<object>", "<embed>",
        "<base>", "<meta>", "<link>", "<input>", "<button>", "<!DOCTYPE html>", "<?xml version='1.0'?>", "<!--[if IE]>", "<![endif]-->", "</0", "<1", "<%", "<//",
    ];

    private static readonly string[] ConfiguredTokens =
    [
        .. ConfigurableElements.Select(name => $"<{name}>"), .. ConfigurableElements.Select(name => $"</{name}>"), .. ConfigurableAttributes.Select(name => $" {name}="),
        "<frame>", "<param>", " data-x=", " ARIA-x=", " shadowrootmode=open", "<template shadowrootmode=open>",
    ];

    private static readonly string[] UrlTokens =
    [
        "javascript:", "vbscript:", "data:", "data:text/html;base64,", "data:image/png;base64,", "data:image/svg+xml;base64,", "data:video/mp4;base64,", "data:audio/mp3;base64,", "http:", "https://", "mailto:", "ftp:", "tel:", "file:",
        ":", "/", "//", "?", "#", "&", "&#", "&#x", ";", "&colon;", "&Tab;", "&#58;", "&#106;", ",", " ", "\t", "\n", "\r", "\f", "\0", "\u0001", "\u00A0", "\u3000", " 1x", " 2x", " 100w", "(", ")", "=", "+", "iVBORw0KGgo=",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("html-sanitizer", Sanitize) { Seeds = GetHtmlSeeds, Dictionary = HtmlTokens },
        new("html-sanitizer-config", SanitizeWithConfiguration) { Seeds = GetConfiguredHtmlSeeds, Dictionary = [.. HtmlTokens, .. ConfiguredTokens] },
        new("url-sanitizer", SanitizeUrl) { Seeds = GetUrlSeeds, Dictionary = UrlTokens },
    ];

    /// <summary>The input is a byte of options (<c>AllowComments</c>), then the HTML fragment.</summary>
    private static void Sanitize(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var html = FuzzText.Decode(data);
        var sanitizer = new HtmlSanitizer { AllowComments = (flags & 1) != 0 };
        SanitizeAndCheck(sanitizer, html);
    }

    /// <summary>
    /// The input is <see cref="ConfigurationLength"/> bytes that change the configuration (<see cref="Configure"/>),
    /// then the HTML fragment. The checks are the ones of the default configuration, against the sets of the
    /// configuration: an element, an attribute or a URL attribute that the configuration allows is allowed.
    /// </summary>
    private static void SanitizeWithConfiguration(ReadOnlySpan<byte> data)
    {
        var sanitizer = new HtmlSanitizer();
        Configure(sanitizer, ref data);
        var html = FuzzText.Decode(data);
        SanitizeAndCheck(sanitizer, html);
    }

    /// <summary>
    /// Byte 0: bit 0 allows the comments, bit 1 clears <c>BlockedElements</c>, bit 2 adds the <c>data-</c> prefix,
    /// bit 3 clears <c>ValidAttributePrefixes</c>, bit 4 adds <c>srcset</c> to <c>UriAttributes</c>, bit 5 adds
    /// <c>href</c> and <c>src</c> to <c>SrcsetAttributes</c>, bit 6 blocks some default elements, bit 7 removes some
    /// default elements. Bytes 1-5: a mask of <see cref="ConfigurableElements"/> to add to <c>ValidElements</c>.
    /// Bytes 6-7, 8-9 and 10-11: masks of <see cref="ConfigurableAttributes"/> to add to <c>ValidAttributes</c>,
    /// <c>UriAttributes</c> and <c>SrcsetAttributes</c>.
    /// </summary>
    private static void Configure(HtmlSanitizer sanitizer, ref ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        sanitizer.AllowComments = (options & 1) != 0;
        if ((options & 2) != 0)
        {
            sanitizer.BlockedElements.Clear();
        }

        if ((options & 4) != 0)
        {
            sanitizer.ValidAttributePrefixes.Add("data-");
        }

        if ((options & 8) != 0)
        {
            sanitizer.ValidAttributePrefixes.Clear();
        }

        if ((options & 16) != 0)
        {
            sanitizer.UriAttributes.Add("srcset");
        }

        if ((options & 32) != 0)
        {
            sanitizer.SrcsetAttributes.Add("href");
            sanitizer.SrcsetAttributes.Add("src");
        }

        if ((options & 64) != 0)
        {
            sanitizer.BlockedElements.UnionWith(["p", "b", "a", "td"]);
        }

        if ((options & 128) != 0)
        {
            sanitizer.ValidElements.ExceptWith(["img", "a", "table", "li"]);
        }

        for (var i = 0; i < ConfigurableElements.Length; i += 8)
        {
            AddMasked(sanitizer.ValidElements, ConfigurableElements, i, FuzzInput.ReadByte(ref data));
        }

        foreach (var set in (ISet<string>[])[sanitizer.ValidAttributes, sanitizer.UriAttributes, sanitizer.SrcsetAttributes])
        {
            AddMasked(set, ConfigurableAttributes, 0, FuzzInput.ReadByte(ref data));
            AddMasked(set, ConfigurableAttributes, 8, FuzzInput.ReadByte(ref data));
        }

        static void AddMasked(ISet<string> set, string[] names, int start, byte mask)
        {
            for (var bit = 0; bit < 8 && start + bit < names.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    set.Add(names[start + bit]);
                }
            }
        }
    }

    private static void SanitizeAndCheck(HtmlSanitizer sanitizer, string html)
    {
        var sanitized = sanitizer.SanitizeHtmlFragment(html);
        try
        {
            CheckOutput(sanitizer, sanitized);
        }
        catch (FuzzAssertionException ex)
        {
            throw new FuzzAssertionException($"{ex.Message}{Environment.NewLine}Output: {FuzzAssert.Format(sanitized)}", ex);
        }

        var twice = sanitizer.SanitizeHtmlFragment(sanitized);
        FuzzAssert.Equal(sanitized, twice, "Sanitizing the output again changes it");
    }

    /// <summary>
    /// A URL with a script scheme, once the browser has removed the leading and trailing control characters and spaces
    /// and the tabs and newlines, is unsafe, also when it is written with character references (the sanitizer validates
    /// the raw text of the attribute, which the browser decodes). Every candidate of a srcset value is checked the same
    /// way, and a srcset value that is a single URL (no white space, no comma) is safe exactly when the URL is.
    /// </summary>
    private static void SanitizeUrl(ReadOnlySpan<byte> data)
    {
        var url = FuzzText.Decode(data);
        var isSafe = UrlSanitizer.IsSafeUrl(url);
        if (isSafe)
        {
            FuzzAssert.False(IsDangerousUrl(url, out var scheme), $"IsSafeUrl accepts a URL with the scheme '{scheme}'");
            FuzzAssert.False(IsDangerousUrl(DecodeCharacterReferences(url), out scheme), $"IsSafeUrl accepts a URL whose character references decode to the scheme '{scheme}'");
        }

        var isSafeSrcset = UrlSanitizer.IsSafeSrcset(url);
        if (isSafeSrcset)
        {
            foreach (var candidate in GetSrcsetUrls(url))
            {
                FuzzAssert.False(IsDangerousUrl(candidate, out var scheme), $"IsSafeSrcset accepts the candidate {FuzzAssert.Format(candidate)} with the scheme '{scheme}'");
            }
        }

        if (url.Length > 0 && url.AsSpan().IndexOfAny("\t\n\f\r ,") < 0)
        {
            FuzzAssert.Equal(isSafe, isSafeSrcset, "IsSafeSrcset and IsSafeUrl disagree on a single URL");
        }
    }

    /// <summary>Tokenizes the output as a browser does and checks every tag.</summary>
    private static void CheckOutput(HtmlSanitizer sanitizer, string html)
    {
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] != '<')
            {
                index++;
                continue;
            }

            if (index + 1 >= html.Length)
                FuzzAssert.Fail("The output ends with '<': the markup that follows the fragment in the page would start a tag");

            var next = html[index + 1];
            if (char.IsAsciiLetter(next))
            {
                index = ReadTag(sanitizer, html, index + 1, isEndTag: false);
                continue;
            }

            if (next == '/')
            {
                if (index + 2 >= html.Length)
                    FuzzAssert.Fail("The output ends with '</': the markup that follows the fragment in the page would start a tag");

                var afterSlash = html[index + 2];
                if (char.IsAsciiLetter(afterSlash))
                {
                    index = ReadTag(sanitizer, html, index + 2, isEndTag: true);
                    continue;
                }

                // "</>" is dropped by the tokenizer
                if (afterSlash == '>')
                {
                    index += 3;
                    continue;
                }

                FuzzAssert.Fail($"The output has a bogus comment at {index}: {FuzzAssert.Format(Excerpt(html, index))}");
            }

            if (next == '!' && html.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                FuzzAssert.True(sanitizer.AllowComments, $"The output has a comment at {index} although comments are not allowed: {FuzzAssert.Format(Excerpt(html, index))}");
                index = ReadComment(html, index + 4);
                continue;
            }

            if (next is '!' or '?')
                FuzzAssert.Fail($"The output has a bogus comment at {index}: {FuzzAssert.Format(Excerpt(html, index))}");

            FuzzAssert.Fail($"The output has a '<' in text at {index}: {FuzzAssert.Format(Excerpt(html, index))}");
        }

    }

    /// <summary>Reads a tag from its name to its '&gt;' (WHATWG tag name, attribute name and attribute value states) and checks the element and its attributes.</summary>
    private static int ReadTag(HtmlSanitizer sanitizer, string html, int index, bool isEndTag)
    {
        var nameStart = index;
        while (index < html.Length && !IsTagWhiteSpace(html[index]) && html[index] is not '/' and not '>')
        {
            index++;
        }

        if (index >= html.Length)
            FuzzAssert.Fail("The output ends inside a tag name: the markup that follows the fragment in the page would be part of the tag");

        var name = ToAsciiLowercase(html[nameStart..index]);
        if (!isEndTag)
        {
            FuzzAssert.True(sanitizer.ValidElements.Contains(name) && !sanitizer.BlockedElements.Contains(name), $"The output has the element <{name}>, which is not allowed");
            FuzzAssert.False(NeverKeptElements.Contains(name), $"The output has the element <{name}>, whose content the tokenizer does not read as HTML markup");
        }

        while (true)
        {
            while (index < html.Length && IsTagWhiteSpace(html[index]))
            {
                index++;
            }

            if (index >= html.Length)
                FuzzAssert.Fail($"The output ends inside the tag <{name}>: the markup that follows the fragment in the page would be part of the tag");

            if (html[index] == '>')
                return index + 1;

            if (html[index] == '/')
            {
                index++;
                continue;
            }

            // The first character of an attribute name can be '='
            var attributeStart = index;
            index++;
            while (index < html.Length && !IsTagWhiteSpace(html[index]) && html[index] is not '/' and not '>' and not '=')
            {
                index++;
            }

            var attributeName = ToAsciiLowercase(html[attributeStart..index]);
            while (index < html.Length && IsTagWhiteSpace(html[index]))
            {
                index++;
            }

            var value = "";
            if (index < html.Length && html[index] == '=')
            {
                index++;
                while (index < html.Length && IsTagWhiteSpace(html[index]))
                {
                    index++;
                }

                if (index >= html.Length)
                    FuzzAssert.Fail($"The output ends before the value of the attribute '{attributeName}' of <{name}>");

                var quote = html[index];
                if (quote is '"' or '\'')
                {
                    var end = html.IndexOf(quote, index + 1, StringComparison.Ordinal);
                    if (end < 0)
                        FuzzAssert.Fail($"The output ends inside the quoted value of the attribute '{attributeName}' of <{name}>");

                    value = html[(index + 1)..end];
                    index = end + 1;
                }
                else if (quote != '>')
                {
                    var valueStart = index;
                    while (index < html.Length && !IsTagWhiteSpace(html[index]) && html[index] != '>')
                    {
                        index++;
                    }

                    if (index >= html.Length)
                        FuzzAssert.Fail($"The output ends inside the unquoted value of the attribute '{attributeName}' of <{name}>");

                    value = html[valueStart..index];
                }
            }

            if (!isEndTag)
            {
                CheckAttribute(sanitizer, name, attributeName, value);
            }
        }
    }

    private static void CheckAttribute(HtmlSanitizer sanitizer, string element, string name, string value)
    {
        var isAllowed = sanitizer.ValidAttributes.Contains(name) || sanitizer.ValidAttributePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        FuzzAssert.True(isAllowed, $"The output has the attribute '{name}' on <{element}>, which is not allowed");

        var decoded = DecodeCharacterReferences(value);
        if (sanitizer.UriAttributes.Contains(name))
        {
            FuzzAssert.False(IsDangerousUrl(decoded, out var scheme), $"The attribute '{name}' of <{element}> is the URL {FuzzAssert.Format(decoded)} (written {FuzzAssert.Format(value)}) with the scheme '{scheme}'");
        }

        if (sanitizer.SrcsetAttributes.Contains(name))
        {
            foreach (var candidate in GetSrcsetUrls(decoded))
            {
                FuzzAssert.False(IsDangerousUrl(candidate, out var scheme), $"The attribute '{name}' of <{element}> has the candidate {FuzzAssert.Format(candidate)} with the scheme '{scheme}' (written {FuzzAssert.Format(value)})");
            }
        }
    }

    /// <summary>Reads a comment from after its "&lt;!--" to after its end (WHATWG comment states).</summary>
    private static int ReadComment(string html, int index)
    {
        // "<!-->" and "<!--->" are empty comments
        if (index < html.Length && html[index] == '>')
            return index + 1;

        if (index + 1 < html.Length && html[index] == '-' && html[index + 1] == '>')
            return index + 2;

        var end = html.IndexOf("-->", index, StringComparison.Ordinal);
        var bangEnd = html.IndexOf("--!>", index, StringComparison.Ordinal);
        if (end < 0 && bangEnd < 0)
            FuzzAssert.Fail("The output ends inside a comment: the page that follows the fragment would be commented out");

        return end >= 0 && (bangEnd < 0 || end < bangEnd) ? end + 3 : bangEnd + 4;
    }

    private static bool IsTagWhiteSpace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    private static string ToAsciiLowercase(string value) => string.Create(value.Length, value, (span, state) =>
    {
        for (var i = 0; i < state.Length; i++)
        {
            span[i] = char.IsAsciiLetterUpper(state[i]) ? (char)(state[i] + 32) : state[i];
        }
    });

    /// <summary>Decodes the character references of an attribute value: numeric ones (with or without ';'), and named ones that end with ';'.</summary>
    private static string DecodeCharacterReferences(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] != '&')
            {
                builder.Append(value[index]);
                index++;
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == '#')
            {
                var position = index + 2;
                var isHex = position < value.Length && value[position] is 'x' or 'X';
                if (isHex)
                {
                    position++;
                }

                var digitStart = position;
                while (position < value.Length && (isHex ? char.IsAsciiHexDigit(value[position]) : char.IsAsciiDigit(value[position])))
                {
                    position++;
                }

                if (position == digitStart)
                {
                    builder.Append('&');
                    index++;
                    continue;
                }

                var digits = value.AsSpan(digitStart, position - digitStart).TrimStart('0');
                var codePoint = digits.Length > 7 ? int.MaxValue : digits.IsEmpty ? 0 : int.Parse(digits, isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture);
                if (position < value.Length && value[position] == ';')
                {
                    position++;
                }

                builder.Append(codePoint is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? "\uFFFD" : char.ConvertFromUtf32(codePoint));
                index = position;
                continue;
            }

            var end = index + 1;
            while (end < value.Length && char.IsAsciiLetterOrDigit(value[end]))
            {
                end++;
            }

            if (end > index + 1 && end < value.Length && value[end] == ';')
            {
                var name = value[(index + 1)..end];
                var reference = value[index..(end + 1)];
                var decoded = AsciiCharacterReferences.TryGetValue(name, out var ascii) ? ascii : WebUtility.HtmlDecode(reference);
                if (!string.Equals(decoded, reference, StringComparison.Ordinal))
                {
                    builder.Append(decoded);
                    index = end + 1;
                    continue;
                }
            }

            // The references without ';' that a browser decodes in an attribute (&amp, &lt, &nbsp, ...) never give a
            // character that changes the scheme of a URL or splits a srcset value
            builder.Append('&');
            index++;
        }

        return builder.ToString();
    }

    /// <summary>Determines whether a browser loads a URL with a script scheme: javascript:, vbscript:, or a data: URL that is not an image, a video or an audio.</summary>
    private static bool IsDangerousUrl(string url, out string scheme)
    {
        scheme = "";
        var span = url.AsSpan();

        // The URL parser removes the leading and trailing C0 controls and spaces, then every tab and newline
        var start = 0;
        var end = span.Length;
        while (start < end && span[start] <= ' ')
        {
            start++;
        }

        while (end > start && span[end - 1] <= ' ')
        {
            end--;
        }

        var builder = new StringBuilder(end - start);
        foreach (var c in span[start..end])
        {
            if (c is not ('\t' or '\n' or '\r'))
            {
                builder.Append(c);
            }
        }

        var normalized = builder.ToString();
        if (normalized.Length == 0 || !char.IsAsciiLetter(normalized[0]))
            return false;

        var colon = 1;
        while (colon < normalized.Length && (char.IsAsciiLetterOrDigit(normalized[colon]) || normalized[colon] is '+' or '-' or '.'))
        {
            colon++;
        }

        if (colon >= normalized.Length || normalized[colon] != ':')
            return false;

        scheme = ToAsciiLowercase(normalized[..colon]);
        switch (scheme)
        {
            case "javascript" or "vbscript" or "livescript":
                return true;

            case "data":
                var rest = normalized.AsSpan(colon + 1);
                var mimeEnd = rest.IndexOfAny(';', ',');
                var mime = ToAsciiLowercase((mimeEnd < 0 ? rest : rest[..mimeEnd]).Trim(" \t\n\f\r").ToString());
                var isMedia = mime.StartsWith("image/", StringComparison.Ordinal) || mime.StartsWith("video/", StringComparison.Ordinal) || mime.StartsWith("audio/", StringComparison.Ordinal);
                return !isMedia || mime.Contains("svg", StringComparison.Ordinal) || mime.Contains("xml", StringComparison.Ordinal);

            default:
                return false;
        }
    }

    /// <summary>Gets the URLs of the candidates of a srcset value, as the WHATWG algorithm "parse a srcset attribute" splits them.</summary>
    private static List<string> GetSrcsetUrls(string value)
    {
        var result = new List<string>();
        var index = 0;
        while (true)
        {
            while (index < value.Length && (IsTagWhiteSpace(value[index]) || value[index] == ','))
            {
                index++;
            }

            if (index >= value.Length)
                return result;

            var urlStart = index;
            while (index < value.Length && !IsTagWhiteSpace(value[index]))
            {
                index++;
            }

            var url = value[urlStart..index];
            if (url.EndsWith(',', StringComparison.Ordinal))
            {
                result.Add(url.TrimEnd(','));
                continue;
            }

            result.Add(url);

            // The descriptors end at a comma that is not inside parentheses
            var inParentheses = false;
            while (index < value.Length)
            {
                var c = value[index++];
                if (inParentheses)
                {
                    inParentheses = c != ')';
                }
                else if (c == '(')
                {
                    inParentheses = true;
                }
                else if (c == ',')
                {
                    break;
                }
            }
        }
    }

    private static string Excerpt(string html, int index)
    {
        var start = Math.Max(0, index - 40);
        return html[start..Math.Min(html.Length, index + 60)];
    }

    private static IEnumerable<FuzzSeed> GetHtmlSeeds()
    {
        var fragments = GetHtmlFragments();
        for (var i = 0; i < fragments.Length; i++)
        {
            // The comments are allowed in one seed out of two
            yield return FuzzSeed.FromText(i.ToString("D3", CultureInfo.InvariantCulture), [(byte)(i % 2)], fragments[i]);
        }
    }

    /// <summary>The fragments of html-sanitizer and fragments that need a configuration, each with every configuration of <see cref="GetConfigurations"/>.</summary>
    private static IEnumerable<FuzzSeed> GetConfiguredHtmlSeeds()
    {
        string[] configuredFragments =
        [
            "<form><template><form>x</form></template></form>", "<form><object><form>x</form></object></form>", "<select><option>a<template><option>b</option></template></select>",
            "<select><b>x</b><img src=x onerror=alert(1)><option>y</select>", "<template><style><img src=x onerror=alert(1)></style></template>", "<template><template><p>x</p></template></template>",
            "<table><template><tr><td>x</td></tr></template></table>", "<a href='https://a 1x, javascript:alert(1) 2x'>x</a>", "<img src='https://a 1x, javascript:alert(1) 2x'>",
            "<img srcset='https://example.com/a.png 1x, javascript:alert(1) 2x'>", "<img srcset='https://example.com/a.png'>",
            "<p data-srcset='a 1x, javascript:alert(1) 2x' data-url='javascript:alert(1)' formaction='javascript:alert(1)' action='java&#115;cript:alert(1)' poster='data:text/html,x'>x</p>",
            "<p imagesrcset='javascript:alert(1) 1x' srcdoc='<script>alert(1)</script>' xmlns='http://www.w3.org/2000/svg'>x</p>",
            "<p is='x-foo' onclick='alert(1)' onerror='alert(1)' style='background:url(javascript:alert(1))' id='a' name='b'>x</p>", "<button formaction='javascript:alert(1)'>x</button>",
            "<form action='javascript:alert(1)'><input name='a' formaction='javascript:alert(1)'></form>", "<object data='javascript:alert(1)'><embed src='javascript:alert(1)'></object>",
            "<html><head><base href='javascript:alert(1)'><meta name='a'><link href='x'></head><body><p>x</p></body></html>", "<frameset><frame src='javascript:alert(1)'></frameset>",
            "<image src='javascript:alert(1)'>", "<listing>\n\nx</listing>", "<details open><summary>s</summary>d</details><dialog open>x</dialog><marquee>m</marquee>",
            "<svg><foreignObject><p>x</p></foreignObject><desc><b>y</b></desc><style><img src=x onerror=alert(1)></style></svg>", "<math><mtext><mi>x</mi><table><mglyph><style><img src=x onerror=alert(1)>",
            "<custom-element><slot name='a'></slot></custom-element>", "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">", "<p><xmp></p><img src=x onerror=alert(1)></xmp>",
            "<textarea></textarea><img src=x onerror=alert(1)>", "<title><!--</title><img src=x onerror=alert(1)>-->", "<iframe srcdoc='<script>alert(1)</script>'></iframe>",
            "<button>a<template><b><button>b</button></b></template></button>", "<nobr>a<template><nobr>b</nobr></template></nobr>", "<p>a<template><div>b</div><hr><table><tr><td>c</td></tr></table></template></p>",
            "<li>a<template><b><li>b</li></b></template></li>", "<dl><dd>a<template><dt>b</dt></template></dd></dl>", "<select><template><select>a</select><input></template></select>",
            "<select><option>a<template><option>b</option><optgroup>c</optgroup><hr></template></option></select>", "<ruby>a<p><template><rt>b</rt><rp>c</rp></template></p></ruby>",
            "<table><template><tr><td>a</td></tr></template></table>", "<table><tbody><tr><td>a</td><template><b>b</b></template></tr></tbody></table>", "<div><template><tr><td>a</td></tr><col></template></div>",
            "<template><caption><button>a</button></caption><tbody><tr><td>b</td></tr></tbody></template>", "<template><tr><td>a</td></tr></template><template><td>b</td><col></template>",
            "<form><div></form><form>a</form>", "<p>a<table><tr><td>b</td></tr></table></p>",
        ];

        var configurations = GetConfigurations();
        string[] fragments = [.. GetHtmlFragments(), .. configuredFragments];
        for (var i = 0; i < fragments.Length; i++)
        {
            for (var j = 0; j < configurations.Length; j++)
            {
                yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"{i:D3}-{j:D2}"), configurations[j], fragments[i]);
            }
        }
    }

    /// <summary>The configurations of the tests of the sanitizer, and configurations that allow the elements and the attributes the parser or the browser handle in a special way.</summary>
    private static byte[][] GetConfigurations()
    {
        string[] uriAttributes = ["data-url", "formaction", "action", "poster", "srcdoc", "href"];
        string[] srcsetAttributes = ["data-srcset", "imagesrcset", "srcset"];
        return
        [
            Configuration(0),
            Configuration(1, elements: ["template"]),
            Configuration(0, elements: ["svg", "math", "mi"]),
            Configuration(2, elements: ["script", "style", "iframe", "title", "textarea", "noscript", "noembed", "noframes", "xmp", "plaintext"]),
            Configuration(4, uri: ["data-url"]),
            Configuration(0, valid: ["data-url", "is"], uri: ["data-url"]),
            Configuration(0b0001_0000),
            Configuration(0b0010_0000),
            Configuration(0b1100_1000, elements: ["custom-element", "slot"]),
            Configuration(1, elements: ["form", "select", "option", "optgroup", "template", "object", "input", "button"], valid: ["name", "id", "formaction", "action"], uri: ["formaction", "action"]),
            Configuration(0b0011_0111, elements: ConfigurableElements, valid: ConfigurableAttributes, uri: uriAttributes, srcset: srcsetAttributes),
            Configuration(0b0000_0011, elements: ConfigurableElements, valid: [.. uriAttributes, .. srcsetAttributes], uri: [.. uriAttributes, .. srcsetAttributes], srcset: [.. uriAttributes, .. srcsetAttributes]),
        ];

        static byte[] Configuration(byte options, string[]? elements = null, string[]? valid = null, string[]? uri = null, string[]? srcset = null)
        {
            var result = new byte[ConfigurationLength];
            result[0] = options;
            SetMask(result.AsSpan(1, 5), ConfigurableElements, elements);
            SetMask(result.AsSpan(6, 2), ConfigurableAttributes, valid);
            SetMask(result.AsSpan(8, 2), ConfigurableAttributes, uri);
            SetMask(result.AsSpan(10, 2), ConfigurableAttributes, srcset);
            return result;
        }

        static void SetMask(Span<byte> mask, string[] names, string[]? selected)
        {
            foreach (var name in selected ?? [])
            {
                var index = Array.IndexOf(names, name);
                if (index < 0)
                    throw new ArgumentException($"'{name}' cannot be configured", nameof(selected));

                mask[index / 8] |= (byte)(1 << (index % 8));
            }
        }
    }

    private static string[] GetHtmlFragments()
    {
        string[] fragments =
        [
            "", "test", "<p>test</p>", "<strong>test</strong>", "<p id='test' id='test2'>test</p>", "<p style='color:red'>test</p>", "<div><script></script>test</div>",
            "<a href='javascript:alert(\"toto\")'>test</a>", "<a href='https://example.com'>test</a>",
            "<img srcset='javascript:alert() 300w, https://example.com 600w'>", "<img srcset='https://example.com 300w, https://example.com 600w'>",
            "<![CDATA[a><script>alert(1)</script>]]>", "<p>a<![CDATA[><img src=x onerror=alert(1)>]]>b</p>", "<![CDATA[a & b]]>",
            "<html><body><p>test</p></body></html>", "<unknown-element>test</unknown-element>", "<p><object data='x'><b>keep</b></object></p>", "<form><form><form>test</form></form></form>",
            "<button onclick='alert(1)'>test</button>", "<form><a href='javascript:alert(1)'>test</a></form>", "<!DOCTYPE html><p>test</p>", "<?xml version='1.0'?><p>test</p>",
            "<?php echo '<script>alert(1)</script>'; ?><p>test</p>", "<SCRIPT>alert(1)</SCRIPT>", "<style>body{color:red}</style>",
            "<title><img src=x onerror=alert(1)></title>", "<textarea><img src=x onerror=alert(1)></textarea>", "<xmp><img src=x onerror=alert(1)></xmp>", "<iframe><img src=x onerror=alert(1)></iframe>",
            "<noembed><img src=x onerror=alert(1)></noembed>", "<noframes><img src=x onerror=alert(1)></noframes>", "<noscript><img src=x onerror=alert(1)></noscript>",
            "<p xxx:onclick='alert(1)'>x</p>", "<p xmlns:x='u' x:onclick='alert(1)'>y</p>", "<p ONCLICK='alert(1)' class='c'>x</p>", "<a href=x onclick=alert(1)>x</a>", "<p/onclick=alert(1)>x</p>",
            "<a xlink:href='javascript:alert(1)'>x</a>", "<a href='https://ok' href='javascript:alert(1)'>x</a>", "<a href>x</a>", "<p role='note' aria-label='a' ARIA-HIDDEN='true'>x</p>",
            "<p hidden translate='no'><time datetime='2026-01-01'>x</time></p>", "<p title='a\" onmouseover=\"alert(1)'>x</p>", "<p title=a\"onmouseover=alert(1)\"x>y</p>", "<p title=a\"b'c>x</p>",
            "<p title=\"</p><script>alert(1)</script>\">x</p>", "<p title=&quot;onmouseover=alert(1)&quot;>x</p>", "<a href='http://example.com/?a=1&amp;b=2'>x</a>",
            "&lt;script&gt;alert(1)&lt;/script&gt;", "<p>&#x3c;script&#x3e;alert(1)&#x3c;/script&#x3e;</p>", "a & b", "<p>a < b</p>", "<b><!--", "<p>a<!--b", "<b><!x", "<b><?x", "<b></0", "<b><",
            "<p>a</0 onerror=alert(1)>b</p>", "<p>a<1 onerror=alert(1)>b</p>", "<p>a<%img src=x onerror=alert(1)>b</p>", "<p>a<//script>b</p>", "-->", "--><img src=x onerror=alert(1)>",
            "<p>a</b <img src=x onerror=alert(1)>b</p>", "0\"?</<s->s\"\" 7v1v", "<td>]]></style></title><?x", "<table><!--", "<p><!x]]><!--<style>", "<svg><table></0<!--",
            "<a href='javascript:alert(1)'></td><!--<script>>", "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">", "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
            "<!-- comment -->", "<!--[if gte IE 4]><script>alert(1)</script><![endif]-->", "<p><!--a--!>b--></p>", "<svg><script>alert(1)</script></svg>", "<svg onload='alert(1)'></svg>",
            "<template><script>alert(1)</script></template>", "<base href='javascript:alert(1)'>", "<meta http-equiv='refresh' content='0;url=javascript:alert(1)'>",
            "<link rel='stylesheet' href='javascript:alert(1)'>", "<embed src='javascript:alert(1)'>", "<input onfocus='alert(1)' autofocus>", "<p><plaintext><img src=x onerror=alert(1)>", "<plaintext>a<b>c",
            "<img srcset='data:image/png;base64,iVBORw0KGgo= 1x'>", "<img srcset='https://example.com/a.png 1x, javascript:alert(1) 2x'>", "<img srcset=''>",
            "<a href=' javascript:alert(1)'>x</a>", "<a href='java&#x0A;script:alert(1)'>x</a>", "<a href='&#106;avascript:alert(1)'>x</a>", "<a href='javascript&colon;alert(1)'>x</a>",
            "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>", "<img src=\"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=\">",
            "<div><ul><li>a<li>b</ul><ol><li>c</ol><dl><dt>t<dd>d</dl><blockquote cite='https://x'>q</blockquote><pre>p</pre></div>",
            "<table><caption>c</caption><colgroup><col></colgroup><thead><tr><th>h</th></tr></thead><tbody><tr><td>d</td></tr></tbody><tfoot><tr><td>f</td></tr></tfoot></table>",
            "<ruby>a<rp>(</rp><rt>b</rt><rp>)</rp></ruby><bdo dir='rtl'>x</bdo><abbr title='t'>a</abbr><q cite='/x'>q</q><del datetime='2020'>d</del><ins>i</ins><map name='m'><area href='/a'></map>",
            "<div>\u00E9\u4E2D\U0001F600 \uD800 <b title='\uDC00'>x</b></div>",
        ];

        return fragments;
    }

    private static IEnumerable<FuzzSeed> GetUrlSeeds()
    {
        string[] urls =
        [
            "", "https://example.com", "HTTP://example.com", "http://example.com/?a=1&b=2", "ftp://example.com", "mailto:user@example.com", "tel:+33123456789", "file:///tmp/test.txt",
            "//example.com", "/path/to/page", "relative/path", "#fragment", "?query=1", "#javascript:alert(1)", "?javascript:alert(1)", "/javascript:alert(1)",
            "data:image/png;base64,iVBORw0KGgo=", "data:image/gif;base64,R0lGOD==", "data:video/mp4;base64,AAAA", "data:audio/mp3;base64,AAAA",
            "javascript:alert(1)", "JaVaScRiPt:alert(1)", " javascript:alert(1)", "\tjavascript:alert(1)", "\njavascript:alert(1)", "java\tscript:alert(1)", "java\0script:alert(1)", "vbscript:msgbox(1)",
            "&#106;avascript:alert(1)", "&#x6a;avascript:alert(1)", "java&#9;script:alert(1)", "&Tab;javascript:alert(1)", "javascript&colon;alert(1)",
            "data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==", "data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=", "data:image/png,<svg onload=alert(1)>", "data:application/javascript;base64,YWxlcnQoMSk=",
            "   ", ",", ",,,", "https://example.com/a.png 1x", "https://example.com/a.png 300w, https://example.com/b.png 600w", "https://example.com/a.png 1x,", "https://example.com/a.png,https://example.com/b.png",
            " , https://example.com/a.png 1x , https://example.com/b.png 2x , ", "/a.png 1x, /b.png 2x", "data:image/png;base64,iVBORw0KGgo= 1x, data:image/gif;base64,R0lGOD== 2x", "a,b.png", "a,b.png 1x, c,d.png 2x",
            "javascript:alert(1) 1x", ",javascript:alert(1)", " , javascript:alert(1) 1x", "https://example.com/a.png 1x, javascript:alert(1) 2x", "https://example.com/a.png 1x,javascript:alert(1)",
            "data:text/html;base64,PHNjcmlwdD4= 1x", "a,javascript:alert(1)", "a.png 1x(, javascript:alert(1)) 2x", "a.png 1x&#44;javascript:alert(1)", "\u0001javascript:alert(1)\u0001", "\u3000javascript:alert(1)",
        ];

        for (var i = 0; i < urls.Length; i++)
        {
            yield return FuzzSeed.FromText(i.ToString("D3", CultureInfo.InvariantCulture), urls[i]);
        }
    }
}
