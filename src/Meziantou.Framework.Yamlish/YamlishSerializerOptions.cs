using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.Yamlish;

public sealed class YamlishSerializerOptions
{
    private readonly ConcurrentDictionary<Type, YamlishTypeInfo> _typeInfoCache = new();
    private readonly List<(Func<MemberInfo, bool> Condition, YamlishAttribute Attribute)> _memberAttributes = [];

    public bool IsReadOnly { get; private set; }

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

    public void MakeReadOnly() => IsReadOnly = true;

    internal YamlishTypeInfo GetTypeInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        MakeReadOnly();
        return _typeInfoCache.GetOrAdd(type, static (type, options) => YamlishTypeInfo.Create(type, options), this);
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
}
