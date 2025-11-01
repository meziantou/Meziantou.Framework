using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable;

/// <summary>
/// Provides options for configuring the behavior of the human-readable serializer.
/// </summary>
public sealed record HumanReadableSerializerOptions
{
    // Cache
    private readonly ConcurrentDictionary<Type, HumanReadableConverter> _convertersCache;
    private readonly ConcurrentDictionary<Type, HumanReadableMemberInfo[]> _memberInfoCache;

    private readonly List<(Func<Type, bool>? Condition, HumanReadableAttribute Attribute)> _typeAttributes;
    private readonly List<(Func<MemberInfo, bool>? Condition, HumanReadableAttribute Attribute)> _memberAttributes;
    private readonly Dictionary<string, ValueFormatter> _valueFormatters;
    [ThreadStatic]
    private static SerializationContext s_currentContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="HumanReadableSerializerOptions"/> class with default settings.
    /// </summary>
    public HumanReadableSerializerOptions()
    {
        _memberAttributes = [];
        _typeAttributes = [];
        _memberInfoCache = new();
        _convertersCache = new();
        _valueFormatters = new(StringComparer.OrdinalIgnoreCase);

        Converters = new ConverterList(this);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Clone constructor (use by the with keyword)")]
    private HumanReadableSerializerOptions(HumanReadableSerializerOptions? options)
    {
        _memberAttributes = [];
        _typeAttributes = [];
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

    /// <summary>
    /// Gets or sets serialization data associated with the specified name for the current serialization context.
    /// </summary>
    /// <typeparam name="T">The type of the data.</typeparam>
    /// <param name="name">The name of the data.</param>
    /// <param name="addValue">A function to create the value if it doesn't exist.</param>
    /// <returns>The data associated with the specified name.</returns>
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

    /// <summary>
    /// Gets a value indicating whether the options instance is read-only.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Gets or sets the maximum depth allowed when serializing nested objects. The default value is 64.
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// Gets or sets a value indicating whether invisible characters (such as control characters) should be shown in serialized string values.
    /// </summary>
    public bool ShowInvisibleCharactersInValues { get; set; }

    /// <summary>
    /// Gets the list of converters used to serialize types.
    /// </summary>
    public IList<HumanReadableConverter> Converters { get; }

    /// <summary>
    /// Gets or sets a comparer to determine the order in which properties are serialized.
    /// </summary>
    public IComparer<string>? PropertyOrder
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets a comparer to determine the order in which dictionary keys are serialized.
    /// </summary>
    public IComparer<string>? DictionaryKeyOrder
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether fields should be included during serialization.
    /// </summary>
    public bool IncludeFields
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether members marked with the ObsoleteAttribute should be included during serialization.
    /// </summary>
    public bool IncludeObsoleteMembers
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the default condition for ignoring properties during serialization.
    /// </summary>
    public HumanReadableIgnoreCondition DefaultIgnoreCondition
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>
    /// Adds a value formatter for the specified media type.
    /// </summary>
    /// <param name="mediaType">The media type (e.g., "application/json").</param>
    /// <param name="formatter">The formatter to use for the media type.</param>
    public void AddFormatter(string mediaType, ValueFormatter formatter)
    {
        VerifyMutable();
        _valueFormatters[mediaType] = formatter;
    }

    /// <summary>
    /// Adds an attribute to the specified type for serialization purposes.
    /// </summary>
    /// <param name="type">The type to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    public void AddAttribute(Type type, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _typeAttributes.Add((t => t == type, attribute));
    }

    /// <summary>
    /// Adds an attribute to the specified member of a type for serialization purposes.
    /// </summary>
    /// <param name="type">The type containing the member.</param>
    /// <param name="memberName">The name of the member.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <exception cref="ArgumentException">Thrown when the member is not found in the type.</exception>
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

    /// <summary>
    /// Adds an attribute to the specified field for serialization purposes.
    /// </summary>
    /// <param name="member">The field to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    public void AddAttribute(FieldInfo member, HumanReadableAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    /// <summary>
    /// Adds an attribute to the specified property for serialization purposes.
    /// </summary>
    /// <param name="member">The property to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    public void AddAttribute(PropertyInfo member, HumanReadableAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    /// <summary>
    /// Adds an attribute to all properties that match the specified condition.
    /// </summary>
    /// <param name="condition">A function to test each property.</param>
    /// <param name="attribute">The attribute to add to matching properties.</param>
    public void AddPropertyAttribute(Func<PropertyInfo, bool> condition, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _memberAttributes.Add((Condition: member => member is PropertyInfo property && condition(property), attribute));
    }

    /// <summary>
    /// Adds an attribute to the member specified by the expression.
    /// </summary>
    /// <typeparam name="T">The type containing the member.</typeparam>
    /// <param name="member">An expression that identifies the member.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <exception cref="ArgumentException">Thrown when the expression does not refer to a field or property.</exception>
    public void AddAttribute<T>(Expression<Func<T, object>> member, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        var memberInfos = member.GetMemberInfos();
        if (memberInfos.Count is 0)
            throw new ArgumentException($"Expression '{member}' does not refer to a field or a property.", nameof(member));

        foreach (var memberInfo in memberInfos)
        {
            if (memberInfo is PropertyInfo propertyInfo)
            {
                AddAttribute(propertyInfo, attribute);
                continue;
            }

            if (memberInfo is FieldInfo fieldInfo)
            {
                AddAttribute(fieldInfo, attribute);
                continue;
            }

            throw new ArgumentException($"Member '{member.Name}' does not refer to a field or a property", nameof(member));
        }
    }

    /// <summary>
    /// Adds an attribute to all fields that match the specified condition.
    /// </summary>
    /// <param name="condition">A function to test each field.</param>
    /// <param name="attribute">The attribute to add to matching fields.</param>
    public void AddFieldAttribute(Func<FieldInfo, bool> condition, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        _memberAttributes.Add((Condition: member => member is FieldInfo field && condition(field), attribute));
    }

    /// <summary>
    /// Adds an attribute to all types that match the specified condition.
    /// </summary>
    /// <param name="condition">A function to test each type.</param>
    /// <param name="attribute">The attribute to add to matching types.</param>
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
                if (factoryConverter is null)
                    return null;

                return WrapConverter(factoryConverter);
            }

            return WrapConverter(converter);
        }

        HumanReadableConverter FindConverter(Type type, IList<HumanReadableConverter> converters)
        {
            // Priority 1: Attempt to get custom converter from the Converters list
            var converter = TryGetFromList(type, converters, this);
            if (converter is not null)
                return converter;

            // Priority 2: Attempt to get converter from [HumanReadableConverterAttribute] on the type being converted.
            var converterAttribute = GetCustomAttribute<HumanReadableConverterAttribute>(type);
            if (converterAttribute is not null)
                return HumanReadableConverter.CreateFromAttribute(converterAttribute, type);

            // Priority 3: Query the built-in converters.
            converter = TryGetFromList(type, ConverterList.DefaultConverters, this);
            if (converter is not null)
                return converter;

            throw new InvalidOperationException($"No converter for type '{type}'");

            static HumanReadableConverter? TryGetFromList(Type type, IEnumerable<HumanReadableConverter> converters, HumanReadableSerializerOptions options)
            {
                foreach (var converter in converters)
                {
                    if (converter is null)
                        continue;

                    if (converter.CanConvert(type))
                    {
                        var result = TryGetConverter(converter, type, options);
                        if (result is not null)
                            return result;
                    }
                }

                return null;
            }
        }
    }

    internal HumanReadableMemberInfo[] GetMembers(Type type)
    {
        return _memberInfoCache.GetOrAdd(type, static (type, options) => HumanReadableMemberInfo.Get(type, options), this);
    }

    /// <summary>
    /// Gets the value formatter for the specified media type.
    /// </summary>
    /// <param name="mediaType">The media type to get the formatter for.</param>
    /// <returns>The formatter for the media type, or <see langword="null"/> if no formatter is registered.</returns>
    public ValueFormatter? GetFormatter(string mediaType)
    {
        if (mediaType is not null)
        {
            // Exact match
            if (_valueFormatters.TryGetValue(mediaType, out var formatter))
                return formatter;

            // Normalize the format
            mediaType = GetFormat(mediaType);
            if (mediaType is not null && _valueFormatters.TryGetValue(mediaType, out formatter))
                return formatter;
        }

        return null;

        static string? GetFormat(string mediaType)
        {
            return mediaType switch
            {
                _ when IsJson(mediaType) => ValueFormatter.JsonMediaTypeName,
                _ when IsXml(mediaType) => ValueFormatter.XmlMediaTypeName,
                _ when IsHtml(mediaType) => ValueFormatter.HtmlMediaTypeName,
                _ when IsUrlEncodedForm(mediaType) => ValueFormatter.WwwFormUrlEncodedMediaTypeName,
                _ when IsCss(mediaType) => ValueFormatter.CssMediaTypeName,
                _ when IsJavaScript(mediaType) => ValueFormatter.JavascriptMediaTypeName,
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

    /// <summary>
    /// Makes the options instance read-only, preventing further modifications.
    /// </summary>
    public void MakeReadOnly() => IsReadOnly = true;

    private sealed class ConverterList : ConfigurationList<HumanReadableConverter>
    {
        internal static readonly HumanReadableConverter[] DefaultConverters =
        [
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
#if NET8_0_OR_GREATER
            new IPNetworkConverter(),
#endif
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
        ];

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
