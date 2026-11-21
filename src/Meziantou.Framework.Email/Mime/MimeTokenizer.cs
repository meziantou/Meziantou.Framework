using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Reads the lexical tokens of structured header fields ([RFC 5322] 3.2): comments, quoted strings, atoms.</summary>
internal ref struct MimeTokenizer
{
    private readonly ReadOnlySpan<char> _text;

    public MimeTokenizer(ReadOnlySpan<char> text)
    {
        _text = text;
    }

    public int Position { get; set; }

    public readonly bool IsAtEnd => Position >= _text.Length;

    public readonly char Current => _text[Position];

    public readonly ReadOnlySpan<char> Remaining => _text[Position..];

    /// <summary>Skips whitespace and comments (nested parentheses, with quoted pairs).</summary>
    public void SkipCfws()
    {
        while (Position < _text.Length)
        {
            var c = _text[Position];
            if (char.IsWhiteSpace(c))
            {
                Position++;
            }
            else if (c == '(')
            {
                var depth = 0;
                while (Position < _text.Length)
                {
                    c = _text[Position++];
                    if (c == '\\' && Position < _text.Length)
                    {
                        Position++;
                    }
                    else if (c == '(')
                    {
                        depth++;
                    }
                    else if (c == ')' && --depth == 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    public bool TryConsume(char c)
    {
        SkipCfws();
        if (Position < _text.Length && _text[Position] == c)
        {
            Position++;
            return true;
        }

        return false;
    }

    /// <summary>Reads a quoted string, without its quotes and with its quoted pairs resolved. The tokenizer is on the opening quote.</summary>
    public string ReadQuotedString()
    {
        var builder = new StringBuilder();
        Position++;
        while (Position < _text.Length)
        {
            var c = _text[Position++];
            if (c == '"')
            {
                break;
            }

            if (c == '\\' && Position < _text.Length)
            {
                c = _text[Position++];
            }
            else if (c is '\r' or '\n')
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Reads characters until one of the delimiters or whitespace, after skipping whitespace and comments.</summary>
    public string ReadToken(ReadOnlySpan<char> delimiters)
    {
        SkipCfws();
        var start = Position;
        while (Position < _text.Length && !char.IsWhiteSpace(_text[Position]) && _text[Position] != '(' && delimiters.IndexOf(_text[Position]) < 0)
        {
            Position++;
        }

        return _text[start..Position].ToString();
    }

    /// <summary>Reads a quoted string or a token.</summary>
    public string ReadValue(ReadOnlySpan<char> delimiters)
    {
        SkipCfws();
        if (Position < _text.Length && _text[Position] == '"')
        {
            return ReadQuotedString();
        }

        return ReadToken(delimiters);
    }

    /// <summary>Reads a phrase (words and quoted strings) until one of the delimiters, keeping the spaces between the words.</summary>
    public string ReadPhrase(ReadOnlySpan<char> delimiters)
    {
        var builder = new StringBuilder();
        while (true)
        {
            SkipCfws();
            if (Position >= _text.Length || delimiters.IndexOf(_text[Position]) >= 0)
            {
                break;
            }

            string word;
            if (_text[Position] == '"')
            {
                word = ReadQuotedString();
            }
            else
            {
                var start = Position;
                while (Position < _text.Length && !char.IsWhiteSpace(_text[Position]) && _text[Position] is not '"' and not '(' && delimiters.IndexOf(_text[Position]) < 0)
                {
                    Position++;
                }

                word = _text[start..Position].ToString();
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(word);
        }

        return builder.ToString();
    }

    /// <summary>Quotes a value when it contains characters that a token cannot contain ([RFC 2045] 5.1).</summary>
    public static string Quote(string value, string specials = "()<>@,;:\\\"/[]?=")
    {
        var needsQuotes = value.Length == 0;
        foreach (var c in value)
        {
            if (c <= ' ' || c >= 0x7F || specials.Contains(c, StringComparison.Ordinal))
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.Append('"').ToString();
    }
}
