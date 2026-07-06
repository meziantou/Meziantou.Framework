using System.Collections.Immutable;
using System.Reflection;
using Meziantou.Framework.Yaml.Serialization.Converters;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Shared base class for <see cref="YamlReader"/> and <see cref="YamlWriter"/>.
/// </summary>
/// <remarks>
/// This type centralizes option access and caches frequently computed values (naming policy conversions and converter resolution)
/// to reduce repeated runtime work during serialization and deserialization.
/// </remarks>
public abstract class YamlReaderWriterBase
{
    private readonly StringComparer _propertyNameComparer;
    private Dictionary<string, string>? _propertyNameCache;
    private Dictionary<string, string>? _dictionaryKeyCache;
    private Dictionary<Type, YamlConverter>? _converterCache;
    private Dictionary<Type, YamlConverter?>? _customConverterCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlReaderWriterBase"/> class.
    /// </summary>
    /// <param name="options">The options associated with this reader or writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    protected YamlReaderWriterBase(YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        _propertyNameComparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    /// <summary>Gets the options associated with this reader or writer instance.</summary>
    public YamlSerializerOptions Options { get; }

    internal StringComparer PropertyNameComparer => _propertyNameComparer;

    /// <summary>
    /// Converts a CLR member name into a YAML member name using <see cref="YamlSerializerOptions.PropertyNamingPolicy"/>.
    /// </summary>
    /// <param name="name">The CLR member name.</param>
    /// <returns>The converted YAML member name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public string ConvertName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var policy = Options.PropertyNamingPolicy;
        if (policy is null || name.Length == 0)
        {
            return name;
        }

        _propertyNameCache ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (_propertyNameCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var converted = policy.ConvertName(name);
        _propertyNameCache[name] = converted;
        return converted;
    }

    /// <summary>
    /// Converts a dictionary key into a YAML key using <see cref="YamlSerializerOptions.DictionaryKeyPolicy"/>.
    /// </summary>
    /// <param name="key">The dictionary key.</param>
    /// <returns>The converted YAML key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public string ConvertDictionaryKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var policy = Options.DictionaryKeyPolicy;
        if (policy is null || key.Length == 0)
        {
            return key;
        }

        _dictionaryKeyCache ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (_dictionaryKeyCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var converted = policy.ConvertName(key);
        _dictionaryKeyCache[key] = converted;
        return converted;
    }

    /// <summary>
    /// Gets a converter that can handle <paramref name="typeToConvert"/>.
    /// </summary>
    /// <param name="typeToConvert">The CLR type to resolve.</param>
    /// <returns>The converter for <paramref name="typeToConvert"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeToConvert"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">No converter can handle <paramref name="typeToConvert"/>.</exception>
    public YamlConverter GetConverter(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        _converterCache ??= new Dictionary<Type, YamlConverter>();
        if (_converterCache.TryGetValue(typeToConvert, out var cached))
        {
            return cached;
        }

        var attributeConverter = CreateConverterFromAttribute(typeToConvert);
        if (attributeConverter is not null)
        {
            _converterCache[typeToConvert] = attributeConverter;
            return attributeConverter;
        }

        if (TryGetCustomConverter(typeToConvert, out var custom) && custom is not null)
        {
            _converterCache[typeToConvert] = custom;
            return custom;
        }

        var created = YamlBuiltInConverters.CreateConverter(typeToConvert);
        if (created is null)
        {
            throw new NotSupportedException($"No YAML converter is registered for '{typeToConvert}'.");
        }

        _converterCache[typeToConvert] = created;
        return created;
    }

    private YamlConverter? CreateConverterFromAttribute(Type typeToConvert)
    {
        var attribute = typeToConvert.GetCustomAttribute<YamlConverterAttribute>(inherit: false);
        if (attribute is null)
        {
            return null;
        }

        var converterType = attribute.ConverterType;
        if (converterType.IsGenericTypeDefinition)
        {
            throw new NotSupportedException($"Converter type '{converterType}' cannot be an open generic type.");
        }

        if (!typeof(YamlConverter).IsAssignableFrom(converterType))
        {
            throw new NotSupportedException($"Converter type '{converterType}' must derive from '{typeof(YamlConverter)}'.");
        }

        var converter = (YamlConverter)Activator.CreateInstance(converterType)!;
        if (converter is YamlConverterFactory factory)
        {
            var created = factory.CreateConverter(typeToConvert, Options);
            if (created is null || !created.CanConvert(typeToConvert))
            {
                throw new InvalidOperationException($"Converter factory '{factory.GetType()}' returned an invalid converter for '{typeToConvert}'.");
            }

            return created;
        }

        if (!converter.CanConvert(typeToConvert))
        {
            throw new NotSupportedException($"Converter '{converterType}' cannot handle '{typeToConvert}'.");
        }

        return converter;
    }

    /// <summary>
    /// Attempts to resolve a custom converter for <paramref name="typeToConvert"/> from <see cref="YamlSerializerOptions.Converters"/>.
    /// </summary>
    /// <param name="typeToConvert">The CLR type to resolve.</param>
    /// <param name="converter">When successful, receives the converter instance.</param>
    /// <returns><see langword="true"/> when a custom converter was resolved; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeToConvert"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A converter factory returned <see langword="null"/> or returned a converter that does not handle <paramref name="typeToConvert"/>.
    /// </exception>
    public bool TryGetCustomConverter(Type typeToConvert, out YamlConverter? converter)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        if (Options.Converters.Count == 0)
        {
            converter = null;
            return false;
        }

        _customConverterCache ??= new Dictionary<Type, YamlConverter?>();
        if (_customConverterCache.TryGetValue(typeToConvert, out converter))
        {
            return converter is not null;
        }

        // Search user-provided converters first (same precedence rule as System.Text.Json).
        for (var i = 0; i < Options.Converters.Count; i++)
        {
            var candidate = Options.Converters[i];
            if (candidate is null)
            {
                continue;
            }

            if (candidate is YamlConverterFactory factory)
            {
                if (!factory.CanConvert(typeToConvert))
                {
                    continue;
                }

                var created = factory.CreateConverter(typeToConvert, Options);
                if (created is null || !created.CanConvert(typeToConvert))
                {
                    throw new InvalidOperationException($"Converter factory '{factory.GetType()}' returned an invalid converter for '{typeToConvert}'.");
                }

                converter = created;
                _customConverterCache[typeToConvert] = converter;
                return true;
            }

            if (candidate.CanConvert(typeToConvert))
            {
                converter = candidate;
                _customConverterCache[typeToConvert] = converter;
                return true;
            }
        }

        converter = null;
        _customConverterCache[typeToConvert] = null;
        return false;
    }

    private static class YamlBuiltInConverters
    {
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050",
            Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2071",
            Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2070",
            Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
        public static YamlConverter? CreateConverter(Type typeToConvert)
        {
            if (typeToConvert == typeof(string))
            {
                return YamlStringConverter.Instance;
            }

            if (typeToConvert == typeof(bool))
            {
                return YamlBooleanConverter.Instance;
            }

            if (typeToConvert == typeof(byte))
            {
                return YamlByteConverter.Instance;
            }

            if (typeToConvert == typeof(sbyte))
            {
                return YamlSByteConverter.Instance;
            }

            if (typeToConvert == typeof(short))
            {
                return YamlInt16Converter.Instance;
            }

            if (typeToConvert == typeof(ushort))
            {
                return YamlUInt16Converter.Instance;
            }

            if (typeToConvert == typeof(int))
            {
                return YamlInt32Converter.Instance;
            }

            if (typeToConvert == typeof(uint))
            {
                return YamlUInt32Converter.Instance;
            }

            if (typeToConvert == typeof(long))
            {
                return YamlInt64Converter.Instance;
            }

            if (typeToConvert == typeof(ulong))
            {
                return YamlUInt64Converter.Instance;
            }

            if (typeToConvert == typeof(double))
            {
                return YamlDoubleConverter.Instance;
            }

            if (typeToConvert == typeof(float))
            {
                return YamlSingleConverter.Instance;
            }

            if (typeToConvert == typeof(decimal))
            {
                return YamlDecimalConverter.Instance;
            }

            if (typeToConvert == typeof(char))
            {
                return YamlCharConverter.Instance;
            }

            if (typeToConvert == typeof(DateTime))
            {
                return YamlDateTimeConverter.Instance;
            }

            if (typeToConvert == typeof(DateTimeOffset))
            {
                return YamlDateTimeOffsetConverter.Instance;
            }

            if (typeToConvert == typeof(Guid))
            {
                return YamlGuidConverter.Instance;
            }

            if (typeToConvert == typeof(TimeSpan))
            {
                return YamlTimeSpanConverter.Instance;
            }

            if (typeToConvert == typeof(DateOnly))
            {
                return YamlDateOnlyConverter.Instance;
            }

            if (typeToConvert == typeof(TimeOnly))
            {
                return YamlTimeOnlyConverter.Instance;
            }

            if (typeToConvert == typeof(Half))
            {
                return YamlHalfConverter.Instance;
            }

            if (typeToConvert == typeof(Int128))
            {
                return YamlInt128Converter.Instance;
            }

            if (typeToConvert == typeof(UInt128))
            {
                return YamlUInt128Converter.Instance;
            }

            if (typeToConvert == typeof(nint))
            {
                return YamlIntPtrConverter.Instance;
            }

            if (typeToConvert == typeof(nuint))
            {
                return YamlUIntPtrConverter.Instance;
            }

            if (typeToConvert == typeof(object))
            {
                return YamlUntypedObjectConverter.Instance;
            }

            if (typeToConvert == typeof(object[]))
            {
                return YamlObjectArrayConverter.Instance;
            }

            if (typeToConvert == typeof(List<object>))
            {
                return YamlListObjectConverter.Instance;
            }

            if (typeToConvert == typeof(Dictionary<string, object>))
            {
                return YamlDictionaryObjectConverter.Instance;
            }

            if (typeof(Model.YamlNode).IsAssignableFrom(typeToConvert))
            {
                return YamlModelNodeConverter.Instance;
            }

            if (YamlCSharpUnionConverter<object>.CanConvertUnionType(typeToConvert))
            {
                var converterType = typeof(YamlCSharpUnionConverter<>).MakeGenericType(typeToConvert);
                return (YamlConverter)Activator.CreateInstance(converterType)!;
            }

            var underlyingNullable = Nullable.GetUnderlyingType(typeToConvert);
            if (underlyingNullable is not null)
            {
                var converterType = typeof(YamlNullableConverter<>).MakeGenericType(underlyingNullable);
                return (YamlConverter)Activator.CreateInstance(converterType)!;
            }

            if (typeToConvert.IsEnum)
            {
                var converterType = typeof(YamlEnumConverter<>).MakeGenericType(typeToConvert);
                return (YamlConverter)Activator.CreateInstance(converterType)!;
            }

            if (typeToConvert.IsArray)
            {
                var elementType = typeToConvert.GetElementType()!;
                var converterType = typeof(YamlArrayConverter<>).MakeGenericType(elementType);
                return (YamlConverter)Activator.CreateInstance(converterType)!;
            }

            if (typeToConvert.IsGenericType)
            {
                var definition = typeToConvert.GetGenericTypeDefinition();
                var args = typeToConvert.GetGenericArguments();

                if (definition == typeof(List<>))
                {
                    if (args[0] == typeof(object))
                    {
                        return YamlListObjectConverter.Instance;
                    }

                    var converterType = typeof(YamlListConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(Dictionary<,>) && args[0] == typeof(string))
                {
                    if (args[1] == typeof(object))
                    {
                        return YamlDictionaryObjectConverter.Instance;
                    }

                    var converterType = typeof(YamlDictionaryConverter<>).MakeGenericType(args[1]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(Dictionary<,>))
                {
                    var converterType = typeof(YamlDictionaryConverter<,>).MakeGenericType(args[0], args[1]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(OrderedDictionary<,>) && args[0] == typeof(string))
                {
                    var converterType = typeof(YamlOrderedDictionaryConverter<>).MakeGenericType(args[1]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(OrderedDictionary<,>))
                {
                    var converterType = typeof(YamlOrderedDictionaryConverter<,>).MakeGenericType(args[0], args[1]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(IDictionary<,>))
                {
                    var converterType = typeof(YamlIDictionaryConverter<,>).MakeGenericType(args[0], args[1]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(IReadOnlyDictionary<,>))
                {
                    var converterType = typeof(YamlIReadOnlyDictionaryConverter<,>).MakeGenericType(args[0], args[1]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(HashSet<>))
                {
                    var converterType = typeof(YamlHashSetConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(ISet<>))
                {
                    var converterType = typeof(YamlISetConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(IList<>))
                {
                    var converterType = typeof(YamlIListConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(ICollection<>))
                {
                    var converterType = typeof(YamlICollectionConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(IReadOnlyList<>))
                {
                    var converterType = typeof(YamlIReadOnlyListConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(IReadOnlyCollection<>))
                {
                    var converterType = typeof(YamlIReadOnlyCollectionConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(ImmutableArray<>))
                {
                    var converterType = typeof(YamlImmutableArrayConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(ImmutableList<>))
                {
                    var converterType = typeof(YamlImmutableListConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(ImmutableHashSet<>))
                {
                    var converterType = typeof(YamlImmutableHashSetConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }

                if (definition == typeof(IEnumerable<>))
                {
                    var converterType = typeof(YamlEnumerableConverter<>).MakeGenericType(args[0]);
                    return (YamlConverter)Activator.CreateInstance(converterType)!;
                }
            }

            var objectConverterType = typeof(YamlObjectConverter<>).MakeGenericType(typeToConvert);
            return (YamlConverter)Activator.CreateInstance(objectConverterType)!;
        }
    }
}
