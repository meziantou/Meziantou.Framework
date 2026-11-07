using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Html.Parsing;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.WebVtt;

/// <summary>The concrete classes of the WebVTT node objects (https://w3c.github.io/webvtt/#webvtt-node-object).</summary>
internal enum WebVttNodeKind
{
    /// <summary>A list of WebVTT node objects: the root of the tree of a cue.</summary>
    List,
    Class,
    Italic,
    Bold,
    Underline,
    Ruby,
    RubyText,
    Voice,
    Language,

    /// <summary>A WebVTT text object: a leaf.</summary>
    Text,

    /// <summary>A WebVTT timestamp object: a leaf.</summary>
    Timestamp,
}

/// <summary>
/// A WebVTT node object (https://w3c.github.io/webvtt/#webvtt-node-object): a component of the cue text. The internal node
/// objects have children, applicable classes and an applicable language; the leaf node objects have a value.
/// </summary>
internal sealed class WebVttNode(WebVttNodeKind kind)
{
    public WebVttNodeKind Kind { get; } = kind;

    public WebVttNode? Parent { get; private set; }

    public List<WebVttNode> Children { get; } = [];

    /// <summary>The applicable classes of an internal node object.</summary>
    public IReadOnlyList<string> Classes { get; init; } = [];

    /// <summary>The applicable language of an internal node object, or null.</summary>
    public string? Language { get; set; }

    /// <summary>The name of the voice of a WebVTT voice object, or the text of a WebVTT text object.</summary>
    public string Value { get; init; } = "";

    /// <summary>The time of a WebVTT timestamp object, in seconds.</summary>
    public double Time { get; init; }

    public bool IsLeaf => Kind is WebVttNodeKind.Text or WebVttNodeKind.Timestamp;

    public void Append(WebVttNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>
    /// The name of the node object for the type selectors of <c>::cue()</c> (https://w3c.github.io/webvtt/#the-cue-pseudo-element)
    /// and the local name of its HTML element (https://w3c.github.io/webvtt/#dom-construction-rules); the lists of node
    /// objects have no name.
    /// </summary>
    public string? SelectorName => Kind switch
    {
        WebVttNodeKind.Class => "c",
        WebVttNodeKind.Italic => "i",
        WebVttNodeKind.Bold => "b",
        WebVttNodeKind.Underline => "u",
        WebVttNodeKind.Ruby => "ruby",
        WebVttNodeKind.RubyText => "rt",
        WebVttNodeKind.Voice => "v",
        WebVttNodeKind.Language => "lang",
        _ => null,
    };

    /// <summary>
    /// The WebVTT cue text DOM construction rules (https://w3c.github.io/webvtt/#dom-construction-rules): a tree of DOM nodes
    /// that is isomorphous to the tree of WebVTT node objects.
    /// </summary>
    public DocumentFragment ToDocumentFragment(Document document)
    {
        var fragment = document.CreateDocumentFragment();
        AppendChildrenTo(fragment, document);
        return fragment;
    }

    private void AppendChildrenTo(Node parent, Document document)
    {
        foreach (var child in Children)
        {
            switch (child.Kind)
            {
                case WebVttNodeKind.Text:
                    parent.AppendChild(document.CreateTextNode(child.Value));
                    continue;

                case WebVttNodeKind.Timestamp:
                    parent.AppendChild(new ProcessingInstruction(document, "timestamp", FormatTimestamp(child.Time), validate: false));
                    continue;
            }

            var localName = child.Kind switch
            {
                WebVttNodeKind.Italic => "i",
                WebVttNodeKind.Bold => "b",
                WebVttNodeKind.Underline => "u",
                WebVttNodeKind.Ruby => "ruby",
                WebVttNodeKind.RubyText => "rt",
                _ => "span",
            };

            var element = ElementFactory.CreateElement(document, localName, Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
            if (child.Kind == WebVttNodeKind.Voice)
            {
                element.SetAttribute("title", child.Value);
            }
            else if (child.Kind == WebVttNodeKind.Language)
            {
                element.SetAttribute("lang", child.Language ?? "");
            }

            if (child.Classes.Count > 0)
            {
                element.SetAttribute("class", string.Join(' ', child.Classes));
            }

            parent.AppendChild(element);
            child.AppendChildrenTo(element, document);
        }
    }

    /// <summary>
    /// A WebVTT timestamp with all the optional components included, with one leading zero when the hours component is less
    /// than ten, and with no leading zeros otherwise (https://w3c.github.io/webvtt/#webvtt-timestamp).
    /// </summary>
    internal static string FormatTimestamp(double seconds)
    {
        var milliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        var hours = milliseconds / 3_600_000;
        var minutes = milliseconds / 60_000 % 60;
        var wholeSeconds = milliseconds / 1000 % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{wholeSeconds:00}.{milliseconds % 1000:000}");
    }
}

/// <summary>The WebVTT cue text parsing rules (https://w3c.github.io/webvtt/#cue-text-parsing-rules).</summary>
internal static class WebVttCueTextParser
{
    private enum TokenKind
    {
        String,
        StartTag,
        EndTag,
        TimestampTag,
    }

    private enum TokenizerState
    {
        Data,
        Tag,
        StartTag,
        StartTagClass,
        StartTagAnnotation,
        EndTag,
        TimestampTag,
    }

    /// <summary>Parses the cue text into a list of WebVTT node objects, with an optional fallback language.</summary>
    public static WebVttNode Parse(string input, string? language)
    {
        var result = new WebVttNode(WebVttNodeKind.List);
        var current = result;
        var languageStack = new List<string>();
        if (language is not null)
        {
            result.Language = language;
            languageStack.Add(language);
        }

        // As in the browsers, an empty cue text is an empty WebVTT text object (the specification returns an empty list)
        if (input.Length == 0)
        {
            result.Append(new WebVttNode(WebVttNodeKind.Text));
        }

        var position = 0;
        while (position < input.Length)
        {
            var kind = Tokenize(input, ref position, out var value, out var classes, out var annotation);
            switch (kind)
            {
                case TokenKind.String:
                    current.Append(new WebVttNode(WebVttNodeKind.Text) { Value = value });
                    break;

                case TokenKind.StartTag:
                {
                    WebVttNodeKind? nodeKind = value switch
                    {
                        "c" => WebVttNodeKind.Class,
                        "i" => WebVttNodeKind.Italic,
                        "b" => WebVttNodeKind.Bold,
                        "u" => WebVttNodeKind.Underline,
                        "ruby" => WebVttNodeKind.Ruby,
                        "rt" when current.Kind == WebVttNodeKind.Ruby => WebVttNodeKind.RubyText,
                        "v" => WebVttNodeKind.Voice,
                        "lang" => WebVttNodeKind.Language,
                        _ => null,
                    };

                    if (nodeKind is not { } internalKind)
                        break;

                    if (internalKind == WebVttNodeKind.Language)
                    {
                        languageStack.Add(annotation ?? "");
                    }

                    // The classes that are the empty string are excluded
                    var node = new WebVttNode(internalKind)
                    {
                        Classes = classes is null ? [] : classes.FindAll(static name => name.Length > 0),
                        Language = languageStack.Count > 0 ? languageStack[^1] : null,
                        Value = internalKind == WebVttNodeKind.Voice ? annotation ?? "" : "",
                    };

                    current.Append(node);
                    current = node;
                    break;
                }

                case TokenKind.EndTag:
                    if ((value == "c" && current.Kind == WebVttNodeKind.Class)
                        || (value == "i" && current.Kind == WebVttNodeKind.Italic)
                        || (value == "b" && current.Kind == WebVttNodeKind.Bold)
                        || (value == "u" && current.Kind == WebVttNodeKind.Underline)
                        || (value == "ruby" && current.Kind == WebVttNodeKind.Ruby)
                        || (value == "rt" && current.Kind == WebVttNodeKind.RubyText)
                        || (value == "v" && current.Kind == WebVttNodeKind.Voice))
                    {
                        current = current.Parent!;
                    }
                    else if (value == "lang" && current.Kind == WebVttNodeKind.Language)
                    {
                        current = current.Parent!;
                        languageStack.RemoveAt(languageStack.Count - 1);
                    }
                    else if (value == "ruby" && current.Kind == WebVttNodeKind.RubyText)
                    {
                        current = current.Parent!.Parent!;
                    }

                    break;

                case TokenKind.TimestampTag:
                {
                    var timestampPosition = 0;
                    if (WebVttParser.CollectTimestamp(value, ref timestampPosition) is { } time && timestampPosition == value.Length)
                    {
                        current.Append(new WebVttNode(WebVttNodeKind.Timestamp) { Time = time });
                    }

                    break;
                }
            }
        }

        return result;
    }

    /// <summary>The WebVTT cue text tokenizer (https://w3c.github.io/webvtt/#webvtt-cue-text-tokenizer).</summary>
    private static TokenKind Tokenize(string input, ref int position, out string value, out List<string>? classes, out string? annotation)
    {
        var state = TokenizerState.Data;
        var result = new StringBuilder();
        var buffer = new StringBuilder();
        classes = null;
        annotation = null;
        while (true)
        {
            // -1 is the end-of-file marker
            var c = position < input.Length ? input[position] : -1;
            switch (state)
            {
                case TokenizerState.Data:
                    if (c == '&')
                    {
                        position++;
                        result.Append(ConsumeCharacterReference(input, ref position) ?? "&");
                        continue;
                    }

                    if (c == '<')
                    {
                        if (result.Length == 0)
                        {
                            state = TokenizerState.Tag;
                            break;
                        }

                        value = result.ToString();
                        return TokenKind.String;
                    }

                    if (c < 0)
                    {
                        value = result.ToString();
                        return TokenKind.String;
                    }

                    result.Append((char)c);
                    break;

                case TokenizerState.Tag:
                    switch (c)
                    {
                        case '\t' or '\n' or '\f' or ' ':
                            state = TokenizerState.StartTagAnnotation;
                            break;

                        case '.':
                            state = TokenizerState.StartTagClass;
                            break;

                        case '/':
                            state = TokenizerState.EndTag;
                            break;

                        case >= '0' and <= '9':
                            result.Append((char)c);
                            state = TokenizerState.TimestampTag;
                            break;

                        case '>' or < 0:
                            if (c == '>')
                            {
                                position++;
                            }

                            value = "";
                            return TokenKind.StartTag;

                        default:
                            result.Append((char)c);
                            state = TokenizerState.StartTag;
                            break;
                    }

                    break;

                case TokenizerState.StartTag:
                    switch (c)
                    {
                        case '\t' or '\f' or ' ':
                            state = TokenizerState.StartTagAnnotation;
                            break;

                        case '\n':
                            buffer.Append((char)c);
                            state = TokenizerState.StartTagAnnotation;
                            break;

                        case '.':
                            state = TokenizerState.StartTagClass;
                            break;

                        case '>' or < 0:
                            if (c == '>')
                            {
                                position++;
                            }

                            value = result.ToString();
                            return TokenKind.StartTag;

                        default:
                            result.Append((char)c);
                            break;
                    }

                    break;

                case TokenizerState.StartTagClass:
                    switch (c)
                    {
                        case '\t' or '\f' or ' ':
                            (classes ??= []).Add(buffer.ToString());
                            buffer.Clear();
                            state = TokenizerState.StartTagAnnotation;
                            break;

                        case '\n':
                            (classes ??= []).Add(buffer.ToString());
                            buffer.Clear();
                            buffer.Append((char)c);
                            state = TokenizerState.StartTagAnnotation;
                            break;

                        case '.':
                            (classes ??= []).Add(buffer.ToString());
                            buffer.Clear();
                            break;

                        case '>' or < 0:
                            if (c == '>')
                            {
                                position++;
                            }

                            (classes ??= []).Add(buffer.ToString());
                            value = result.ToString();
                            return TokenKind.StartTag;

                        default:
                            buffer.Append((char)c);
                            break;
                    }

                    break;

                case TokenizerState.StartTagAnnotation:
                    switch (c)
                    {
                        case '&':
                            position++;
                            buffer.Append(ConsumeCharacterReference(input, ref position) ?? "&");
                            continue;

                        case '>' or < 0:
                            if (c == '>')
                            {
                                position++;
                            }

                            // The leading and trailing ASCII whitespace is removed, and the sequences of ASCII whitespace
                            // are replaced by a single U+0020 SPACE character
                            annotation = InfraString.StripAndCollapseAsciiWhitespace(buffer.ToString());
                            value = result.ToString();
                            return TokenKind.StartTag;

                        default:
                            buffer.Append((char)c);
                            break;
                    }

                    break;

                case TokenizerState.EndTag:
                    if (c is '>' or < 0)
                    {
                        if (c == '>')
                        {
                            position++;
                        }

                        value = result.ToString();
                        return TokenKind.EndTag;
                    }

                    result.Append((char)c);
                    break;

                case TokenizerState.TimestampTag:
                    if (c is '>' or < 0)
                    {
                        if (c == '>')
                        {
                            position++;
                        }

                        value = result.ToString();
                        return TokenKind.TimestampTag;
                    }

                    result.Append((char)c);
                    break;
            }

            position++;
        }
    }

    /// <summary>
    /// Attempts to consume an HTML character reference (https://html.spec.whatwg.org/multipage/parsing.html#character-reference-state),
    /// not as part of an attribute: <paramref name="position"/> is the position of the character that follows the ampersand.
    /// Returns the characters of the reference and moves the position after it, or returns null (nothing is consumed).
    /// </summary>
    internal static string? ConsumeCharacterReference(string input, ref int position)
    {
        if (position >= input.Length)
            return null;

        if (input[position] == '#')
        {
            var index = position + 1;
            var radix = 10;
            if (index < input.Length && input[index] is 'x' or 'X')
            {
                radix = 16;
                index++;
            }

            var start = index;
            long code = 0;
            while (index < input.Length && (radix == 16 ? char.IsAsciiHexDigit(input[index]) : char.IsAsciiDigit(input[index])))
            {
                // Any value above 0x10FFFF is handled the same way
                if (code <= 0x10FFFF)
                {
                    code = (code * radix) + Convert.ToInt32(input[index].ToString(), 16);
                }

                index++;
            }

            if (index == start)
                return null;

            if (index < input.Length && input[index] == ';')
            {
                index++;
            }

            position = index;
            return GetNumericCharacterReference(code);
        }

        if (!char.IsAsciiLetterOrDigit(input[position]))
            return null;

        // The longest name of the named character references that matches
        var node = 0;
        var matchedLength = 0;
        var matchedNode = -1;
        var firstChildren = NamedCharacterReferences.NodeFirstChild;
        var childCounts = NamedCharacterReferences.NodeChildCount;
        var characters = NamedCharacterReferences.NodeCharacter;
        for (var i = position; i < input.Length && i - position < NamedCharacterReferences.LongestName; i++)
        {
            var c = input[i];
            var firstChild = firstChildren[node];
            var childCount = childCounts[node];
            var next = -1;
            for (var child = firstChild; child < firstChild + childCount; child++)
            {
                if (characters[child] == c)
                {
                    next = child;
                    break;
                }

                if (characters[child] > c)
                    break;
            }

            if (next < 0)
                break;

            node = next;
            if (NamedCharacterReferences.NodeValueLength[node] > 0)
            {
                matchedNode = node;
                matchedLength = i - position + 1;
            }
        }

        if (matchedNode < 0)
            return null;

        position += matchedLength;
        return NamedCharacterReferences.Values.Substring(NamedCharacterReferences.NodeValueOffset[matchedNode], NamedCharacterReferences.NodeValueLength[matchedNode]);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#numeric-character-reference-end-state</summary>
    private static string GetNumericCharacterReference(long code)
    {
        if (code == 0 || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
            return "\U0000FFFD";

        code = code switch
        {
            0x80 => 0x20AC,
            0x82 => 0x201A,
            0x83 => 0x0192,
            0x84 => 0x201E,
            0x85 => 0x2026,
            0x86 => 0x2020,
            0x87 => 0x2021,
            0x88 => 0x02C6,
            0x89 => 0x2030,
            0x8A => 0x0160,
            0x8B => 0x2039,
            0x8C => 0x0152,
            0x8E => 0x017D,
            0x91 => 0x2018,
            0x92 => 0x2019,
            0x93 => 0x201C,
            0x94 => 0x201D,
            0x95 => 0x2022,
            0x96 => 0x2013,
            0x97 => 0x2014,
            0x98 => 0x02DC,
            0x99 => 0x2122,
            0x9A => 0x0161,
            0x9B => 0x203A,
            0x9C => 0x0153,
            0x9E => 0x017E,
            0x9F => 0x0178,
            _ => code,
        };

        return char.ConvertFromUtf32((int)code);
    }
}
