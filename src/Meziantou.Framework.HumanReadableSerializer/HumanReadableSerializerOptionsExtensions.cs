using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable;

/// <summary>Provides extension methods for <see cref="HumanReadableSerializerOptions"/>.</summary>
public static class HumanReadableSerializerOptionsExtensions
{
    /// <summary>Configures the options to ignore members that throw any exception when accessed.</summary>
    /// <param name="options">The serialization options.</param>
    public static void IgnoreMembersThatThrow(this HumanReadableSerializerOptions options)
    {
        IgnoreMembersThatThrow(options, typeof(Exception));
    }

    /// <summary>Configures the options to ignore members that throw an exception of the specified type when accessed.</summary>
    /// <typeparam name="T">The type of exception to ignore.</typeparam>
    /// <param name="options">The serialization options.</param>
    public static void IgnoreMembersThatThrow<T>(this HumanReadableSerializerOptions options)
    {
        IgnoreMembersThatThrow(options, typeof(T));
    }

    /// <summary>Configures the options to ignore members that throw an exception of the specified type when accessed.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="exceptionType">The type of exception to ignore.</param>
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

    /// <summary>Configures the options to ignore the specified member.</summary>
    /// <typeparam name="T">The type containing the member.</typeparam>
    /// <param name="options">The serialization options.</param>
    /// <param name="member">An expression identifying the member to ignore.</param>
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

    /// <summary>Configures the options to ignore the specified member by name.</summary>
    /// <typeparam name="T">The type containing the member.</typeparam>
    /// <param name="options">The serialization options.</param>
    /// <param name="memberName">The name of the member to ignore.</param>
    public static void IgnoreMember<T>(this HumanReadableSerializerOptions options, string memberName)
    {
        IgnoreMember(options, typeof(T), memberName);
    }

    /// <summary>Configures the options to ignore the specified member by name.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="type">The type containing the member.</param>
    /// <param name="memberName">The name of the member to ignore.</param>
    public static void IgnoreMember(this HumanReadableSerializerOptions options, Type type, string memberName)
    {
        options.AddAttribute(type, memberName, new HumanReadableIgnoreAttribute());
    }

    /// <summary>Configures the options to ignore the specified property.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="propertyInfo">The property to ignore.</param>
    public static void IgnoreMember(this HumanReadableSerializerOptions options, PropertyInfo propertyInfo)
    {
        options.AddAttribute(propertyInfo, new HumanReadableIgnoreAttribute());
    }

    /// <summary>Configures the options to ignore the specified field.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="fieldInfo">The field to ignore.</param>
    public static void IgnoreMember(this HumanReadableSerializerOptions options, FieldInfo fieldInfo)
    {
        options.AddAttribute(fieldInfo, new HumanReadableIgnoreAttribute());
    }

    /// <summary>Configures the options to ignore multiple members by name.</summary>
    /// <typeparam name="T">The type containing the members.</typeparam>
    /// <param name="options">The serialization options.</param>
    /// <param name="memberNames">The names of the members to ignore.</param>
    public static void IgnoreMember<T>(this HumanReadableSerializerOptions options, params string[] memberNames)
    {
        IgnoreMember(options, typeof(T), memberNames);
    }

    /// <summary>Configures the options to ignore multiple members by name.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="type">The type containing the members.</param>
    /// <param name="memberNames">The names of the members to ignore.</param>
    public static void IgnoreMember(this HumanReadableSerializerOptions options, Type type, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            options.AddAttribute(type, memberName, new HumanReadableIgnoreAttribute());
        }
    }

    /// <summary>Configures the options to ignore all members with the specified type.</summary>
    /// <typeparam name="T">The type of members to ignore.</typeparam>
    /// <param name="options">The serialization options.</param>
    public static void IgnoreMembersWithType<T>(this HumanReadableSerializerOptions options)
    {
        IgnoreMembersWithType(options, typeof(T));
    }

    /// <summary>Configures the options to ignore all members with the specified type.</summary>
    /// <param name="options">The serialization options.</param>
    /// <param name="type">The type of members to ignore.</param>
    public static void IgnoreMembersWithType(this HumanReadableSerializerOptions options, Type type)
    {
        options.AddPropertyAttribute(property => type.IsAssignableFrom(property.PropertyType), new HumanReadableIgnoreAttribute());
        options.AddFieldAttribute(field => type.IsAssignableFrom(field.FieldType), new HumanReadableIgnoreAttribute());
    }
}
