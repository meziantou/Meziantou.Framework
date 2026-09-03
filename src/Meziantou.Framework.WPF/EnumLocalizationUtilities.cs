using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.WPF;

internal static class EnumLocalizationUtilities
{
    private static readonly ConcurrentDictionary<Type, LocalizedEnumValueCollection> EnumsCache = new();
    private static readonly ConcurrentDictionary<Expression, string?> PropertiesCache = new();

    public static LocalizedEnumValueCollection GetEnumLocalization<T>()
        where T : struct
    {
        return GetEnumLocalization(typeof(T));
    }

    public static LocalizedEnumValueCollection GetEnumLocalization(Type type)
    {
        return EnumsCache.GetOrAdd(type, CreateEnumLocalization);
    }

    private static LocalizedEnumValueCollection CreateEnumLocalization(Type type)
    {
        var result = new List<LocalizedEnumValue>();
        var enumValues = type.GetEnumValues();

        foreach (Enum? enumValue in enumValues)
        {
            Debug.Assert(enumValue is not null);

            var enumName = enumValue.ToString()!;
            var fieldInfo = type.GetField(enumName)!;

            var displayAttribute = fieldInfo.GetCustomAttribute<DisplayAttribute>();
            if (displayAttribute is not null)
            {
                result.Add(new LocalizedEnumValue(enumValue, displayAttribute));
            }
            else
            {
                result.Add(new LocalizedEnumValue(enumValue, enumName));
            }
        }

        return new LocalizedEnumValueCollection(result);
    }

    public static string? GetPropertyLocalization<T>(Expression<Func<T>> exp)
    {
        return PropertiesCache.GetOrAdd(exp, CreatePropertyLocalization);
    }

    private static string? CreatePropertyLocalization(Expression expression)
    {
        var memberExpression = (MemberExpression)((LambdaExpression)expression).Body;
        var displayAttribute = memberExpression.Member.GetCustomAttribute<DisplayAttribute>();
        return displayAttribute?.GetName() ?? memberExpression.Member.Name;
    }

    public static string GetEnumMemberLocalization(Enum value)
    {
        var localizedValueCollection = GetEnumLocalization(value.GetType());
        return localizedValueCollection[value].Name;
    }
}
