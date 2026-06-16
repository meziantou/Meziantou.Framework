using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.Yamlish;

public sealed class YamlishSerializerOptions
{
    private readonly ConcurrentDictionary<Type, YamlishTypeInfo> _typeInfoCache = new();
    private readonly ConcurrentDictionary<Type, ConverterResolution> _converterCache = new();
    private readonly List<(Func<Type, bool> Condition, YamlishAttribute Attribute)> _typeAttributes = [];
    private readonly List<(Func<MemberInfo, bool> Condition, YamlishAttribute Attribute)> _memberAttributes = [];

    public YamlishSerializerOptions()
    {
        Converters = new ConverterList(this);
    }

    public bool IsReadOnly { get; private set; }

    public IList<YamlishConverter> Converters { get; }

    public YamlishNamingPolicy? PropertyNamingPolicy
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    public StringComparer PropertyNameComparer
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = StringComparer.OrdinalIgnoreCase;

    public int IndentSize
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = 2;

    public char IndentCharacter
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = ' ';

    public string NewLine
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = Environment.NewLine;

    public int MaxDepth
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = 64;

    public YamlishIgnoreCondition DefaultIgnoreCondition
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = YamlishIgnoreCondition.WhenWritingNull;

    public bool IncludeFields
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    public bool IgnoreReadOnlyFields
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    public bool IgnoreReadOnlyProperties
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    public YamlishObjectCreationHandling PreferredObjectCreationHandling
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = YamlishObjectCreationHandling.Replace;

    public bool AllowDuplicateProperties
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    public bool RespectRequiredConstructorParameters
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    public bool RespectNullableAnnotations
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    public void AddAttribute(Type type, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();

        _typeAttributes.Add((candidate => candidate == type, attribute));
    }

    public void AddAttribute(Type type, string memberName, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();

        var members = type.GetMember(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (members.Length is 0)
            throw new ArgumentException($"Cannot find an instance member named '{memberName}' in type '{type.AssemblyQualifiedName}'.", nameof(memberName));

        foreach (var member in members)
        {
            AddAttribute(member, attribute);
        }
    }

    public void AddAttribute(PropertyInfo member, YamlishAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    public void AddAttribute(FieldInfo member, YamlishAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    public void AddAttribute<T>(Expression<Func<T, object>> member, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();

        var members = member.GetMemberInfos();
        if (members.Count is 0)
            throw new ArgumentException($"Expression '{member}' does not refer to a field or a property.", nameof(member));

        foreach (var memberInfo in members)
        {
            AddAttribute(memberInfo, attribute);
        }
    }

    public void AddPropertyAttribute(Func<PropertyInfo, bool> condition, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _memberAttributes.Add((member => member is PropertyInfo property && condition(property), attribute));
    }

    public void AddFieldAttribute(Func<FieldInfo, bool> condition, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _memberAttributes.Add((member => member is FieldInfo field && condition(field), attribute));
    }

    public void AddTypeAttribute(Func<Type, bool> condition, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _typeAttributes.Add((condition, attribute));
    }

    public void MakeReadOnly() => IsReadOnly = true;

    internal YamlishTypeInfo GetTypeInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        MakeReadOnly();
        return _typeInfoCache.GetOrAdd(type, static (type, options) => YamlishTypeInfo.Create(type, options), this);
    }

    internal YamlishConverter? GetConverter(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        MakeReadOnly();
        return _converterCache.GetOrAdd(type, static (type, options) => new ConverterResolution(FindConverter(type, options)), this).Converter;

        static YamlishConverter? FindConverter(Type type, YamlishSerializerOptions options)
        {
            foreach (var converter in options.Converters)
            {
                if (!converter.CanConvert(type))
                    continue;

                if (converter is not YamlishConverterFactory factory)
                    return converter;

                var result = factory.CreateConverter(type, options);
                if (result is null)
                    continue;

                if (result is YamlishConverterFactory || !result.CanConvert(type))
                    throw new InvalidOperationException($"The converter '{result.GetType().FullName}' is not compatible with '{type.FullName}'.");

                return result;
            }

            return null;
        }
    }

    internal T? GetCustomAttribute<T>(MemberInfo member) where T : YamlishAttribute
    {
        ArgumentNullException.ThrowIfNull(member);
        MakeReadOnly();

        for (var i = _memberAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _memberAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(member))
                return result;
        }

        return member.GetCustomAttribute<T>();
    }

    internal T? GetCustomAttribute<T>(Type type) where T : YamlishAttribute
    {
        ArgumentNullException.ThrowIfNull(type);
        MakeReadOnly();

        for (var i = _typeAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _typeAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(type))
                return result;
        }

        return type.GetCustomAttribute<T>();
    }

    internal IEnumerable<T> GetCustomAttributes<T>(Type type) where T : YamlishAttribute
    {
        ArgumentNullException.ThrowIfNull(type);
        MakeReadOnly();

        for (var i = _typeAttributes.Count - 1; i >= 0; i--)
        {
            var attribute = _typeAttributes[i];
            if (attribute.Attribute is T result && attribute.Condition(type))
                yield return result;
        }

        foreach (var attribute in type.GetCustomAttributes<T>())
            yield return attribute;
    }

    internal void VerifyMutable()
    {
        if (IsReadOnly)
            throw new InvalidOperationException("YamlishSerializerOptions instance is marked as read-only.");
    }

    private void AddAttribute(MemberInfo member, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _memberAttributes.Add((candidate => candidate.Module == member.Module && candidate.MetadataToken == member.MetadataToken, attribute));
    }

    private sealed class ConverterList(YamlishSerializerOptions options) : ConfigurationList<YamlishConverter>(
    [
        new BigIntegerYamlishConverter(),
        new BitArrayYamlishConverter(),
        new BitVector32YamlishConverter(),
        new BooleanYamlishConverter(),
        new ByteArrayYamlishConverter(),
        new ByteYamlishConverter(),
        new CharYamlishConverter(),
        new ComplexYamlishConverter(),
        new CSharpUnionYamlishConverterFactory(),
        new CultureInfoYamlishConverter(),
        new DateOnlyYamlishConverter(),
        new DateTimeYamlishConverter(),
        new DateTimeOffsetYamlishConverter(),
        new DBNullYamlishConverter(),
        new DecimalYamlishConverter(),
        new DoubleYamlishConverter(),
        new HalfYamlishConverter(),
        new HttpMethodYamlishConverter(),
        new HttpStatusCodeYamlishConverter(),
        new Int16YamlishConverter(),
        new Int32YamlishConverter(),
        new Int64YamlishConverter(),
        new Int128YamlishConverter(),
        new IntPtrYamlishConverter(),
        new IPAddressYamlishConverter(),
        new IPNetworkYamlishConverter(),
        new GuidYamlishConverter(),
        new MediaTypeHeaderValueYamlishConverter(),
        new MemoryByteYamlishConverter(),
        new ReadOnlyMemoryByteYamlishConverter(),
        new SByteYamlishConverter(),
        new SingleYamlishConverter(),
        new StringBuilderYamlishConverter(),
        new StringYamlishConverter(),
        new StringWriterYamlishConverter(),
        new TypeYamlishConverter(),
        new TimeOnlyYamlishConverter(),
        new TimeSpanYamlishConverter(),
        new UInt16YamlishConverter(),
        new UInt32YamlishConverter(),
        new UInt64YamlishConverter(),
        new UInt128YamlishConverter(),
        new UIntPtrYamlishConverter(),
        new UriYamlishConverter(),
        new VersionYamlishConverter(),
        new UnixDomainSocketEndPointYamlishConverter(),
        new EnumYamlishConverter(),
    ])
    {
        protected override bool IsImmutable => options.IsReadOnly;

        protected override void VerifyMutable() => options.VerifyMutable();
    }

    private sealed record ConverterResolution(YamlishConverter? Converter);
}
