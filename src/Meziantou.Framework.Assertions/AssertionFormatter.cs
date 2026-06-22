namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1822, CA1852 // Formatter methods intentionally share an instance-based overridable shape.
internal class AssertionFormatter
{
    private const char CombiningLowLine = '\u0332';
    private int _highlightedContextItemCount = 2;
    private int _maxFormattedItems = 10;
    private int _prefixItemCount = 3;
    private int _suffixItemCount;

    public static AssertionFormatter Default { get; } = new AssertionFormatter();

    /// <summary>
    /// Gets or sets the number of items to format from the start of an enumerable before truncating it.
    /// </summary>
    /// <remarks>
    /// When there is no highlighted item, or when the highlighted item is within this leading range, the formatter writes items from the beginning of the enumerable.
    /// If <see cref="SuffixItemCount"/> requires more items after a highlighted item, the formatter can write more than this value.
    /// When the highlighted item index is greater than or equal to this value, the formatter switches to focused mode: it writes <see cref="PrefixItemCount"/> items from the beginning, an ellipsis, and a window around the highlighted item controlled by <see cref="HighlightedContextItemCount"/>.
    /// </remarks>
    public int MaxFormattedItems
    {
        get => _maxFormattedItems;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxFormattedItems = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of items to keep from the start of an enumerable when a highlighted item is outside the leading range.
    /// </summary>
    /// <remarks>
    /// This value is used only in focused mode, when the highlighted item index is greater than or equal to <see cref="MaxFormattedItems"/>.
    /// It preserves the beginning of the enumerable before the ellipsis and the highlighted-item context window.
    /// </remarks>
    public int PrefixItemCount
    {
        get => _prefixItemCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _prefixItemCount = value;
        }
    }

    /// <summary>
    /// Gets or sets the minimum number of items to format after a highlighted item in the leading range.
    /// </summary>
    /// <remarks>
    /// This value is used when the highlighted item index is less than <see cref="MaxFormattedItems"/>.
    /// In that case, the formatter writes at least <see cref="MaxFormattedItems"/> items, and can continue up to the highlighted item plus this many following items.
    /// This lets assertion failures found near the beginning of a snapshot show extra items after the difference.
    /// </remarks>
    public int SuffixItemCount
    {
        get => _suffixItemCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _suffixItemCount = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of neighboring items to format before and after a highlighted item in focused mode.
    /// </summary>
    /// <remarks>
    /// This value is used only when the highlighted item index is greater than or equal to <see cref="MaxFormattedItems"/>.
    /// The formatter then writes <see cref="PrefixItemCount"/> items from the beginning, an ellipsis when items were skipped, and up to this many items on each side of the highlighted item.
    /// </remarks>
    public int HighlightedContextItemCount
    {
        get => _highlightedContextItemCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _highlightedContextItemCount = value;
        }
    }

    public string Format(FailAssertionError error)
    {
        var result = "Assert.Fail() assertion failed.";
        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }

    public virtual string Format(TrueAssertionError error)
    {
        var result = $"""
            Assert.True() assertion failed.
            Expression: {error.Expression}
            Expected: true
            Actual:   false
            """;

        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }

    public virtual string Format<TExpected, TActual>(EqualAssertionError<TExpected, TActual> error)
    {
        var result = $"""
            Assert.Equal() assertion failed.
            Expected expression: {error.ExpectedExpression}
            Actual expression:   {error.ActualExpression}
            """;

        result += Environment.NewLine + "Expected: " + FormatValue(error.ExpectedValue);
        result += Environment.NewLine + "Actual:   " + FormatValue(error.ActualValue);

        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }

    public virtual string Format<TExpected, TActual>(ReadOnlySpanEqualAssertionError<TExpected, TActual> error)
    {
        var result = string.Create(CultureInfo.InvariantCulture, $"""
            Assert.Equal() assertion failed: Item at index {error.FirstDifferenceIndex} differs.
            Expected expression: {error.ExpectedExpression}
            Actual expression:   {error.ActualExpression}
            Index of first difference: {error.FirstDifferenceIndex}
            Expected item: {FormatReadOnlySpanValue(error.ExpectedValue, error.FirstDifferenceIndex)}
            Actual item:   {FormatReadOnlySpanValue(error.ActualValue, error.FirstDifferenceIndex)}
            """);

        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }

    public virtual string Format<TExpected, TActual>(ReadOnlySpanLengthAssertionError<TExpected, TActual> error)
    {
        var result = string.Create(CultureInfo.InvariantCulture, $"""
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: {error.ExpectedExpression}
            Actual expression:   {error.ActualExpression}
            Expected length: {error.ExpectedValue.Length}
            Actual length:   {error.ActualValue.Length}
            Expected: {FormatReadOnlySpanValue(error.ExpectedValue)}
            Actual:   {FormatReadOnlySpanValue(error.ActualValue)}
            """);

        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }
    public virtual string Format<TExpected, TActual>(CollectionEqualAssertionError<TExpected, TActual> error)
    {
        var result = string.Create(CultureInfo.InvariantCulture, $"""
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: {error.ExpectedExpression}
            Actual expression:   {error.ActualExpression}
            Index of first difference: {error.FirstDifferenceIndex}
            Expected: {FormatValue(error.ExpectedValue, error.FirstDifferenceIndex)}
            Actual:   {FormatValue(error.ActualValue, error.FirstDifferenceIndex)}
            """);

        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }

    protected virtual string FormatValue(object? value, int? highlightedIndex = null)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return FormatValue(value, highlightedIndex, visited);
    }

    protected virtual string FormatValue(object? value, int? highlightedIndex, HashSet<object> visited)
    {
        if (value is null)
            return "<null>";

        if (value is string stringValue)
            return FormatStringValue(stringValue, highlightedIndex);

        if (value is System.Collections.IEnumerable enumerable)
        {
            return FormatEnumerableValue(enumerable, highlightedIndex, visited);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    protected virtual string FormatReadOnlySpanValue<T>(ReadOnlySpan<T> value, int? highlightedIndex = null)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var items = new List<string>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            items.Add(FormatHighlightedValue(FormatValue(value[i], highlightedIndex: null, visited), i, highlightedIndex));
        }

        return $"[{string.Join(", ", items)}]";
    }

    protected virtual string FormatEnumerableValue(System.Collections.IEnumerable value, int? highlightedIndex, HashSet<object> visited)
    {
        if (!visited.Add(value))
            return "<circular reference>";

        try
        {
            var items = new List<string>();
            var shouldFocusHighlightedItem = highlightedIndex is not null && highlightedIndex.GetValueOrDefault() >= MaxFormattedItems;
            var focusStartIndex = shouldFocusHighlightedItem
                ? Math.Max(PrefixItemCount, highlightedIndex.GetValueOrDefault() - HighlightedContextItemCount)
                : -1;
            var focusEndIndex = shouldFocusHighlightedItem
                ? highlightedIndex.GetValueOrDefault() + HighlightedContextItemCount
                : -1;
            var prefixItemCount = shouldFocusHighlightedItem ? PrefixItemCount : MaxFormattedItems;
            var maxIndex = shouldFocusHighlightedItem
                ? focusEndIndex
                : Math.Max(MaxFormattedItems - 1, highlightedIndex.GetValueOrDefault(-1) + SuffixItemCount);
            var hasSkippedItems = false;
            var index = 0;

            foreach (var item in value)
            {
                if (index > maxIndex)
                {
                    items.Add("...");
                    break;
                }

                if (index < prefixItemCount || index >= focusStartIndex)
                {
                    if (hasSkippedItems)
                    {
                        items.Add("...");
                        hasSkippedItems = false;
                    }

                    items.Add(FormatHighlightedValue(FormatValue(item, highlightedIndex: null, visited), index, highlightedIndex));
                }
                else
                {
                    hasSkippedItems = true;
                }

                index++;
            }

            return $"[{string.Join(", ", items)}]";
        }
        finally
        {
            visited.Remove(value);
        }
    }

    private static string FormatHighlightedValue(string value, int index, int? highlightedIndex)
    {
        if (index == highlightedIndex)
            return Underline(value);

        return value;
    }

    private static string Underline(string value)
    {
        var result = new StringBuilder(value.Length * 2);
        foreach (var c in value)
        {
            result.Append(c);
            result.Append(CombiningLowLine);
        }

        return result.ToString();
    }

    private static string FormatStringValue(string value, int? highlightedIndex)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');

        for (var i = 0; i < value.Length; i++)
        {
            var escapedChar = EscapeChar(value[i]);
            if (i == highlightedIndex)
            {
                result.Append(Underline(escapedChar));
            }
            else
            {
                result.Append(escapedChar);
            }
        }

        result.Append('"');
        return result.ToString();

        static string EscapeChar(char value)
        {
            return value switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' => "\\u" + ((int)value).ToString("X4", CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
        }
    }
}
#pragma warning restore CA1822, CA1852
