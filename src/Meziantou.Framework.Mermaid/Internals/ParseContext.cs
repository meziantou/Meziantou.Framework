namespace Meziantou.Framework.Mermaid.Internals;

/// <summary>The text being parsed, with its positions, its warnings and its limits.</summary>
internal sealed class ParseContext
{
    private readonly List<int> _lineStarts = [0];

    public ParseContext(string text, MermaidParseOptions options)
    {
        Text = text;
        Options = options;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    /// <summary>Gets the text, its line endings normalized, with the front matter, the directives and the comments replaced by spaces.</summary>
    public string Text { get; set; }

    public MermaidParseOptions Options { get; }

    public List<MermaidWarning> Warnings { get; } = [];

    public MermaidConfiguration Configuration { get; } = new();

    public string? Title { get; set; }

    public (int Line, int Column) GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        var index = _lineStarts.BinarySearch(offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return (index + 1, offset - _lineStarts[index] + 1);
    }

    public MermaidParseException Error(int offset, string message)
    {
        var (line, column) = GetPosition(offset);
        return new MermaidParseException(new MermaidParseError(message, line, column));
    }

    public void Warn(int offset, string message)
    {
        var (line, column) = GetPosition(offset);
        if (Warnings.Count < 1000)
        {
            Warnings.Add(new MermaidWarning(message, line, column));
        }
    }
}
