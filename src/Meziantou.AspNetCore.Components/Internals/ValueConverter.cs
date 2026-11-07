using System.ComponentModel;

namespace Meziantou.AspNetCore.Components.Internals;

/// <summary>
/// Converts values between <see cref="string"/> and arbitrary CLR types using <see cref="TypeConverter"/>.
/// Query string services and text-based inputs share this so a value written by one is readable by the other.
/// </summary>
internal static class ValueConverter
{
    public static bool TryConvertFromString(string? value, Type type, out object? result)
    {
        if (type == typeof(string))
        {
            result = value;
            return true;
        }

        if (string.IsNullOrEmpty(value))
        {
            // An empty value maps to null for reference and nullable types, and to the default value otherwise
            var isNullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
            result = isNullable ? null : Activator.CreateInstance(type);
            return true;
        }

        var converter = TypeDescriptor.GetConverter(type);
        if (!converter.CanConvertFrom(typeof(string)))
        {
            result = null;
            return false;
        }

        try
        {
            result = converter.ConvertFromString(context: null, CultureInfo.InvariantCulture, value);
            return true;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception)
#pragma warning restore CA1031
        {
            // TypeConverter reports invalid input by throwing, and the exception type is not part of its contract.
            // For instance BaseNumberConverter throws a plain Exception wrapping the original FormatException.
            result = null;
            return false;
        }
    }

    public static string? ConvertToString(object? value)
    {
        if (value is null)
            return null;

        if (value is string s)
            return s;

        var converter = TypeDescriptor.GetConverter(value.GetType());
        if (converter.CanConvertTo(typeof(string)))
        {
            try
            {
                return converter.ConvertToString(context: null, CultureInfo.InvariantCulture, value);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
#pragma warning restore CA1031
            {
                // Fall back to ToString below
            }
        }

        return value.ToString();
    }
}
