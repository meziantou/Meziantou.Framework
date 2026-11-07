namespace Meziantou.Framework.PublicApiGenerator;

// Accumulates the segments of a declaration. Lines are indented with 4 spaces per level, except empty lines.
internal sealed class DeclarationWriter
{
    private readonly List<PublicApiDeclarationSegment> _segments = [];
    private readonly string _newLine;
    private bool _isAtLineStart = true;

    public DeclarationWriter(string newLine)
    {
        _newLine = newLine;
    }

    public int Indentation { get; set; }

    public bool IsEmpty => _segments.Count == 0;

    public DeclarationWriter CreateWriter() => new(_newLine);

    public void Write(PublicApiDeclarationSegmentKind kind, string text, PublicApiTypeReference? typeReference = null, PublicApiSymbol? symbol = null)
    {
        if (text.Length == 0)
            return;

        WriteIndentation();
        _segments.Add(new PublicApiDeclarationSegment(kind, text, typeReference, symbol));
    }

    public void Keyword(string text) => Write(PublicApiDeclarationSegmentKind.Keyword, text);

    public void Punctuation(string text) => Write(PublicApiDeclarationSegmentKind.Punctuation, text);

    public void Operator(string text) => Write(PublicApiDeclarationSegmentKind.Operator, text);

    public void Space() => Write(PublicApiDeclarationSegmentKind.Space, " ");

    public void Text(string text) => Write(PublicApiDeclarationSegmentKind.Text, text);

    public void WriteLine()
    {
        _segments.Add(new PublicApiDeclarationSegment(PublicApiDeclarationSegmentKind.LineBreak, _newLine));
        _isAtLineStart = true;
    }

    // Appends the segments of another writer. Each line of the other writer is indented with the current indentation.
    public void Write(DeclarationWriter other)
    {
        foreach (var segment in other._segments)
        {
            if (segment.Kind == PublicApiDeclarationSegmentKind.LineBreak)
            {
                WriteLine();
            }
            else
            {
                WriteIndentation();
                _segments.Add(segment);
            }
        }
    }

    public bool ContainsText(char value)
    {
        foreach (var segment in _segments)
        {
            if (segment.Text.Contains(value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public bool EndsWith(char value)
    {
        return _segments.Count > 0 && _segments[^1].Text[^1] == value;
    }

    public string GetText() => string.Concat(_segments.Select(static segment => segment.Text));

    public PublicApiFormattedDeclaration ToDeclaration() => new([.. _segments]);

    private void WriteIndentation()
    {
        if (!_isAtLineStart)
            return;

        _isAtLineStart = false;
        if (Indentation > 0)
        {
            _segments.Add(new PublicApiDeclarationSegment(PublicApiDeclarationSegmentKind.Space, new string(' ', Indentation * 4)));
        }
    }
}
