namespace Meziantou.Framework.Diagrams.Mermaid.Internals;

/// <summary>Reads the text of a diagram character by character.</summary>
internal sealed class Scanner(ParseContext context, int position)
{
    public ParseContext Context { get; } = context;

    public string Text { get; } = context.Text;

    public int Position { get; set; } = position;

    public bool IsEnd => Position >= Text.Length;

    public char Current => Position < Text.Length ? Text[Position] : '\0';

    public char Peek(int offset = 1) => Position + offset < Text.Length ? Text[Position + offset] : '\0';

    /// <summary>Skips the spaces and the tabs of the line, and a <c>%%</c> comment up to its end.</summary>
    public void SkipSpaces()
    {
        while (Position < Text.Length && Text[Position] is ' ' or '\t')
        {
            Position++;
        }

        if (IsCommentAt(Position))
        {
            while (Position < Text.Length && Text[Position] != '\n')
            {
                Position++;
            }
        }
    }

    /// <summary>Indicates whether a <c>%%</c> comment starts at a position (the directives are already removed).</summary>
    public bool IsCommentAt(int position) => position + 1 < Text.Length && Text[position] == '%' && Text[position + 1] == '%';

    /// <summary>Skips the white space, the line feeds and the semicolons between statements.</summary>
    public void SkipSeparators()
    {
        while (Position < Text.Length && (char.IsWhiteSpace(Text[Position]) || Text[Position] == ';'))
        {
            Position++;
        }
    }

    public bool StartsWith(string value) => string.CompareOrdinal(Text, Position, value, 0, value.Length) == 0 && Position + value.Length <= Text.Length;

    /// <summary>Indicates whether a keyword starts here, followed by a space or the end of the statement.</summary>
    public bool StartsWithKeyword(string keyword, bool allowColon = false)
    {
        if (!StartsWith(keyword))
            return false;

        var next = Position + keyword.Length;
        return next >= Text.Length || Text[next] is ' ' or '\t' or '\n' or ';' || (allowColon && Text[next] == ':');
    }

    public bool TryConsume(string value)
    {
        if (!StartsWith(value))
            return false;

        Position += value.Length;
        return true;
    }

    /// <summary>Indicates whether the statement ends here: a line feed, a semicolon or the end of the text, after spaces.</summary>
    public bool IsAtStatementEnd()
    {
        var p = Position;
        while (p < Text.Length && Text[p] is ' ' or '\t')
        {
            p++;
        }

        return p >= Text.Length || Text[p] is '\n' or ';' || IsCommentAt(p);
    }

    /// <summary>Reads the rest of the line, without its line feed.</summary>
    public string ReadLine()
    {
        var end = Text.IndexOfOrdinal('\n', Position);
        if (end < 0)
        {
            end = Text.Length;
        }

        var line = Text[Position..end];
        Position = end;
        return line;
    }

    /// <summary>Reads up to the end of the line or a <c>%%</c> comment: the text after the colon of a label, which can contain semicolons.</summary>
    public string ReadRestOfLine()
    {
        var start = Position;
        while (Position < Text.Length && Text[Position] != '\n' && !IsCommentAt(Position))
        {
            Position++;
        }

        var text = Text[start..Position];
        SkipSpaces();
        return text;
    }

    /// <summary>Reads up to the end of the statement: a line feed, a semicolon, or a <c>%%</c> comment.</summary>
    public string ReadStatement()
    {
        var start = Position;
        while (Position < Text.Length && Text[Position] is not ('\n' or ';') && !IsCommentAt(Position))
        {
            Position++;
        }

        var statement = Text[start..Position];
        SkipSpaces();
        return statement;
    }

    /// <summary>Reads a quoted string; the scanner is on the opening quote.</summary>
    public string ReadQuoted()
    {
        var start = Position;
        var quote = Text[Position];
        var end = Text.IndexOfOrdinal(quote, Position + 1);
        if (end < 0)
            throw Error(start, "The string is not closed.");

        Position = end + 1;
        return Text[(start + 1)..end];
    }

    public MermaidParseException Error(string message) => Context.Error(Position, message);

    public MermaidParseException Error(int offset, string message) => Context.Error(offset, message);

    public void Expect(string value)
    {
        if (!TryConsume(value))
            throw Error($"'{value}' is expected.");
    }

    /// <summary>Throws when the statement does not end here.</summary>
    public void ExpectStatementEnd()
    {
        SkipSpaces();
        if (!IsEnd && Current is not ('\n' or ';') && !IsCommentAt(Position))
            throw Error($"Unexpected '{Describe()}'.");
    }

    public string Describe()
    {
        if (IsEnd)
            return "end of text";

        var end = Position;
        while (end < Text.Length && end - Position < 20 && !char.IsWhiteSpace(Text[end]))
        {
            end++;
        }

        return end == Position ? Text[Position].ToString() : Text[Position..end];
    }
}
