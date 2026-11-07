using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;

namespace Meziantou.Framework.WPF;

internal static class EnumLocalizationUtilities
{
    private static readonly ConcurrentDictionary<Type, LocalizedEnumValueCollection> EnumsCache = new();

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
}
