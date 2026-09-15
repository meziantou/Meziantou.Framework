using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private static readonly ConcurrentDictionary<Type, MemoryToArrayMethod> MemoryToArrayMethods = new();
    private static readonly ConcurrentDictionary<ImplicitConversionCacheKey, MethodInfo[]> ImplicitConversionMethods = new();
    private static readonly ConcurrentDictionary<Type, ListValuesComparer?> ListValuesComparers = new();
    private static readonly ConcurrentDictionary<Type, ListValuesComparer> ListValuesComparersByElementType = new();
    private static volatile ListValuesComparerLookup? s_lastListValuesComparerLookup;
    private static readonly ConcurrentDictionary<Type, KeyValuePairAccessor> KeyValuePairAccessors = new();
    private static readonly ConcurrentDictionary<Type, object> DefaultImmutableArrays = new();
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

    /// <summary>Nesting depth from which <see cref="EnumerableValuesEqual"/> starts tracking the pairs it compares to detect cycles.</summary>
    private const int CycleDetectionDepth = 32;

    [ThreadStatic]
    private static int s_structuralComparisonDepth;

    [ThreadStatic]
    private static HashSet<ReferencePair>? s_structuralComparisonsInProgress;

    private static bool TryEqualMemory<TExpected, TActual>(TExpected expected, TActual actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (TryGetMemoryItems(expected, out var expectedItems) && TryGetMemoryItems(actual, out var actualItems))
        {
            Equal(expectedItems, actualItems, message, actualExpression, expectedExpression);
            return true;
        }

        return false;
    }

    private static bool TryNotEqualMemory<TExpected, TActual>(TExpected expected, TActual actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (TryGetMemoryItems(expected, out var expectedItems) && TryGetMemoryItems(actual, out var actualItems))
        {
            NotEqual(expectedItems, actualItems, message, actualExpression, expectedExpression);
            return true;
        }

        return false;
    }

    private static bool TryGetMemoryItems<T>(T value, [NotNullWhen(true)] out System.Collections.IEnumerable? items)
    {
        items = null;
        if (!MemoryCandidate<T>.IsPossible)
            return false;

        // Memory<T> and ReadOnlyMemory<T> are generic structs. The type test and the flag rule out every other value
        // before the dictionary lookup, which would otherwise run for every value compared through object or an interface.
        if (value is not ValueType)
            return false;

        var type = value.GetType();
        if (!type.IsGenericType)
            return false;

        var toArrayMethod = MemoryToArrayMethods.GetOrAdd(type, GetMemoryToArrayMethod).Method;
        if (toArrayMethod is null)
            return false;

        items = (System.Collections.IEnumerable?)toArrayMethod.Invoke(value, parameters: null);
        return items is not null;
    }

    private static class MemoryCandidate<T>
    {
        /// <summary>
        /// Gets a value indicating whether a value declared as <typeparamref name="T"/> can be a Memory or
        /// ReadOnlyMemory at runtime. Reading the type flags once per instantiation keeps them off the hot path.
        /// </summary>
        public static readonly bool IsPossible =
            typeof(T) == typeof(object)
            || typeof(T).IsInterface
            || typeof(T).IsAbstract
            || IsMemoryType(typeof(T));
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

        // object.Equals boxes both operands. When both have the same static type, EqualityComparer<T>.Default gives
        // the same answer without allocating, and the JIT devirtualizes it for value types.
        var sameType = typeof(TExpected) == typeof(TActual);
        if (sameType)
        {
            if (EqualityComparer<TExpected>.Default.Equals(expected, unsafe(Unsafe.As<TActual, TExpected>(ref actual))))
                return true;

            if (typeof(TExpected).IsValueType)
            {
                // Numeric widening and user-defined implicit conversions can only make values of different runtime
                // types compare equal, and a value type has no derived types. Only the structural comparison is left,
                // which still matters for collection-like structs such as ImmutableArray<T> and for pairs and tuples.
                return ContentValuesEqual(expected, actual);
            }
        }
        else if (object.Equals(expected, actual))
        {
            return true;
        }

        if (expected is null || actual is null)
            return false;

        // Numeric widening and user-defined implicit conversions can only make values of different runtime types compare
        // equal. When the runtime types match, Equals has already answered for them, and converting both values to
        // decimal on every mismatch is expensive, so only the structural comparison is left. The check is only a shortcut:
        // two different value types seldom share a runtime type (only through Nullable<T>), so they skip it rather than
        // being boxed for it.
        if ((!typeof(TExpected).IsValueType || !typeof(TActual).IsValueType) && expected.GetType() == actual.GetType())
            return ContentValuesEqual(expected, actual);

        return (TryCompareNumericValues(expected, actual, out var result) && result)
            || ContentValuesEqual(expected, actual)
            || ValuesEqualAfterImplicitConversion(expected, actual);
    }

    /// <summary>
    /// Compares two values that <see cref="object.Equals(object?)"/> reported as different by their content: sequences
    /// item by item, and key/value pairs and tuples component by component, each compared the way <c>Assert.Equal</c>
    /// compares two values.
    /// </summary>
    private static bool ContentValuesEqual<TExpected, TActual>(TExpected expected, TActual actual)
    {
        if (expected is null || actual is null || expected is string || actual is string)
            return false;

        if (expected is System.Collections.IEnumerable expectedEnumerable && actual is System.Collections.IEnumerable actualEnumerable)
            return EnumerableValuesEqual(expectedEnumerable, actualEnumerable);

        return ComponentValuesEqual(expected, actual);
    }

    private static bool EnumerableValuesEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual)
    {
        // A default ImmutableArray<T> throws on enumeration. Like a null collection, it only equals another one.
        var expectedIsDefault = IsDefaultImmutableArray(expected);
        var actualIsDefault = IsDefaultImmutableArray(actual);
        if (expectedIsDefault || actualIsDefault)
            return expectedIsDefault && actualIsDefault;

        if (!HaveSameArrayShape(expected, actual))
            return false;

        var pair = new ReferencePair(expected, actual);
        var isTracked = false;
        s_structuralComparisonDepth++;
        try
        {
            // A collection can contain itself, directly or through other collections. Once the comparison is deep enough
            // to be suspicious, a pair already being compared further up the stack is assumed equal: any difference is
            // reported by the comparison that is still in progress for that pair. Shallow comparisons skip the bookkeeping.
            if (s_structuralComparisonDepth > CycleDetectionDepth)
            {
                s_structuralComparisonsInProgress ??= [];
                if (!s_structuralComparisonsInProgress.Add(pair))
                    return true;

                isTracked = true;
            }

            if (TryListValuesEqual(expected, actual, out var result))
                return result;

            return EnumeratedValuesEqual(expected, actual);
        }
        finally
        {
            if (isTracked)
            {
                s_structuralComparisonsInProgress!.Remove(pair);
            }

            s_structuralComparisonDepth--;
        }
    }

    private static bool EnumeratedValuesEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual)
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

    /// <summary>
    /// Compares two lists with the same element type without boxing their items, when their runtime types allow it.
    /// </summary>
    /// <remarks>
    /// Items are compared with <see cref="ValuesEqual{TExpected, TActual}"/> on their static type, which gives the same
    /// answer as comparing the boxed items, so the result is exact in both directions.
    /// </remarks>
    private static bool TryListValuesEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, out bool result)
    {
        result = false;
        var expectedType = expected.GetType();
        var actualType = actual.GetType();
        var expectedComparer = GetListValuesComparer(expectedType);
        if (expectedComparer is null)
            return false;

        var actualComparer = expectedType == actualType ? expectedComparer : GetListValuesComparer(actualType);
        if (!object.ReferenceEquals(expectedComparer, actualComparer))
            return false;

        result = expectedComparer.ListsEqual(expected, actual);
        return true;
    }

    private static ListValuesComparer? GetListValuesComparer(Type type)
    {
        // Nested collections usually share one type, so remembering the last lookup skips the dictionary for most items.
        var lastLookup = s_lastListValuesComparerLookup;
        if (lastLookup is not null && lastLookup.Type == type)
            return lastLookup.Comparer;

        var comparer = ListValuesComparers.GetOrAdd(type, CreateListValuesComparer);
        s_lastListValuesComparerLookup = new ListValuesComparerLookup(type, comparer);
        return comparer;
    }

    private sealed class ListValuesComparerLookup(Type type, ListValuesComparer? comparer)
    {
        public Type Type { get; } = type;
        public ListValuesComparer? Comparer { get; } = comparer;
    }

    private static ListValuesComparer? CreateListValuesComparer(Type type)
    {
        Type? elementType = null;
        if (type.IsSZArray)
        {
            elementType = type.GetElementType();
        }
        else
        {
            foreach (var implementedInterface in type.GetInterfaces())
            {
                if (implementedInterface.IsGenericType && implementedInterface.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                {
                    // A type that is a list of several element types has no single view to compare.
                    if (elementType is not null)
                        return null;

                    elementType = implementedInterface.GetGenericArguments()[0];
                }
            }
        }

        if (elementType is null || elementType.IsPointer || elementType.IsByRef || elementType.IsByRefLike)
            return null;

        // Comparers are shared per element type, so two list types with the same element type get the same instance.
        return ListValuesComparersByElementType.GetOrAdd(elementType, static elementType => (ListValuesComparer)Activator.CreateInstance(typeof(ListValuesComparer<>).MakeGenericType(elementType))!);
    }

    private abstract class ListValuesComparer
    {
        public abstract bool ListsEqual(object expected, object actual);
    }

    private sealed class ListValuesComparer<T> : ListValuesComparer
    {
        public override bool ListsEqual(object expected, object actual)
        {
            if (TryGetListSpan(expected, out var expectedSpan) && TryGetListSpan(actual, out var actualSpan))
            {
                if (expectedSpan.Length != actualSpan.Length)
                    return false;

                if (BitwiseEquatable<T>.IsSupported)
                    return BitwiseSequenceEqual(expectedSpan, actualSpan);

                for (var i = 0; i < expectedSpan.Length; i++)
                {
                    if (!ValuesEqual(expectedSpan[i], actualSpan[i]))
                        return false;
                }

                return true;
            }

            var expectedList = (IReadOnlyList<T>)expected;
            var actualList = (IReadOnlyList<T>)actual;
            var count = expectedList.Count;
            if (count != actualList.Count)
                return false;

            for (var i = 0; i < count; i++)
            {
                if (!ValuesEqual(expectedList[i], actualList[i]))
                    return false;
            }

            return true;
        }

        private static bool TryGetListSpan(object value, out ReadOnlySpan<T> span)
        {
            switch (value)
            {
                case T[] array:
                    span = array;
                    return true;

                case List<T> list:
                    span = CollectionsMarshal.AsSpan(list);
                    return true;

                case ImmutableArray<T> immutableArray:
                    span = immutableArray.AsSpan();
                    return true;

                default:
                    span = default;
                    return false;
            }
        }
    }

    /// <summary>
    /// Returns <see langword="false"/> when either value is a multidimensional array and the other one is not an array of
    /// the same rank and dimensions. Enumerating such arrays flattens them, so their shape has to be compared separately.
    /// </summary>
    private static bool HaveSameArrayShape(object expected, object actual)
    {
        if (expected is not Array { Rank: > 1 } && actual is not Array { Rank: > 1 })
            return true;

        if (expected is not Array expectedArray || actual is not Array actualArray || expectedArray.Rank != actualArray.Rank)
            return false;

        for (var dimension = 0; dimension < expectedArray.Rank; dimension++)
        {
            if (expectedArray.GetLength(dimension) != actualArray.GetLength(dimension))
                return false;
        }

        return true;
    }

    private static bool IsDefaultImmutableArray(object? value)
    {
        if (value is not (ValueType and System.Collections.IList))
            return false;

        var type = value.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ImmutableArray<>))
            return false;

        // ImmutableArray<T>.Equals compares the underlying arrays, so it only equals the default value when it has none.
        return value.Equals(DefaultImmutableArrays.GetOrAdd(type, static type => Activator.CreateInstance(type)!));
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static System.Collections.IEnumerable? NullIfDefaultImmutableArray(System.Collections.IEnumerable? value)
    {
        return IsDefaultImmutableArray(value) ? null : value;
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static IEnumerable<T>? NullIfDefaultImmutableArray<T>(IEnumerable<T>? value)
    {
        return value is ImmutableArray<T> { IsDefault: true } ? null : value;
    }

    /// <summary>
    /// Compares key/value pairs and tuples component by component, so that a collection held in one of their components is
    /// compared by content, the same way it would be as a direct item.
    /// </summary>
    private static bool ComponentValuesEqual(object expected, object actual)
    {
        var expectedType = expected.GetType();
        var actualType = actual.GetType();
        if (!expectedType.IsGenericType || !actualType.IsGenericType)
            return false;

        var genericTypeDefinition = expectedType.GetGenericTypeDefinition();
        if (genericTypeDefinition != actualType.GetGenericTypeDefinition())
            return false;

        if (genericTypeDefinition == typeof(KeyValuePair<,>))
        {
            var (expectedKey, expectedValue) = KeyValuePairAccessors.GetOrAdd(expectedType, CreateKeyValuePairAccessor).GetKeyAndValue(expected);
            var (actualKey, actualValue) = KeyValuePairAccessors.GetOrAdd(actualType, CreateKeyValuePairAccessor).GetKeyAndValue(actual);
            return ValuesEqual(expectedKey, actualKey) && ValuesEqual(expectedValue, actualValue);
        }

        if (!IsTupleTypeDefinition(genericTypeDefinition) || expected is not ITuple expectedTuple || actual is not ITuple actualTuple || expectedTuple.Length != actualTuple.Length)
            return false;

        for (var i = 0; i < expectedTuple.Length; i++)
        {
            if (!ValuesEqual(expectedTuple[i], actualTuple[i]))
                return false;
        }

        return true;
    }

    private static bool IsTupleTypeDefinition(Type genericTypeDefinition)
    {
        if (genericTypeDefinition.Assembly != typeof(object).Assembly)
            return false;

        var name = genericTypeDefinition.FullName;
        return name is not null && (name.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || name.StartsWith("System.Tuple`", StringComparison.Ordinal));
    }

    private static KeyValuePairAccessor CreateKeyValuePairAccessor(Type type)
    {
        return (KeyValuePairAccessor)Activator.CreateInstance(typeof(KeyValuePairAccessor<,>).MakeGenericType(type.GetGenericArguments()))!;
    }

    private abstract class KeyValuePairAccessor
    {
        public abstract (object? Key, object? Value) GetKeyAndValue(object pair);
    }

    private sealed class KeyValuePairAccessor<TKey, TValue> : KeyValuePairAccessor
    {
        public override (object? Key, object? Value) GetKeyAndValue(object pair)
        {
            var keyValuePair = (KeyValuePair<TKey, TValue>)pair;
            return (keyValuePair.Key, keyValuePair.Value);
        }
    }

    private readonly struct ReferencePair(object expected, object actual) : IEquatable<ReferencePair>
    {
        public object Expected { get; } = expected;
        public object Actual { get; } = actual;

        public bool Equals(ReferencePair other) => object.ReferenceEquals(Expected, other.Expected) && object.ReferenceEquals(Actual, other.Actual);

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is ReferencePair other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Expected), RuntimeHelpers.GetHashCode(Actual));
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

        var expectedType = expected.GetType();
        var actualType = actual.GetType();

        // Type.GetTypeCode reports an enum as its underlying type, so without this guard two unrelated enums, or an
        // enum and a raw integer, would compare equal whenever their numeric values happen to coincide.
        if (expectedType.IsEnum || actualType.IsEnum)
            return false;

        var expectedTypeCode = Type.GetTypeCode(expectedType);
        var actualTypeCode = Type.GetTypeCode(actualType);
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

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is ImplicitConversionCacheKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SourceType, TargetType);
    }
}
