using System.Buffers;
using System.Diagnostics;

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Scans the URLs of extended autolinks, and remembers what it found in an inline text, so that a candidate lying in a
/// run of text already scanned is resolved without scanning that run again. Without it, a run without whitespace such
/// as <c>(www.a</c> or <c>*http://a</c> repeated n times takes O(n²) time and allocations.
/// </summary>
/// <remarks>
/// A scan starting at a position stops at the first unbalanced ')' or at the first character where the URL ends
/// whatever the parentheses (see <see cref="IsRunEnd"/>). The latter does not depend on where the scan started, so all
/// the candidates starting before it share it: the characters up to it form a run. The first scan of a run goes to its
/// end; the per-position tables that resolve the following candidates of the run are only built when there is one.
/// The checks made on the URL (its '@', its trailing emphasis characters, its domain) would also go over the rest of the
/// run for each candidate, so they use the tables or remember their last result as well. The URL is only copied into a
/// string once it is known to be valid.
/// </remarks>
internal sealed class AutoLinkScanCache
{
    private const int NoPosition = -1;
    private const int ValidAtRunEnd = -1;
    private const int InvalidAtRunEnd = -2;

    private string? _text;
    private int _textEnd;

    // The run [_runStart, _runEnd): _runEnd is the first position at or after _runStart where every scan stops.
    private int _runStart = NoPosition;
    private int _runEnd;

    // Built on demand, indexed by position - _runStart
    private int[]? _closings; // The position of the ')' ending a scan started there, or ValidAtRunEnd/InvalidAtRunEnd
    private int[]? _domainStops; // The first position at or after it that ends a domain scan, or _runEnd
    private int[]? _lastDots; // The position of the last '.' before it in the run, or NoPosition
    private int[]? _lastUnderscores; // The position of the last '_' before it in the run, or NoPosition

    // The last search for a '@': there is none in [_atSearchStart, _atPosition), and _atPosition is a '@' or _runEnd
    private int _atSearchStart = NoPosition;
    private int _atPosition;

    // The last search for trailing characters: [_trimStart, _trimEnd) only contains _trimCharacters, and
    // _trimStart == _trimLowerBound or the character before _trimStart is not one of them
    private int _trimEnd = NoPosition;
    private int _trimLowerBound;
    private int _trimStart;
    private string _trimCharacters = "";

    /// <summary>
    /// Gets the end (exclusive) of the URL of the extended autolink starting at <c>slice.Start</c>, or -1 when there is
    /// no valid URL. It is the position where <see cref="LinkHelper.TryParseUrl{T}(ref T, out string?, out bool, bool)"/>
    /// in autolink mode stops, and the URL is valid when that method succeeds.
    /// </summary>
    public int ScanUrl(StringSlice slice)
    {
        SetText(slice);

        var start = slice.Start;
        if (start >= _runStart && start < _runEnd)
        {
            EnsureTables();

            var closing = _closings[start - _runStart];
            return closing switch
            {
                ValidAtRunEnd => _runEnd,
                InvalidAtRunEnd => -1,
                _ => closing,
            };
        }

        return ScanRun(slice);
    }

    /// <summary>
    /// Gets the position from which the characters before <paramref name="end"/> down to <paramref name="start"/> are all
    /// in <paramref name="characters"/>.
    /// </summary>
    public int GetTrailingCharactersStart(int start, int end, ReadOnlySpan<char> characters)
    {
        Debug.Assert(_text is not null);

        // Many candidates of a run can end at the same position
        if (end == _trimEnd && start >= _trimLowerBound && characters.SequenceEqual(_trimCharacters))
        {
            return Math.Max(_trimStart, start);
        }

        var text = _text.AsSpan();
        var position = end;
        while (position > start && characters.Contains(text[position - 1]))
        {
            position--;
        }

        _trimEnd = end;
        _trimLowerBound = start;
        _trimStart = position;
        if (!characters.SequenceEqual(_trimCharacters))
        {
            _trimCharacters = characters.ToString();
        }

        return position;
    }

    /// <summary>
    /// Gets the index of the first '@' in the URL [<paramref name="start"/>, <paramref name="end"/>) returned by
    /// <see cref="ScanUrl"/> (or a prefix of it), relative to <paramref name="start"/>, or -1 when there is none.
    /// </summary>
    public int IndexOfAt(int start, int end)
    {
        Debug.Assert(_text is not null && start >= _runStart && end <= _runEnd);

        if (_atSearchStart == NoPosition || start < _atSearchStart || start > _atPosition)
        {
            var index = _text.AsSpan(start, _runEnd - start).IndexOf('@');
            _atSearchStart = start;
            _atPosition = index < 0 ? _runEnd : start + index;
        }

        return _atPosition < end ? _atPosition - start : -1;
    }

    /// <summary>
    /// Determines whether the domain starting at <paramref name="domainStart"/> in the URL ending at <paramref name="end"/>
    /// is valid. The result is the one of <see cref="LinkHelper.IsValidDomain(string, int, bool)"/>.
    /// </summary>
    public bool IsValidDomain(int start, int domainStart, int end, bool allowDomainWithoutPeriod)
    {
        Debug.Assert(_text is not null && start >= _runStart && end <= _runEnd);

        // The first candidate of a run is checked directly, like the following ones when the run is short
        if (_closings is null)
        {
            return LinkHelper.IsValidDomain(_text.AsSpan(start, end - start), domainStart - start, allowDomainWithoutPeriod);
        }

        if (domainStart >= end)
        {
            return false; // No segment
        }

        var text = _text;
        if (text[domainStart] == '.')
        {
            return false; // Empty segment
        }

        // The domain ends at the first '/', '?', '#' or ':', and is invalid at the first invalid character or at a '.'
        // following another one. The '.' at domainStart has just been checked.
        var domainEnd = _domainStops![domainStart - _runStart];
        if (domainEnd < end)
        {
            if (!IsDomainTerminator(text[domainEnd]))
            {
                return false;
            }
        }
        else
        {
            domainEnd = end;
        }

        // The last segment must not be empty
        if (domainEnd == domainStart || text[domainEnd - 1] == '.')
        {
            return false;
        }

        // There must be at least one period, and no underscores may be present in the last two segments of the domain
        var lastDot = _lastDots![domainEnd - _runStart];
        if (lastDot < domainStart && !allowDomainWithoutPeriod)
        {
            return false;
        }

        var lastUnderscore = _lastUnderscores![domainEnd - _runStart];
        return lastUnderscore < domainStart || (lastDot > lastUnderscore && _lastDots[lastDot - _runStart] > lastUnderscore);
    }

    /// <summary>
    /// Forgets everything remembered.
    /// </summary>
    public void Clear()
    {
        _text = null;
        _textEnd = 0;
        _trimEnd = NoPosition;
        _trimCharacters = "";
        ClearRun();
    }

    private void SetText(StringSlice slice)
    {
        // The outcome of a scan only depends on the characters between its start and the end of the text
        if (!ReferenceEquals(_text, slice.Text) || _textEnd != slice.End)
        {
            Clear();
            _text = slice.Text;
            _textEnd = slice.End;
        }
    }

    private void ClearRun()
    {
        _runStart = NoPosition;
        _runEnd = 0;
        _atSearchStart = NoPosition;
        _atPosition = 0;
        Return(ref _closings);
        Return(ref _domainStops);
        Return(ref _lastDots);
        Return(ref _lastUnderscores);

        static void Return(ref int[]? array)
        {
            if (array is not null)
            {
                ArrayPool<int>.Shared.Return(array);
                array = null;
            }
        }
    }

    private int ScanRun(StringSlice slice)
    {
        ClearRun();

        var text = slice.Text.AsSpan();
        var start = slice.Start;
        var position = start;
        var depth = 0;
        var closing = NoPosition;
        while (true)
        {
            var c = position <= slice.End ? text[position] : '\0';
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                // The first unbalanced ')' ends the URL, but the scan goes on to the end of the run
                depth--;
                if (depth < 0 && closing == NoPosition)
                {
                    closing = position;
                }
            }
            else if (IsRunEnd(slice, position, c))
            {
                break;
            }

            position++;
        }

        _runStart = start;
        _runEnd = position;

        if (closing != NoPosition)
        {
            return closing;
        }

        return depth == 0 ? position : -1;
    }

    /// <summary>
    /// Determines whether the URL ends at <paramref name="position"/> whatever the parentheses before it. Parentheses never do.
    /// </summary>
    private static bool IsRunEnd(StringSlice slice, int position, char c)
    {
        if (LinkHelper.IsEndOfUri(c, isAutoLink: true))
        {
            return true;
        }

        if (c == '&')
        {
            var entity = slice;
            entity.Start = position;
            if (HtmlHelper.ScanEntity(entity, out _, out _, out _) > 0)
            {
                return true;
            }
        }

        return LinkHelper.IsTrailingUrlStopCharacter(c) && LinkHelper.IsEndOfUri(position < slice.End ? slice.Text[position + 1] : '\0', isAutoLink: true);
    }

    [MemberNotNull(nameof(_closings), nameof(_domainStops), nameof(_lastDots), nameof(_lastUnderscores))]
    private void EnsureTables()
    {
        if (_closings is not null)
        {
            Debug.Assert(_domainStops is not null && _lastDots is not null && _lastUnderscores is not null);
            return;
        }

        Debug.Assert(_text is not null);
        var text = _text.AsSpan(_runStart, _runEnd - _runStart);
        var length = text.Length;

        // The depth of the parentheses before each position of the run
        var depths = ArrayPool<int>.Shared.Rent(length + 1);
        var lastDots = ArrayPool<int>.Shared.Rent(length + 1);
        var lastUnderscores = ArrayPool<int>.Shared.Rent(length + 1);
        var depth = 0;
        var minDepth = 0;
        var maxDepth = 0;
        var lastDot = NoPosition;
        var lastUnderscore = NoPosition;
        for (var i = 0; i < length; i++)
        {
            depths[i] = depth;
            lastDots[i] = lastDot;
            lastUnderscores[i] = lastUnderscore;
            switch (text[i])
            {
                case '(':
                    depth++;
                    maxDepth = Math.Max(maxDepth, depth);
                    break;
                case ')':
                    depth--;
                    minDepth = Math.Min(minDepth, depth);
                    break;
                case '.':
                    lastDot = _runStart + i;
                    break;
                case '_':
                    lastUnderscore = _runStart + i;
                    break;
            }
        }

        depths[length] = depth;
        lastDots[length] = lastDot;
        lastUnderscores[length] = lastUnderscore;

        // A scan started at a position ends at the first ')' at the depth of that position. Going backward, remember the
        // nearest ')' at each depth. The depths are replaced by the closings, as each one is only read once.
        var endDepth = depth;
        var nearestClosings = ArrayPool<int>.Shared.Rent(maxDepth - minDepth + 1);
        nearestClosings.AsSpan(0, maxDepth - minDepth + 1).Fill(NoPosition);
        var closings = depths;
        var domainStops = ArrayPool<int>.Shared.Rent(length);
        var domainStop = _runEnd;
        for (var i = length - 1; i >= 0; i--)
        {
            var c = text[i];
            depth = depths[i];
            if (c == ')')
            {
                nearestClosings[depth - minDepth] = _runStart + i;
            }

            var nearestClosing = nearestClosings[depth - minDepth];
            closings[i] = nearestClosing != NoPosition ? nearestClosing : depth == endDepth ? ValidAtRunEnd : InvalidAtRunEnd;

            if (IsDomainTerminator(c) || IsInvalidDomainCharacter(c) || (c == '.' && i > 0 && text[i - 1] == '.'))
            {
                domainStop = _runStart + i;
            }

            domainStops[i] = domainStop;
        }

        ArrayPool<int>.Shared.Return(nearestClosings);

        _closings = closings;
        _domainStops = domainStops;
        _lastDots = lastDots;
        _lastUnderscores = lastUnderscores;
    }

    // See LinkHelper.IsValidDomain
    private static bool IsDomainTerminator(char c) => c is '/' or '?' or '#' or ':';

    private static bool IsInvalidDomainCharacter(char c) => !c.IsAlphaNumeric() && c is not '.' and not '_' and not '-' && !IsDomainTerminator(c) && CharHelper.IsSpaceOrPunctuationForGFMAutoLink(c);
}
