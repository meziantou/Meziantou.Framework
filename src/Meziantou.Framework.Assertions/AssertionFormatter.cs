namespace Meziantou.Framework.Assertions;

internal class AssertionFormatter
{
    public static AssertionFormatter Default { get; } = new AssertionFormatter();

    public virtual string Format(FailAssertionError error)
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
            Actual: false
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
            Actual expression: {error.ActualExpression}
            """;

        result += Environment.NewLine + "Expected: " + FormatValue(error.ExpectedValue);
        result += Environment.NewLine + "Actual: " + FormatValue(error.ActualValue);

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
            Actual expression: {error.ActualExpression}
            Index of first difference: {error.FirstDifferenceIndex}
            Expected item: {FormatReadOnlySpanValue(error.ExpectedValue, error.FirstDifferenceIndex)}
            Actual item: {FormatReadOnlySpanValue(error.ActualValue, error.FirstDifferenceIndex)}
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
            Actual expression: {error.ActualExpression}
            Expected length: {error.ExpectedValue.Length}
            Actual length: {error.ActualValue.Length}
            Expected: {FormatReadOnlySpanValue(error.ExpectedValue)}
            Actual: {FormatReadOnlySpanValue(error.ActualValue)}
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
            Actual expression: {error.ActualExpression}
            Index of first difference: {error.FirstDifferenceIndex}
            Expected: {FormatValue(error.ExpectedValue, error.FirstDifferenceIndex)}
            Actual: {FormatValue(error.ActualValue, error.FirstDifferenceIndex)}
            """);

        if (!string.IsNullOrEmpty(error.Message))
        {
            result += Environment.NewLine + "Message: " + error.Message;
        }

        return result;
    }

    protected virtual string FormatValue(object? value, int? hightlightedIndex = null)
    {
        if (value is null)
            return "<null>";

        if (value is string stringValue)
            return FormatStringValue(stringValue);

        // TODO IEnumerable cache (display first items, then "..." if there are more items), maybe add an option to set the minimum number of items to display (continue enumeration if needed)
        // TODO add a way to highlight an item in a collection or text (using unicode characters) Maybe some "combining characters" like underscore. (COMBINING LEFT TACK BELOW U+0318, COMBINING RIGHT TACK BELOW U+0319, COMBINING MINUS SIGN BELOW U+0320, COMBINING LOW LINE U+0332)
        // TODO handle circular reference

        if (value is System.Collections.IEnumerable enumerable)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(FormatValue(item));
            }

            return $"[{string.Join(", ", items)}]";
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    protected virtual string FormatReadOnlySpanValue<T>(ReadOnlySpan<T> value, int? highlightedIndex = null)
    {
        // TODO + hightlighted index
        return $"[{string.Join(", ", value.ToArray())}]";
    }

    private static string FormatStringValue(string value)
    {
        return $"\"{value}\""; // TODO escape string characters

        static string EscapeChar(char value)
        {
            return value switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '"' => "\\\"",
                '\\' => "\\\\",
                _ => value.ToString(),
            };
        }
    }
}
