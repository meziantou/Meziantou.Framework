using System.Numerics;
using System.Reflection;

namespace Meziantou.Framework.SimpleQueryLanguage;

internal static class ValueConverter
{
    public static bool TryParseValue<TValue>(string value, [MaybeNullWhen(false)] out TValue result)
    {
        // A string needs no parsing. This is what Convert.ChangeType returns at the end of this method, without looking for a TryParse method first.
        if (typeof(TValue) == typeof(string))
        {
            result = (TValue)(object)value;
            return true;
        }

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

#if NET11_0_OR_GREATER
        if (type == typeof(System.Numerics.BFloat16))
        {
            if (System.Numerics.BFloat16.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(System.Numerics.Decimal32))
        {
            if (System.Numerics.Decimal32.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(System.Numerics.Decimal64))
        {
            if (System.Numerics.Decimal64.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

        if (type == typeof(System.Numerics.Decimal128))
        {
            if (System.Numerics.Decimal128.TryParse(value, NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                result = (TValue)(object)parsedValue;
                return true;
            }

            result = default;
            return false;
        }

#endif

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
        var (methodWithProvider, methodWithoutProvider) = TryParseMethods<TValue>.Get(type);
        var methodInfo = methodWithProvider;
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
        methodInfo = methodWithoutProvider;
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
    }

    private static bool ValidateReturnType(MethodInfo methodInfo)
    {
        return methodInfo.ReturnType.Equals(typeof(bool));
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

    /// <summary>
    /// The TryParse methods of the type <see cref="TryParseValue"/> parses <typeparamref name="TValue"/> as. Looking up a method by its
    /// signature costs more than parsing most values, and the result is the same for every value, so it is looked up once per type.
    /// </summary>
    private static class TryParseMethods<TValue>
    {
        private static ResolvedMethods? s_methods;

        public static ResolvedMethods Get(Type type)
        {
            // Not a static constructor: an exception from the lookup, such as an ambiguous match, must reach the caller as it is.
            // Concurrent first calls may both look the methods up: they find the same ones.
            if (s_methods is { } methods)
                return methods;

            var withProvider = GetStaticMethodFromHierarchy(type, "TryParse", [typeof(string), typeof(IFormatProvider), type.MakeByRefType()], ValidateReturnType);
            var withoutProvider = withProvider is null ? GetStaticMethodFromHierarchy(type, "TryParse", [typeof(string), type.MakeByRefType()], ValidateReturnType) : null;
            methods = new ResolvedMethods(withProvider, withoutProvider);
            s_methods = methods;
            return methods;
        }
    }

    private sealed record ResolvedMethods(MethodInfo? WithProvider, MethodInfo? WithoutProvider);
}
