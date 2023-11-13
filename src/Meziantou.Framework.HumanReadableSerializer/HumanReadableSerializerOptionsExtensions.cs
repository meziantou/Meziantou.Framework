namespace Meziantou.Framework.HumanReadable;
public static class HumanReadableSerializerOptionsExtensions
{
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

    public static void IgnoreMember<T>(this HumanReadableSerializerOptions options, string memberName)
    {
        IgnoreMember(options, typeof(T), memberName);
    }

    public static void IgnoreMember(this HumanReadableSerializerOptions options, Type type, string memberName)
    {
        options.AddAttribute(type, memberName, new HumanReadableIgnoreAttribute());
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
