using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable;

/// <summary>Provides options for controlling the behavior of <see cref="HumanReadableSerializer"/>.</summary>
/// <example>
/// <code>
/// var options = new HumanReadableSerializerOptions
/// {
///     IncludeFields = true,
///     DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
/// };
/// var output = HumanReadableSerializer.Serialize(obj, options);
/// </code>
/// </example>
public sealed record HumanReadableSerializerOptions
{
    private const string DefaultNewLine = "\n";

    // Cache
    private readonly ConcurrentDictionary<Type, HumanReadableConverter> _convertersCache;
    private readonly ConcurrentDictionary<Type, HumanReadableMemberInfo[]> _memberInfoCache;

    private readonly List<(Func<Type, bool> Condition, HumanReadableAttribute Attribute)> _typeAttributes;
    // The condition receives the type being serialized and one of its members.
    // The owner identifies the attributes registered by a configuration method, so a later call can replace them.
    private readonly List<(Func<Type, MemberInfo, bool> Condition, HumanReadableAttribute Attribute, object? Owner)> _memberAttributes;
    private readonly Dictionary<string, ValueFormatter> _valueFormatters;
    private string _newLine = DefaultNewLine;

    [ThreadStatic]
    private static SerializationContext? s_currentContext;

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
    private HumanReadableSerializerOptions(HumanReadableSerializerOptions options)
    {
        // Field initializers are not executed in a record copy constructor, so every property must be copied explicitly
        _memberAttributes = [.. options._memberAttributes];
        _typeAttributes = [.. options._typeAttributes];
        _memberInfoCache = new();
        _convertersCache = new();
        _valueFormatters = new(options._valueFormatters, StringComparer.OrdinalIgnoreCase);

        Converters = new ConverterList(this);
        foreach (var converter in options.Converters)
        {
            Converters.Add(converter);
        }

        MaxDepth = options.MaxDepth;
        ShowInvisibleCharactersInValues = options.ShowInvisibleCharactersInValues;
        _newLine = options._newLine;
        PropertyOrder = options.PropertyOrder;
        DictionaryKeyOrder = options.DictionaryKeyOrder;
        IncludeFields = options.IncludeFields;
        IncludeObsoleteMembers = options.IncludeObsoleteMembers;
        DefaultIgnoreCondition = options.DefaultIgnoreCondition;
    }

    /// <summary>Gets or creates serialization data for the current serialization scope.</summary>
    /// <typeparam name="T">The type of the data.</typeparam>
    /// <param name="name">The name of the data.</param>
    /// <param name="addValue">A factory function to create the value if it doesn't exist.</param>
    /// <returns>The value associated with the specified name, creating it if necessary.</returns>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "'By design")]
    public T GetOrSetSerializationData<T>(string name, Func<T> addValue)
    {
        if (s_currentContext is null)
            throw new InvalidOperationException("No serialization context is available. Make sure to call this method within a serialization scope.");

        return s_currentContext.GetOrSetSerializationData(name, addValue);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "By design")]
    internal SerializationContext.ScopeContext BeginScope()
    {
        s_currentContext ??= new SerializationContext();
        return s_currentContext.BeginScope();
    }

    /// <summary>Gets whether this instance is read-only.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>Gets or sets the maximum depth allowed when serializing nested objects.</summary>
    public int MaxDepth
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = 64;

    /// <summary>Gets or sets whether to show invisible characters (like newlines and tabs) in values using Unicode control pictures.</summary>
    /// <remarks>
    /// Every value and property name is affected, including single-line ones. A space is kept between other characters,
    /// but written as <c>␠</c> at the start or the end of a line. The other space characters, the zero-width characters, the format characters
    /// (for example soft hyphens and bidirectional marks) and the C1 control characters, which have no control picture,
    /// are written as their code point, for example <c>&lt;U+00A0&gt;</c> for a no-break space.
    /// </remarks>
    public bool ShowInvisibleCharactersInValues
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets the line terminator written between lines, either <c>"\n"</c> or <c>"\r\n"</c>. The default is <c>"\n"</c> on every platform, so the output does not depend on the operating system.</summary>
    /// <remarks>Line breaks inside serialized values are rewritten with this terminator too.</remarks>
    /// <exception cref="ArgumentException">The value is neither <c>"\n"</c> nor <c>"\r\n"</c>.</exception>
    public string NewLine
    {
        get => _newLine;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value is not ("\n" or "\r\n"))
                throw new ArgumentException("The new line must be either \"\\n\" or \"\\r\\n\".", nameof(value));

            VerifyMutable();
            _newLine = value;
        }
    }

    /// <summary>Gets the list of converters used for serialization.</summary>
    public IList<HumanReadableConverter> Converters { get; }

    /// <summary>Gets or sets the comparer used to sort property names when serializing objects.</summary>
    public IComparer<string>? PropertyOrder
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets the comparer used to sort dictionary keys when serializing dictionaries.</summary>
    /// <remarks>
    /// When <see langword="null"/>, the keys of dictionaries that keep an order (for example <see cref="Dictionary{TKey, TValue}"/>) are written in enumeration order.
    /// The keys of hash-based collections that have no meaningful order (<see cref="System.Collections.Hashtable"/>, <see cref="System.Collections.Specialized.HybridDictionary"/>
    /// and <see cref="System.Collections.Specialized.StringDictionary"/>) are always sorted, using <see cref="StringComparer.Ordinal"/> by default,
    /// so the output does not depend on the process. The comparer also applies to the other non-generic <see cref="System.Collections.IDictionary"/> implementations.
    /// </remarks>
    public IComparer<string>? DictionaryKeyOrder
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets whether to include public fields during serialization.</summary>
    public bool IncludeFields
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets whether to include members marked with the Obsolete attribute.</summary>
    public bool IncludeObsoleteMembers
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets the default ignore condition for properties and fields.</summary>
    public HumanReadableIgnoreCondition DefaultIgnoreCondition
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Adds a value formatter for the specified media type.</summary>
    /// <param name="mediaType">The media type (e.g., "application/json", "text/xml").</param>
    /// <param name="formatter">The formatter to use for the media type.</param>
    public void AddFormatter(string mediaType, ValueFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(mediaType);
        ArgumentNullException.ThrowIfNull(formatter);

        VerifyMutable();
        _valueFormatters[mediaType] = formatter;
    }

    /// <summary>Adds an attribute to the specified type.</summary>
    /// <param name="type">The type to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <remarks>The attribute also applies to the types that derive from <paramref name="type"/> or implement it, as an attribute declared on the type would.</remarks>
    public void AddAttribute(Type type, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attribute);

        VerifyMutable();
        _typeAttributes.Add((type.IsAssignableFrom, attribute));
    }

    /// <summary>Adds an attribute to a member of the specified type.</summary>
    /// <param name="type">The type containing the member.</param>
    /// <param name="memberName">The name of the member.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <remarks>
    /// The member can be declared in <paramref name="type"/> or in one of its base types, including private members of the base types.
    /// The attribute applies when serializing <paramref name="type"/> or a type deriving from it, including to the members that override the member.
    /// </remarks>
    public void AddAttribute(Type type, string memberName, HumanReadableAttribute attribute) => AddAttribute(type, memberName, attribute, owner: null);

    internal void AddAttribute(Type type, string memberName, HumanReadableAttribute attribute, object? owner)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(attribute);

        VerifyMutable();

        // Type.GetMember never returns the private members of the base types, which are serialized when they are included
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        IEnumerable<Type> types = type.IsInterface ? [type, .. type.GetInterfaces()] : GetTypeHierarchy(type);

        var found = false;
        foreach (var current in types)
        {
            foreach (var member in current.GetMember(memberName, Flags))
            {
                AddMemberAttribute(type, member, attribute, owner);
                found = true;
            }
        }

        if (!found)
            throw new ArgumentException($"Cannot find an instance member named '{memberName}' in type '{type.AssemblyQualifiedName}'.", nameof(memberName));

        static IEnumerable<Type> GetTypeHierarchy(Type type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                yield return current;
            }
        }
    }

    private void AddMemberAttribute(Type ownerType, MemberInfo member, HumanReadableAttribute attribute, object? owner = null)
    {
        VerifyMutable();

        var definition = GetRootDefinition(member);
        _memberAttributes.Add(((type, candidate) => ownerType.IsAssignableFrom(type) && IsSameMember(type, candidate, definition), attribute, owner));
    }

    internal void RemoveMemberAttributes(object owner)
    {
        VerifyMutable();
        _memberAttributes.RemoveAll(item => item.Owner == owner);
    }

    // Identifies a member independently of the type it is reflected from, and of the overrides of a virtual property
    private static MemberInfo GetRootDefinition(MemberInfo member)
    {
        if (member is PropertyInfo property && (property.GetMethod ?? property.SetMethod) is { } accessor)
            return accessor.GetBaseDefinition();

        return member;
    }

    private static bool IsSameMember(Type type, MemberInfo candidate, MemberInfo definition)
    {
        var candidateDefinition = GetRootDefinition(candidate);
        if (IsSameDefinition(candidateDefinition, definition))
            return true;

        // A property declared by an interface is implemented by a property of the class
        if (definition is MethodInfo { DeclaringType: { IsInterface: true } interfaceType } interfaceMethod && !type.IsInterface && candidateDefinition is MethodInfo candidateMethod && interfaceType.IsAssignableFrom(type))
        {
            var map = type.GetInterfaceMap(interfaceType);
            for (var i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (IsSameDefinition(map.InterfaceMethods[i], interfaceMethod))
                    return IsSameDefinition(map.TargetMethods[i].GetBaseDefinition(), candidateMethod);
            }
        }

        return false;

        // Members of different instantiations of a generic type share their metadata token
        static bool IsSameDefinition(MemberInfo a, MemberInfo b)
            => a.MetadataToken == b.MetadataToken && a.Module == b.Module && a.DeclaringType == b.DeclaringType;
    }

    /// <summary>Adds an attribute to the specified field.</summary>
    /// <param name="member">The field to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <remarks>The attribute applies when serializing the type the field is retrieved from (<see cref="MemberInfo.ReflectedType"/>) or a type deriving from it.</remarks>
    public void AddAttribute(FieldInfo member, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attribute);

        AddMemberAttribute(GetOwnerType(member), member, attribute);
    }

    /// <summary>Adds an attribute to the specified property.</summary>
    /// <param name="member">The property to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <remarks>
    /// The attribute applies when serializing the type the property is retrieved from (<see cref="MemberInfo.ReflectedType"/>) or a type deriving from it,
    /// including to the properties that override it.
    /// </remarks>
    public void AddAttribute(PropertyInfo member, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attribute);

        AddMemberAttribute(GetOwnerType(member), member, attribute);
    }

    private static Type GetOwnerType(MemberInfo member)
        => member.ReflectedType ?? member.DeclaringType ?? throw new ArgumentException($"Member '{member.Name}' is not declared by a type.", nameof(member));

    /// <summary>Adds an attribute to all properties matching the specified condition.</summary>
    /// <param name="condition">A function that determines which properties to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    public void AddPropertyAttribute(Func<PropertyInfo, bool> condition, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);

        VerifyMutable();
        _memberAttributes.Add((Condition: (type, member) => member is PropertyInfo property && condition(property), attribute, Owner: null));
    }

    /// <summary>Adds an attribute to a member identified by an expression.</summary>
    /// <typeparam name="T">The type containing the member.</typeparam>
    /// <param name="member">An expression identifying the member, such as <c>x =&gt; x.Name</c>, or several members, such as <c>x =&gt; new { x.Name, x.Age }</c>.</param>
    /// <param name="attribute">The attribute to add.</param>
    /// <remarks>
    /// The attribute applies to the member when serializing the type it is accessed on, or a type deriving from it, including to the members that override it.
    /// For <c>x =&gt; x.Name</c>, this is <typeparamref name="T"/>. An attribute belongs to a member, not to a path in the object graph:
    /// for <c>x =&gt; x.Address.City</c>, the attribute applies to the <c>City</c> member of every <c>Address</c> instance, wherever it is.
    /// </remarks>
    public void AddAttribute<T>(Expression<Func<T, object>> member, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attribute);

        VerifyMutable();
        var memberInfos = member.GetMemberInfos();
        if (memberInfos.Count is 0)
            throw new ArgumentException($"Expression '{member}' does not refer to a field or a property.", nameof(member));

        foreach (var (ownerType, memberInfo) in memberInfos)
        {
            AddMemberAttribute(ownerType, memberInfo, attribute);
        }
    }

    /// <summary>Adds an attribute to all fields matching the specified condition.</summary>
    /// <param name="condition">A function that determines which fields to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    public void AddFieldAttribute(Func<FieldInfo, bool> condition, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _memberAttributes.Add((Condition: (type, member) => member is FieldInfo field && condition(field), attribute, Owner: null));
    }

    /// <summary>Adds an attribute to all types matching the specified condition.</summary>
    /// <param name="condition">A function that determines which types to add the attribute to.</param>
    /// <param name="attribute">The attribute to add.</param>
    public void AddTypeAttribute(Func<Type, bool> condition, HumanReadableAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);

        VerifyMutable();
        _typeAttributes.Add((condition, attribute));
    }

    internal T? GetCustomAttribute<T>(Type type) where T : HumanReadableAttribute
    {
        ArgumentNullException.ThrowIfNull(type);
        MakeReadOnly();

        // Read reverse, so attributes set by the user override the default attributes
        for (var i = _typeAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _typeAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(type))
                return result;
        }

        var typeAttribute = type.GetCustomAttribute<T>();
        if (typeAttribute is not null)
            return typeAttribute;

        // Attributes declared on an interface are never inherited by the types implementing it
        var interfaces = type.GetInterfaces().Where(iface => iface.GetCustomAttribute<T>() is not null).ToArray();
        var mostSpecificInterfaces = interfaces.Where(iface => !interfaces.Any(other => other != iface && iface.IsAssignableFrom(other))).ToArray();
        return mostSpecificInterfaces switch
        {
            [] => null,
            [var iface] => iface.GetCustomAttribute<T>(),
            _ => throw new HumanReadableSerializerException($"The type '{type}' inherits '{typeof(T).Name}' from several interfaces ({string.Join(", ", mostSpecificInterfaces.Select(iface => iface.FullName))}). Add the attribute to the type to remove the ambiguity."),
        };
    }

    internal T? GetCustomAttribute<T>(Type type, MemberInfo member) where T : HumanReadableAttribute
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        MakeReadOnly();

        // Read reverse, so attributes set by the user override the default attributes
        for (var i = _memberAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _memberAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(type, member))
                return result;
        }

        return member.GetCustomAttribute<T>();
    }

    internal IEnumerable<T> GetCustomAttributes<T>(Type type, MemberInfo member) where T : HumanReadableAttribute
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        MakeReadOnly();

        return GetCustomAttributes(type, member);
        IEnumerable<T> GetCustomAttributes(Type type, MemberInfo member)
        {
            // Read reverse, so attributes set by the user override the default attributes
            for (var i = _memberAttributes.Count - 1; i >= 0; i--)
            {
                var attribute = _memberAttributes[i];
                if (attribute.Attribute is T result && attribute.Condition(type, member))
                    yield return result;
            }

            foreach (var attribute in member.GetCustomAttributes<T>())
                yield return attribute;
        }
    }

    internal HumanReadableConverter GetConverter(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Make sure the instance is readonly on the first usage
        MakeReadOnly();
        return _convertersCache.GetOrAdd(type, static (type, options) => options.FindConverter(type), this);
    }

    private HumanReadableConverter FindConverter(Type type)
    {
        // Priority 1: Attempt to get custom converter from the Converters list
        var converter = TryGetFromList(type, Converters, this);
        if (converter is not null)
            return converter;

        // Priority 2: Attempt to get converter from [HumanReadableConverterAttribute] on the type being converted.
        var converterAttribute = GetCustomAttribute<HumanReadableConverterAttribute>(type);
        if (converterAttribute is not null)
            return HumanReadableConverter.CreateFromAttribute(converterAttribute, type, this);

        // Priority 3: Query the built-in converters.
        converter = TryGetFromList(type, ConverterList.DefaultConverters, this);
        if (converter is not null)
            return converter;

        throw new InvalidOperationException($"No converter for type '{type}'");

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

    internal HumanReadableMemberInfo[] GetMembers(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return _memberInfoCache.GetOrAdd(type, static (type, options) => HumanReadableMemberInfo.Get(type, options), this);
    }

    public ValueFormatter? GetFormatter(string mediaType)
    {
        if (mediaType is not null)
        {
            // Exact match
            if (_valueFormatters.TryGetValue(mediaType, out var formatter))
                return formatter;

            // Normalize the format
            var normalizedMediaType = GetFormat(mediaType);
            if (normalizedMediaType is not null && _valueFormatters.TryGetValue(normalizedMediaType, out formatter))
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

            static bool IsJson(string mediaType)
                => string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "text/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Makes this instance read-only, preventing further modifications.</summary>
    public void MakeReadOnly()
    {
        // Avoid writing the field on every call, as a shared instance is read by many threads
        if (!IsReadOnly)
        {
            IsReadOnly = true;
        }
    }

    private sealed class ConverterList : ConfigurationList<HumanReadableConverter>
    {
        internal static readonly HumanReadableConverter[] DefaultConverters =
        [
#if NET11_0_OR_GREATER
            new BFloat16Converter(),
#endif
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
            new DateOnlyConverter(),
            new DateTimeConverter(),
            new DateTimeOffsetConverter(),
            new DBNullConverter(),
            new DecimalConverter(),
#if NET11_0_OR_GREATER
            new Decimal32Converter(),
            new Decimal64Converter(),
            new Decimal128Converter(),
#endif
            new DoubleConverter(),
            new ExpressionConverter(),
            new HalfConverter(),
            new HttpContentConverter(),
            new HttpMethodConverter(),
            new HttpHeadersConverter(),
            new HttpStatusCodeConverter(),
            new Int16Converter(),
            new Int32Converter(),
            new Int64Converter(),
            new Int128Converter(),
            new IntPtrConverter(),
            new IPAddressConverter(),
            new IPNetworkConverter(),
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
            new TimeOnlyConverter(),
            new TimeSpanConverter(),
            new UInt16Converter(),
            new UInt32Converter(),
            new UInt64Converter(),
            new UInt128Converter(),
            new UIntPtrConverter(),
            new UriConverter(),
            new VersionConverter(),
            new XmlNodeConverter(),
            new XObjectConverter(),
            new EnumConverter(),
            new JsonNodeConverter(),
            new JsonDocumentConverter(),
            new JsonElementConverter(),
            new ValueTupleConverter(),
            new UnixDomainSocketEndPointConverter(),

            // Last converters
            new NullableConverterFactory(),
            new MultiDimensionalArrayConverter(),
            new AsyncEnumerableKeyValuePairConverterFactory(),
            new AsyncEnumerableConverterFactory(),
            new GroupingConverterFactory(),
            new EnumerableKeyValuePairConverterFactory(),
            new EnumerableConverterFactory(),
            new DictionaryConverter(),
            new EnumerableConverter(),
            new CSharpUnionConverterFactory(),
            new FSharpOptionConverterFactory(),
            new FSharpValueOptionConverterFactory(),
            new FSharpDiscriminatedUnionConverterFactory(),
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
