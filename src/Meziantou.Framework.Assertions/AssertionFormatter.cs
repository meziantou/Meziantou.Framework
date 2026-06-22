namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1822, CA1852 // Formatter methods intentionally share an instance-based overridable shape.
internal class AssertionFormatter
{
    private const char CombiningLowLine = '\u0332';
    private const int MaxFormattedItems = 10;
    private const int PrefixItemCount = 3;
    private const int HighlightedContextItemCount = 2;

    public static AssertionFormatter Default { get; } = new AssertionFormatter();

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
            var focusStartIndex = highlightedIndex is >= MaxFormattedItems
                ? Math.Max(PrefixItemCount, highlightedIndex.GetValueOrDefault() - HighlightedContextItemCount)
                : -1;
            var focusEndIndex = highlightedIndex is >= MaxFormattedItems
                ? highlightedIndex.GetValueOrDefault() + HighlightedContextItemCount
                : -1;
            var prefixItemCount = highlightedIndex is >= MaxFormattedItems ? PrefixItemCount : MaxFormattedItems;
            var maxIndex = highlightedIndex is >= MaxFormattedItems ? focusEndIndex : MaxFormattedItems - 1;
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
