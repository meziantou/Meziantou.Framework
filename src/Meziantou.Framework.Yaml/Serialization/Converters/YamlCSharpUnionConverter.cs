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
        return TryCreateCases(typeToConvert, out _, out _);
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
                throw CreateNoMatchingCaseException(reader, GetKind(aliasValue.GetType()));
            }

            return CreateValue(reader, aliasCase.Value, aliasValue);
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return CreateNullValue(reader);
        }

        var kind = GetCurrentKind(reader);
        if (TryGetSingleCaseForKind(kind, out var unionCase, out var ambiguous))
        {
            var converter = reader.GetConverter(unionCase.Type);
            var caseValue = converter.Read(reader, unionCase.Type);
            return CreateValue(reader, unionCase, caseValue);
        }

        if (!ambiguous)
        {
            throw CreateNoMatchingCaseException(reader, kind);
        }

        return ReadClassifiedValue(reader, kind);
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

        var converter = writer.GetConverter(unionCase.Value.Type);
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
        valueProperty = null;
        cases = ImmutableArray<UnionCase>.Empty;

        if (!HasUnionAttribute(unionType))
        {
            return false;
        }

        valueProperty = unionType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty is null || valueProperty.PropertyType != typeof(object))
        {
            return false;
        }

        var constructors = unionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        if (constructors.Length == 0)
        {
            return false;
        }

        var nullabilityInfoContext = new NullabilityInfoContext();
        var builder = ImmutableArray.CreateBuilder<UnionCase>();
        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            var parameter = parameters[0];
            var caseType = parameter.ParameterType;
            var runtimeType = Nullable.GetUnderlyingType(caseType) ?? caseType;
            var acceptsNull = IsNullableParameter(nullabilityInfoContext, parameter);
            builder.Add(new UnionCase(caseType, runtimeType, constructor, GetKind(caseType), acceptsNull));
        }

        if (builder.Count == 0)
        {
            return false;
        }

        cases = builder.ToImmutable();
        return true;
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

    private static ImmutableArray<UnionCase> SortCasesForWriting(ImmutableArray<UnionCase> cases)
    {
        var builder = cases.ToBuilder();
        builder.Sort((left, right) =>
        {
            if (left.RuntimeType == right.RuntimeType)
            {
                return 0;
            }

            if (left.RuntimeType.IsAssignableFrom(right.RuntimeType))
            {
                return 1;
            }

            if (right.RuntimeType.IsAssignableFrom(left.RuntimeType))
            {
                return -1;
            }

            return 0;
        });
        return builder.ToImmutable();
    }

    private static UnionCaseKind GetCurrentKind(YamlReader reader)
    {
        if (reader.TokenType == YamlTokenType.StartMapping)
        {
            return UnionCaseKind.Mapping;
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            return UnionCaseKind.Sequence;
        }

        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Token '{reader.TokenType}' cannot be deserialized into union type '{typeof(T)}'.");
        }

        var scalarValue = YamlScalar.ResolveObject(reader);
        return scalarValue switch
        {
            null => UnionCaseKind.Null,
            bool => UnionCaseKind.Boolean,
            sbyte or byte or short or ushort or int or uint or long or ulong or nint or nuint or float or double or decimal or Half or Int128 or UInt128 => UnionCaseKind.Number,
            _ => UnionCaseKind.String,
        };
    }

    private static UnionCaseKind GetKind(Type type)
    {
        var runtimeType = Nullable.GetUnderlyingType(type) ?? type;
        if (runtimeType == typeof(object) || typeof(YamlNode).IsAssignableFrom(runtimeType))
        {
            return UnionCaseKind.Any;
        }

        if (runtimeType == typeof(bool))
        {
            return UnionCaseKind.Boolean;
        }

        if (IsNumeric(runtimeType))
        {
            return UnionCaseKind.Number;
        }

        if (runtimeType == typeof(string) ||
            runtimeType == typeof(char) ||
            runtimeType == typeof(DateTime) ||
            runtimeType == typeof(DateTimeOffset) ||
            runtimeType == typeof(Guid) ||
            runtimeType == typeof(TimeSpan) ||
            runtimeType == typeof(DateOnly) ||
            runtimeType == typeof(TimeOnly) ||
            runtimeType.IsEnum)
        {
            return UnionCaseKind.String;
        }

        if (IsDictionary(runtimeType))
        {
            return UnionCaseKind.Mapping;
        }

        if (runtimeType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(runtimeType))
        {
            return UnionCaseKind.Sequence;
        }

        return UnionCaseKind.Mapping;
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

    private bool TryGetSingleCaseForKind(UnionCaseKind kind, out UnionCase unionCase, out bool ambiguous)
    {
        UnionCase? match = null;
        var matchCount = 0;
        for (var i = 0; i < _readCases.Length; i++)
        {
            var candidate = _readCases[i];
            if (candidate.Kind == kind || candidate.Kind == UnionCaseKind.Any)
            {
                match ??= candidate;
                matchCount++;
            }
        }

        ambiguous = matchCount > 1;
        unionCase = match.GetValueOrDefault();
        return matchCount == 1;
    }

    private T? ReadClassifiedValue(YamlReader reader, UnionCaseKind kind)
    {
        var classified = YamlUnionClassification.Classify(reader, GetClassifierContext(reader), out var bufferedNode);
        if (classified is null)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Cannot deserialize union type '{_unionType}' because multiple cases match YAML {GetKindDescription(kind)} values.");
        }

        for (var i = 0; i < _readCases.Length; i++)
        {
            var unionCase = _readCases[i];
            if (unionCase.RuntimeType != classified)
            {
                continue;
            }

            // Classification consumed the value, so the case is deserialized from the buffered copy.
            var caseReader = reader.CreateReader(bufferedNode!);
            if (!caseReader.Read())
            {
                return default;
            }

            var converter = caseReader.GetConverter(unionCase.Type);
            return CreateValue(reader, unionCase, converter.Read(caseReader, unionCase.Type));
        }

        throw CreateNoMatchingCaseException(reader, kind);
    }

    private YamlTypeClassifierContext GetClassifierContext(YamlReader reader)
    {
        if (_classifierContext is not null && ReferenceEquals(_classifierContextOptions, reader.Options))
        {
            return _classifierContext;
        }

        var cases = new YamlUnionCaseInfo[_readCases.Length];
        for (var i = 0; i < _readCases.Length; i++)
        {
            var unionCase = _readCases[i];
            IReadOnlyList<YamlUnionCaseProperty>? properties = null;
            var disallowUnmappedProperties = false;
            if (unionCase.Kind is UnionCaseKind.Mapping && reader.GetConverter(unionCase.RuntimeType) is IYamlUnionCaseShapeProvider provider)
            {
                properties = provider.GetUnionCaseProperties(reader, out disallowUnmappedProperties);
            }

            cases[i] = new YamlUnionCaseInfo(unionCase.RuntimeType, GetShape(unionCase.Kind), properties, disallowUnmappedProperties);
        }

        var context = new YamlTypeClassifierContext(_unionType, YamlTypeClassifierKind.Union, cases);
        _classifierContextOptions = reader.Options;
        _classifierContext = context;
        return context;
    }

    private static YamlUnionCaseShape GetShape(UnionCaseKind kind)
        => kind switch
        {
            UnionCaseKind.Boolean => YamlUnionCaseShape.Boolean,
            UnionCaseKind.Number => YamlUnionCaseShape.Number,
            UnionCaseKind.String => YamlUnionCaseShape.Text,
            UnionCaseKind.Sequence => YamlUnionCaseShape.Sequence,
            UnionCaseKind.Mapping => YamlUnionCaseShape.Mapping,
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

    private YamlException CreateNoMatchingCaseException(YamlReader reader, UnionCaseKind kind)
        => new(reader.SourceName, reader.Start, reader.End, $"Union type '{_unionType}' does not define a case that matches YAML {GetKindDescription(kind)} values.");

    private static string GetKindDescription(UnionCaseKind kind)
        => kind switch
        {
            UnionCaseKind.Null => "null",
            UnionCaseKind.Boolean => "boolean",
            UnionCaseKind.Number => "number",
            UnionCaseKind.String => "scalar string",
            UnionCaseKind.Sequence => "sequence",
            UnionCaseKind.Mapping => "mapping",
            _ => "untyped",
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

    private enum UnionCaseKind
    {
        Null,
        Boolean,
        Number,
        String,
        Sequence,
        Mapping,
        Any,
    }

    private readonly record struct UnionCase(
        Type Type,
        Type RuntimeType,
        ConstructorInfo Constructor,
        UnionCaseKind Kind,
        bool AcceptsNull);
}
