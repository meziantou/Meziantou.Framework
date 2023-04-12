using System.Collections.Concurrent;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable;

public sealed record HumanReadableSerializerOptions
{
    private readonly ConcurrentDictionary<Type, HumanReadableConverter> _converters = new();
    private readonly ConcurrentDictionary<Type, List<HumanReadableMemberInfo>> _memberInfos = new();


    private readonly Dictionary<Type, List<HumanReadableAttribute>> _typeAttributes = new();
    private readonly Dictionary<MemberInfo, List<HumanReadableAttribute>> _memberAttributes = new();
    private bool _includeFields;
    private HumanReadableIgnoreCondition _defaultIgnoreCondition;

    public HumanReadableSerializerOptions()
    {
        Converters = new ConverterList(this);
    }

    public bool IsReadOnly { get; private set; }
    public int MaxDepth { get; set; } = 64;
    public bool ShowInvisibleCharactersInValues { get; set; }
    public IList<HumanReadableConverter> Converters { get; }

    public bool IncludeFields
    {
        get => _includeFields; set
        {
            VerifyMutable();
            _includeFields = value;
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

    public void AddAttribute(Type type, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        AddValue(_typeAttributes, type, attribute);
    }

    public void AddAttribute(Type type, string memberName, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        var members = type.GetMember(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (members.Length == 0)
            throw new ArgumentException($"Cannot find an instance member named '{memberName}' in type '{type.AssemblyQualifiedName}'.", nameof(memberName));

        foreach (var member in members)
        {
            AddValue(_memberAttributes, member, attribute);
        }
    }

    public void AddAttribute(FieldInfo member, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        AddValue(_memberAttributes, member, attribute);
    }

    public void AddAttribute(PropertyInfo member, HumanReadableAttribute attribute)
    {
        VerifyMutable();
        AddValue(_memberAttributes, member, attribute);
    }

    private static void AddValue<TKey, TValue>(Dictionary<TKey, List<TValue>> dict, TKey key, TValue value)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<TValue>(capacity: 1);
            dict[key] = list;
        }
        else
        {
            // All attributes has "AllowMultiple = false", so only one attribute can be added to a member
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].GetType() == value.GetType())
                {
                    list[i] = value;
                    return;
                }
            }
        }

        list.Add(value);
    }

    internal T? GetCustomAttribute<T>(Type type) where T : HumanReadableAttribute
    {
        MakeReadOnly();
        if (_typeAttributes.TryGetValue(type, out var attributes))
        {
            foreach (var attribute in attributes)
            {
                if (attribute is T result)
                    return result;
            }
        }

        return type.GetCustomAttribute<T>();
    }

    internal T? GetCustomAttribute<T>(MemberInfo member) where T : HumanReadableAttribute
    {
        MakeReadOnly();
        if (_memberAttributes.TryGetValue(member, out var attributes))
        {
            foreach (var attribute in attributes)
            {
                if (attribute is T result)
                    return result;
            }
        }

        return member.GetCustomAttribute<T>();
    }

    internal HumanReadableConverter GetConverter(Type type)
    {
        // Make sure the instance is readonly on the first usage
        MakeReadOnly();

#if NETSTANDARD2_0 || NET471
        return _converters.GetOrAdd(type, type => FindConverter(type, Converters));
#else
        return _converters.GetOrAdd(type, FindConverter, Converters);
#endif

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

    internal List<HumanReadableMemberInfo> GetMembers(Type type)
    {
#if NET6_0_OR_GREATER
        return _memberInfos.GetOrAdd(type, static (type, options) => HumanReadableMemberInfo.Get(type, options), this);
#else
        return _memberInfos.GetOrAdd(type, type => HumanReadableMemberInfo.Get(type, this));
#endif
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
            new BooleanConverter(),
            new ByteArrayConverter(),
            new ByteConverter(),
            new CharConverter(),
            new ComplexConverter(),
            new CultureInfoConverter(),
#if NET6_0_OR_GREATER
            new DateOnlyConverter(),
#endif
            new DateTimeConverter(),
            new DateTimeOffsetConverter(),
            new DBNullConverter(),
            new DecimalConverter(),
            new DoubleConverter(),
#if NET5_0_OR_GREATER
            new HalfConverter(),
#endif
            new HttpContentConverter(),
            new HttpMethodConverter(),
            new HttpHeadersConverter(),
            new Int16Converter(),
            new Int32Converter(),
            new Int64Converter(),
#if NET7_0_OR_GREATER
            new Int128Converter(),
#endif
            new IntPtrConverter(),
            new GuidConverter(),
            new RegexConverter(),
            new SByteConverter(),
            new SingleConverter(),
            new StringConverter(),
            new SystemTypeConverter(),
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
#if NETCOREAPP3_0_OR_GREATER
            new JsonNodeConverter(),
            new JsonDocumentConverter(),
            new JsonElementConverter(),
#endif
#if NETCOREAPP2_0_OR_GREATER || NET471_OR_GREATER
            new ValueTupleConverter(),
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
