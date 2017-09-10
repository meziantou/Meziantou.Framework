using System;
using System.Reflection;

namespace Meziantou.Framework.Utilities
{
    public static class ReflectionUtilities
    {
        public static bool IsNullableOfT(this Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        public static bool IsFlagsEnum(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (!type.GetTypeInfo().IsEnum)
                return false;

            return type.GetTypeInfo().IsDefined(typeof(FlagsAttribute), true);
        }
    }
}
