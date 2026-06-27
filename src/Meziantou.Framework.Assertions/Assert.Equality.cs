namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private static bool TryEqualMemory<TExpected, TActual>(TExpected expected, TActual actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (TryGetMemoryItems(expected, out var expectedItems) && TryGetMemoryItems(actual, out var actualItems))
        {
            Equal(expectedItems, actualItems, message, actualExpression, expectedExpression);
            return true;
        }

        return false;
    }

    private static bool TryGetMemoryItems<T>(T value, [NotNullWhen(true)] out System.Collections.IEnumerable? items)
    {
        items = null;
        if (value is null)
            return false;

        var type = value.GetType();
        if (!type.IsConstructedGenericType)
            return false;

        var genericTypeDefinition = type.GetGenericTypeDefinition();
        if (genericTypeDefinition != typeof(Memory<>) && genericTypeDefinition != typeof(ReadOnlyMemory<>))
            return false;

        items = (System.Collections.IEnumerable?)type.GetMethod(nameof(Memory<int>.ToArray), Type.EmptyTypes)?.Invoke(value, parameters: null);
        return items is not null;
    }

    private static bool ValuesEqual<TExpected, TActual>(TExpected expected, TActual actual, System.Collections.IEqualityComparer? comparer = null)
    {
        if (comparer is not null)
            return comparer.Equals(expected, actual);

        if (object.Equals(expected, actual))
            return true;

        return TryCompareNumericValues(expected, actual, out var result) && result;
    }

    private static bool TryCompareNumericValues<TExpected, TActual>(TExpected expected, TActual actual, out bool result)
    {
        result = false;
        if (expected is null || actual is null)
            return false;

        var expectedTypeCode = Type.GetTypeCode(expected.GetType());
        var actualTypeCode = Type.GetTypeCode(actual.GetType());
        if (!IsNumericTypeCode(expectedTypeCode) || !IsNumericTypeCode(actualTypeCode))
            return false;

        if (IsFloatingPointTypeCode(expectedTypeCode) || IsFloatingPointTypeCode(actualTypeCode))
        {
            var expectedDouble = Convert.ToDouble(expected, CultureInfo.InvariantCulture);
            var actualDouble = Convert.ToDouble(actual, CultureInfo.InvariantCulture);
            result = expectedDouble.Equals(actualDouble);
            return true;
        }

        var expectedDecimal = Convert.ToDecimal(expected, CultureInfo.InvariantCulture);
        var actualDecimal = Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
        result = expectedDecimal == actualDecimal;
        return true;
    }

    private static bool IsNumericTypeCode(TypeCode typeCode)
    {
        return typeCode is TypeCode.Byte
            or TypeCode.SByte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Decimal;
    }

    private static bool IsFloatingPointTypeCode(TypeCode typeCode)
    {
        return typeCode is TypeCode.Single or TypeCode.Double;
    }
}
