using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private static readonly ConcurrentDictionary<StructuralMembersCacheKey, Dictionary<string, StructuralMember>> StructuralMembersCache = new();
    private static readonly ConcurrentDictionary<Type, bool> StructuralNumberTypes = new();
    private static readonly ConcurrentDictionary<Type, StructuralCollectionKind> StructuralCollectionKinds = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> StructuralSequenceToArrayMethods = new();

    public static void Equivalent(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        Equivalent(expected, actual, options: null, message, actualExpression, expectedExpression);
    }

    public static void Equivalent(object? expected, object? actual, EquivalentOptions? options, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparisonOptions = StructuralComparisonOptions.Create(options);

        // Comparing two values that are not walked structurally needs none of the state the walk carries, so the
        // common case of a scalar or a string allocates nothing.
        if (TryCompareStructuralLeaves(expected, actual, comparisonOptions, out var areEquivalent))
        {
            if (areEquivalent)
                return;

            throw new AssertionException(ErrorFormatter.Format(new EquivalentAssertionError(expected, actual, StructuralPath.Root, "Values differ.", message, actualExpression, expectedExpression)));
        }

        var failure = GetStructuralDifference(expected, actual, new StructuralPath(), new HashSet<StructuralReferencePair>(), [], comparisonOptions);
        if (failure is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EquivalentAssertionError(failure.Value.ExpectedValue, failure.Value.ActualValue, failure.Value.Path, failure.Value.Reason, message, actualExpression, expectedExpression)));
    }

    /// <summary>Compares the values when neither of them needs to be walked, and reports whether it could.</summary>
    private static bool TryCompareStructuralLeaves(object? expected, object? actual, StructuralComparisonOptions options, out bool areEquivalent)
    {
        if (object.ReferenceEquals(expected, actual))
        {
            areEquivalent = true;
            return true;
        }

        if (expected is null || actual is null)
        {
            areEquivalent = ValuesEqual(expected, actual);
            return true;
        }

        if (IsSimpleStructuralValue(expected.GetType()) || IsSimpleStructuralValue(actual.GetType()))
        {
            areEquivalent = StructuralValuesEqual(expected, actual, options);
            return true;
        }

        areEquivalent = false;
        return false;
    }

    private static StructuralDifference? GetStructuralDifference(object? expected, object? actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        if (TryCompareStructuralLeaves(expected, actual, options, out var areEquivalent))
            return areEquivalent ? null : new StructuralDifference(path.ToString(), expected, actual, "Values differ.");

        Debug.Assert(expected is not null);
        Debug.Assert(actual is not null);
        var expectedType = expected.GetType();
        var actualType = actual.GetType();
        if (!expectedType.IsValueType && !actualType.IsValueType)
        {
            var pair = new StructuralReferencePair(expected, actual);
            if (!visited.Add(pair))
                return null;

            visitedAdditions.Add(pair);
        }

        // Memory<T>, ReadOnlyMemory<T> and ReadOnlySequence<T> are sequences that do not implement IEnumerable. Walking
        // their members would compare where the content is stored rather than the content itself.
        if (TryGetStructuralSequenceItems(expected, out var expectedSequenceItems))
        {
            expected = expectedSequenceItems;
        }

        if (TryGetStructuralSequenceItems(actual, out var actualSequenceItems))
        {
            actual = actualSequenceItems;
        }

        if (expected is System.Collections.IEnumerable expectedEnumerable && actual is System.Collections.IEnumerable actualEnumerable)
            return GetStructuralEnumerableDifference(expectedEnumerable, actualEnumerable, path, visited, visitedAdditions, options);

        return GetStructuralMemberDifference(expected, actual, path, visited, visitedAdditions, options);
    }

    private static StructuralDifference? GetStructuralEnumerableDifference(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        var expectedKind = GetStructuralCollectionKind(expected.GetType());
        var actualKind = GetStructuralCollectionKind(actual.GetType());

        // The position of an entry in a dictionary is not part of its value, so entries are matched by key. The
        // entries are read before deciding, as a type can implement a dictionary interface and still enumerate
        // something else than key/value pairs.
        if (expectedKind is StructuralCollectionKind.Dictionary && actualKind is StructuralCollectionKind.Dictionary
            && TryGetStructuralDictionaryEntries(expected, out var expectedEntries)
            && TryGetStructuralDictionaryEntries(actual, out var actualEntries))
        {
            return GetStructuralDictionaryDifference(expectedEntries, actualEntries, path, visited, visitedAdditions, options);
        }

        // A set has no meaningful order, so comparing it by position against anything would depend on its internal layout.
        if (options.IgnoreCollectionOrder || expectedKind is StructuralCollectionKind.Set || actualKind is StructuralCollectionKind.Set)
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
        var matches = MatchStructuralItems(expectedItems, actualItems, path, visited, visitedAdditions, options, out var matchedActualIndexes);

        for (var expectedIndex = 0; expectedIndex < matches.Length; expectedIndex++)
        {
            if (matches[expectedIndex] >= 0)
                continue;

            using var scope = path.Push(expectedIndex);
            return new StructuralDifference(path.ToString(), expectedItems[expectedIndex], StructuralMissingValue.Instance, "Actual collection is missing an equivalent item.");
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

    private static StructuralDifference? GetStructuralDictionaryDifference(List<KeyValuePair<object?, object?>> expectedEntries, List<KeyValuePair<object?, object?>> actualEntries, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options)
    {
        var expectedKeys = new List<object?>(expectedEntries.Count);
        foreach (var entry in expectedEntries)
        {
            expectedKeys.Add(entry.Key);
        }

        var actualKeys = new List<object?>(actualEntries.Count);
        foreach (var entry in actualEntries)
        {
            actualKeys.Add(entry.Key);
        }

        var matches = MatchStructuralItems(expectedKeys, actualKeys, path, visited, visitedAdditions, options, out var matchedActualIndexes);

        for (var expectedIndex = 0; expectedIndex < matches.Length; expectedIndex++)
        {
            var expectedEntry = expectedEntries[expectedIndex];
            using var scope = path.PushKey(expectedEntry.Key);
            var actualIndex = matches[expectedIndex];
            if (actualIndex < 0)
                return new StructuralDifference(path.ToString(), expectedEntry.Value, StructuralMissingValue.Instance, "Actual dictionary is missing a key.");

            var difference = GetStructuralDifference(expectedEntry.Value, actualEntries[actualIndex].Value, path, visited, visitedAdditions, options);
            if (difference is not null)
                return difference;
        }

        for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
        {
            if (matchedActualIndexes[actualIndex])
                continue;

            var actualEntry = actualEntries[actualIndex];
            using var scope = path.PushKey(actualEntry.Key);
            return new StructuralDifference(path.ToString(), StructuralMissingValue.Instance, actualEntry.Value, "Actual dictionary contains an unexpected key.");
        }

        return null;
    }

    /// <summary>
    /// Pairs each expected item with an equivalent actual item, and returns the index of the actual item matched by each
    /// expected item, or -1 when there is none.
    /// </summary>
    private static int[] MatchStructuralItems(List<object?> expectedItems, List<object?> actualItems, StructuralPath path, HashSet<StructuralReferencePair> visited, List<StructuralReferencePair> visitedAdditions, StructuralComparisonOptions options, out bool[] matchedActualIndexes)
    {
        var matches = new int[expectedItems.Count];
        matchedActualIndexes = new bool[actualItems.Count];

        // Keys and set items are usually leaves such as strings or numbers. Equal leaves are equivalent, so they are
        // paired through a hash lookup first, and only the remaining items need the quadratic structural search. The
        // lookup is restricted to leaves because hashing an arbitrary object can recurse forever on a cyclic graph.
        var leafComparer = options.StringComparison is StringComparison.Ordinal ? StructuralLeafEqualityComparer.Ordinal : StructuralLeafEqualityComparer.OrdinalIgnoreCase;
        Dictionary<object, int>? firstActualIndexByLeaf = null;
        int[]? nextActualIndexWithSameLeaf = null;
        for (var actualIndex = actualItems.Count - 1; actualIndex >= 0; actualIndex--)
        {
            var actualItem = actualItems[actualIndex];
            if (actualItem is null || !IsSimpleStructuralValue(actualItem.GetType()))
                continue;

            firstActualIndexByLeaf ??= new Dictionary<object, int>(leafComparer);
            nextActualIndexWithSameLeaf ??= new int[actualItems.Count];
            nextActualIndexWithSameLeaf[actualIndex] = firstActualIndexByLeaf.TryGetValue(actualItem, out var nextIndex) ? nextIndex : -1;
            firstActualIndexByLeaf[actualItem] = actualIndex;
        }

        for (var expectedIndex = 0; expectedIndex < expectedItems.Count; expectedIndex++)
        {
            matches[expectedIndex] = -1;
            var expectedItem = expectedItems[expectedIndex];
            if (firstActualIndexByLeaf is null || expectedItem is null || !IsSimpleStructuralValue(expectedItem.GetType()))
                continue;

            if (firstActualIndexByLeaf.TryGetValue(expectedItem, out var actualIndex) && actualIndex >= 0)
            {
                matches[expectedIndex] = actualIndex;
                matchedActualIndexes[actualIndex] = true;
                firstActualIndexByLeaf[expectedItem] = nextActualIndexWithSameLeaf![actualIndex];
            }
        }

        for (var expectedIndex = 0; expectedIndex < expectedItems.Count; expectedIndex++)
        {
            if (matches[expectedIndex] >= 0)
                continue;

            for (var actualIndex = 0; actualIndex < actualItems.Count; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                var visitedAdditionsCount = visitedAdditions.Count;
                var depth = path.Depth;
                var difference = GetStructuralDifference(expectedItems[expectedIndex], actualItems[actualIndex], path, visited, visitedAdditions, options);
                RollbackStructuralVisitedAdditions(visited, visitedAdditions, visitedAdditionsCount);

                // A rejected candidate leaves the segments of the mismatch behind; drop them before the next one.
                path.TruncateTo(depth);
                if (difference is not null)
                    continue;

                matches[expectedIndex] = actualIndex;
                matchedActualIndexes[actualIndex] = true;
                break;
            }

            // Callers report the first unmatched expected item, so matching the following ones would be wasted work.
            if (matches[expectedIndex] < 0)
                break;
        }

        return matches;
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

        // A value that exposes nothing to compare keeps its state private. Treating it as equivalent to any other such
        // value would make every assertion on it pass, so its own equality decides.
        if (expectedMembers.Count == 0 && actualMembers.Count == 0)
            return ValuesEqual(expected, actual) ? null : new StructuralDifference(path.ToString(), expected, actual, "Values differ.");

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
            if (property.GetMethod is null || property.GetIndexParameters().Length != 0 || !CanReadStructuralMember(property.PropertyType))
                continue;

            result.TryAdd(property.Name, new StructuralMember(property.Name, property));
        }

        foreach (var field in cacheKey.Type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!CanReadStructuralMember(field.FieldType))
                continue;

            result.TryAdd(field.Name, new StructuralMember(field.Name, field));
        }

        return result;
    }

    /// <summary>Reports whether reflection can read a member of the type as an object that can be compared.</summary>
    private static bool CanReadStructuralMember(Type type)
    {
        if (type.IsByRef)
        {
            type = type.GetElementType()!;
        }

        // A ref struct such as Span<T> cannot be boxed, so reading it throws, and a pointer is an address rather than a value.
        return !type.IsByRefLike && !type.IsPointer && !type.IsFunctionPointer;
    }

    private static bool TryGetStructuralSequenceItems(object value, [NotNullWhen(true)] out System.Collections.IEnumerable? items)
    {
        if (TryGetMemoryItems(value, out items))
            return true;

        var toArrayMethod = StructuralSequenceToArrayMethods.GetOrAdd(value.GetType(), GetStructuralSequenceToArrayMethod);
        if (toArrayMethod is null)
            return false;

        items = (System.Collections.IEnumerable?)toArrayMethod.Invoke(obj: null, [value]);
        return items is not null;
    }

    private static MethodInfo? GetStructuralSequenceToArrayMethod(Type type)
    {
        if (!type.IsConstructedGenericType || type.GetGenericTypeDefinition() != typeof(System.Buffers.ReadOnlySequence<>))
            return null;

        return typeof(System.Buffers.BuffersExtensions).GetMethod(nameof(System.Buffers.BuffersExtensions.ToArray), PublicStatic)!.MakeGenericMethod(type.GetGenericArguments());
    }

    private static StructuralCollectionKind GetStructuralCollectionKind(Type type)
    {
        return StructuralCollectionKinds.GetOrAdd(type, CreateStructuralCollectionKind);
    }

    private static StructuralCollectionKind CreateStructuralCollectionKind(Type type)
    {
        var result = StructuralCollectionKind.Sequence;
        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface == typeof(System.Collections.IDictionary))
                return StructuralCollectionKind.Dictionary;

            if (!@interface.IsGenericType)
                continue;

            var definition = @interface.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                return StructuralCollectionKind.Dictionary;

            if (definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>))
            {
                result = StructuralCollectionKind.Set;
            }
        }

        return result;
    }

    private static bool TryGetStructuralDictionaryEntries(System.Collections.IEnumerable dictionary, [NotNullWhen(true)] out List<KeyValuePair<object?, object?>>? entries)
    {
        entries = [];
        if (dictionary is System.Collections.IDictionary nonGenericDictionary)
        {
            var enumerator = nonGenericDictionary.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    entries.Add(new KeyValuePair<object?, object?>(enumerator.Key, enumerator.Value));
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }

            return true;
        }

        foreach (var item in dictionary)
        {
            if (item?.GetType() is not { IsGenericType: true } itemType || itemType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
            {
                entries = null;
                return false;
            }

            var members = GetStructuralMembers(itemType, StringComparer.Ordinal);
            entries.Add(new KeyValuePair<object?, object?>(members[nameof(KeyValuePair<object, object>.Key)].GetValue(item), members[nameof(KeyValuePair<object, object>.Value)].GetValue(item)));
        }

        return true;
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
            || type == typeof(Uri)
            || StructuralNumberTypes.GetOrAdd(type, IsNumberType);
    }

    /// <summary>
    /// Reports whether the type is a number, such as <see cref="Int128"/>, <see cref="Half"/> or <see cref="System.Numerics.BigInteger"/>.
    /// The public properties of a number describe it (IsZero, Sign, …) without identifying it, so a number is a leaf.
    /// </summary>
    private static bool IsNumberType(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(System.Numerics.INumberBase<>))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Tracks where the comparison currently is. Segments are pushed and popped as the walk descends and unwinds,
    /// and the string is built only when a difference is found, so a comparison that succeeds formats no path at all.
    /// </summary>
    private sealed class StructuralPath
    {
        public const string Root = "$";

        private readonly List<StructuralPathSegment> _segments = [];

        public int Depth => _segments.Count;

        public Scope Push(string memberName) => Push(new StructuralPathSegment(memberName, index: -1));

        public Scope Push(int index) => Push(new StructuralPathSegment(memberName: null, index));

        public Scope PushKey(object? key) => Push(new StructuralPathSegment(key));

        public void TruncateTo(int depth) => _segments.RemoveRange(depth, _segments.Count - depth);

        public override string ToString()
        {
            var builder = new StringBuilder(Root);
            foreach (var segment in _segments)
            {
                if (segment.IsKey)
                {
                    builder.Append('[').Append(FormatKey(segment.Key)).Append(']');
                }
                else if (segment.MemberName is null)
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

        private static string FormatKey(object? key)
        {
            return key switch
            {
                null => "<null>",
                string value => AssertionFormatter.FormatStringValue(value, highlightedIndex: null),
                IFormattable value => value.ToString(format: null, CultureInfo.InvariantCulture),
                _ => key.ToString() ?? string.Empty,
            };
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

    private readonly struct StructuralPathSegment
    {
        public StructuralPathSegment(string? memberName, int index)
        {
            MemberName = memberName;
            Index = index;
        }

        public StructuralPathSegment(object? key)
        {
            Key = key;
            IsKey = true;
            Index = -1;
        }

        public string? MemberName { get; }
        public int Index { get; }
        public object? Key { get; }
        public bool IsKey { get; }
    }

    private enum StructuralCollectionKind
    {
        Sequence,
        Dictionary,
        Set,
    }

    /// <summary>Compares leaves the way <see cref="StructuralValuesEqual"/> does when they are equal, so a match is always equivalent.</summary>
    private sealed class StructuralLeafEqualityComparer(StringComparer stringComparer) : IEqualityComparer<object>
    {
        public static StructuralLeafEqualityComparer Ordinal { get; } = new(StringComparer.Ordinal);
        public static StructuralLeafEqualityComparer OrdinalIgnoreCase { get; } = new(StringComparer.OrdinalIgnoreCase);

        public new bool Equals(object? x, object? y)
        {
            if (x is string xString && y is string yString)
                return stringComparer.Equals(xString, yString);

            return object.Equals(x, y);
        }

        public int GetHashCode(object obj)
        {
            if (obj is string value)
                return stringComparer.GetHashCode(value);

            return obj.GetHashCode();
        }
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
