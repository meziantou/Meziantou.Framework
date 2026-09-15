namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1822 // Formatter methods intentionally share an instance-based overridable shape.
internal partial class AssertionFormatter
{
    public virtual string Format<TValue, TRounded>(EqualWithPrecisionAssertionError<TValue, TRounded> error)
    {
        var precision = error.Precision.ToString(CultureInfo.InvariantCulture) + (error.Precision == 1 ? " decimal place" : " decimal places");
        var builder = CreateMessage(error.IsNegative ? "Assert.NotEqual() assertion failed." : "Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.IsNegative ? "Not expected" : "Expected", FormatValue(error.RoundedExpectedValue) + " (rounded from " + FormatValue(error.ExpectedValue) + ")"),
                ("Actual", FormatValue(error.RoundedActualValue) + " (rounded from " + FormatValue(error.ActualValue) + ")"));

        if (error.Rounding is { } rounding)
        {
            builder.AppendGroup(
                ("Precision", precision),
                ("Rounding", FormatValue(rounding)));
        }
        else
        {
            builder.Append("Precision", precision);
        }

        return builder.ToString();
    }

    public virtual string Format<T>(EqualWithTimePrecisionAssertionError<T> error)
    {
        return CreateMessage(error.IsNegative ? "Assert.NotEqual() assertion failed." : "Assert.Equal() assertion failed.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                (error.IsNegative ? "Not expected" : "Expected", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .AppendGroup(
                ("Difference", FormatValue(error.Difference)),
                ("Precision", FormatValue(error.Precision)))
            .ToString();
    }

    public virtual string Format(ArrayDimensionsEqualAssertionError error)
    {
        return CreateMessage("Assert.Equal() assertion failed: Dimensions differ.", error.Message)
            .AppendGroup(
                ("Expected expression", error.ExpectedExpression),
                ("Actual expression", error.ActualExpression))
            .AppendGroup(
                ("Expected dimensions", FormatArrayDimensions(error.ExpectedValue)),
                ("Actual dimensions", FormatArrayDimensions(error.ActualValue)))
            .AppendGroup(
                ("Expected", FormatValue(error.ExpectedValue)),
                ("Actual", FormatValue(error.ActualValue)))
            .ToString();
    }

    private static string FormatArrayDimensions(System.Collections.IEnumerable value)
    {
        if (value is not Array array)
            return "<not an array>";

        var result = new StringBuilder("[");
        for (var dimension = 0; dimension < array.Rank; dimension++)
        {
            if (dimension > 0)
            {
                result.Append(", ");
            }

            result.Append(array.GetLength(dimension).ToString(CultureInfo.InvariantCulture));
        }

        return result.Append(']').ToString();
    }
}
