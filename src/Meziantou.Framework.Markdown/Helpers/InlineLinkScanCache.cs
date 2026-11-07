namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Remembers how failed inline link scans ended in an inline text, so that a link opener lying inside a region
/// already scanned fails without scanning that region again. Without it, inputs such as <c>[a](</c> or
/// <c>[a](b (</c> repeated n times take O(n²) time.
/// </summary>
internal sealed class InlineLinkScanCache
{
    private const int MaximumRetainedCapacity = 1024;

    private string? _text;
    private int _textEnd;
    private List<int> _unmatchedOpeningParentheses = [];
    private List<int> _destinationScan = [];
    private int _failedTitleStart;
    private int _failedTitleEnd;

    /// <summary>
    /// Forgets everything remembered unless <paramref name="text"/> is the text the previous scans ran on.
    /// </summary>
    public void SetText(StringSlice text)
    {
        // The outcome of a scan only depends on the characters between its start and the end of the text
        if (!ReferenceEquals(_text, text.Text) || _textEnd != text.End)
        {
            Clear();
            _text = text.Text;
            _textEnd = text.End;
        }
    }

    /// <summary>
    /// Forgets everything remembered.
    /// </summary>
    public void Clear()
    {
        _text = null;
        _textEnd = 0;
        _failedTitleStart = 0;
        _failedTitleEnd = 0;

        // Do not keep large buffers alive in pooled instances
        if (_unmatchedOpeningParentheses.Capacity > MaximumRetainedCapacity || _destinationScan.Capacity > MaximumRetainedCapacity)
        {
            _unmatchedOpeningParentheses = [];
            _destinationScan = [];
        }
        else
        {
            _unmatchedOpeningParentheses.Clear();
        }
    }

    /// <summary>
    /// Gets an empty list that a destination scan fills with the positions of the '(' it leaves unmatched.
    /// </summary>
    public List<int> BeginDestinationScan()
    {
        _destinationScan.Clear();
        return _destinationScan;
    }

    /// <summary>
    /// Remembers the destination scan that just failed.
    /// </summary>
    public void EndFailedDestinationScan()
    {
        // A destination that does not start with '<' fails only when it ends with unmatched '(': only those are recorded.
        if (_destinationScan.Count > 0)
        {
            (_unmatchedOpeningParentheses, _destinationScan) = (_destinationScan, _unmatchedOpeningParentheses);
        }
    }

    /// <summary>
    /// Determines whether the destination following the '(' at <paramref name="openingParenthesisPosition"/> is known to be invalid.
    /// </summary>
    /// <param name="openingParenthesisPosition">The position of the '(' that opens the destination.</param>
    /// <param name="firstChar">The first character of the destination, after the leading whitespace.</param>
    public bool IsKnownInvalidDestination(int openingParenthesisPosition, char firstChar)
    {
        // The recorded scan went over this '(' and left it unmatched, so there is no space or control character between
        // them and the end of that scan (the whitespace trimmed after this '(' can only be characters such as U+00A0).
        // A scan started after it goes through the same characters and reaches the same end, where the '(' that
        // were unmatched after this one still are. Only the last unmatched '(' reaches that end with balanced
        // parentheses, so it is excluded. A destination starting with '<' follows other rules.
        var count = _unmatchedOpeningParentheses.Count - 1;
        return firstChar != '<' && count > 0 && _unmatchedOpeningParentheses.BinarySearch(0, count, openingParenthesisPosition, comparer: null) >= 0;
    }

    /// <summary>
    /// Remembers that the inline link failed after its title scan went from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
    public void SetFailedTitle(int start, int end, char enclosingCharacter)
    {
        // A title between quotes cannot contain the unescaped quote that would open a later title, so only titles
        // between parentheses can overlap.
        if (enclosingCharacter == '(')
        {
            _failedTitleStart = start;
            _failedTitleEnd = end;
        }
    }

    /// <summary>
    /// Determines whether the inline link is known to fail on the title starting at <paramref name="start"/>.
    /// </summary>
    public bool IsKnownFailedTitle(int start, char enclosingCharacter)
    {
        // A title opened by an unescaped '(' inside a recorded title scan continues exactly like that scan: it ends at
        // the same position and the inline link fails the same way.
        return enclosingCharacter == '(' && start > _failedTitleStart && start < _failedTitleEnd;
    }
}
