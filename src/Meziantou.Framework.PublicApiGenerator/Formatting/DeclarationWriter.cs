namespace Meziantou.Framework.PublicApiGenerator;

// Accumulates the segments of a declaration. Lines are indented with 4 spaces per level, except empty lines.
// A text-only writer keeps the text without creating the segments, for the callers that only need the text (the generated files):
// the segments are the main allocation of the generation.
internal sealed class DeclarationWriter
{
    private readonly List<PublicApiDeclarationSegment>? _segments;
    private readonly StringBuilder? _text;

    // The offsets in _text right after each line break, so that Write(DeclarationWriter) can indent each line of a text-only writer
    private List<int>? _lineBreakEnds;
    private readonly string _newLine;
    private bool _isAtLineStart = true;
    private bool _isEmpty = true;

    public DeclarationWriter(string newLine, bool textOnly = false)
    {
        _newLine = newLine;
        if (textOnly)
        {
            _text = new StringBuilder();
        }
        else
        {
            _segments = [];
        }
    }

    public int Indentation { get; set; }

    public bool IsEmpty => _isEmpty;

    public bool IsTextOnly => _text is not null;

    public DeclarationWriter CreateWriter() => new(_newLine, IsTextOnly);

    public void Write(PublicApiDeclarationSegmentKind kind, string text, PublicApiTypeReference? typeReference = null, PublicApiSymbol? symbol = null)
    {
        if (text.Length == 0)
            return;

        WriteIndentation();
        if (_text is not null)
        {
            _text.Append(text);
            _isEmpty = false;
        }
        else
        {
            Add(new PublicApiDeclarationSegment(kind, text, typeReference, symbol));
        }
    }

    public void Keyword(string text) => Write(PublicApiDeclarationSegmentKind.Keyword, text);

    public void Punctuation(string text) => Write(PublicApiDeclarationSegmentKind.Punctuation, text);

    public void Operator(string text) => Write(PublicApiDeclarationSegmentKind.Operator, text);

    public void Space() => Write(PublicApiDeclarationSegmentKind.Space, " ");

    public void Text(string text) => Write(PublicApiDeclarationSegmentKind.Text, text);

    public void WriteLine()
    {
        if (_text is not null)
        {
            _text.Append(_newLine);
            (_lineBreakEnds ??= []).Add(_text.Length);
            _isEmpty = false;
        }
        else
        {
            Add(new PublicApiDeclarationSegment(PublicApiDeclarationSegmentKind.LineBreak, _newLine));
        }

        _isAtLineStart = true;
    }

    // Appends the segments of another writer. Each line of the other writer is indented with the current indentation.
    public void Write(DeclarationWriter other)
    {
        if (other._segments is not null)
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
                    if (_text is not null)
                    {
                        _text.Append(segment.Text);
                        _isEmpty = false;
                    }
                    else
                    {
                        Add(segment);
                    }
                }
            }

            return;
        }

        // The segments of a text-only writer are lost, so it can only be written to another text-only writer
        if (_text is null)
            throw new InvalidOperationException("A text-only writer cannot be written to a writer that keeps the segments.");

        var otherText = other._text!;
        var lineStart = 0;
        if (other._lineBreakEnds is not null)
        {
            foreach (var lineBreakEnd in other._lineBreakEnds)
            {
                AppendLine(otherText, lineStart, lineBreakEnd - other._newLine.Length - lineStart);
                WriteLine();
                lineStart = lineBreakEnd;
            }
        }

        AppendLine(otherText, lineStart, otherText.Length - lineStart);

        void AppendLine(StringBuilder source, int start, int length)
        {
            // Segments are never empty, so an empty line has no segment and is not indented
            if (length == 0)
                return;

            WriteIndentation();
            _text.Append(source, start, length);
            _isEmpty = false;
        }
    }

    public bool ContainsText(char value)
    {
        if (_text is not null)
        {
            foreach (var chunk in _text.GetChunks())
            {
                if (chunk.Span.Contains(value))
                    return true;
            }

            return false;
        }

        foreach (var segment in _segments!)
        {
            if (segment.Text.Contains(value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public bool EndsWith(char value)
    {
        if (_text is not null)
            return _text.Length > 0 && _text[^1] == value;

        return _segments!.Count > 0 && _segments[^1].Text[^1] == value;
    }

    public string GetText() => _text is not null ? _text.ToString() : string.Concat(_segments!.Select(static segment => segment.Text));

    public PublicApiFormattedDeclaration ToDeclaration()
    {
        if (_segments is null)
            throw new InvalidOperationException("A text-only writer does not keep the segments.");

        return new([.. _segments]);
    }

    private void Add(PublicApiDeclarationSegment segment)
    {
        _segments!.Add(segment);
        _isEmpty = false;
    }

    private void WriteIndentation()
    {
        if (!_isAtLineStart)
            return;

        _isAtLineStart = false;
        if (Indentation > 0)
        {
            var indentation = Indentation * 4;
            if (_text is not null)
            {
                _text.Append(' ', indentation);
                _isEmpty = false;
            }
            else
            {
                Add(new PublicApiDeclarationSegment(PublicApiDeclarationSegmentKind.Space, new string(' ', indentation)));
            }
        }
    }
}
