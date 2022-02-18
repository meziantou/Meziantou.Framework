using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.WPF
{
    internal static class EnumLocalizationUtilities
    {
        private static readonly IDictionary<Type, LocalizedEnumValueCollection> s_enums = new Dictionary<Type, LocalizedEnumValueCollection>();
        private static readonly IDictionary<Expression, object> s_properties = new Dictionary<Expression, object>();

        public static LocalizedEnumValueCollection GetEnumLocalization<T>()
            where T : struct
        {
            return GetEnumLocalization(typeof(T));
        }

        public static LocalizedEnumValueCollection GetEnumLocalization(Type type)
        {
            if (s_enums.TryGetValue(type, out var value))
                return value;

            IList<LocalizedEnumValue> result = new List<LocalizedEnumValue>();
            var enumValues = type.GetEnumValues();

            foreach (Enum? enumValue in enumValues)
            {
                Debug.Assert(enumValue != null);

                var enumName = enumValue.ToString()!;
                var fieldInfo = type.GetField(enumName)!;

                var displayAttribute = fieldInfo.GetCustomAttribute<DisplayAttribute>();
                if (displayAttribute != null)
                {
                    result.Add(new LocalizedEnumValue(enumValue, displayAttribute));
                }
                else
                {
                    result.Add(new LocalizedEnumValue(enumValue, enumName));
                }
            }

            var localizedValueCollection = new LocalizedEnumValueCollection(result);
            s_enums.Add(type, localizedValueCollection);
            return localizedValueCollection;
        }

        public static string? GetPropertyLocalization<T>(Expression<Func<T>> exp)
        {
            if (!s_properties.TryGetValue(exp, out var value))
            {
                var memberExpression = (MemberExpression)exp.Body;
                var displayAttribute = memberExpression.Member.GetCustomAttribute<DisplayAttribute>();
                if (displayAttribute == null)
                {
                    value = memberExpression.Member.Name;
                }
                else
                {
                    value = displayAttribute.GetName();
                }

                s_properties.Add(exp, value);
            }

            if (value is DisplayAttribute attribute)
                return attribute.GetName();

            return value.ToString();
        }

        public static string GetEnumMemberLocalization(Enum value!!)
        {
            var localizedValueCollection = GetEnumLocalization(value.GetType());
            return localizedValueCollection[value].Name;
        }
    }
}
