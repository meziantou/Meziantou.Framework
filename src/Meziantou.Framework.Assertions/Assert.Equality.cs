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

        return (TryCompareNumericValues(expected, actual, out var result) && result)
            || (TryCompareEnumerableValues(expected, actual, out result) && result)
            || ValuesEqualAfterImplicitConversion(expected, actual);
    }

    private static bool TryCompareEnumerableValues<TExpected, TActual>(TExpected expected, TActual actual, out bool result)
    {
        result = false;
        if (expected is string || actual is string)
            return false;

        if (expected is not System.Collections.IEnumerable expectedEnumerable || actual is not System.Collections.IEnumerable actualEnumerable)
            return false;

        result = EnumerableValuesEqual(expectedEnumerable, actualEnumerable);
        return true;
    }

    private static bool EnumerableValuesEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual)
    {
        var expectedEnumerator = expected.GetEnumerator();
        var actualEnumerator = actual.GetEnumerator();

        try
        {
            while (true)
            {
                var expectedHasNext = expectedEnumerator.MoveNext();
                var actualHasNext = actualEnumerator.MoveNext();

                if (!expectedHasNext && !actualHasNext)
                    return true;

                if (expectedHasNext != actualHasNext)
                    return false;

                if (!ValuesEqual(expectedEnumerator.Current, actualEnumerator.Current))
                    return false;
            }
        }
        finally
        {
            (expectedEnumerator as IDisposable)?.Dispose();
            (actualEnumerator as IDisposable)?.Dispose();
        }
    }

    private static bool TryCompareNumericValues<TExpected, TActual>(TExpected expected, TActual actual, out bool result)
    {
        result = false;

        if (expected is IntPtr expectedIntPtr)
            return TryCompareNumericValues(expectedIntPtr.ToInt64(), actual, out result);

        if (actual is IntPtr actualIntPtr)
            return TryCompareNumericValues(expected, actualIntPtr.ToInt64(), out result);

        if (expected is UIntPtr expectedUIntPtr)
            return TryCompareNumericValues(expectedUIntPtr.ToUInt64(), actual, out result);

        if (actual is UIntPtr actualUIntPtr)
            return TryCompareNumericValues(expected, actualUIntPtr.ToUInt64(), out result);

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

    private static bool ValuesEqualAfterImplicitConversion<TExpected, TActual>(TExpected expected, TActual actual)
    {
        if (expected is null || actual is null)
            return false;

        return ValuesEqualAfterImplicitConversion(expected, actual, actual.GetType(), out var result) && result
            || ValuesEqualAfterImplicitConversion(actual, expected, expected.GetType());
    }

    private static bool ValuesEqualAfterImplicitConversion<TSource>(TSource source, object target, Type targetType)
    {
        return ValuesEqualAfterImplicitConversion(source, target, targetType, out var result) && result;
    }

    private static bool ValuesEqualAfterImplicitConversion<TSource>(TSource source, object target, Type targetType, out bool result)
    {
        result = false;
        var sourceType = source?.GetType();
        if (sourceType is null || sourceType == targetType)
            return false;

        foreach (var method in sourceType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Concat(targetType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)))
        {
            if (method.Name != "op_Implicit" || method.ReturnType != targetType)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(sourceType))
            {
                var convertedValue = method.Invoke(obj: null, [source]);
                if (object.Equals(convertedValue, target))
                {
                    result = true;
                    return true;
                }
            }
        }

        return false;
    }
}
