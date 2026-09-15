using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private const string StructuralValuesDifferReason = "Values differ.";
    private const string StructuralMemberThrewReason = "Member getter threw an exception.";

    private static readonly ConcurrentDictionary<Type, StructuralTypeInfo> StructuralTypeInfos = new();
    private static readonly ConcurrentDictionary<StructuralMemberPairsCacheKey, StructuralMemberPairing> StructuralMemberPairingsCache = new();

    /// <summary>Public key tokens of the assemblies that ship with .NET.</summary>
    private static readonly string[] FrameworkPublicKeyTokens = ["7cec85d7bea7798e", "b03f5f7f11d50a3a", "cc7b13ffcd2ddd51", "b77a5c561934e089", "31bf3856ad364e35"];

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

            throw new AssertionException(ErrorFormatter.Format(new EquivalentAssertionError(expected, actual, StructuralPath.Root, StructuralValuesDifferReason, message, actualExpression, expectedExpression)));
        }

        var comparer = new StructuralComparer(comparisonOptions, reportDifference: true);
        if (comparer.AreEquivalent(expected, actual))
            return;

        var difference = comparer.Difference;
        Debug.Assert(difference is not null);
        throw new AssertionException(ErrorFormatter.Format(new EquivalentAssertionError(difference.ExpectedValue, difference.ActualValue, difference.Path, difference.Reason, message, actualExpression, expectedExpression)));
    }

    /// <summary>Reports whether the values are equivalent without describing how they differ.</summary>
    private static bool AreStructurallyEquivalent(object? expected, object? actual, StructuralComparisonOptions options)
    {
        if (TryCompareStructuralLeaves(expected, actual, options, out var areEquivalent))
            return areEquivalent;

        return new StructuralComparer(options, reportDifference: false).AreEquivalent(expected, actual);
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
            areEquivalent = false;
            return true;
        }

        if (GetStructuralTypeInfo(expected.GetType()).Kind is StructuralTypeKind.Leaf || GetStructuralTypeInfo(actual.GetType()).Kind is StructuralTypeKind.Leaf)
        {
            areEquivalent = StructuralValuesEqual(expected, actual, options);
            return true;
        }

        areEquivalent = false;
        return false;
    }

    private static bool StructuralValuesEqual(object? expected, object? actual, StructuralComparisonOptions options)
    {
        if (expected is string expectedString && actual is string actualString)
            return string.Equals(expectedString, actualString, options.StringComparison);

        return ValuesEqual(expected, actual);
    }

    private static StructuralTypeInfo GetStructuralTypeInfo(Type type)
    {
        return StructuralTypeInfos.GetOrAdd(type, CreateStructuralTypeInfo);
    }

    private static StructuralTypeInfo CreateStructuralTypeInfo(Type type)
    {
        if (type == typeof(StructuralThrownValue))
            return new StructuralTypeInfo(type, StructuralTypeKind.ThrownValue);

        if (IsSimpleStructuralValue(type))
            return new StructuralTypeInfo(type, StructuralTypeKind.Leaf) { IsHashSafeLeaf = IsHashSafeLeafType(type) };

        // The namespace is checked first, so comparing unrelated values does not load System.Text.Json or System.Xml.Linq.
        if (IsDefinedInNamespace(type, "System.Text.Json") && IsJsonType(type))
            return new StructuralTypeInfo(type, StructuralTypeKind.Json);

        if (IsDefinedInNamespace(type, "System.Xml.Linq") && IsXmlType(type))
            return new StructuralTypeInfo(type, StructuralTypeKind.Xml);

        if (type == typeof(StringBuilder))
            return new StructuralTypeInfo(type, StructuralTypeKind.StringBuilder);

        if (typeof(FileSystemInfo).IsAssignableFrom(type))
            return new StructuralTypeInfo(type, StructuralTypeKind.FileSystemInfo);

        if (typeof(Regex).IsAssignableFrom(type))
            return new StructuralTypeInfo(type, StructuralTypeKind.Regex);

        // Memory<T>, ReadOnlyMemory<T> and ReadOnlySequence<T> are sequences that do not implement IEnumerable. Walking
        // their members would compare where the content is stored rather than the content itself.
        if (IsMemoryType(type))
        {
            var toArrayMethod = type.GetMethod(nameof(Memory<int>.ToArray), Type.EmptyTypes)!;
            return new StructuralTypeInfo(type, StructuralTypeKind.Object) { GetSequenceItems = value => (System.Collections.IEnumerable)toArrayMethod.Invoke(value, parameters: null)! };
        }

        if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(System.Buffers.ReadOnlySequence<>))
        {
            var toArrayMethod = typeof(System.Buffers.BuffersExtensions).GetMethod(nameof(System.Buffers.BuffersExtensions.ToArray), PublicStatic)!.MakeGenericMethod(type.GetGenericArguments());
            return new StructuralTypeInfo(type, StructuralTypeKind.Object) { GetSequenceItems = value => (System.Collections.IEnumerable)toArrayMethod.Invoke(obj: null, [value])! };
        }

        if (type.IsArray && type.GetArrayRank() > 1)
            return new StructuralTypeInfo(type, StructuralTypeKind.MultiDimensionalArray);

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return new StructuralTypeInfo(type, StructuralTypeKind.Enumerable) { CollectionKind = GetStructuralCollectionKind(type) };

        return new StructuralTypeInfo(type, StructuralTypeKind.Object)
        {
            UsesEquality = UsesFrameworkEquality(type),
            IsKeyValuePair = type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>),
        };
    }

    private static bool IsSimpleStructuralValue(Type type)
    {
        return IsHashSafeLeafType(type)
            || type == typeof(Uri)
            || type == typeof(Version)
            || typeof(MemberInfo).IsAssignableFrom(type)
            || typeof(Assembly).IsAssignableFrom(type)
            || typeof(Module).IsAssignableFrom(type)
            || typeof(CultureInfo).IsAssignableFrom(type)
            || typeof(System.Net.IPAddress).IsAssignableFrom(type)
            || IsNumberType(type);
    }

    /// <summary>
    /// Reports whether values of the type are equivalent exactly when <see cref="object.Equals(object)"/> says so, and
    /// hash consistently with it. Only such values can be paired or told apart through their hash code.
    /// </summary>
    private static bool IsHashSafeLeafType(Type type)
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
            || type == typeof(Guid);
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

    private static bool IsDefinedInNamespace(Type type, string @namespace)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Namespace is { } currentNamespace && currentNamespace.StartsWith(@namespace, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsJsonType(Type type)
    {
        return type == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsXmlType(Type type)
    {
        return typeof(XNode).IsAssignableFrom(type) || typeof(XAttribute).IsAssignableFrom(type);
    }

    /// <summary>
    /// Reports whether two values of the type are compared with their own equality rather than member by member. A type
    /// that ships with .NET and defines its equality describes a value (IPAddress, CultureInfo, Encoding, …), and its
    /// public members often do not identify that value. Tuples are still walked so that their items are compared structurally.
    /// </summary>
    private static bool UsesFrameworkEquality(Type type)
    {
        if (typeof(ITuple).IsAssignableFrom(type) || !IsFrameworkAssembly(type.Assembly))
            return false;

        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsConstructedGenericType && @interface.GetGenericTypeDefinition() == typeof(IEquatable<>) && @interface.GenericTypeArguments[0] == type)
                return true;
        }

        var equalsMethod = type.GetMethod(nameof(object.Equals), BindingFlags.Public | BindingFlags.Instance, [typeof(object)]);
        return equalsMethod?.DeclaringType is { } declaringType && declaringType != typeof(object) && declaringType != typeof(ValueType);
    }

    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        var name = assembly.GetName();
        if (name.Name is not ("System" or "mscorlib" or "netstandard") && name.Name?.StartsWith("System.", StringComparison.Ordinal) is not true)
            return false;

        // A third-party package can use a System.* name, so the assembly must also be signed by Microsoft.
        var publicKeyToken = name.GetPublicKeyToken();
        return publicKeyToken is { Length: > 0 } && FrameworkPublicKeyTokens.Contains(Convert.ToHexStringLower(publicKeyToken), StringComparer.Ordinal);
    }

    private static StructuralCollectionKind GetStructuralCollectionKind(Type type)
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

    private static StructuralMemberPairing GetStructuralMemberPairing(StructuralTypeInfo expectedInfo, StructuralTypeInfo actualInfo, bool ignoreMemberNameCase)
    {
        if (expectedInfo == actualInfo)
            return expectedInfo.MemberSet.IdentityPairing;

        return StructuralMemberPairingsCache.GetOrAdd(new StructuralMemberPairsCacheKey(expectedInfo.Type, actualInfo.Type, ignoreMemberNameCase), CreateStructuralMemberPairing);
    }

    /// <summary>
    /// Pairs the members of two types by name. Members with the same name are paired first, so when the case of names is
    /// ignored, members whose names differ only by case are still all compared rather than one of them being dropped.
    /// </summary>
    private static StructuralMemberPairing CreateStructuralMemberPairing(StructuralMemberPairsCacheKey key)
    {
        var expectedMembers = GetStructuralTypeInfo(key.ExpectedType).MemberSet;
        var actualMembers = GetStructuralTypeInfo(key.ActualType).MemberSet;
        var actualIndexes = new int[expectedMembers.Members.Length];
        var matchedActualIndexes = new bool[actualMembers.Members.Length];
        for (var expectedIndex = 0; expectedIndex < expectedMembers.Members.Length; expectedIndex++)
        {
            actualIndexes[expectedIndex] = -1;
            if (actualMembers.IndexByName.TryGetValue(expectedMembers.Members[expectedIndex].Name, out var actualIndex))
            {
                actualIndexes[expectedIndex] = actualIndex;
                matchedActualIndexes[actualIndex] = true;
            }
        }

        if (key.IgnoreMemberNameCase)
        {
            for (var expectedIndex = 0; expectedIndex < expectedMembers.Members.Length; expectedIndex++)
            {
                if (actualIndexes[expectedIndex] >= 0)
                    continue;

                for (var actualIndex = 0; actualIndex < actualMembers.Members.Length; actualIndex++)
                {
                    if (!matchedActualIndexes[actualIndex] && string.Equals(expectedMembers.Members[expectedIndex].Name, actualMembers.Members[actualIndex].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        actualIndexes[expectedIndex] = actualIndex;
                        matchedActualIndexes[actualIndex] = true;
                        break;
                    }
                }
            }
        }

        var result = new List<StructuralMemberPair>(expectedMembers.Members.Length + actualMembers.Members.Length);
        for (var expectedIndex = 0; expectedIndex < expectedMembers.Members.Length; expectedIndex++)
        {
            var actualIndex = actualIndexes[expectedIndex];
            result.Add(new StructuralMemberPair(expectedMembers.Members[expectedIndex], actualIndex >= 0 ? actualMembers.Members[actualIndex] : null));
        }

        for (var actualIndex = 0; actualIndex < actualMembers.Members.Length; actualIndex++)
        {
            if (!matchedActualIndexes[actualIndex])
            {
                result.Add(new StructuralMemberPair(expected: null, actualMembers.Members[actualIndex]));
            }
        }

        return new StructuralMemberPairing([.. result]);
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

        // The entries are read before deciding, as a type can implement a dictionary interface and still enumerate
        // something else than key/value pairs.
        foreach (var item in dictionary)
        {
            if (item is null || GetStructuralTypeInfo(item.GetType()) is not { IsKeyValuePair: true } itemInfo)
            {
                entries = null;
                return false;
            }

            var memberSet = itemInfo.MemberSet;
            var key = memberSet.Members[memberSet.IndexByName[nameof(KeyValuePair<object, object>.Key)]].GetValue(item);
            var value = memberSet.Members[memberSet.IndexByName[nameof(KeyValuePair<object, object>.Value)]].GetValue(item);
            entries.Add(new KeyValuePair<object?, object?>(key, value));
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object NormalizeJsonValue(object value)
    {
        // A parsed JsonValue wraps a JsonElement; unwrapping it lets a JsonNode be compared with a JsonElement.
        return value is JsonValue jsonValue && jsonValue.TryGetValue<JsonElement>(out var element) ? element : value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StructuralJsonShape GetJsonShape(object value)
    {
        return value switch
        {
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.Object => StructuralJsonShape.Object,
                JsonValueKind.Array => StructuralJsonShape.Array,
                JsonValueKind.String => StructuralJsonShape.String,
                _ => StructuralJsonShape.Scalar,
            },
            JsonObject => StructuralJsonShape.Object,
            JsonArray => StructuralJsonShape.Array,
            JsonValue jsonValue when jsonValue.GetValueKind() is JsonValueKind.String && jsonValue.TryGetValue<string>(out _) => StructuralJsonShape.String,
            JsonValue => StructuralJsonShape.Scalar,
            _ => StructuralJsonShape.None,
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<KeyValuePair<object?, object?>> GetJsonObjectEntries(object value)
    {
        var entries = new List<KeyValuePair<object?, object?>>();
        if (value is JsonElement element)
        {
            foreach (var property in element.EnumerateObject())
            {
                // A JSON null is null, the way JsonNode represents it.
                entries.Add(new KeyValuePair<object?, object?>(property.Name, property.Value.ValueKind is JsonValueKind.Null ? null : property.Value));
            }
        }
        else
        {
            foreach (var property in (JsonObject)value)
            {
                entries.Add(new KeyValuePair<object?, object?>(property.Key, property.Value));
            }
        }

        return entries;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<object?> GetJsonArrayItems(object value)
    {
        var items = new List<object?>();
        if (value is JsonElement element)
        {
            foreach (var item in element.EnumerateArray())
            {
                items.Add(item.ValueKind is JsonValueKind.Null ? null : item);
            }
        }
        else
        {
            foreach (var item in (JsonArray)value)
            {
                items.Add(item);
            }
        }

        return items;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GetJsonString(object value)
    {
        return value is JsonElement element ? element.GetString()! : ((JsonValue)value).GetValue<string>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool JsonScalarsEqual(object expected, object actual)
    {
        if (expected is JsonElement expectedElement && actual is JsonElement actualElement)
        {
            if (expectedElement.ValueKind is JsonValueKind.Undefined || actualElement.ValueKind is JsonValueKind.Undefined)
                return expectedElement.ValueKind == actualElement.ValueKind;

            return JsonElement.DeepEquals(expectedElement, actualElement);
        }

        if (expected is JsonElement { ValueKind: JsonValueKind.Undefined } || actual is JsonElement { ValueKind: JsonValueKind.Undefined })
            return false;

        return JsonNode.DeepEquals(ToJsonNode(expected), ToJsonNode(actual));

        static JsonNode? ToJsonNode(object value) => value is JsonElement element ? JsonValue.Create(element) : (JsonNode)value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool XmlValuesEqual(object expected, object actual)
    {
        return (expected, actual) switch
        {
            (XNode expectedNode, XNode actualNode) => XNode.DeepEquals(expectedNode, actualNode),
            (XAttribute expectedAttribute, XAttribute actualAttribute) => expectedAttribute.Name == actualAttribute.Name && string.Equals(expectedAttribute.Value, actualAttribute.Value, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string FormatArrayDimensions(Array array)
    {
        var builder = new StringBuilder();
        for (var dimension = 0; dimension < array.Rank; dimension++)
        {
            if (dimension > 0)
            {
                builder.Append('x');
            }

            builder.Append(array.GetLength(dimension).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Walks two values to decide whether they are equivalent. Describing a difference is only needed for the one that is
    /// reported, so the path is formatted only while reporting is enabled, and never while trying candidate pairings.
    /// </summary>
    private sealed class StructuralComparer(StructuralComparisonOptions options, bool reportDifference)
    {
        private readonly HashSet<StructuralReferencePair> _visited = [];
        private readonly List<StructuralReferencePair> _visitedAdditions = [];
        private readonly StructuralPath _path = new();
        private int _suppressedReportingCount = reportDifference ? 0 : 1;

        public StructuralDifference? Difference { get; private set; }

        private bool IsReporting => _suppressedReportingCount is 0;

        public bool AreEquivalent(object? expected, object? actual)
        {
            if (object.ReferenceEquals(expected, actual))
                return true;

            if (expected is null || actual is null)
                return Fail(expected, actual, (expected ?? actual) is StructuralThrownValue ? StructuralMemberThrewReason : StructuralValuesDifferReason);

            var expectedInfo = GetStructuralTypeInfo(expected.GetType());
            var actualInfo = GetStructuralTypeInfo(actual.GetType());
            if (expectedInfo.Kind is StructuralTypeKind.ThrownValue || actualInfo.Kind is StructuralTypeKind.ThrownValue)
                return CompareThrownValues(expected, actual);

            if (expectedInfo.Kind is StructuralTypeKind.Leaf || actualInfo.Kind is StructuralTypeKind.Leaf)
                return StructuralValuesEqual(expected, actual, options) || Fail(expected, actual, StructuralValuesDifferReason);

            if (expectedInfo.Kind is StructuralTypeKind.Json || actualInfo.Kind is StructuralTypeKind.Json)
                return CompareJson(expected, actual);

            if (IsSpecialLeaf(expectedInfo.Kind) || IsSpecialLeaf(actualInfo.Kind))
                return CompareSpecialLeaves(expected, expectedInfo, actual, actualInfo);

            if (expectedInfo.UsesEquality && expectedInfo == actualInfo)
                return ValuesEqual(expected, actual) || Fail(expected, actual, StructuralValuesDifferReason);

            if (!expectedInfo.Type.IsValueType && !actualInfo.Type.IsValueType)
            {
                var pair = new StructuralReferencePair(expected, actual);
                if (!_visited.Add(pair))
                    return true;

                _visitedAdditions.Add(pair);
            }

            if (expectedInfo.GetSequenceItems is not null)
            {
                expected = expectedInfo.GetSequenceItems(expected);
                expectedInfo = GetStructuralTypeInfo(expected.GetType());
            }

            if (actualInfo.GetSequenceItems is not null)
            {
                actual = actualInfo.GetSequenceItems(actual);
                actualInfo = GetStructuralTypeInfo(actual.GetType());
            }

            if (expectedInfo.Kind is StructuralTypeKind.MultiDimensionalArray || actualInfo.Kind is StructuralTypeKind.MultiDimensionalArray)
                return CompareMultiDimensionalArrays(expected, actual);

            if (expected is System.Collections.IEnumerable expectedEnumerable && actual is System.Collections.IEnumerable actualEnumerable)
                return CompareEnumerables(expectedEnumerable, expectedInfo, actualEnumerable, actualInfo);

            return CompareMembers(expected, expectedInfo, actual, actualInfo);
        }

        private static bool IsSpecialLeaf(StructuralTypeKind kind)
        {
            return kind is StructuralTypeKind.StringBuilder or StructuralTypeKind.FileSystemInfo or StructuralTypeKind.Regex or StructuralTypeKind.Xml;
        }

        private bool Fail(object? expected, object? actual, string reason)
        {
            if (IsReporting)
            {
                Difference = new StructuralDifference(_path.ToString(), expected, actual, reason);
            }

            return false;
        }

        private void PushMember(string name)
        {
            if (IsReporting)
            {
                _path.Push(StructuralPathSegment.ForMember(name));
            }
        }

        private void PushIndex(int index, int[]? lengths)
        {
            if (IsReporting)
            {
                _path.Push(StructuralPathSegment.ForIndex(index, lengths));
            }
        }

        private void PushKey(object? key)
        {
            if (IsReporting)
            {
                _path.Push(StructuralPathSegment.ForKey(key));
            }
        }

        private void Pop()
        {
            if (IsReporting)
            {
                _path.Pop();
            }
        }

        /// <summary>Compares a candidate pairing without reporting, and forgets what it visited so later candidates are compared in full.</summary>
        private bool IsEquivalentCandidate(object? expected, object? actual)
        {
            var visitedAdditionsCount = _visitedAdditions.Count;
            _suppressedReportingCount++;
            var result = AreEquivalent(expected, actual);
            _suppressedReportingCount--;

            for (var index = _visitedAdditions.Count - 1; index >= visitedAdditionsCount; index--)
            {
                _visited.Remove(_visitedAdditions[index]);
                _visitedAdditions.RemoveAt(index);
            }

            return result;
        }

        private bool CompareThrownValues(object expected, object actual)
        {
            if (expected is StructuralThrownValue expectedThrown && actual is StructuralThrownValue actualThrown && expectedThrown.ExceptionType == actualThrown.ExceptionType)
                return true;

            return Fail(expected, actual, StructuralMemberThrewReason);
        }

        private bool CompareSpecialLeaves(object expected, StructuralTypeInfo expectedInfo, object actual, StructuralTypeInfo actualInfo)
        {
            if (expectedInfo.Kind != actualInfo.Kind)
                return ValuesEqual(expected, actual) || Fail(expected, actual, StructuralValuesDifferReason);

            switch (expectedInfo.Kind)
            {
                case StructuralTypeKind.StringBuilder:
                    var expectedText = expected.ToString();
                    var actualText = actual.ToString();
                    return string.Equals(expectedText, actualText, options.StringComparison) || Fail(expectedText, actualText, StructuralValuesDifferReason);

                case StructuralTypeKind.FileSystemInfo:
                    return (expectedInfo == actualInfo && string.Equals(((FileSystemInfo)expected).FullName, ((FileSystemInfo)actual).FullName, StringComparison.Ordinal))
                        || Fail(expected, actual, StructuralValuesDifferReason);

                case StructuralTypeKind.Regex:
                    var expectedRegex = (Regex)expected;
                    var actualRegex = (Regex)actual;
                    return (expectedInfo == actualInfo
                            && string.Equals(expectedRegex.ToString(), actualRegex.ToString(), StringComparison.Ordinal)
                            && expectedRegex.Options == actualRegex.Options
                            && expectedRegex.MatchTimeout == actualRegex.MatchTimeout)
                        || Fail(expected, actual, StructuralValuesDifferReason);

                default:
                    return XmlValuesEqual(expected, actual) || Fail(expected, actual, StructuralValuesDifferReason);
            }
        }

        /// <summary>
        /// Compares JSON values the way the data they hold is compared elsewhere: objects as dictionaries of their
        /// properties, arrays as collections and strings as strings, so the options apply to them. Other values are
        /// compared with <see cref="JsonElement.DeepEquals"/> and <see cref="JsonNode.DeepEquals"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool CompareJson(object expected, object actual)
        {
            expected = NormalizeJsonValue(expected);
            actual = NormalizeJsonValue(actual);
            var expectedShape = GetJsonShape(expected);
            var actualShape = GetJsonShape(actual);
            if (expectedShape is StructuralJsonShape.None || actualShape is StructuralJsonShape.None)
                return ValuesEqual(expected, actual) || Fail(expected, actual, StructuralValuesDifferReason);

            if (expectedShape is StructuralJsonShape.Object && actualShape is StructuralJsonShape.Object)
                return CompareDictionaryEntries(GetJsonObjectEntries(expected), GetJsonObjectEntries(actual));

            if (expectedShape is StructuralJsonShape.Array && actualShape is StructuralJsonShape.Array)
            {
                var expectedItems = GetJsonArrayItems(expected);
                var actualItems = GetJsonArrayItems(actual);
                return options.IgnoreCollectionOrder ? CompareUnordered(expectedItems, actualItems, expectedLengths: null, actualLengths: null) : CompareOrdered(expectedItems, actualItems, lengths: null);
            }

            if (expectedShape is StructuralJsonShape.String && actualShape is StructuralJsonShape.String)
            {
                var expectedString = GetJsonString(expected);
                var actualString = GetJsonString(actual);
                return string.Equals(expectedString, actualString, options.StringComparison) || Fail(expectedString, actualString, StructuralValuesDifferReason);
            }

            var isContainer = expectedShape is StructuralJsonShape.Object or StructuralJsonShape.Array || actualShape is StructuralJsonShape.Object or StructuralJsonShape.Array;
            return (!isContainer && JsonScalarsEqual(expected, actual)) || Fail(expected, actual, StructuralValuesDifferReason);
        }

        private bool CompareMultiDimensionalArrays(object expected, object actual)
        {
            if (expected is not Array expectedArray || actual is not Array actualArray || expectedArray.Rank != actualArray.Rank)
                return Fail(expected, actual, "Array ranks differ.");

            var lengths = new int[expectedArray.Rank];
            for (var dimension = 0; dimension < lengths.Length; dimension++)
            {
                lengths[dimension] = expectedArray.GetLength(dimension);
                if (lengths[dimension] != actualArray.GetLength(dimension))
                    return Fail(expected, actual, IsReporting ? "Array dimensions differ: expected " + FormatArrayDimensions(expectedArray) + ", actual " + FormatArrayDimensions(actualArray) + "." : string.Empty);
            }

            if (options.IgnoreCollectionOrder)
                return CompareUnordered([.. EnumerateObjects(expectedArray)], [.. EnumerateObjects(actualArray)], lengths, lengths);

            return CompareOrdered(expectedArray, actualArray, lengths);
        }

        private bool CompareEnumerables(System.Collections.IEnumerable expected, StructuralTypeInfo expectedInfo, System.Collections.IEnumerable actual, StructuralTypeInfo actualInfo)
        {
            // The position of an entry in a dictionary is not part of its value, so entries are matched by key.
            if (expectedInfo.CollectionKind is StructuralCollectionKind.Dictionary && actualInfo.CollectionKind is StructuralCollectionKind.Dictionary
                && TryGetStructuralDictionaryEntries(expected, out var expectedEntries)
                && TryGetStructuralDictionaryEntries(actual, out var actualEntries))
            {
                return CompareDictionaryEntries(expectedEntries, actualEntries);
            }

            // A set has no meaningful order, so comparing it by position against anything would depend on its internal layout.
            if (options.IgnoreCollectionOrder || expectedInfo.CollectionKind is StructuralCollectionKind.Set || actualInfo.CollectionKind is StructuralCollectionKind.Set)
                return CompareUnordered([.. EnumerateObjects(expected)], [.. EnumerateObjects(actual)], expectedLengths: null, actualLengths: null);

            return CompareOrdered(expected, actual, lengths: null);
        }

        private bool CompareOrdered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, int[]? lengths)
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
                        return true;

                    PushIndex(index, lengths);

                    if (!expectedHasNext)
                        return Fail(StructuralMissingValue.Instance, actualEnumerator.Current, "Actual collection contains an unexpected item.");

                    if (!actualHasNext)
                        return Fail(expectedEnumerator.Current, StructuralMissingValue.Instance, "Actual collection is missing an item.");

                    if (!AreEquivalent(expectedEnumerator.Current, actualEnumerator.Current))
                        return false;

                    Pop();
                    index++;
                }
            }
            finally
            {
                (expectedEnumerator as IDisposable)?.Dispose();
                (actualEnumerator as IDisposable)?.Dispose();
            }
        }

        private bool CompareUnordered(List<object?> expectedItems, List<object?> actualItems, int[]? expectedLengths, int[]? actualLengths)
        {
            var matches = MatchItems(expectedItems, actualItems, out var matchedActualIndexes);

            for (var expectedIndex = 0; expectedIndex < matches.Length; expectedIndex++)
            {
                if (matches[expectedIndex] >= 0)
                    continue;

                PushIndex(expectedIndex, expectedLengths);
                return Fail(expectedItems[expectedIndex], StructuralMissingValue.Instance, "Actual collection is missing an equivalent item.");
            }

            for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                PushIndex(actualIndex, actualLengths);
                return Fail(StructuralMissingValue.Instance, actualItems[actualIndex], "Actual collection contains an unexpected item.");
            }

            return true;
        }

        /// <summary>
        /// Pairs each expected item with an equivalent actual item, and returns the index of the actual item matched by each
        /// expected item, or -1 when there is none.
        /// </summary>
        private int[] MatchItems(List<object?> expectedItems, List<object?> actualItems, out bool[] matchedActualIndexes)
        {
            var matches = new int[expectedItems.Count];
            matches.AsSpan().Fill(-1);
            matchedActualIndexes = new bool[actualItems.Count];

            // Equal leaves are equivalent, so they are paired through a hash lookup first, and only the remaining items need
            // the quadratic structural search. The lookup is restricted to leaves because hashing an arbitrary object can
            // recurse forever on a cyclic graph.
            Dictionary<object, int>? firstActualIndexByLeaf = null;
            int[]? nextActualIndexWithSameLeaf = null;
            for (var actualIndex = actualItems.Count - 1; actualIndex >= 0; actualIndex--)
            {
                var actualItem = actualItems[actualIndex];
                if (actualItem is null || GetStructuralTypeInfo(actualItem.GetType()).Kind is not StructuralTypeKind.Leaf)
                    continue;

                firstActualIndexByLeaf ??= new Dictionary<object, int>(options.LeafComparer);
                nextActualIndexWithSameLeaf ??= new int[actualItems.Count];
                nextActualIndexWithSameLeaf[actualIndex] = firstActualIndexByLeaf.TryGetValue(actualItem, out var nextIndex) ? nextIndex : -1;
                firstActualIndexByLeaf[actualItem] = actualIndex;
            }

            if (firstActualIndexByLeaf is not null)
            {
                for (var expectedIndex = 0; expectedIndex < expectedItems.Count; expectedIndex++)
                {
                    var expectedItem = expectedItems[expectedIndex];
                    if (expectedItem is null || GetStructuralTypeInfo(expectedItem.GetType()).Kind is not StructuralTypeKind.Leaf)
                        continue;

                    if (firstActualIndexByLeaf.TryGetValue(expectedItem, out var actualIndex) && actualIndex >= 0)
                    {
                        matches[expectedIndex] = actualIndex;
                        matchedActualIndexes[actualIndex] = true;
                        firstActualIndexByLeaf[expectedItem] = nextActualIndexWithSameLeaf![actualIndex];
                    }
                }
            }

            StructuralSignature?[]? actualSignatures = null;
            for (var expectedIndex = 0; expectedIndex < expectedItems.Count; expectedIndex++)
            {
                if (matches[expectedIndex] >= 0)
                    continue;

                var expectedItem = expectedItems[expectedIndex];
                var expectedInfo = expectedItem is null ? null : GetStructuralTypeInfo(expectedItem.GetType());
                Type? pairedActualType = null;
                StructuralMemberPairing? signaturePairing = null;
                var expectedSignature = 0;
                for (var actualIndex = 0; actualIndex < actualItems.Count; actualIndex++)
                {
                    if (matchedActualIndexes[actualIndex])
                        continue;

                    var actualItem = actualItems[actualIndex];
                    if (expectedInfo is not null && actualItem is not null)
                    {
                        // Cheap rejections. The hash lookup above already paired every equal leaf, and two objects whose
                        // signatures differ have a member whose values differ.
                        var actualType = actualItem.GetType();
                        if (expectedInfo.IsHashSafeLeaf && actualType == expectedInfo.Type)
                            continue;

                        if (actualType != pairedActualType)
                        {
                            pairedActualType = actualType;
                            signaturePairing = GetSignaturePairing(expectedInfo, GetStructuralTypeInfo(actualType));
                            expectedSignature = signaturePairing is null ? 0 : ComputeSignature(expectedItem!, signaturePairing, expectedSide: true);
                        }

                        if (signaturePairing is not null)
                        {
                            actualSignatures ??= new StructuralSignature?[actualItems.Count];
                            if (actualSignatures[actualIndex] is not { } actualSignature || actualSignature.ExpectedType != expectedInfo.Type)
                            {
                                actualSignature = new StructuralSignature(expectedInfo.Type, ComputeSignature(actualItem, signaturePairing, expectedSide: false));
                                actualSignatures[actualIndex] = actualSignature;
                            }

                            if (actualSignature.Value != expectedSignature)
                                continue;
                        }
                    }

                    if (IsEquivalentCandidate(expectedItem, actualItem))
                    {
                        matches[expectedIndex] = actualIndex;
                        matchedActualIndexes[actualIndex] = true;
                        break;
                    }
                }

                // Callers report the first unmatched expected item, so matching the following ones would be wasted work.
                if (matches[expectedIndex] < 0)
                    break;
            }

            return matches;
        }

        /// <summary>
        /// Returns the member pairing used to hash values of the two types, or <see langword="null"/> when a hash cannot
        /// tell them apart: the values must both be walked member by member for the hash of their members to matter.
        /// </summary>
        private StructuralMemberPairing? GetSignaturePairing(StructuralTypeInfo expectedInfo, StructuralTypeInfo actualInfo)
        {
            if (expectedInfo.Kind is not StructuralTypeKind.Object || actualInfo.Kind is not StructuralTypeKind.Object
                || expectedInfo.GetSequenceItems is not null || actualInfo.GetSequenceItems is not null
                || (expectedInfo.UsesEquality && expectedInfo == actualInfo))
            {
                return null;
            }

            var pairing = GetStructuralMemberPairing(expectedInfo, actualInfo, options.IgnoreMemberNameCase);
            return pairing.SignaturePairs.Length > 0 ? pairing : null;
        }

        /// <summary>
        /// Hashes the paired members that are declared with the same leaf type on both sides, so two values whose
        /// signatures differ cannot be equivalent. Other members do not contribute, as values of different runtime types
        /// can still be equivalent.
        /// </summary>
        private int ComputeSignature(object value, StructuralMemberPairing pairing, bool expectedSide)
        {
            var hash = new HashCode();
            foreach (var pair in pairing.SignaturePairs)
            {
                var member = expectedSide ? pair.Expected! : pair.Actual!;
                hash.Add(member.GetValue(value) switch
                {
                    null => 0,
                    string text => options.StringComparer.GetHashCode(text),
                    StructuralThrownValue thrown => thrown.ExceptionType.GetHashCode(),
                    var memberValue => memberValue.GetHashCode(),
                });
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Compares dictionaries entry by entry. A key can be equivalent to several keys of the other dictionary when the
        /// case of strings is ignored, so an entry is paired with a key whose value is also equivalent when there is one,
        /// preferring a key that is exactly equal.
        /// </summary>
        private bool CompareDictionaryEntries(List<KeyValuePair<object?, object?>> expectedEntries, List<KeyValuePair<object?, object?>> actualEntries)
        {
            var matches = new int[expectedEntries.Count];
            matches.AsSpan().Fill(-1);
            var matchedValueIsEquivalent = new bool[expectedEntries.Count];
            var matchedActualIndexes = new bool[actualEntries.Count];
            var exactKeys = StructuralLeafIndex.Create(actualEntries, StructuralLeafEqualityComparer.Ordinal);
            var equivalentKeys = options.StringComparison is StringComparison.Ordinal ? default : StructuralLeafIndex.Create(actualEntries, StructuralLeafEqualityComparer.OrdinalIgnoreCase);

            for (var expectedIndex = 0; expectedIndex < expectedEntries.Count; expectedIndex++)
            {
                var expectedEntry = expectedEntries[expectedIndex];
                var keyInfo = expectedEntry.Key is null ? null : GetStructuralTypeInfo(expectedEntry.Key.GetType());
                var hasLeafCandidate = false;
                if (keyInfo is { Kind: StructuralTypeKind.Leaf })
                {
                    foreach (var actualIndex in CandidateIndexes(expectedEntry.Key!, exactKeys, equivalentKeys, actualEntries))
                    {
                        if (matchedActualIndexes[actualIndex])
                            continue;

                        hasLeafCandidate = true;
                        if (IsEquivalentCandidate(expectedEntry.Value, actualEntries[actualIndex].Value))
                        {
                            Match(expectedIndex, actualIndex, valueIsEquivalent: true);
                            break;
                        }
                    }
                }

                if (matches[expectedIndex] >= 0 || hasLeafCandidate)
                    continue;

                for (var actualIndex = 0; actualIndex < actualEntries.Count; actualIndex++)
                {
                    if (!matchedActualIndexes[actualIndex] && IsKeyCandidate(expectedEntry.Key, keyInfo, actualEntries[actualIndex].Key) && IsEquivalentCandidate(expectedEntry.Value, actualEntries[actualIndex].Value))
                    {
                        Match(expectedIndex, actualIndex, valueIsEquivalent: true);
                        break;
                    }
                }
            }

            // The remaining entries have no key with an equivalent value. Pairing them with an equivalent key reports the
            // value that differs rather than a missing key.
            for (var expectedIndex = 0; expectedIndex < expectedEntries.Count; expectedIndex++)
            {
                if (matches[expectedIndex] >= 0)
                    continue;

                var expectedEntry = expectedEntries[expectedIndex];
                var keyInfo = expectedEntry.Key is null ? null : GetStructuralTypeInfo(expectedEntry.Key.GetType());
                if (keyInfo is { Kind: StructuralTypeKind.Leaf })
                {
                    foreach (var actualIndex in CandidateIndexes(expectedEntry.Key!, exactKeys, equivalentKeys, actualEntries))
                    {
                        if (!matchedActualIndexes[actualIndex])
                        {
                            Match(expectedIndex, actualIndex, valueIsEquivalent: false);
                            break;
                        }
                    }
                }

                for (var actualIndex = 0; matches[expectedIndex] < 0 && actualIndex < actualEntries.Count; actualIndex++)
                {
                    if (!matchedActualIndexes[actualIndex] && IsKeyCandidate(expectedEntry.Key, keyInfo, actualEntries[actualIndex].Key))
                    {
                        Match(expectedIndex, actualIndex, valueIsEquivalent: false);
                    }
                }
            }

            for (var expectedIndex = 0; expectedIndex < expectedEntries.Count; expectedIndex++)
            {
                var expectedEntry = expectedEntries[expectedIndex];
                PushKey(expectedEntry.Key);
                var actualIndex = matches[expectedIndex];
                if (actualIndex < 0)
                    return Fail(expectedEntry.Value, StructuralMissingValue.Instance, "Actual dictionary is missing a key.");

                if (!matchedValueIsEquivalent[expectedIndex] && !AreEquivalent(expectedEntry.Value, actualEntries[actualIndex].Value))
                    return false;

                Pop();
            }

            for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                var actualEntry = actualEntries[actualIndex];
                PushKey(actualEntry.Key);
                return Fail(StructuralMissingValue.Instance, actualEntry.Value, "Actual dictionary contains an unexpected key.");
            }

            return true;

            void Match(int expectedIndex, int actualIndex, bool valueIsEquivalent)
            {
                matches[expectedIndex] = actualIndex;
                matchedValueIsEquivalent[expectedIndex] = valueIsEquivalent;
                matchedActualIndexes[actualIndex] = true;
            }
        }

        /// <summary>Returns the indexes of the keys equal to a leaf key, the exactly equal ones first.</summary>
        private static IEnumerable<int> CandidateIndexes(object key, StructuralLeafIndex exactKeys, StructuralLeafIndex equivalentKeys, List<KeyValuePair<object?, object?>> actualEntries)
        {
            for (var index = exactKeys.GetFirstIndex(key); index >= 0; index = exactKeys.GetNextIndex(index))
            {
                yield return index;
            }

            for (var index = equivalentKeys.GetFirstIndex(key); index >= 0; index = equivalentKeys.GetNextIndex(index))
            {
                if (!StructuralLeafEqualityComparer.Ordinal.Equals(key, actualEntries[index].Key))
                    yield return index;
            }
        }

        private bool IsKeyCandidate(object? expectedKey, StructuralTypeInfo? expectedKeyInfo, object? actualKey)
        {
            // Every key equal to a leaf of such a type was found through the hash lookup.
            if (expectedKeyInfo is { IsHashSafeLeaf: true } && actualKey is not null && actualKey.GetType() == expectedKeyInfo.Type)
                return false;

            return IsEquivalentCandidate(expectedKey, actualKey);
        }

        private bool CompareMembers(object expected, StructuralTypeInfo expectedInfo, object actual, StructuralTypeInfo actualInfo)
        {
            // A value that exposes nothing to compare keeps its state private. Treating it as equivalent to any other such
            // value would make every assertion on it pass, so its own equality decides.
            if (expectedInfo.MemberSet.Members.Length == 0 && actualInfo.MemberSet.Members.Length == 0)
                return ValuesEqual(expected, actual) || Fail(expected, actual, StructuralValuesDifferReason);

            foreach (var pair in GetStructuralMemberPairing(expectedInfo, actualInfo, options.IgnoreMemberNameCase).Pairs)
            {
                if (pair.Expected is null)
                {
                    PushMember(pair.Actual!.Name);
                    return Fail(StructuralMissingValue.Instance, IsReporting ? pair.Actual.GetValue(actual) : null, "Actual member is unexpected.");
                }

                PushMember(pair.Expected.Name);
                if (pair.Actual is null)
                    return Fail(IsReporting ? pair.Expected.GetValue(expected) : null, StructuralMissingValue.Instance, "Actual member is missing.");

                if (!AreEquivalent(pair.Expected.GetValue(expected), pair.Actual.GetValue(actual)))
                    return false;

                Pop();
            }

            return true;
        }
    }

    /// <summary>
    /// Tracks where the comparison currently is. Segments are pushed and popped as the walk descends and unwinds,
    /// and the string is built only when a difference is found, so a comparison that succeeds formats no path at all.
    /// </summary>
    private sealed class StructuralPath
    {
        public const string Root = "$";

        private readonly List<StructuralPathSegment> _segments = [];

        public void Push(StructuralPathSegment segment) => _segments.Add(segment);

        public void Pop() => _segments.RemoveAt(_segments.Count - 1);

        public override string ToString()
        {
            var builder = new StringBuilder(Root);
            foreach (var segment in _segments)
            {
                if (segment.IsKey)
                {
                    builder.Append('[').Append(FormatKey(segment.Key)).Append(']');
                }
                else if (segment.MemberName is not null)
                {
                    builder.Append('.').Append(segment.MemberName);
                }
                else if (segment.Lengths is { } lengths)
                {
                    AppendMultiDimensionalIndex(builder, segment.Index, lengths);
                }
                else
                {
                    builder.Append('[').Append(segment.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
            }

            return builder.ToString();
        }

        private static void AppendMultiDimensionalIndex(StringBuilder builder, int flatIndex, int[] lengths)
        {
            // Arrays enumerate their items in row-major order, so the last dimension varies the fastest.
            var indexes = new int[lengths.Length];
            for (var dimension = lengths.Length - 1; dimension >= 0; dimension--)
            {
                indexes[dimension] = flatIndex % lengths[dimension];
                flatIndex /= lengths[dimension];
            }

            builder.Append('[');
            for (var dimension = 0; dimension < indexes.Length; dimension++)
            {
                if (dimension > 0)
                {
                    builder.Append(',');
                }

                builder.Append(indexes[dimension].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(']');
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
    }

    private readonly struct StructuralPathSegment
    {
        private StructuralPathSegment(string? memberName, int index, object? key, bool isKey, int[]? lengths)
        {
            MemberName = memberName;
            Index = index;
            Key = key;
            IsKey = isKey;
            Lengths = lengths;
        }

        public string? MemberName { get; }
        public int Index { get; }
        public object? Key { get; }
        public bool IsKey { get; }

        /// <summary>Gets the lengths of the dimensions of the multi-dimensional array the index points into.</summary>
        public int[]? Lengths { get; }

        public static StructuralPathSegment ForMember(string memberName) => new(memberName, index: -1, key: null, isKey: false, lengths: null);

        public static StructuralPathSegment ForIndex(int index, int[]? lengths) => new(memberName: null, index, key: null, isKey: false, lengths);

        public static StructuralPathSegment ForKey(object? key) => new(memberName: null, index: -1, key, isKey: true, lengths: null);
    }

    private enum StructuralTypeKind
    {
        Object,
        Leaf,
        Enumerable,
        MultiDimensionalArray,
        ThrownValue,
        Json,
        Xml,
        StringBuilder,
        FileSystemInfo,
        Regex,
    }

    private enum StructuralCollectionKind
    {
        Sequence,
        Dictionary,
        Set,
    }

    private enum StructuralJsonShape
    {
        None,
        Object,
        Array,
        String,
        Scalar,
    }

    /// <summary>What the comparison needs to know about a type, computed once per type.</summary>
    private sealed class StructuralTypeInfo(Type type, StructuralTypeKind kind)
    {
        private StructuralMemberSet? _memberSet;

        public Type Type { get; } = type;
        public StructuralTypeKind Kind { get; } = kind;
        public bool IsHashSafeLeaf { get; init; }
        public bool UsesEquality { get; init; }
        public bool IsKeyValuePair { get; init; }
        public StructuralCollectionKind CollectionKind { get; init; }
        public Func<object, System.Collections.IEnumerable>? GetSequenceItems { get; init; }

        /// <summary>Gets the members, created on first use as only walked types need them. A race creates identical sets.</summary>
        public StructuralMemberSet MemberSet => _memberSet ??= new StructuralMemberSet(Type);
    }

    private sealed class StructuralMemberSet
    {
        public StructuralMemberSet(Type type)
        {
            var members = new List<StructuralMember>();
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetMethod is null || property.GetIndexParameters().Length != 0 || !CanReadStructuralMember(property.PropertyType))
                    continue;

                // A property hidden with the new modifier is listed after the one hiding it, which is the one kept.
                if (indexByName.TryAdd(property.Name, members.Count))
                {
                    members.Add(new StructuralMember(property.Name, property, property.PropertyType));
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (CanReadStructuralMember(field.FieldType) && indexByName.TryAdd(field.Name, members.Count))
                {
                    members.Add(new StructuralMember(field.Name, field, field.FieldType));
                }
            }

            Members = [.. members];
            IndexByName = indexByName;
            var identityPairs = new StructuralMemberPair[Members.Length];
            for (var index = 0; index < Members.Length; index++)
            {
                identityPairs[index] = new StructuralMemberPair(Members[index], Members[index]);
            }

            IdentityPairing = new StructuralMemberPairing(identityPairs);
        }

        public StructuralMember[] Members { get; }
        public Dictionary<string, int> IndexByName { get; }
        public StructuralMemberPairing IdentityPairing { get; }
    }

    private sealed class StructuralMemberPairing
    {
        public StructuralMemberPairing(StructuralMemberPair[] pairs)
        {
            Pairs = pairs;
            var signaturePairs = new List<StructuralMemberPair>();
            foreach (var pair in pairs)
            {
                if (pair.Expected is not null && pair.Actual is not null && GetSignatureType(pair.Expected.ValueType) is { } type && type == GetSignatureType(pair.Actual.ValueType))
                {
                    signaturePairs.Add(pair);
                }
            }

            SignaturePairs = [.. signaturePairs];
        }

        /// <summary>Gets the paired members, followed by the unexpected actual members.</summary>
        public StructuralMemberPair[] Pairs { get; }

        /// <summary>Gets the pairs whose values can be hashed to tell two values apart.</summary>
        public StructuralMemberPair[] SignaturePairs { get; }

        private static Type? GetSignatureType(Type type)
        {
            if (type.IsByRef)
            {
                type = type.GetElementType()!;
            }

            type = Nullable.GetUnderlyingType(type) ?? type;

            // The declared type must be the runtime type of every value, which only holds for value types and sealed types.
            return (type.IsValueType || type == typeof(string)) && IsHashSafeLeafType(type) ? type : null;
        }
    }

    private readonly record struct StructuralSignature(Type ExpectedType, int Value);

    private sealed class StructuralMember(string name, MemberInfo member, Type valueType)
    {
        public string Name { get; } = name;
        public Type ValueType { get; } = valueType;

        public object? GetValue(object obj)
        {
            try
            {
                return member switch
                {
                    PropertyInfo property => property.GetValue(obj),
                    FieldInfo field => field.GetValue(obj),
                    _ => throw new UnreachableException(),
                };
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // Some getters throw depending on the state of the object, such as IPAddress.ScopeId for an IPv4 address.
                // The exception stands for the value, so two getters throwing the same exception are equivalent.
                return new StructuralThrownValue(ex.InnerException);
            }
        }
    }

    private readonly struct StructuralMemberPair(StructuralMember? expected, StructuralMember? actual)
    {
        public StructuralMember? Expected { get; } = expected;
        public StructuralMember? Actual { get; } = actual;
    }

    private readonly record struct StructuralMemberPairsCacheKey(Type ExpectedType, Type ActualType, bool IgnoreMemberNameCase);

    /// <summary>Indexes the leaf keys of dictionary entries, chaining the indexes of the keys that are equal.</summary>
    private readonly struct StructuralLeafIndex
    {
        private readonly Dictionary<object, int>? _firstIndexes;
        private readonly int[]? _nextIndexes;

        private StructuralLeafIndex(Dictionary<object, int> firstIndexes, int[] nextIndexes)
        {
            _firstIndexes = firstIndexes;
            _nextIndexes = nextIndexes;
        }

        public static StructuralLeafIndex Create(List<KeyValuePair<object?, object?>> entries, StructuralLeafEqualityComparer comparer)
        {
            var firstIndexes = new Dictionary<object, int>(comparer);
            var nextIndexes = new int[entries.Count];
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                var key = entries[index].Key;
                if (key is null || GetStructuralTypeInfo(key.GetType()).Kind is not StructuralTypeKind.Leaf)
                    continue;

                nextIndexes[index] = firstIndexes.TryGetValue(key, out var nextIndex) ? nextIndex : -1;
                firstIndexes[key] = index;
            }

            return new StructuralLeafIndex(firstIndexes, nextIndexes);
        }

        public int GetFirstIndex(object key) => _firstIndexes is not null && _firstIndexes.TryGetValue(key, out var index) ? index : -1;

        public int GetNextIndex(int index) => _nextIndexes![index];
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

    private sealed class StructuralDifference(string path, object? expectedValue, object? actualValue, string reason)
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

    private readonly struct StructuralComparisonOptions
    {
        private StructuralComparisonOptions(bool ignoreCollectionOrder, bool ignoreMemberNameCase, bool ignoreStringCase)
        {
            IgnoreCollectionOrder = ignoreCollectionOrder;
            IgnoreMemberNameCase = ignoreMemberNameCase;
            StringComparison = ignoreStringCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            StringComparer = ignoreStringCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            LeafComparer = ignoreStringCase ? StructuralLeafEqualityComparer.OrdinalIgnoreCase : StructuralLeafEqualityComparer.Ordinal;
        }

        public bool IgnoreCollectionOrder { get; }
        public bool IgnoreMemberNameCase { get; }
        public StringComparison StringComparison { get; }
        public StringComparer StringComparer { get; }
        public StructuralLeafEqualityComparer LeafComparer { get; }

        public static StructuralComparisonOptions Create(EquivalentOptions? options)
        {
            if (options is null)
                return new StructuralComparisonOptions(ignoreCollectionOrder: false, ignoreMemberNameCase: false, ignoreStringCase: false);

            return new StructuralComparisonOptions(options.IgnoreCollectionOrder, options.IgnoreMemberNameCase, options.IgnoreStringCase);
        }
    }
}
