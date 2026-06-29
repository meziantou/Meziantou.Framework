using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void Equivalent(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        Equivalent(expected, actual, options: null, message, actualExpression, expectedExpression);
    }

    public static void Equivalent(object? expected, object? actual, EquivalentOptions? options, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        AssertEquivalent(expected, actual, options, assertionName: nameof(Equivalent), message, actualExpression, expectedExpression);
    }

    public static void EqualByStructure(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        AssertEquivalent(expected, actual, options: null, assertionName: nameof(EqualByStructure), message, actualExpression, expectedExpression);
    }

    private static void AssertEquivalent(object? expected, object? actual, EquivalentOptions? options, string assertionName, string? message, string? actualExpression, string? expectedExpression)
    {
        var comparisonOptions = StructuralComparisonOptions.Create(options);
        var failure = GetStructuralDifference(expected, actual, "$", new HashSet<StructuralReferencePair>(), comparisonOptions);
        if (failure is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualByStructureAssertionError(assertionName, failure.Value.ExpectedValue, failure.Value.ActualValue, failure.Value.Path, failure.Value.Reason, message, actualExpression, expectedExpression)));
    }

    private static StructuralDifference? GetStructuralDifference(object? expected, object? actual, string path, HashSet<StructuralReferencePair> visited, StructuralComparisonOptions options)
    {
        if (object.ReferenceEquals(expected, actual))
            return null;

        if (expected is null || actual is null)
            return ValuesEqual(expected, actual) ? null : new StructuralDifference(path, expected, actual, "Values differ.");

        var expectedType = expected.GetType();
        var actualType = actual.GetType();
        if (IsSimpleStructuralValue(expectedType) || IsSimpleStructuralValue(actualType))
            return StructuralValuesEqual(expected, actual, options) ? null : new StructuralDifference(path, expected, actual, "Values differ.");

        if (!expectedType.IsValueType && !actualType.IsValueType)
        {
            var pair = new StructuralReferencePair(expected, actual);
            if (!visited.Add(pair))
                return null;
        }

        if (expected is System.Collections.IEnumerable expectedEnumerable && actual is System.Collections.IEnumerable actualEnumerable)
            return GetStructuralEnumerableDifference(expectedEnumerable, actualEnumerable, path, visited, options);

        return GetStructuralMemberDifference(expected, actual, path, visited, options);
    }

    private static StructuralDifference? GetStructuralEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string path, HashSet<StructuralReferencePair> visited, StructuralComparisonOptions options)
    {
        if (options.IgnoreCollectionOrder)
            return GetStructuralUnorderedEnumerableDifference(expected, actual, path, visited, options);

        return GetStructuralOrderedEnumerableDifference(expected, actual, path, visited, options);
    }

    private static StructuralDifference? GetStructuralOrderedEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string path, HashSet<StructuralReferencePair> visited, StructuralComparisonOptions options)
    {
        var index = 0;
        var expectedEnumerator = expected.GetEnumerator();
        var actualEnumerator = actual.GetEnumerator();

        try
        {
            while (true)
            {
                var expectedHasNext = expectedEnumerator.MoveNext();
                var actualHasNext = actualEnumerator.MoveNext();
                var itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

                if (!expectedHasNext && !actualHasNext)
                    return null;

                if (!expectedHasNext)
                    return new StructuralDifference(itemPath, StructuralMissingValue.Instance, actualEnumerator.Current, "Actual collection contains an unexpected item.");

                if (!actualHasNext)
                    return new StructuralDifference(itemPath, expectedEnumerator.Current, StructuralMissingValue.Instance, "Actual collection is missing an item.");

                var difference = GetStructuralDifference(expectedEnumerator.Current, actualEnumerator.Current, itemPath, visited, options);
                if (difference is not null)
                    return difference;

                index++;
            }
        }
        finally
        {
            (expectedEnumerator as IDisposable)?.Dispose();
            (actualEnumerator as IDisposable)?.Dispose();
        }
    }

    private static StructuralDifference? GetStructuralUnorderedEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string path, HashSet<StructuralReferencePair> visited, StructuralComparisonOptions options)
    {
        var expectedItems = new List<object?>(EnumerateObjects(expected));
        var actualItems = new List<object?>(EnumerateObjects(actual));
        var matchedActualIndexes = new bool[actualItems.Count];

        for (var expectedIndex = 0; expectedIndex < expectedItems.Count; expectedIndex++)
        {
            var itemPath = path + "[" + expectedIndex.ToString(CultureInfo.InvariantCulture) + "]";
            var expectedItem = expectedItems[expectedIndex];
            var found = false;

            for (var actualIndex = 0; actualIndex < actualItems.Count; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                var candidateVisited = new HashSet<StructuralReferencePair>(visited);
                var difference = GetStructuralDifference(expectedItem, actualItems[actualIndex], itemPath, candidateVisited, options);
                if (difference is not null)
                    continue;

                matchedActualIndexes[actualIndex] = true;
                found = true;
                break;
            }

            if (!found)
                return new StructuralDifference(itemPath, expectedItem, StructuralMissingValue.Instance, "Actual collection is missing an equivalent item.");
        }

        for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
        {
            if (matchedActualIndexes[actualIndex])
                continue;

            var itemPath = path + "[" + actualIndex.ToString(CultureInfo.InvariantCulture) + "]";
            return new StructuralDifference(itemPath, StructuralMissingValue.Instance, actualItems[actualIndex], "Actual collection contains an unexpected item.");
        }

        return null;
    }

    private static StructuralDifference? GetStructuralMemberDifference(object expected, object actual, string path, HashSet<StructuralReferencePair> visited, StructuralComparisonOptions options)
    {
        var expectedMembers = GetStructuralMembers(expected.GetType(), options.MemberNameComparer);
        var actualMembers = GetStructuralMembers(actual.GetType(), options.MemberNameComparer);

        foreach (var expectedMember in expectedMembers.Values)
        {
            var memberPath = path + "." + expectedMember.Name;
            if (!actualMembers.TryGetValue(expectedMember.Name, out var actualMember))
                return new StructuralDifference(memberPath, expectedMember.GetValue(expected), StructuralMissingValue.Instance, "Actual member is missing.");

            var difference = GetStructuralDifference(expectedMember.GetValue(expected), actualMember.GetValue(actual), memberPath, visited, options);
            if (difference is not null)
                return difference;
        }

        foreach (var actualMember in actualMembers.Values)
        {
            if (!expectedMembers.ContainsKey(actualMember.Name))
            {
                var memberPath = path + "." + actualMember.Name;
                return new StructuralDifference(memberPath, StructuralMissingValue.Instance, actualMember.GetValue(actual), "Actual member is unexpected.");
            }
        }

        return null;
    }

    private static Dictionary<string, StructuralMember> GetStructuralMembers(Type type, StringComparer comparer)
    {
        var result = new Dictionary<string, StructuralMember>(comparer);
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length != 0)
                continue;

            result.TryAdd(property.Name, new StructuralMember(property.Name, value => property.GetValue(value)));
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            result.TryAdd(field.Name, new StructuralMember(field.Name, value => field.GetValue(value)));
        }

        return result;
    }

    private static bool StructuralValuesEqual(object? expected, object? actual, StructuralComparisonOptions options)
    {
        if (expected is string expectedString && actual is string actualString)
            return string.Equals(expectedString, actualString, options.StringComparison);

        return ValuesEqual(expected, actual);
    }

    private static bool IsSimpleStructuralValue(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(Uri);
    }

    private readonly struct StructuralDifference(string path, object? expectedValue, object? actualValue, string reason)
    {
        public string Path { get; } = path;
        public object? ExpectedValue { get; } = expectedValue;
        public object? ActualValue { get; } = actualValue;
        public string Reason { get; } = reason;
    }

    private readonly struct StructuralReferencePair(object expected, object actual) : IEquatable<StructuralReferencePair>
    {
        public object Expected { get; } = expected;
        public object Actual { get; } = actual;

        public bool Equals(StructuralReferencePair other)
        {
            return object.ReferenceEquals(Expected, other.Expected)
                && object.ReferenceEquals(Actual, other.Actual);
        }

        public override bool Equals(object? obj)
        {
            return obj is StructuralReferencePair other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(Expected), RuntimeHelpers.GetHashCode(Actual));
        }
    }

    private sealed class StructuralMember(string name, Func<object, object?> getValue)
    {
        public string Name { get; } = name;

        public object? GetValue(object obj)
        {
            return getValue(obj);
        }
    }

    private readonly struct StructuralComparisonOptions(bool ignoreCollectionOrder, StringComparer memberNameComparer, StringComparison stringComparison)
    {
        public bool IgnoreCollectionOrder { get; } = ignoreCollectionOrder;
        public StringComparer MemberNameComparer { get; } = memberNameComparer;
        public StringComparison StringComparison { get; } = stringComparison;

        public static StructuralComparisonOptions Create(EquivalentOptions? options)
        {
            if (options is null)
                return new StructuralComparisonOptions(ignoreCollectionOrder: false, StringComparer.Ordinal, StringComparison.Ordinal);

            return new StructuralComparisonOptions(
                options.IgnoreCollectionOrder,
                options.IgnoreMemberNameCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal,
                options.IgnoreStringCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }
}
