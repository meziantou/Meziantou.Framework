using System.Reflection;

namespace Meziantou.Framework;

#if ReflectionUtilities_Internal
internal
#else
public
#endif
static class ReflectionUtilities
{
    public static bool IsNullableOfT(this Type type!!)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    public static bool IsFlagsEnum<T>()
    {
        return IsFlagsEnum(typeof(T));
    }

    public static bool IsFlagsEnum(this Type type!!)
    {
        if (!type.IsEnum)
            return false;

        return type.IsDefined(typeof(FlagsAttribute), inherit: true);
    }

    [RequiresUnreferencedCode("Use reflection to find static methods")]
    public static MethodInfo? GetImplicitConversion(object? value, Type targetType)
    {
        if (value == null)
            return null;

        var valueType = value.GetType();
        var methods = valueType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (var method in methods)
        {
            if (IsImplicitOperator(method, valueType, targetType))
                return method;
        }

        return null;

        static bool IsImplicitOperator(MethodInfo mi, Type sourceType, Type targetType)
        {
            if (!string.Equals(mi.Name, "op_Implicit", StringComparison.Ordinal))
                return false;

            if (!targetType.IsAssignableFrom(mi.ReturnType))
                return false;

            var p = mi.GetParameters();
            if (p.Length != 1)
                return false;

            if (!p[0].ParameterType.IsAssignableFrom(sourceType))
                return false;

            return true;
        }
    }
}
