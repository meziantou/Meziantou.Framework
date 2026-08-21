using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private static readonly ConcurrentDictionary<StructuralMembersCacheKey, Dictionary<string, StructuralMember>> StructuralMembersCache = new();

    public static void Equivalent(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        Equivalent(expected, actual, options: null, message, actualExpression, expectedExpression);
    }

    public static void Equivalent(object? expected, object? actual, EquivalentOptions? options, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var failure = GetStructuralDifference(expected, actual, new StructuralPath(), new HashSet<StructuralReferencePair>(), [], StructuralComparisonOptions.Create(options));
        if (failure is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EquivalentAssertionError(failure.Value.ExpectedValue, failure.Value.ActualValue, failure.Value.Path, failure.Value.Reason, message, actualExpression, expectedExpression)));
    }

    private static StructuralDifference? GetStructuralDifference(object? expected, object? actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        if (object.ReferenceEquals(expected, actual))
            return null;

        if (expected is null || actual is null)
            return ValuesEqual(expected, actual) ? null : new StructuralDifference(path.ToString(), expected, actual, "Values differ.");

        var expectedType = expected.GetType();
        var actualType = actual.GetType();
        if (IsSimpleStructuralValue(expectedType) || IsSimpleStructuralValue(actualType))
            return StructuralValuesEqual(expected, actual, options) ? null : new StructuralDifference(path.ToString(), expected, actual, "Values differ.");

        if (!expectedType.IsValueType && !actualType.IsValueType)
        {
            var pair = new StructuralReferencePair(expected, actual);
            if (!visited.Add(pair))
                return null;

            visitedAdditions.Add(pair);
        }

        if (expected is System.Collections.IEnumerable expectedEnumerable && actual is System.Collections.IEnumerable actualEnumerable)
            return GetStructuralEnumerableDifference(expectedEnumerable, actualEnumerable, path, visited, visitedAdditions, options);

        return GetStructuralMemberDifference(expected, actual, path, visited, visitedAdditions, options);
    }

    private static StructuralDifference? GetStructuralEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        if (options.IgnoreCollectionOrder)
            return GetStructuralUnorderedEnumerableDifference(expected, actual, path, visited, visitedAdditions, options);

        return GetStructuralOrderedEnumerableDifference(expected, actual, path, visited, visitedAdditions, options);
    }

    private static StructuralDifference? GetStructuralOrderedEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
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

                if (!expectedHasNext && !actualHasNext)
                    return null;

                using var scope = path.Push(index);

                if (!expectedHasNext)
                    return new StructuralDifference(path.ToString(), StructuralMissingValue.Instance, actualEnumerator.Current, "Actual collection contains an unexpected item.");

                if (!actualHasNext)
                    return new StructuralDifference(path.ToString(), expectedEnumerator.Current, StructuralMissingValue.Instance, "Actual collection is missing an item.");

                var difference = GetStructuralDifference(expectedEnumerator.Current, actualEnumerator.Current, path, visited, visitedAdditions, options);
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

    private static StructuralDifference? GetStructuralUnorderedEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        var expectedItems = new List<object?>(EnumerateObjects(expected));
        var actualItems = new List<object?>(EnumerateObjects(actual));
        var matchedActualIndexes = new bool[actualItems.Count];

        for (var expectedIndex = 0; expectedIndex < expectedItems.Count; expectedIndex++)
        {
            using var scope = path.Push(expectedIndex);
            var expectedItem = expectedItems[expectedIndex];
            var found = false;

            for (var actualIndex = 0; actualIndex < actualItems.Count; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                var visitedAdditionsCount = visitedAdditions.Count;
                var depth = path.Depth;
                var difference = GetStructuralDifference(expectedItem, actualItems[actualIndex], path, visited, visitedAdditions, options);
                RollbackStructuralVisitedAdditions(visited, visitedAdditions, visitedAdditionsCount);
                if (difference is not null)
                {
                    // A rejected candidate leaves the segments of the mismatch behind; drop them before the next one.
                    path.TruncateTo(depth);
                    continue;
                }

                matchedActualIndexes[actualIndex] = true;
                found = true;
                break;
            }

            if (!found)
                return new StructuralDifference(path.ToString(), expectedItem, StructuralMissingValue.Instance, "Actual collection is missing an equivalent item.");
        }

        for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
        {
            if (matchedActualIndexes[actualIndex])
                continue;

            using var scope = path.Push(actualIndex);
            return new StructuralDifference(path.ToString(), StructuralMissingValue.Instance, actualItems[actualIndex], "Actual collection contains an unexpected item.");
        }

        return null;
    }

    private static void RollbackStructuralVisitedAdditions(HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, int count)
    {
        for (var index = visitedAdditions.Count - 1; index >= count; index--)
        {
            visited.Remove(visitedAdditions[index]);
            visitedAdditions.RemoveAt(index);
        }
    }

    private static StructuralDifference? GetStructuralMemberDifference(object expected, object actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        var expectedMembers = GetStructuralMembers(expected.GetType(), options.MemberNameComparer);
        var actualMembers = GetStructuralMembers(actual.GetType(), options.MemberNameComparer);

        foreach (var expectedMember in expectedMembers.Values)
        {
            using var scope = path.Push(expectedMember.Name);
            if (!actualMembers.TryGetValue(expectedMember.Name, out var actualMember))
                return new StructuralDifference(path.ToString(), expectedMember.GetValue(expected), StructuralMissingValue.Instance, "Actual member is missing.");

            var difference = GetStructuralDifference(expectedMember.GetValue(expected), actualMember.GetValue(actual), path, visited, visitedAdditions, options);
            if (difference is not null)
                return difference;
        }

        foreach (var actualMember in actualMembers.Values)
        {
            if (!expectedMembers.ContainsKey(actualMember.Name))
            {
                using var scope = path.Push(actualMember.Name);
                return new StructuralDifference(path.ToString(), StructuralMissingValue.Instance, actualMember.GetValue(actual), "Actual member is unexpected.");
            }
        }

        return null;
    }

    private static Dictionary<string, StructuralMember> GetStructuralMembers(Type type, StringComparer comparer)
    {
        return StructuralMembersCache.GetOrAdd(new StructuralMembersCacheKey(type, comparer), CreateStructuralMembers);
    }

    private static Dictionary<string, StructuralMember> CreateStructuralMembers(StructuralMembersCacheKey cacheKey)
    {
        var result = new Dictionary<string, StructuralMember>(cacheKey.Comparer);
        foreach (var property in cacheKey.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length != 0)
                continue;

            result.TryAdd(property.Name, new StructuralMember(property.Name, property));
        }

        foreach (var field in cacheKey.Type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            result.TryAdd(field.Name, new StructuralMember(field.Name, field));
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

    /// <summary>
    /// Tracks where the comparison currently is. Segments are pushed and popped as the walk descends and unwinds,
    /// and the string is built only when a difference is found, so a comparison that succeeds formats no path at all.
    /// </summary>
    private sealed class StructuralPath
    {
        private readonly List<StructuralPathSegment> _segments = [];

        public int Depth => _segments.Count;

        public Scope Push(string memberName) => Push(new StructuralPathSegment(memberName, index: -1));

        public Scope Push(int index) => Push(new StructuralPathSegment(memberName: null, index));

        public void TruncateTo(int depth) => _segments.RemoveRange(depth, _segments.Count - depth);

        public override string ToString()
        {
            var builder = new StringBuilder("$");
            foreach (var segment in _segments)
            {
                if (segment.MemberName is null)
                {
                    builder.Append('[').Append(segment.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
                else
                {
                    builder.Append('.').Append(segment.MemberName);
                }
            }

            return builder.ToString();
        }

        private Scope Push(StructuralPathSegment segment)
        {
            _segments.Add(segment);

            return new Scope(this);
        }

        public readonly struct Scope(StructuralPath path) : IDisposable
        {
            public void Dispose() => path._segments.RemoveAt(path._segments.Count - 1);
        }
    }

    private readonly struct StructuralPathSegment(string? memberName, int index)
    {
        public string? MemberName { get; } = memberName;
        public int Index { get; } = index;
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

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is StructuralReferencePair other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Expected), RuntimeHelpers.GetHashCode(Actual));
    }

    private sealed class StructuralMember(string name, MemberInfo member)
    {
        public string Name { get; } = name;

        public object? GetValue(object obj)
        {
            return member switch
            {
                PropertyInfo property => property.GetValue(obj),
                FieldInfo field => field.GetValue(obj),
                _ => throw new UnreachableException(),
            };
        }
    }

    private readonly struct StructuralMembersCacheKey(Type type, StringComparer comparer) : IEquatable<StructuralMembersCacheKey>
    {
        public Type Type { get; } = type;
        public StringComparer Comparer { get; } = comparer;

        public bool Equals(StructuralMembersCacheKey other)
        {
            return Type == other.Type
                && Comparer == other.Comparer;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is StructuralMembersCacheKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Type, Comparer);
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
