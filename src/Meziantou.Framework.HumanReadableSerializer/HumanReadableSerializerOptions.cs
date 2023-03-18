using System.Collections.Concurrent;
using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable;

public sealed record HumanReadableSerializerOptions
{
    private readonly ConcurrentDictionary<Type, HumanReadableConverter> _converters = new();

    public HumanReadableSerializerOptions()
    {
        Converters = new ConverterList(this);
    }

    public bool IsReadOnly { get; private set; }
    public int MaxDepth { get; set; } = 64;
    public bool ShowInvisibleCharactersInValues { get; set; }
    public IList<HumanReadableConverter> Converters { get; }

    internal HumanReadableConverter GetConverter(Type type)
    {
        // Make sure the instance is readonly on the first usage
        MakeReadOnly();

#if NETSTANDARD2_0
        return _converters.GetOrAdd(type, type => FindConverter(type, Converters));
#else
        return _converters.GetOrAdd(type, FindConverter, Converters);
#endif

        static HumanReadableConverter WrapConverter(HumanReadableConverter converter)
            => converter.HandleNull ? converter : new NullConverterWrapper(converter);

        static HumanReadableConverter FindConverter(Type type, IList<HumanReadableConverter> converters)
        {
            foreach (var convert in converters)
            {
                if (convert == null)
                    continue;

                if (convert.CanConvert(type))
                    return WrapConverter(convert);
            }

            throw new InvalidOperationException($"No converter for type '{type}'");
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
        private static readonly HumanReadableConverter[] DefaultConverters = new HumanReadableConverter[]
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

            // Last converters
            new NullableConverter(),
            new MultiDimentionalArrayConverter(),
            new EnumerableKeyValuePairConverter(),
            new EnumerableConverter(),
            new ObjectConverter(),
        };

        private readonly HumanReadableSerializerOptions _options;

        public ConverterList(HumanReadableSerializerOptions options, IList<HumanReadableConverter>? source = null)
            : base(source ?? DefaultConverters)
        {
            _options = options;
        }

        protected override bool IsImmutable => _options.IsReadOnly;
        protected override void VerifyMutable() => _options.VerifyMutable();
    }
}
