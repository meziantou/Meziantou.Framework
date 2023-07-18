﻿using System.Collections.Concurrent;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable;

public sealed record HumanReadableSerializerOptions
{
    // Cache
    private readonly ConcurrentDictionary<Type, HumanReadableConverter> _convertersCache;
    private readonly ConcurrentDictionary<Type, HumanReadableMemberInfo[]> _memberInfoCache;

    private readonly List<(Func<Type, bool>? Condition, HumanReadableAttribute Attribute)> _typeAttributes;
    private readonly List<(Func<MemberInfo, bool>? Condition, HumanReadableAttribute Attribute)> _memberAttributes;
    private readonly Dictionary<string, ValueFormatter> _valueFormatters;
    private bool _includeFields;
    private HumanReadableIgnoreCondition _defaultIgnoreCondition;
    private IComparer<string>? _dictionaryKeyOrder;
    private IComparer<string>? _propertyOrder;
    private bool _includeObsoleteMembers;

    [ThreadStatic]
    private static SerializationContext s_currentContext;

    public HumanReadableSerializerOptions()
    {
        _memberAttributes = new();
        _typeAttributes = new();
        _memberInfoCache = new();
        _convertersCache = new();
        _valueFormatters = new(StringComparer.OrdinalIgnoreCase);

        Converters = new ConverterList(this);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Clone constructor (use by the with keyword)")]
    private HumanReadableSerializerOptions(HumanReadableSerializerOptions? options)
    {
        _memberAttributes = new();
        _typeAttributes = new();
        _memberInfoCache = new();
        _convertersCache = new();
        _valueFormatters = new(StringComparer.OrdinalIgnoreCase);

        Converters = new ConverterList(this);
        if (options != null)
        {
            MaxDepth = options.MaxDepth;
            ShowInvisibleCharactersInValues = options.ShowInvisibleCharactersInValues;
            IncludeFields = options.IncludeFields;
            DefaultIgnoreCondition = options.DefaultIgnoreCondition;
            foreach (var converter in options.Converters)
            {
                Converters.Add(converter);
            }

            _typeAttributes.AddRange(options._typeAttributes);
            _memberAttributes.AddRange(options._memberAttributes);

            foreach (var formatter in options._valueFormatters)
            {
                _valueFormatters.Add(formatter.Key, formatter.Value);
            }
        }
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "'By design")]
    public T GetOrSetSerializationData<T>(string name, Func<T> addValue)
    {
        return s_currentContext.GetOrSetSerializationData(name, addValue);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "By design")]
    internal IDisposable BeginScope()
    {
        s_currentContext ??= new SerializationContext();
        return s_currentContext.BeginScope();
    }

    public bool IsReadOnly { get; private set; }
    public int MaxDepth { get; set; } = 64;
    public bool ShowInvisibleCharactersInValues { get; set; }
    public IList<HumanReadableConverter> Converters { get; }

    public IComparer<string>? PropertyOrder
    {
        get => _propertyOrder;
        set
        {
            VerifyMutable();
            _propertyOrder = value;
        }
    }
    
    public IComparer<string>? DictionaryKeyOrder
    {
        get => _dictionaryKeyOrder;
        set
        {
            VerifyMutable();
            _dictionaryKeyOrder = value;
        }
    }

    public bool IncludeFields
    {
        get => _includeFields;
        set
        {
            VerifyMutable();
            _includeFields = value;
        }
    }

    public bool IncludeObsoleteMembers
    {
        get => _includeObsoleteMembers;
        set
        {
            VerifyMutable();
            _includeObsoleteMembers = value;
        }
    }

    public HumanReadableIgnoreCondition DefaultIgnoreCondition
    {
        get => _defaultIgnoreCondition;
        set
        {
            VerifyMutable();
            _defaultIgnoreCondition = value;
        }
    }

    public void AddFormatter(string name, ValueFormatter formatter)
    {
        VerifyMutable();
        _valueFormatters[name] = formatter;
    }

    public void AddAttribute(Type type, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _typeAttributes.Add((t => t == type, attribute));
    }

    public void AddAttribute(Type type, string memberName, HumanReadableAttribute attribute)
    {
        VerifyMutable();

#if !NET7_0_OR_GREATER
        // .NET 6 and earlier versions, the GetProperties method does not return properties in a particular order, such as alphabetical or declaration order.
        // Type.GetMember can change this order. To make sure the order is "always" the same, let's first list all properties.
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
#endif

        var members = type.GetMember(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (members.Length == 0)
            throw new ArgumentException($"Cannot find an instance member named '{memberName}' in type '{type.AssemblyQualifiedName}'.", nameof(memberName));

        foreach (var member in members)
        {
            AddAttribute(member, attribute);
        }
    }

    private void AddAttribute(MemberInfo member, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _memberAttributes.Add((m => m == member, attribute));
    }

    public void AddAttribute(FieldInfo member, HumanReadableAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    public void AddAttribute(PropertyInfo member, HumanReadableAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    public void AddPropertyAttribute(Func<PropertyInfo, bool> condition, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _memberAttributes.Add((Condition: member => member is PropertyInfo property && condition(property), attribute));
    }

    public void AddFieldAttribute(Func<FieldInfo, bool> condition, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _memberAttributes.Add((Condition: member => member is FieldInfo field && condition(field), attribute));
    }

    public void AddTypeAttribute(Func<Type, bool> condition, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _typeAttributes.Add((condition, attribute));
    }

    internal T? GetCustomAttribute<T>(Type type) where T : HumanReadableAttribute
    {
        MakeReadOnly();

        // Read reverse, so attributes set by the user override the default attributes
        for (var i = _typeAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _typeAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(type))
                return result;
        }

        return type.GetCustomAttribute<T>();
    }

    internal T? GetCustomAttribute<T>(MemberInfo member) where T : HumanReadableAttribute
    {
        MakeReadOnly();

        // Read reverse, so attributes set by the user override the default attributes
        for (var i = _memberAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _memberAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(member))
                return result;
        }

        return member.GetCustomAttribute<T>();
    }

    internal IEnumerable<T> GetCustomAttributes<T>(MemberInfo member) where T : HumanReadableAttribute
    {
        MakeReadOnly();

        // Read reverse, so attributes set by the user override the default attributes
        for (var i = _memberAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _memberAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(member))
                yield return result;
        }

        foreach (var attribute in member.GetCustomAttributes<T>())
            yield return attribute;
    }

    internal HumanReadableConverter GetConverter(Type type)
    {
        // Make sure the instance is readonly on the first usage
        MakeReadOnly();
        return _convertersCache.GetOrAdd(type, type => FindConverter(type, Converters));

        static HumanReadableConverter WrapConverter(HumanReadableConverter converter)
            => converter.HandleNull ? converter : new NullConverterWrapper(converter);

        static HumanReadableConverter? TryGetConverter(HumanReadableConverter converter, Type type, HumanReadableSerializerOptions options)
        {
            if (converter is HumanReadableConverterFactory factory)
            {
                var factoryConverter = factory.CreateConverter(type, options);
                if (factoryConverter == null)
                    return null;

                return WrapConverter(factoryConverter);
            }

            return WrapConverter(converter);
        }

        HumanReadableConverter FindConverter(Type type, IList<HumanReadableConverter> converters)
        {
            // Priority 1: Attempt to get custom converter from the Converters list
            var converter = TryGetFromList(type, converters, this);
            if (converter != null)
                return converter;

            // Priority 2: Attempt to get converter from [HumanReadableConverterAttribute] on the type being converted.
            var converterAttribute = GetCustomAttribute<HumanReadableConverterAttribute>(type);
            if (converterAttribute != null)
                return HumanReadableConverter.CreateFromAttribute(converterAttribute, type);

            // Priority 3: Query the built-in converters.
            converter = TryGetFromList(type, ConverterList.DefaultConverters, this);
            if (converter != null)
                return converter;

            throw new InvalidOperationException($"No converter for type '{type}'");

            static HumanReadableConverter? TryGetFromList(Type type, IEnumerable<HumanReadableConverter> converters, HumanReadableSerializerOptions options)
            {
                foreach (var converter in converters)
                {
                    if (converter == null)
                        continue;

                    if (converter.CanConvert(type))
                    {
                        var result = TryGetConverter(converter, type, options);
                        if (result != null)
                            return result;
                    }
                }

                return null;
            }
        }
    }

    internal HumanReadableMemberInfo[] GetMembers(Type type)
    {
#if NET6_0_OR_GREATER
        return _memberInfoCache.GetOrAdd(type, static (type, options) => HumanReadableMemberInfo.Get(type, options), this);
#else
        return _memberInfoCache.GetOrAdd(type, type => HumanReadableMemberInfo.Get(type, this));
#endif
    }

    internal string FormatValue(string? format, string? value)
    {
        MakeReadOnly();

        if (format != null && value != null)
        {
            if (_valueFormatters.TryGetValue(format, out var formatter))
                return formatter.Format(value);

            // Normalize the format
            format = GetFormat(format);
            if (format != null && _valueFormatters.TryGetValue(format, out formatter))
                return formatter.Format(value);
        }


        return value ?? "";

        static string? GetFormat(string mediaType)
        {
            return mediaType switch
            {
                _ when IsJson(mediaType) => "application/json",
                _ when IsXml(mediaType) => "application/xml",
                _ when IsHtml(mediaType) => "text/html",
                _ when IsUrlEncodedForm(mediaType) => "application/x-www-form-urlencoded",
                _ when IsCss(mediaType) => "text/css",
                _ when IsJavaScript(mediaType) => "text/javascript",
                _ => null,
            };

            static bool IsHtml(string mediaType) => string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase);

            static bool IsCss(string mediaType) => string.Equals(mediaType, "text/css", StringComparison.OrdinalIgnoreCase);

            static bool IsJavaScript(string mediaType)
                => string.Equals(mediaType, "text/javascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/ecmascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/javascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/x-ecmascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/x-javascript", StringComparison.OrdinalIgnoreCase);

            static bool IsUrlEncodedForm(string mediaType) => string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

            static bool IsJson(string mediaType) => string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

            static bool IsXml(string mediaType)
                => string.Equals(mediaType, "application/xml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "text/xml", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
        }

    }

    internal void VerifyMutable()
    {
        if (IsReadOnly)
            throw new InvalidOperationException("HumanReadableSerializerOptions instance is marked as read-only");
    }

    public void MakeReadOnly() => IsReadOnly = true;

    private sealed class ConverterList : ConfigurationList<HumanReadableConverter>
    {
        internal static readonly HumanReadableConverter[] DefaultConverters = new HumanReadableConverter[]
        {
            new BigIntegerConverter(),
            new BitArrayConverter(),
            new BitVector32Converter(),
            new BooleanConverter(),
            new ByteArrayConverter(),
            new ByteConverter(),
            new CharConverter(),
            new ComplexConverter(),
            new ConstructorInfoConverter(),
            new CultureInfoConverter(),
#if NET6_0_OR_GREATER
            new DateOnlyConverter(),
#endif
            new DateTimeConverter(),
            new DateTimeOffsetConverter(),
            new DBNullConverter(),
            new DecimalConverter(),
            new DoubleConverter(),
            new ExpressionConverter(),
#if NET5_0_OR_GREATER
            new HalfConverter(),
#endif
            new HttpContentConverter(),
            new HttpMethodConverter(),
            new HttpHeadersConverter(),
            new HttpStatusCodeConverter(),
            new Int16Converter(),
            new Int32Converter(),
            new Int64Converter(),
#if NET7_0_OR_GREATER
            new Int128Converter(),
#endif
            new IntPtrConverter(),
            new IPAddressConverter(),
            new FieldInfoConverter(),
            new GuidConverter(),
            new MediaTypeHeaderValueConverter(),
            new MemoryConverterFactory(),
            new MethodInfoConverter(),
            new NameValueCollectionConverter(),
            new ParameterInfoConverter(),
            new PropertyInfoConverter(),
            new ReadOnlyMemoryConverterFactory(),
            new RegexConverter(),
            new SByteConverter(),
            new SingleConverter(),
            new StringBuilderConverter(),
            new StringConverter(),
            new StringDictionaryConverter(),
            new StringWriterConverter(),
            new SystemTypeConverter(),
            new TargetInvocationExceptionConverter(),
#if NET6_0_OR_GREATER
            new TimeOnlyConverter(),
#endif
            new TimeSpanConverter(),
            new UInt16Converter(),
            new UInt32Converter(),
            new UInt64Converter(),
#if NET7_0_OR_GREATER
            new UInt128Converter(),
#endif
            new UIntPtrConverter(),
            new UriConverter(),
            new VersionConverter(),
            new XmlNodeConverter(),
            new XObjectConverter(),
            new EnumConverter(),
            new JsonNodeConverter(),
            new JsonDocumentConverter(),
            new JsonElementConverter(),
#if NETCOREAPP2_0_OR_GREATER || NET471_OR_GREATER
            new ValueTupleConverter(),
#endif
#if NETCOREAPP2_1_OR_GREATER
            new UnixDomainSocketEndPointConverter(),
#endif

            // Last converters
            new NullableConverterFactory(),
            new MultiDimensionalArrayConverter(),
            new AsyncEnumerableKeyValuePairConverterFactory(),
            new AsyncEnumerableConverterFactory(),
            new EnumerableKeyValuePairConverterFactory(),
            new EnumerableConverterFactory(),
            new EnumerableConverter(),
            new FSharpOptionConverterFactory(),
            new FSharpValueOptionConverterFactory(),
            new FSharpDiscriminatedUnionConverter(),
            new ObjectConverterFactory(),
        };

        private readonly HumanReadableSerializerOptions _options;

        public ConverterList(HumanReadableSerializerOptions options, IList<HumanReadableConverter>? source = null)
            : base(source)
        {
            _options = options;
        }

        protected override bool IsImmutable => _options.IsReadOnly;
        protected override void VerifyMutable() => _options.VerifyMutable();
    }
}
