using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable;
public static class HumanReadableSerializerOptionsExtensions
{
    public static void IgnoreMembersThatThrow(this HumanReadableSerializerOptions options)
    {
        IgnoreMembersThatThrow(options, typeof(Exception));
    }

    public static void IgnoreMembersThatThrow<T>(this HumanReadableSerializerOptions options)
    {
        IgnoreMembersThatThrow(options, typeof(T));
    }

    public static void IgnoreMembersThatThrow(this HumanReadableSerializerOptions options, Type exceptionType)
    {
        options.AddPropertyAttribute(type => true, new HumanReadableIgnoreAttribute()
        {
            Condition = HumanReadableIgnoreCondition.Custom,
            CustomCondition = data =>
            {
                if (data.Exception is null)
                    return false;

                return exceptionType.IsAssignableFrom(data.Exception.GetType());
            },
        });
    }

    public static void IgnoreMember<T>(this HumanReadableSerializerOptions options, Expression<Func<T, object>> member)
    {
        var memberInfos = member.GetMemberInfos();
        if (memberInfos.Count is 0)
            throw new ArgumentException($"Expression '{member}' does not refer to a field or a property.", nameof(member));

        foreach (var memberInfo in memberInfos)
        {
            if (memberInfo is PropertyInfo propertyInfo)
            {
                IgnoreMember(options, propertyInfo);
                continue;
            }
            if (memberInfo is FieldInfo fieldInfo)
            {
                IgnoreMember(options, fieldInfo);
                continue;
            }
        }
    }

    public static void IgnoreMember<T>(this HumanReadableSerializerOptions options, string memberName)
    {
        IgnoreMember(options, typeof(T), memberName);
    }

    public static void IgnoreMember(this HumanReadableSerializerOptions options, Type type, string memberName)
    {
        options.AddAttribute(type, memberName, new HumanReadableIgnoreAttribute());
    }

    public static void IgnoreMember(this HumanReadableSerializerOptions options, PropertyInfo propertyInfo)
    {
        options.AddAttribute(propertyInfo, new HumanReadableIgnoreAttribute());
    }

    public static void IgnoreMember(this HumanReadableSerializerOptions options, FieldInfo fieldInfo)
    {
        options.AddAttribute(fieldInfo, new HumanReadableIgnoreAttribute());
    }

    public static void IgnoreMember<T>(this HumanReadableSerializerOptions options, params string[] memberNames)
    {
        IgnoreMember(options, typeof(T), memberNames);
    }

    public static void IgnoreMember(this HumanReadableSerializerOptions options, Type type, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            options.AddAttribute(type, memberName, new HumanReadableIgnoreAttribute());
        }
    }

    public static void IgnoreMembersWithType<T>(this HumanReadableSerializerOptions options)
    {
        IgnoreMembersWithType(options, typeof(T));
    }

    public static void IgnoreMembersWithType(this HumanReadableSerializerOptions options, Type type)
    {
        options.AddPropertyAttribute(property => type.IsAssignableFrom(property.PropertyType), new HumanReadableIgnoreAttribute());
        options.AddFieldAttribute(field => type.IsAssignableFrom(field.FieldType), new HumanReadableIgnoreAttribute());
    }
}
