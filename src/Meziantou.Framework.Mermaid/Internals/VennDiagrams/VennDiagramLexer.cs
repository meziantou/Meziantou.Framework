namespace Meziantou.Framework.Mermaid.Internals.VennDiagrams;

/// <summary>
/// The lexer of the Venn diagrams of Mermaid (<c>venn.jison</c>): its rules are tried in order and the first that matches
/// wins, as in jison. A line feed starts the <c>bol</c> state, where an indented <c>text</c> after a <c>set</c> or a
/// <c>union</c> is an indented text of that area. Before lexing, the text is encoded as Mermaid does: in a line with
/// <c>style</c>, a colon and a <c>#</c>, the last <c>;</c> is removed (the same for <c>classDef</c>), and the entity codes
/// (<c>#quot;</c>) are opaque, so they only appear in the strings, the labels and the title.
/// </summary>
internal sealed class VennDiagramLexer
{
    private readonly ParseContext _context;

    // The text being lexed: the text of the context without the removed semicolons, a line feed added at the end
    private readonly char[] _text;

    // Whether a character is the '#' or the ';' of an entity code, which no rule matches outside of the strings
    private readonly bool[] _entity;

    // The offset in the text of the context of each character, and of the end
    private readonly int[] _offsets;
    private int _position;
    private bool _bol;
    private bool _consumeIndentText;

    public VennDiagramLexer(ParseContext context, int position)
    {
        _context = context;
        var text = context.Text;
        var removed = GetRemovedCharacters(text);
        var kept = new List<int>(text.Length + 1);
        for (var i = 0; i < text.Length; i++)
        {
            if (!removed.Contains(i))
            {
                kept.Add(i);
            }
        }

        _text = new char[kept.Count + 1];
        _offsets = new int[kept.Count + 2];
        _entity = new bool[kept.Count + 1];
        for (var i = 0; i < kept.Count; i++)
        {
            _text[i] = text[kept[i]];
            _offsets[i] = kept[i];
            if (kept[i] < position)
            {
                _position = i + 1;
            }
        }

        // Mermaid adds a line feed at the end of the text
        _text[^1] = '\n';
        _offsets[kept.Count] = text.Length;
        _offsets[kept.Count + 1] = text.Length;
        MarkEntities();
    }

    /// <summary>Gets or sets a value indicating whether an indented <c>text</c> is a text of the last area (set by the <c>set</c> and <c>union</c> statements).</summary>
    public bool IndentMode { get; set; }

    /// <summary>Returns the offset, in the text of the context, of the end of the text.</summary>
    public int EndOffset => _context.Text.Length;

    /// <summary>Indicates whether Mermaid would change a line before parsing it: the last <c>;</c> of a line with <c>style</c> (or <c>classDef</c>), a colon, and a <c>#</c> after the colon.</summary>
    public static bool IsAlteredByEncoding(string line) => FindRemovedSemicolon(line, "style") >= 0 || FindRemovedSemicolon(line, "classDef") >= 0;

    /// <summary>Indicates whether a character is white space for JavaScript (<c>\s</c>).</summary>
    public static bool IsWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';

    /// <summary>Removes the white space of JavaScript at both ends of a text, as <c>String.prototype.trim</c>.</summary>
    public static string Trim(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    /// <summary>Indicates whether a character is a word character for JavaScript (<c>\w</c>).</summary>
    public static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    public MermaidParseException Error(int offset, string message) => _context.Error(offset, message);

    /// <summary>Returns the next token.</summary>
    public VennToken Next()
    {
        var text = _text;
        while (true)
        {
            var p = _position;
            if (p >= text.Length)
                return new VennToken(VennTokenKind.EndOfFile, "", EndOffset);

            // %%(?!\{)[^\n]* and [^\}]%%[^\n]*: comments
            if (IsAt(p, '%') && IsAt(p + 1, '%') && !IsAt(p + 2, '{'))
            {
                _position = SkipToLineEnd(p);
                continue;
            }

            if (p + 2 < text.Length && text[p] != '}' && IsAt(p + 1, '%') && IsAt(p + 2, '%'))
            {
                _position = SkipToLineEnd(p + 1);
                continue;
            }

            if (_bol)
            {
                var end = p;
                while (end < text.Length && text[end] is ' ' or '\t')
                {
                    end++;
                }

                if (end > p)
                {
                    if (end < text.Length && text[end] is '\n' or '\r')
                    {
                        // A line of white space
                        _position = end;
                        continue;
                    }

                    if (MatchesKeyword(end, "text") && IndentMode)
                    {
                        _consumeIndentText = true;
                        _bol = false;
                        _position = end;
                        return Token(VennTokenKind.IndentText, p, end);
                    }

                    _position = end;
                    continue;
                }

                if (text[p] is not ('\n' or '\r'))
                {
                    // The first character of a line ends the indented texts
                    IndentMode = false;
                    _bol = false;
                }
            }

            var c = text[p];
            if (c is '\n' or '\r')
            {
                var end = p;
                while (end < text.Length && text[end] is '\n' or '\r')
                {
                    end++;
                }

                _bol = true;
                _position = end;
                return Token(VennTokenKind.Newline, p, end);
            }

            if (IsAt(p, '%') && IsAt(p + 1, '%'))
            {
                _position = SkipToLineEnd(p);
                continue;
            }

            if (c is ' ' or '\t')
            {
                while (_position < text.Length && text[_position] is ' ' or '\t')
                {
                    _position++;
                }

                continue;
            }

            // title\s[^#\n;]+
            if (MatchesIgnoreCase(p, "title") && p + 5 < text.Length && IsWhiteSpace(text[p + 5]))
            {
                var end = p + 6;
                while (end < text.Length && !IsTitleEnd(end))
                {
                    end++;
                }

                if (end > p + 6)
                    return Consume(VennTokenKind.Title, p, end, p + 6);
            }

            if (MatchesKeyword(p, "venn-beta"))
                return Consume(VennTokenKind.Venn, p, p + 9);

            if (MatchesKeyword(p, "set"))
                return Consume(VennTokenKind.Set, p, p + 3);

            if (MatchesKeyword(p, "union"))
                return Consume(VennTokenKind.Union, p, p + 5);

            if (MatchesKeyword(p, "text"))
            {
                if (_consumeIndentText)
                {
                    // The keyword of an indented text, already returned as its indentation
                    _consumeIndentText = false;
                    _position = p + 4;
                    continue;
                }

                return Consume(VennTokenKind.Text, p, p + 4);
            }

            if (MatchesKeyword(p, "style"))
                return Consume(VennTokenKind.Style, p, p + 5);

            if (c == '[')
            {
                // \["[^"]*"\]
                if (IsAt(p + 1, '"'))
                {
                    var quote = IndexOf('"', p + 2);
                    if (quote >= 0 && IsAt(quote + 1, ']'))
                        return Consume(VennTokenKind.BracketLabel, p, quote + 2);
                }

                // \[[^\]"]+\]
                var end = p + 1;
                while (end < text.Length && text[end] is not (']' or '"'))
                {
                    end++;
                }

                if (end > p + 1 && IsAt(end, ']'))
                    return Consume(VennTokenKind.BracketLabel, p, end + 1);
            }

            if (MatchNumber(p) is var numberEnd and > 0)
                return Consume(VennTokenKind.Numeric, p, numberEnd);

            if (c == '#' && !_entity[p])
            {
                var end = p + 1;
                while (end < text.Length && end - p <= 8 && char.IsAsciiHexDigit(text[end]))
                {
                    end++;
                }

                if (end - p >= 4)
                    return Consume(VennTokenKind.HexColor, p, end);
            }

            if (MatchColorFunction(p, "rgba", 4) is var rgbaEnd and > 0)
                return Consume(VennTokenKind.RgbaColor, p, rgbaEnd);

            if (MatchColorFunction(p, "rgb", 3) is var rgbEnd and > 0)
                return Consume(VennTokenKind.RgbColor, p, rgbEnd);

            if (char.IsAsciiLetter(c) || c == '_')
            {
                var end = p + 1;
                while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '-' or '_'))
                {
                    end++;
                }

                return Consume(VennTokenKind.Identifier, p, end);
            }

            if (c == '"')
            {
                var quote = IndexOf('"', p + 1);
                if (quote >= 0)
                    return Consume(VennTokenKind.String, p, quote + 1);
            }

            if (c == ',')
                return Consume(VennTokenKind.Comma, p, p + 1);

            if (c == ':')
                return Consume(VennTokenKind.Colon, p, p + 1);

            throw _context.Error(_offsets[p], $"Unexpected character '{c}'.");
        }
    }

    private VennToken Consume(VennTokenKind kind, int start, int end) => Consume(kind, start, end, start, end);

    private VennToken Consume(VennTokenKind kind, int start, int end, int valueStart) => Consume(kind, start, end, valueStart, end);

    /// <summary>Returns a token whose value is a part of its text, and moves after it.</summary>
    private VennToken Consume(VennTokenKind kind, int start, int end, int valueStart, int valueEnd)
    {
        _position = end;
        return new VennToken(kind, new string(_text, valueStart, valueEnd - valueStart), _offsets[start]);
    }

    private VennToken Token(VennTokenKind kind, int start, int end) => new(kind, new string(_text, start, end - start), _offsets[start]);

    private bool IsAt(int position, char c) => position < _text.Length && _text[position] == c && !_entity[position];

    private bool IsTitleEnd(int position) => _text[position] == '\n' || (!_entity[position] && _text[position] is '#' or ';');

    private int IndexOf(char c, int start)
    {
        for (var i = start; i < _text.Length; i++)
        {
            if (_text[i] == c)
                return i;
        }

        return -1;
    }

    private int SkipToLineEnd(int position)
    {
        while (position < _text.Length && _text[position] != '\n')
        {
            position++;
        }

        return position;
    }

    private bool MatchesIgnoreCase(int position, string value)
    {
        if (position + value.Length > _text.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (char.ToLowerInvariant(_text[position + i]) != value[i] || !char.IsAscii(_text[position + i]))
                return false;
        }

        return true;
    }

    /// <summary>Indicates whether a keyword, in any case, is at a position, followed by a non-word character (<c>\b</c>).</summary>
    private bool MatchesKeyword(int position, string keyword) => MatchesIgnoreCase(position, keyword) && (position + keyword.Length >= _text.Length || !IsWordCharacter(_text[position + keyword.Length]));

    /// <summary>Matches <c>[+-]?(\d+(\.\d+)?|\.\d+)</c>, and returns its end, or 0.</summary>
    private int MatchNumber(int position)
    {
        var i = position;
        if (i < _text.Length && _text[i] is '+' or '-')
        {
            i++;
        }

        var digits = i;
        while (digits < _text.Length && char.IsAsciiDigit(_text[digits]))
        {
            digits++;
        }

        if (digits > i)
        {
            if (digits + 1 < _text.Length && _text[digits] == '.' && char.IsAsciiDigit(_text[digits + 1]))
            {
                digits += 2;
                while (digits < _text.Length && char.IsAsciiDigit(_text[digits]))
                {
                    digits++;
                }
            }

            return digits;
        }

        if (i + 1 < _text.Length && _text[i] == '.' && char.IsAsciiDigit(_text[i + 1]))
        {
            i += 2;
            while (i < _text.Length && char.IsAsciiDigit(_text[i]))
            {
                i++;
            }

            return i;
        }

        return 0;
    }

    /// <summary>Matches <c>rgb\(\s*[0-9.]+\s*[,]\s*[0-9.]+...\s*\)</c> with a number of components, and returns its end, or 0.</summary>
    private int MatchColorFunction(int position, string name, int components)
    {
        if (!MatchesIgnoreCase(position, name) || !IsAt(position + name.Length, '('))
            return 0;

        var i = position + name.Length + 1;
        for (var component = 0; component < components; component++)
        {
            if (component > 0)
            {
                i = SkipWhiteSpace(i);
                if (!IsAt(i, ','))
                    return 0;

                i++;
            }

            i = SkipWhiteSpace(i);
            var start = i;
            while (i < _text.Length && (char.IsAsciiDigit(_text[i]) || _text[i] == '.'))
            {
                i++;
            }

            if (i == start)
                return 0;
        }

        i = SkipWhiteSpace(i);
        return IsAt(i, ')') ? i + 1 : 0;

        int SkipWhiteSpace(int index)
        {
            while (index < _text.Length && IsWhiteSpace(_text[index]))
            {
                index++;
            }

            return index;
        }
    }

    /// <summary>Marks the entity codes <c>#\w+;</c> of the text, as Mermaid encodes them before parsing.</summary>
    private void MarkEntities()
    {
        var i = 0;
        while (i < _text.Length)
        {
            if (_text[i] == '#')
            {
                var end = i + 1;
                while (end < _text.Length && IsWordCharacter(_text[end]))
                {
                    end++;
                }

                if (end > i + 1 && end < _text.Length && _text[end] == ';')
                {
                    _entity[i] = true;
                    _entity[end] = true;
                    i = end + 1;
                    continue;
                }
            }

            i++;
        }
    }

    /// <summary>Returns the semicolons Mermaid removes before parsing: the last one of a line with <c>style</c>, a colon, and a <c>#</c>, then the same for <c>classDef</c>.</summary>
    private static HashSet<int> GetRemovedCharacters(string text)
    {
        var removed = new HashSet<int>();
        var lineStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            // The '.' of JavaScript does not match the line terminators
            if (i < text.Length && text[i] is not ('\n' or '\r' or '\u2028' or '\u2029'))
                continue;

            var line = text[lineStart..i];
            var index = FindRemovedSemicolon(line, "style");
            if (index >= 0)
            {
                removed.Add(lineStart + index);
                line = line.Remove(index, 1);
            }

            var classDefIndex = FindRemovedSemicolon(line, "classDef");
            if (classDefIndex >= 0)
            {
                removed.Add(lineStart + (index >= 0 && classDefIndex >= index ? classDefIndex + 1 : classDefIndex));
            }

            lineStart = i + 1;
        }

        return removed;
    }

    /// <summary>Returns the index of the semicolon <c>/keyword.*:\S*#.*;/</c> removes from a line, or -1.</summary>
    private static int FindRemovedSemicolon(string line, string keyword)
    {
        var start = line.IndexOf(keyword, StringComparison.Ordinal);
        if (start < 0)
            return -1;

        var lastSemicolon = line.LastIndexOf(';', StringComparison.Ordinal);
        for (var colon = start + keyword.Length; colon < lastSemicolon; colon++)
        {
            if (line[colon] != ':')
                continue;

            var hash = colon + 1;
            while (hash < line.Length && !IsWhiteSpace(line[hash]) && line[hash] != '#')
            {
                hash++;
            }

            if (hash < lastSemicolon && line[hash] == '#')
                return lastSemicolon;
        }

        return -1;
    }
}
