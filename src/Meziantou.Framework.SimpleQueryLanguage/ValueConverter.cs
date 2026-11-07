using System.Numerics;
using System.Reflection;

namespace Meziantou.Framework.SimpleQueryLanguage;

internal static class ValueConverter
{
    public static bool TryParseValue<TValue>(string value, [MaybeNullWhen(false)] out TValue result)
    {
        // A boxed Nullable<T> is a boxed T, so parsing the underlying type and casting back to TValue
        // works for every type below. Without this, Nullable<T> falls all the way through to
        // Convert.ChangeType, which throws for a null-able target and silently yields "no match".
        var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        if (type.IsEnum)
        {
            if (Enum.TryParse(type, value, ignoreCase: true, out var parsedEnum))
            {
                result = (TValue)parsedEnum;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(DateTimeOffset))
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(DateTime))
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(DateOnly))
        {
            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(TimeOnly))
        {
            if (TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(long))
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(ulong))
        {
            if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(int))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(uint))
        {
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(short))
        {
            if (short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(ushort))
        {
            if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(byte))
        {
            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(sbyte))
        {
            if (sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(double))
        {
            if (double.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(float))
        {
            if (float.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(Half))
        {
            if (Half.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(decimal))
        {
            if (decimal.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(IntPtr))
        {
            if (IntPtr.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(BigInteger))
        {
            if (BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(Int128))
        {
            if (Int128.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(UInt128))
        {
            if (UInt128.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        // Try find TryParse(string, IFormatProvider, out TValue) method
        var methodInfo = GetStaticMethodFromHierarchy(type, "TryParse", [typeof(string), typeof(IFormatProvider), type.MakeByRefType()], ValidateReturnType);
        if (methodInfo != null)
        {
            var parameters = new object?[] { value, CultureInfo.InvariantCulture, null };
            var parsed = (bool)methodInfo.Invoke(obj: null, parameters)!;
            if (parsed)
            {
                result = (TValue)parameters[2]!;
                return true;
            }

            result = default;
            return false;
        }

        // Try find TryParse(string, out TValue) method
        methodInfo = GetStaticMethodFromHierarchy(type, "TryParse", [typeof(string), type.MakeByRefType()], ValidateReturnType);
        if (methodInfo != null)
        {
            var parameters = new object?[] { value, null };
            var parsed = (bool)methodInfo.Invoke(obj: null, parameters)!;
            if (parsed)
            {
                result = (TValue)parameters[1]!;
                return true;
            }

            result = default;
            return false;
        }

        // Fallback to ChangeType
        try
        {
            result = (TValue)Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }

        static bool ValidateReturnType(MethodInfo methodInfo)
        {
            return methodInfo.ReturnType.Equals(typeof(bool));
        }
    }

    private static MethodInfo? GetStaticMethodFromHierarchy(Type type, string name, Type[] parameterTypes, Func<MethodInfo, bool> validateReturnType)
    {
        bool IsMatch(MethodInfo? method) => method is not null && !method.IsAbstract && validateReturnType(method);

        var methodInfo = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, parameterTypes);

        if (IsMatch(methodInfo))
        {
            return methodInfo;
        }

        return null;
    }
}
