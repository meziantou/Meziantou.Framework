using System.Collections.Concurrent;
using System.Reflection;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private static readonly ConcurrentDictionary<Type, MemoryToArrayMethod> MemoryToArrayMethods = new();
    private static readonly ConcurrentDictionary<ImplicitConversionCacheKey, MethodInfo[]> ImplicitConversionMethods = new();
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

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
        var declaredType = typeof(T);
        if (declaredType != typeof(object) && !declaredType.IsInterface && !declaredType.IsAbstract && !IsMemoryType(declaredType))
            return false;

        if (value is null)
            return false;

        var type = value.GetType();
        var toArrayMethod = MemoryToArrayMethods.GetOrAdd(type, GetMemoryToArrayMethod).Method;
        if (toArrayMethod is null)
            return false;

        items = (System.Collections.IEnumerable?)toArrayMethod.Invoke(value, parameters: null);
        return items is not null;
    }

    private static bool IsMemoryType(Type type)
    {
        return type.IsConstructedGenericType
            && (type.GetGenericTypeDefinition() == typeof(Memory<>) || type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>));
    }

    private static MemoryToArrayMethod GetMemoryToArrayMethod(Type type)
    {
        if (!IsMemoryType(type))
            return default;

        return new MemoryToArrayMethod(type.GetMethod(nameof(Memory<int>.ToArray), Type.EmptyTypes));
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

        foreach (var method in ImplicitConversionMethods.GetOrAdd(new ImplicitConversionCacheKey(sourceType, targetType), GetImplicitConversionMethods))
        {
            var convertedValue = method.Invoke(obj: null, [source]);
            if (object.Equals(convertedValue, target))
            {
                result = true;
                return true;
            }
        }

        return false;
    }

    private static MethodInfo[] GetImplicitConversionMethods(ImplicitConversionCacheKey key)
    {
        var methods = new List<MethodInfo>();
        AddImplicitConversionMethods(methods, key.SourceType.GetMethods(PublicStatic), key.SourceType, key.TargetType);
        AddImplicitConversionMethods(methods, key.TargetType.GetMethods(PublicStatic), key.SourceType, key.TargetType);

        return methods.Count == 0 ? [] : methods.ToArray();
    }

    private static void AddImplicitConversionMethods(List<MethodInfo> methods, MethodInfo[] candidateMethods, Type sourceType, Type targetType)
    {
        foreach (var method in candidateMethods)
        {
            if (method.Name != "op_Implicit" || method.ReturnType != targetType)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(sourceType))
            {
                methods.Add(method);
            }
        }
    }

    private readonly struct MemoryToArrayMethod(MethodInfo? method)
    {
        public MethodInfo? Method { get; } = method;
    }

    private readonly struct ImplicitConversionCacheKey(Type sourceType, Type targetType) : IEquatable<ImplicitConversionCacheKey>
    {
        public Type SourceType { get; } = sourceType;
        public Type TargetType { get; } = targetType;

        public bool Equals(ImplicitConversionCacheKey other)
        {
            return SourceType == other.SourceType && TargetType == other.TargetType;
        }

        public override bool Equals(object? obj)
        {
            return obj is ImplicitConversionCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceType, TargetType);
        }
    }
}
