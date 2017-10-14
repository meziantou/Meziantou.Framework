using System;

namespace Meziantou.Framework.Utilities
{
    public static class ReflectionUtilities
    {
        public static bool IsNullableOfT(this Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        public static bool IsFlagsEnum(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (!type.IsEnum)
                return false;

            return type.IsDefined(typeof(FlagsAttribute), true);
        }
    }
}
