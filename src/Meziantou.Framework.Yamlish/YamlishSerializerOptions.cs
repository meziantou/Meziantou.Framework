using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.Yamlish;

/// <summary>Provides options that control Yamlish serialization and deserialization.</summary>
public sealed class YamlishSerializerOptions
{
    private readonly ConcurrentDictionary<Type, YamlishTypeInfo> _typeInfoCache = new();
    private readonly ConcurrentDictionary<Type, ConverterResolution> _converterCache = new();
    private readonly List<(Func<Type, bool> Condition, YamlishAttribute Attribute)> _typeAttributes = [];
    private readonly List<(Func<MemberInfo, bool> Condition, YamlishAttribute Attribute)> _memberAttributes = [];

    /// <summary>Initializes a new instance of the <see cref="YamlishSerializerOptions" /> class.</summary>
    public YamlishSerializerOptions()
    {
        Converters = new ConverterList(this);
    }

    /// <summary>Gets a value indicating whether this instance is read-only.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>Gets the list of converters used by the serializer.</summary>
    public IList<YamlishConverter> Converters { get; }

    /// <summary>Gets or sets the naming policy used to convert property names.</summary>
    public YamlishNamingPolicy? PropertyNamingPolicy
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets the comparer used to match property names during deserialization.</summary>
    public StringComparer PropertyNameComparer
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Gets or sets the number of indentation characters to write for each nesting level.</summary>
    public int IndentSize
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = 2;

    /// <summary>Gets or sets the indentation character used when writing Yamlish.</summary>
    public char IndentCharacter
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = ' ';

    /// <summary>Gets or sets a value indicating whether block sequence items are indented relative to their parent mapping key.</summary>
    public bool IndentBlockSequenceItems
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    /// <summary>Gets or sets the newline sequence used when writing Yamlish.</summary>
    public string NewLine
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = Environment.NewLine;

    /// <summary>Gets or sets the maximum depth allowed during serialization and deserialization.</summary>
    public int MaxDepth
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = 64;

    /// <summary>Gets or sets the default condition that determines when members are ignored during serialization.</summary>
    public YamlishIgnoreCondition DefaultIgnoreCondition
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = YamlishIgnoreCondition.WhenWritingNull;

    /// <summary>Gets or sets a value indicating whether public fields are included during serialization and deserialization.</summary>
    public bool IncludeFields
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether read-only fields are ignored during serialization.</summary>
    public bool IgnoreReadOnlyFields
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether read-only properties are ignored during serialization.</summary>
    public bool IgnoreReadOnlyProperties
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets the preferred behavior for object creation during deserialization.</summary>
    public YamlishObjectCreationHandling PreferredObjectCreationHandling
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = YamlishObjectCreationHandling.Replace;

    /// <summary>Gets or sets a value indicating whether duplicate mapping keys are allowed when parsing Yamlish.</summary>
    public bool AllowDuplicateProperties
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    /// <summary>Gets or sets a value indicating whether deserialization rejects properties that do not match a .NET member.</summary>
    public bool RejectUnmatchedProperties
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether required constructor parameters must be present during deserialization.</summary>
    public bool RespectRequiredConstructorParameters
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    /// <summary>Gets or sets a value indicating whether nullable annotations are enforced during serialization and deserialization.</summary>
    public bool RespectNullableAnnotations
    {
        get;
        set
        {
            VerifyMutable();
            field = value;
        }
    } = true;

    /// <summary>Adds a Yamlish attribute that applies to the specified type.</summary>
    /// <param name="type">The type to which the attribute applies.</param>
    /// <param name="attribute">The attribute to apply.</param>
    public void AddAttribute(Type type, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();

        _typeAttributes.Add((candidate => candidate == type, attribute));
    }

    /// <summary>Adds a Yamlish attribute that applies to a member with the specified name on the specified type.</summary>
    /// <param name="type">The type that declares the member.</param>
    /// <param name="memberName">The name of the member to which the attribute applies.</param>
    /// <param name="attribute">The attribute to apply.</param>
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

    /// <summary>Adds a Yamlish attribute that applies to the specified property.</summary>
    /// <param name="member">The property to which the attribute applies.</param>
    /// <param name="attribute">The attribute to apply.</param>
    public void AddAttribute(PropertyInfo member, YamlishAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    /// <summary>Adds a Yamlish attribute that applies to the specified field.</summary>
    /// <param name="member">The field to which the attribute applies.</param>
    /// <param name="attribute">The attribute to apply.</param>
    public void AddAttribute(FieldInfo member, YamlishAttribute attribute) => AddAttribute((MemberInfo)member, attribute);

    /// <summary>Adds a Yamlish attribute that applies to the member selected by the specified expression.</summary>
    /// <typeparam name="T">The type that declares the member.</typeparam>
    /// <param name="member">An expression that selects one or more fields or properties.</param>
    /// <param name="attribute">The attribute to apply.</param>
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

    /// <summary>Adds a Yamlish attribute that applies to properties matching the specified condition.</summary>
    /// <param name="condition">The condition used to select properties.</param>
    /// <param name="attribute">The attribute to apply.</param>
    public void AddPropertyAttribute(Func<PropertyInfo, bool> condition, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _memberAttributes.Add((member => member is PropertyInfo property && condition(property), attribute));
    }

    /// <summary>Adds a Yamlish attribute that applies to fields matching the specified condition.</summary>
    /// <param name="condition">The condition used to select fields.</param>
    /// <param name="attribute">The attribute to apply.</param>
    public void AddFieldAttribute(Func<FieldInfo, bool> condition, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _memberAttributes.Add((member => member is FieldInfo field && condition(field), attribute));
    }

    /// <summary>Adds a Yamlish attribute that applies to types matching the specified condition.</summary>
    /// <param name="condition">The condition used to select types.</param>
    /// <param name="attribute">The attribute to apply.</param>
    public void AddTypeAttribute(Func<Type, bool> condition, YamlishAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(attribute);
        VerifyMutable();
        _typeAttributes.Add((condition, attribute));
    }

    /// <summary>Makes this instance read-only.</summary>
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

        return GetCustomAttributes(type);
        IEnumerable<T> GetCustomAttributes(Type type)
        {
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
