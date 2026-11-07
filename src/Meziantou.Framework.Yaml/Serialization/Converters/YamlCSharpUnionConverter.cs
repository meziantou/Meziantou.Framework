using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

[SuppressMessage("Performance", "CA1812", Justification = "Instantiated through reflection by the built-in converter factory.")]
internal sealed class YamlCSharpUnionConverter<T> : YamlConverter<T?>
{
    private const string UnionAttributeMetadataName = "System.Runtime.CompilerServices.UnionAttribute";

    private readonly Type _unionType = typeof(T);
    private readonly PropertyInfo _valueProperty;
    private readonly ImmutableArray<UnionCase> _cases;
    private readonly ImmutableArray<UnionCase> _readCases;
    private readonly ImmutableArray<UnionCase> _writeCases;
    private readonly UnionCase? _nullableCase;
    private YamlSerializerOptions? _classifierContextOptions;
    private YamlTypeClassifierContext? _classifierContext;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    public YamlCSharpUnionConverter()
    {
        if (!TryCreateCases(_unionType, out _valueProperty!, out _cases))
        {
            throw new InvalidOperationException($"Type '{_unionType}' is not a supported C# union type.");
        }

        _readCases = CollapseNullableOverloads(_cases);
        _writeCases = SortCasesForWriting(_cases);
        _nullableCase = FindNullableCase(_cases);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    internal static bool CanConvertUnionType(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return TryGetCaseParameters(typeToConvert, out _, out _);
    }

    public override T? Read(YamlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.TryReadAlias(out var aliasValue))
        {
            if (aliasValue is null)
            {
                return CreateNullValue(reader);
            }

            if (aliasValue is T unionValue)
            {
                return unionValue;
            }

            var aliasCase = GetCaseForRuntimeValue(aliasValue.GetType());
            if (aliasCase is null)
            {
                throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Union type '{_unionType}' does not define a case that can represent the referenced value.");
            }

            return CreateValue(reader, aliasCase.Value, aliasValue);
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return CreateNullValue(reader);
        }

        var kind = GetCurrentKind(reader);
        Span<bool> isCandidate = _readCases.Length <= 32 ? stackalloc bool[_readCases.Length] : new bool[_readCases.Length];
        var candidateCount = FindCandidates(reader, kind, isCandidate);
        if (candidateCount == 1)
        {
            var unionCase = _readCases[isCandidate.IndexOf(true)];
            var converter = GetCaseConverter(reader, unionCase);
            var caseValue = converter.Read(reader, unionCase.Type);
            return CreateValue(reader, unionCase, caseValue);
        }

        if (candidateCount == 0)
        {
            throw CreateNoMatchingCaseException(reader.SourceName, reader.Start, reader.End, kind);
        }

        return ReadClassifiedValue(reader, kind, isCandidate);
    }

    public override void Write(YamlWriter writer, T? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var caseValue = _valueProperty.GetValue(value);
        if (caseValue is null)
        {
            writer.WriteNullValue();
            return;
        }

        var unionCase = GetCaseForRuntimeValue(caseValue.GetType());
        if (unionCase is null)
        {
            throw new NotSupportedException($"Union type '{_unionType}' does not define a case that can represent '{caseValue.GetType()}'.");
        }

        var converter = GetCaseConverter(writer, unionCase.Value);
        converter.Write(writer, caseValue);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static bool TryCreateCases(Type unionType, [NotNullWhen(true)] out PropertyInfo? valueProperty, out ImmutableArray<UnionCase> cases)
    {
        cases = ImmutableArray<UnionCase>.Empty;

        if (!TryGetCaseParameters(unionType, out valueProperty, out var parameters))
        {
            return false;
        }

        var numberHandling = GetNumberHandling(unionType);
        var nullabilityInfoContext = new NullabilityInfoContext();
        var visitedUnions = new HashSet<Type> { unionType };
        var builder = ImmutableArray.CreateBuilder<UnionCase>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var caseType = parameter.ParameterType;
            var runtimeType = Nullable.GetUnderlyingType(caseType) ?? caseType;
            var acceptsNull = IsNullableParameter(nullabilityInfoContext, parameter);
            var caseNumberHandling = YamlNumberHandlingConverter.IsSupportedType(caseType) ? numberHandling : YamlNumberHandling.None;

            var exactKinds = UnionCaseKinds.None;
            var fallbackKinds = UnionCaseKinds.None;
            var converterTypes = new List<Type>();

            // The number handling of this union is applied by the exact match of the case, so it is not passed here.
            AddCaseKinds(runtimeType, YamlNumberHandling.None, visitedUnions, ref exactKinds, ref fallbackKinds, converterTypes);

            builder.Add(new UnionCase(caseType, runtimeType, (ConstructorInfo)parameter.Member, exactKinds, fallbackKinds, [.. converterTypes], acceptsNull, caseNumberHandling));
        }

        cases = builder.MoveToImmutable();
        return true;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static bool TryGetCaseParameters(Type unionType, [NotNullWhen(true)] out PropertyInfo? valueProperty, out List<ParameterInfo> parameters)
    {
        parameters = [];
        valueProperty = null;
        if (!HasUnionAttribute(unionType))
        {
            return false;
        }

        valueProperty = unionType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty is null || valueProperty.PropertyType != typeof(object))
        {
            return false;
        }

        foreach (var constructor in unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
        {
            var constructorParameters = constructor.GetParameters();
            if (constructorParameters.Length == 1)
            {
                parameters.Add(constructorParameters[0]);
            }
        }

        return parameters.Count > 0;
    }

    private static YamlNumberHandling GetNumberHandling(Type unionType)
        => unionType.GetCustomAttribute<YamlNumberHandlingAttribute>(inherit: true)?.Handling ?? YamlNumberHandling.None;

    // Computes the YAML kinds a case reads. The exact kinds are the kinds the case type is represented by. The fallback
    // kinds are the other scalar kinds the case can still read, such as a number for a string case; they are only
    // considered when no case matches the kind exactly. A nested union matches the kinds of its own cases. A case whose
    // type is a union being computed, such as 'union U(bool, U?)', never matches: reading it would read the enclosing
    // union again. The converter types are the types whose custom converter, when one is registered, lets the case read
    // any kind as a fallback.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static void AddCaseKinds(Type runtimeType, YamlNumberHandling numberHandling, HashSet<Type> visitedUnions, ref UnionCaseKinds exactKinds, ref UnionCaseKinds fallbackKinds, List<Type> converterTypes)
    {
        if (visitedUnions.Contains(runtimeType))
        {
            return;
        }

        if (!converterTypes.Contains(runtimeType))
        {
            converterTypes.Add(runtimeType);
        }

        // A type-level converter can represent the type by any YAML kind.
        if (runtimeType.GetCustomAttribute<YamlConverterAttribute>(inherit: false) is not null)
        {
            fallbackKinds |= UnionCaseKinds.All;
        }

        // The number handling declared on a nested union lets its numeric cases read some string scalars, such as "42".
        if (YamlNumberHandlingConverter.CanReadStringScalars(runtimeType, numberHandling))
        {
            fallbackKinds |= UnionCaseKinds.String;
        }

        if (TryGetCaseParameters(runtimeType, out _, out var parameters))
        {
            var nestedNumberHandling = GetNumberHandling(runtimeType);
            visitedUnions.Add(runtimeType);
            foreach (var parameter in parameters)
            {
                var caseType = parameter.ParameterType;
                var caseNumberHandling = YamlNumberHandlingConverter.IsSupportedType(caseType) ? nestedNumberHandling : YamlNumberHandling.None;
                AddCaseKinds(Nullable.GetUnderlyingType(caseType) ?? caseType, caseNumberHandling, visitedUnions, ref exactKinds, ref fallbackKinds, converterTypes);
            }

            visitedUnions.Remove(runtimeType);
            return;
        }

        exactKinds |= GetKind(runtimeType);
        fallbackKinds |= GetFallbackKinds(runtimeType);
    }

    private static bool HasUnionAttribute(Type type)
    {
        foreach (var attribute in type.GetCustomAttributesData())
        {
            if (string.Equals(attribute.AttributeType.FullName, UnionAttributeMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNullableParameter(NullabilityInfoContext context, ParameterInfo parameter)
    {
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        if (parameter.ParameterType.IsValueType)
        {
            return false;
        }

        var nullabilityInfo = context.Create(parameter);
        return nullabilityInfo.ReadState != NullabilityState.NotNull;
    }

    private static ImmutableArray<UnionCase> CollapseNullableOverloads(ImmutableArray<UnionCase> cases)
    {
        var builder = ImmutableArray.CreateBuilder<UnionCase>(cases.Length);
        foreach (var unionCase in cases)
        {
            var replaced = false;
            for (var i = 0; i < builder.Count; i++)
            {
                if (builder[i].RuntimeType != unionCase.RuntimeType)
                {
                    continue;
                }

                // 'union Foo(int, int?)' declares two cases for the same underlying type. They are indistinguishable
                // when reading a non-null value, so keep the non-nullable overload as the canonical one. The nullable
                // overload is still used when reading a null scalar.
                if (Nullable.GetUnderlyingType(builder[i].Type) is not null)
                {
                    builder[i] = unionCase;
                }

                replaced = true;
                break;
            }

            if (!replaced)
            {
                builder.Add(unionCase);
            }
        }

        return builder.ToImmutable();
    }

    // Orders the cases so that every case comes before the cases its type derives from, so the first case accepting a
    // value is the most specific one. A comparison sort cannot do this: unrelated types compare as equal, which is not
    // a consistent ordering, so 'union U(Animal, int, string, Dog)' could keep 'Animal' before 'Dog'. Unrelated cases
    // keep their declaration order.
    private static ImmutableArray<UnionCase> SortCasesForWriting(ImmutableArray<UnionCase> cases)
    {
        var remaining = new List<UnionCase>(cases);
        var builder = ImmutableArray.CreateBuilder<UnionCase>(cases.Length);
        while (remaining.Count > 0)
        {
            var index = 0;
            for (var i = 0; i < remaining.Count; i++)
            {
                if (!HasMoreSpecificCase(remaining, remaining[i]))
                {
                    index = i;
                    break;
                }
            }

            builder.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return builder.MoveToImmutable();

        static bool HasMoreSpecificCase(List<UnionCase> cases, UnionCase unionCase)
        {
            foreach (var other in cases)
            {
                if (other.RuntimeType != unionCase.RuntimeType && unionCase.RuntimeType.IsAssignableFrom(other.RuntimeType))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static UnionCaseKinds GetCurrentKind(YamlReader reader)
    {
        if (reader.TokenType == YamlTokenType.StartMapping)
        {
            return UnionCaseKinds.Mapping;
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            return UnionCaseKinds.Sequence;
        }

        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Token '{reader.TokenType}' cannot be deserialized into union type '{typeof(T)}'.");
        }

        var scalarValue = YamlScalar.ResolveObject(reader);
        return scalarValue switch
        {
            bool => UnionCaseKinds.Boolean,
            sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or float or double or decimal or Half or Int128 or UInt128 => UnionCaseKinds.Number,
            _ => UnionCaseKinds.String,
        };
    }

    private static UnionCaseKinds GetKind(Type runtimeType)
    {
        if (runtimeType == typeof(object))
        {
            return UnionCaseKinds.All;
        }

        if (typeof(YamlNode).IsAssignableFrom(runtimeType))
        {
            return GetYamlNodeKind(runtimeType);
        }

        if (runtimeType == typeof(bool))
        {
            return UnionCaseKinds.Boolean;
        }

        if (IsNumeric(runtimeType))
        {
            return UnionCaseKinds.Number;
        }

        if (runtimeType == typeof(string) ||
            runtimeType == typeof(char) ||
            runtimeType == typeof(DateTime) ||
            runtimeType == typeof(DateTimeOffset) ||
            runtimeType == typeof(Guid) ||
            runtimeType == typeof(TimeSpan) ||
            runtimeType == typeof(DateOnly) ||
            runtimeType == typeof(TimeOnly) ||
            runtimeType == typeof(Uri) ||
            runtimeType == typeof(CultureInfo) ||
            runtimeType == typeof(System.Version) ||
            runtimeType == typeof(System.Text.Rune) ||
            runtimeType.IsEnum)
        {
            return UnionCaseKinds.String;
        }

        if (IsDictionary(runtimeType))
        {
            return UnionCaseKinds.Mapping;
        }

        if (IsSequence(runtimeType))
        {
            return UnionCaseKinds.Sequence;
        }

        return UnionCaseKinds.Mapping;
    }

    // A YAML model case reads the nodes of its own shape; the base node types read any node.
    private static UnionCaseKinds GetYamlNodeKind(Type runtimeType)
    {
        if (typeof(YamlSequence).IsAssignableFrom(runtimeType))
        {
            return UnionCaseKinds.Sequence;
        }

        if (typeof(YamlMapping).IsAssignableFrom(runtimeType))
        {
            return UnionCaseKinds.Mapping;
        }

        if (typeof(YamlValue).IsAssignableFrom(runtimeType))
        {
            return UnionCaseKinds.Scalar;
        }

        if (typeof(YamlContainer).IsAssignableFrom(runtimeType))
        {
            return UnionCaseKinds.Sequence | UnionCaseKinds.Mapping;
        }

        return UnionCaseKinds.All;
    }

    // The scalar kinds a case reads besides its exact kind: a string reads the text of any scalar, and a char or an enum
    // reads the text or the value of a number.
    private static UnionCaseKinds GetFallbackKinds(Type runtimeType)
    {
        if (runtimeType == typeof(string))
        {
            return UnionCaseKinds.Boolean | UnionCaseKinds.Number;
        }

        if (runtimeType == typeof(char) || runtimeType.IsEnum)
        {
            return UnionCaseKinds.Number;
        }

        return UnionCaseKinds.None;
    }

    // A type is a sequence case only when it is serialized through a built-in collection converter. Other enumerable
    // types, such as a class implementing only IEnumerable<T>, are serialized as objects, so they read YAML mappings.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static bool IsSequence(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) ||
                definition == typeof(IList<>) ||
                definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(HashSet<>) ||
                definition == typeof(ISet<>) ||
                definition == typeof(IReadOnlySet<>) ||
                definition == typeof(ImmutableArray<>) ||
                definition == typeof(ImmutableList<>) ||
                definition == typeof(ImmutableHashSet<>) ||
                definition == typeof(Queue<>) ||
                definition == typeof(Stack<>) ||
                definition == typeof(System.Collections.Concurrent.ConcurrentQueue<>) ||
                definition == typeof(System.Collections.Concurrent.ConcurrentStack<>) ||
                definition == typeof(System.Collections.Concurrent.ConcurrentBag<>) ||
                definition == typeof(System.Collections.Frozen.FrozenSet<>) ||
                definition == typeof(ArraySegment<>))
            {
                return true;
            }
        }

        return YamlReaderWriterBase.YamlBuiltInConverters.TryGetMutableCollectionElementType(type, out _);
    }

    private static bool IsNumeric(Type type)
        => type == typeof(byte) ||
           type == typeof(sbyte) ||
           type == typeof(short) ||
           type == typeof(ushort) ||
           type == typeof(int) ||
           type == typeof(uint) ||
           type == typeof(long) ||
           type == typeof(ulong) ||
           type == typeof(nint) ||
           type == typeof(nuint) ||
           type == typeof(float) ||
           type == typeof(double) ||
           type == typeof(decimal) ||
           type == typeof(Half) ||
           type == typeof(Int128) ||
           type == typeof(System.Numerics.BigInteger) ||
#if NET11_0_OR_GREATER
           type == typeof(System.Numerics.BFloat16) ||
           type == typeof(System.Numerics.Decimal32) ||
           type == typeof(System.Numerics.Decimal64) ||
           type == typeof(System.Numerics.Decimal128) ||
#endif
           type == typeof(UInt128);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static bool IsDictionary(Type type)
    {
        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            return true;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var definition = interfaceType.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                return true;
            }
        }

        return false;
    }

    private UnionCase? GetCaseForRuntimeValue(Type runtimeType)
    {
        for (var i = 0; i < _writeCases.Length; i++)
        {
            var unionCase = _writeCases[i];
            if (unionCase.RuntimeType.IsAssignableFrom(runtimeType))
            {
                return unionCase;
            }
        }

        return null;
    }

    // Marks the cases that can read the current value and returns how many there are. The cases matching the kind of the
    // value exactly win. Only when there is none, the cases that can still read the value are considered, such as a
    // string case for a number or a case whose type has a custom converter.
    private int FindCandidates(YamlReader reader, UnionCaseKinds kind, Span<bool> isCandidate)
    {
        var count = 0;
        for (var i = 0; i < _readCases.Length; i++)
        {
            var unionCase = _readCases[i];
            if ((unionCase.ExactKinds & kind) != UnionCaseKinds.None || CanReadStringScalar(reader, kind, unionCase))
            {
                isCandidate[i] = true;
                count++;
            }
        }

        if (count > 0)
        {
            return count;
        }

        for (var i = 0; i < _readCases.Length; i++)
        {
            var unionCase = _readCases[i];
            if ((unionCase.FallbackKinds & kind) != UnionCaseKinds.None || HasCustomConverter(reader, unionCase))
            {
                isCandidate[i] = true;
                count++;
            }
        }

        return count;
    }

    // A numeric case whose number handling reads strings also matches a string scalar holding a number, such as "42".
    // When a string case also matches, the scalar is ambiguous and requires a type classifier.
    private static bool CanReadStringScalar(YamlReader reader, UnionCaseKinds kind, UnionCase unionCase)
        => kind == UnionCaseKinds.String &&
           unionCase.NumberHandling != YamlNumberHandling.None &&
           YamlNumberHandlingConverter.CanReadStringScalar(reader, unionCase.Type, unionCase.NumberHandling);

    // A converter registered in the options can represent the type of the case, or of a case of a nested union, by any
    // YAML kind. A type-level converter is already part of the fallback kinds.
    private static bool HasCustomConverter(YamlReader reader, UnionCase unionCase)
    {
        foreach (var converterType in unionCase.ConverterTypes)
        {
            if (reader.TryGetCustomConverter(converterType, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static YamlConverter GetCaseConverter(YamlReaderWriterBase readerWriter, UnionCase unionCase)
    {
        var converter = readerWriter.GetConverter(unionCase.Type);
        if (unionCase.NumberHandling == YamlNumberHandling.None)
        {
            return converter;
        }

        return new YamlNumberHandlingConverter(converter, unionCase.Type, unionCase.NumberHandling);
    }

    private T? ReadClassifiedValue(YamlReader reader, UnionCaseKinds kind, ReadOnlySpan<bool> isCandidate)
    {
        // Classification consumes the value, so errors report the position of the value captured before it runs.
        var sourceName = reader.SourceName;
        var start = reader.Start;
        var end = reader.End;
        var classified = YamlTypeClassification.Classify(reader, GetClassifierContext(reader), out var bufferedNode);
        if (classified is null)
        {
            throw new YamlException(sourceName, start, end, $"Cannot deserialize union type '{_unionType}' because multiple cases match YAML {GetKindDescription(kind)} values.");
        }

        for (var i = 0; i < _readCases.Length; i++)
        {
            // A classifier selecting a case that cannot represent the value, such as a number case for a mapping, is
            // reported like a value no case matches.
            var unionCase = _readCases[i];
            if (!isCandidate[i] || unionCase.RuntimeType != classified)
            {
                continue;
            }

            // Classification consumed the value, so the case is deserialized from the buffered copy.
            var caseReader = reader.CreateReader(bufferedNode!);
            if (!caseReader.Read())
            {
                return default;
            }

            var converter = GetCaseConverter(caseReader, unionCase);
            return CreateValue(reader, unionCase, converter.Read(caseReader, unionCase.Type));
        }

        throw CreateNoMatchingCaseException(sourceName, start, end, kind);
    }

    private YamlTypeClassifierContext GetClassifierContext(YamlReader reader)
    {
        if (_classifierContext is not null && ReferenceEquals(_classifierContextOptions, reader.Options))
        {
            return _classifierContext;
        }

        var cases = new List<YamlUnionCaseInfo>(_readCases.Length);
        foreach (var unionCase in _readCases)
        {
            // A case referencing the union itself never reads a value, so it is not a case to classify.
            if (unionCase.ConverterTypes.IsEmpty)
            {
                continue;
            }

            IReadOnlyList<YamlUnionCaseProperty>? properties = null;
            var disallowUnmappedProperties = false;
            if (unionCase.ExactKinds is UnionCaseKinds.Mapping && reader.GetConverter(unionCase.RuntimeType) is IYamlUnionCaseShapeProvider provider)
            {
                properties = provider.GetUnionCaseProperties(reader, out disallowUnmappedProperties);
            }

            cases.Add(new YamlUnionCaseInfo(unionCase.RuntimeType, GetShape(unionCase.ExactKinds), properties, disallowUnmappedProperties));
        }

        var context = YamlTypeClassifierContext.CreateForUnion(_unionType, [.. cases]);
        _classifierContextOptions = reader.Options;
        _classifierContext = context;
        return context;
    }

    // A case matching several kinds, such as a nested union, is described as matching any shape.
    private static YamlUnionCaseShape GetShape(UnionCaseKinds kinds)
        => kinds switch
        {
            UnionCaseKinds.Boolean => YamlUnionCaseShape.Boolean,
            UnionCaseKinds.Number => YamlUnionCaseShape.Number,
            UnionCaseKinds.String => YamlUnionCaseShape.Text,
            UnionCaseKinds.Sequence => YamlUnionCaseShape.Sequence,
            UnionCaseKinds.Mapping => YamlUnionCaseShape.Mapping,
            _ => YamlUnionCaseShape.Any,
        };

    private T? CreateNullValue(YamlReader reader)
    {
        // A union with no nullable case still has a null state: 'default(TUnion)' selects no case and exposes a null
        // value. Returning the default value makes 'default(TUnion)' round-trip through a null scalar.
        if (_nullableCase is null)
        {
            return default;
        }

        return CreateValue(reader, _nullableCase.Value, null);
    }

    private T? CreateValue(YamlReader reader, UnionCase unionCase, object? caseValue)
    {
        try
        {
            return (T?)unionCase.Constructor.Invoke(new object?[] { caseValue });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Cannot create union value '{_unionType}'.", exception.InnerException);
        }
    }

    private YamlException CreateNoMatchingCaseException(string? sourceName, Mark start, Mark end, UnionCaseKinds kind)
        => new(sourceName, start, end, $"Union type '{_unionType}' does not define a case that matches YAML {GetKindDescription(kind)} values.");

    private static string GetKindDescription(UnionCaseKinds kind)
        => kind switch
        {
            UnionCaseKinds.Boolean => "boolean",
            UnionCaseKinds.Number => "number",
            UnionCaseKinds.String => "scalar string",
            UnionCaseKinds.Sequence => "sequence",
            _ => "mapping",
        };

    private static UnionCase? FindNullableCase(ImmutableArray<UnionCase> cases)
    {
        for (var i = 0; i < cases.Length; i++)
        {
            var unionCase = cases[i];
            if (unionCase.AcceptsNull)
            {
                return unionCase;
            }
        }

        return null;
    }

    [Flags]
    private enum UnionCaseKinds
    {
        None = 0,
        Boolean = 1,
        Number = 2,
        String = 4,
        Sequence = 8,
        Mapping = 16,
        Scalar = Boolean | Number | String,
        All = Scalar | Sequence | Mapping,
    }

    private readonly record struct UnionCase(
        Type Type,
        Type RuntimeType,
        ConstructorInfo Constructor,
        UnionCaseKinds ExactKinds,
        UnionCaseKinds FallbackKinds,
        ImmutableArray<Type> ConverterTypes,
        bool AcceptsNull,
        YamlNumberHandling NumberHandling);
}
