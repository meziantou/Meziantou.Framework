#if !NET9_0_OR_GREATER
#pragma warning disable MA0048 // File name must match type name

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Http;
internal static class Polyfill
{
    public static SpanSplitEnumerator<T> Split<T>(this ReadOnlySpan<T> source, T separator) where T : IEquatable<T>
        => new SpanSplitEnumerator<T>(source, separator);

    public static SpanSplitEnumerator<T> Split<T>(this ReadOnlySpan<T> source, ReadOnlySpan<T> separator) where T : IEquatable<T>
        => new SpanSplitEnumerator<T>(source, separator);
}

internal enum SpanSplitEnumeratorMode
{
    None = 0,
    SingleElement,
    Any,
    Sequence,
    EmptySequence,
    SearchValues,
}

internal ref struct SpanSplitEnumerator<T> where T : IEquatable<T>
{
    private readonly ReadOnlySpan<T> _span;
    private readonly T _separator = default!;
    private readonly ReadOnlySpan<T> _separatorBuffer;
    private readonly SearchValues<T> _searchValues = default!;
    private SpanSplitEnumeratorMode _splitMode;
    private int _startCurrent = 0;
    private int _endCurrent = 0;
    private int _startNext = 0;

    public SpanSplitEnumerator<T> GetEnumerator() => this;
    public Range Current => new Range(_startCurrent, _endCurrent);

    internal SpanSplitEnumerator(ReadOnlySpan<T> span, SearchValues<T> searchValues)
    {
        _span = span;
        _splitMode = SpanSplitEnumeratorMode.SearchValues;
        _searchValues = searchValues;
    }

    internal SpanSplitEnumerator(ReadOnlySpan<T> span, ReadOnlySpan<T> separators)
    {
        _span = span;

        if (typeof(T) == typeof(char) && separators.Length == 0)
        {
            _searchValues = Unsafe.As<SearchValues<T>>(WhiteSpaceChars);
            _splitMode = SpanSplitEnumeratorMode.SearchValues;
            return;
        }

        _separatorBuffer = separators;
        _splitMode = SpanSplitEnumeratorMode.Any;
    }

    internal SpanSplitEnumerator(ReadOnlySpan<T> span, T separator)
    {
        _span = span;
        _separator = separator;
        _splitMode = SpanSplitEnumeratorMode.SingleElement;
    }

    public bool MoveNext()
    {
        // Search for the next separator index.
        int separatorIndex, separatorLength;
        switch (_splitMode)
        {
            case SpanSplitEnumeratorMode.None:
                return false;

            case SpanSplitEnumeratorMode.SingleElement:
                separatorLength = 1;
                separatorIndex = _span.Slice(_startNext)
                    .IndexOf(_separator);
                break;

            case SpanSplitEnumeratorMode.Any:
                separatorLength = 1;
                separatorIndex = _span.Slice(_startNext)
                    .IndexOfAny(_separatorBuffer);
                break;

            case SpanSplitEnumeratorMode.Sequence:
                separatorIndex = _span.Slice(_startNext)
                    .IndexOf(_separatorBuffer);
                separatorLength = _separatorBuffer.Length;
                break;

            case SpanSplitEnumeratorMode.EmptySequence:
                separatorIndex = -1;
                separatorLength = 1;
                break;

            case SpanSplitEnumeratorMode.SearchValues:
                separatorIndex = _span.Slice(_startNext).IndexOfAny(_searchValues);
                separatorLength = 1;
                break;

            default:
                throw new InvalidOperationException($"Invalid split mode: {_splitMode}");
        }

        _startCurrent = _startNext;
        if (separatorIndex >= 0)
        {
            _endCurrent = _startCurrent + separatorIndex;
            _startNext = _endCurrent + separatorLength;
        }
        else
        {
            _startNext = _endCurrent = _span.Length;

            // Set _splitMode to None so that subsequent MoveNext calls will return false.
            _splitMode = SpanSplitEnumeratorMode.None;
        }

        return true;
    }

    private const string Whitespaces = "\t\n\v\f\r\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";

    public static readonly SearchValues<char> WhiteSpaceChars = SearchValues.Create(Whitespaces.AsSpan());
}
#endif