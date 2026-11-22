namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>The text being parsed, with its positions, its warnings and its limits.</summary>
internal sealed class DotContext
{
    private const int MaxWarnings = 1000;

    private readonly List<int> _lineStarts = [0];
    private readonly HashSet<string> _warningKeys = new(StringComparer.Ordinal);

    public DotContext(string text, GraphvizParseOptions options)
    {
        Text = text;
        Options = options;
        for (var i = 0; i < text.Length; i++)
        {
            // A line ends with \n, \r\n or \r
            if (text[i] is '\n' || (text[i] is '\r' && (i + 1 >= text.Length || text[i + 1] is not '\n')))
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    public string Text { get; }

    public GraphvizParseOptions Options { get; }

    public List<GraphvizWarning> Warnings { get; } = [];

    public (int Line, int Column) GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);

        // The \n of \r\n is reported at the \r, the end of its line
        if (offset > 0 && offset < Text.Length && Text[offset] is '\n' && Text[offset - 1] is '\r')
        {
            offset--;
        }

        var index = _lineStarts.BinarySearch(offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return (index + 1, offset - _lineStarts[index] + 1);
    }

    public GraphvizParseException Error(int offset, string message)
    {
        var (line, column) = GetPosition(offset);
        return new GraphvizParseException(new GraphvizParseError(message, line, column));
    }

    public void Warn(int offset, string message)
    {
        if (Warnings.Count >= MaxWarnings)
            return;

        var (line, column) = GetPosition(offset);
        Warnings.Add(new GraphvizWarning(message, line, column));
    }

    /// <summary>Reports a warning the first time its key is seen, so that an attribute used on every node is reported once.</summary>
    public void WarnOnce(string key, int offset, string message)
    {
        if (_warningKeys.Add(key))
        {
            Warn(offset, message);
        }
    }
}
