namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The pipes and the line breaks of a paragraph that is read as a pipe table, as the pipe table parser of Markdig tracks them.
/// The pipes of a link are removed from them when the link is closed.
/// </summary>
internal sealed class PipeTableState
{
    private readonly List<InlineNode> _delimiters = [];
    private readonly List<int> _previousDelimiters = [];
    private readonly List<int> _nextDelimiters = [];
    private int _firstDelimiter = -1;
    private int _lastDelimiter = -1;
    private int _removedDelimiterCount;

    public bool IsInvalid { get; set; }

    public bool IsParsingStopped { get; set; }

    public bool LineHasPipe { get; set; }

    // Whether a delimiter of the table is in a container that Markdig has open: a delimiter of emphasis or a bracket
    public bool HasNestedDelimiters { get; set; }

    // Whether a pipe is a child of the paragraph, not in a delimiter of emphasis or a bracket: Markdig finds it from the code spans
    // and the generic attributes
    public bool HasRootPipe { get; set; }

    // The line break that ends the table, and where the line after it starts
    public InlineNode? EndOfTable { get; set; }

    public int EndOfTableLineStart { get; set; }

    // Where the current line starts
    public int LineStart { get; set; }

    public List<InlineNode> EndOfLines { get; } = [];

    public List<InlineNode> Delimiters
    {
        get
        {
            CompactDelimiters();
            return _delimiters;
        }
    }

    // A pipe, which is a delimiter run, knows its index in the delimiters by its value. It is still a pipe for Markdig once a link
    // has made it text.
    public void AddDelimiter(InlineNode delimiter)
    {
        var index = _delimiters.Count;
        _delimiters.Add(delimiter);
        _previousDelimiters.Add(_lastDelimiter);
        _nextDelimiters.Add(-1);
        if (_lastDelimiter >= 0)
        {
            _nextDelimiters[_lastDelimiter] = index;
        }
        else
        {
            _firstDelimiter = index;
        }

        _lastDelimiter = index;
        if (delimiter.IsDelimiterRun)
        {
            delimiter.Value = index;
        }
    }

    /// <summary>
    /// Removes the pipes of a link that is closed, the last one first. Returns whether a pipe is next to them in the delimiters,
    /// as Markdig computes it: when there is none, the line has no pipe.
    /// </summary>
    public bool RemovePipesOfClosedInline(List<InlineNode> pipes)
    {
        var lastPipeIndex = pipes[0].Value;
        var hasDelimiterNextToPipes = lastPipeIndex >= 0 && IsPipe(_previousDelimiters[lastPipeIndex]);
        var firstPipeIndex = pipes[^1].Value;
        foreach (var pipe in pipes)
        {
            RemovePipe(pipe);
        }

        if (pipes.Count > 1)
        {
            hasDelimiterNextToPipes |= IsPipe(firstPipeIndex >= 0 ? _nextDelimiters[firstPipeIndex] : _firstDelimiter);
        }

        return hasDelimiterNextToPipes;
    }

    private bool IsPipe(int index) => index >= 0 && _delimiters[index].IsDelimiterRun;

    private void RemovePipe(InlineNode pipe)
    {
        var index = pipe.Value;
        if (index < 0 || index >= _delimiters.Count || !ReferenceEquals(_delimiters[index], pipe))
        {
            return;
        }

        var previous = _previousDelimiters[index];
        var next = _nextDelimiters[index];
        if (previous >= 0)
        {
            _nextDelimiters[previous] = next;
        }
        else
        {
            _firstDelimiter = next;
        }

        if (next >= 0)
        {
            _previousDelimiters[next] = previous;
        }
        else
        {
            _lastDelimiter = previous;
        }

        pipe.Value = -1;
        _removedDelimiterCount++;
    }

    private void CompactDelimiters()
    {
        if (_removedDelimiterCount == 0)
        {
            return;
        }

        var count = 0;
        for (var index = _firstDelimiter; index >= 0; index = _nextDelimiters[index])
        {
            var delimiter = _delimiters[index];
            _delimiters[count] = delimiter;
            if (delimiter.IsDelimiterRun)
            {
                delimiter.Value = count;
            }

            count++;
        }

        _delimiters.RemoveRange(count, _delimiters.Count - count);
        _previousDelimiters.Clear();
        _nextDelimiters.Clear();
        for (var index = 0; index < count; index++)
        {
            _previousDelimiters.Add(index - 1);
            _nextDelimiters.Add(index + 1 < count ? index + 1 : -1);
        }

        _firstDelimiter = count > 0 ? 0 : -1;
        _lastDelimiter = count - 1;
        _removedDelimiterCount = 0;
    }
}
