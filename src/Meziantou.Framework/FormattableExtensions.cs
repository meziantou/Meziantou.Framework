namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="IFormattable"/> types to format values using the invariant culture.
/// </summary>
/// <example>
/// <code>
/// int value = 1234;
/// string str = value.ToStringInvariant(); // "1234"
/// double price = 12.34;
/// string formatted = price.ToStringInvariant("F2"); // "12.34"
/// </code>
/// </example>
public static class FormattableExtensions
{
    /// <summary>Converts the value to its string representation using the invariant culture.</summary>
    public static string ToStringInvariant<T>(this T value) where T : IFormattable
    {
        return ToStringInvariant(value, format: null);
    }

    /// <summary>Converts the value to its string representation using the invariant culture and the specified format.</summary>
    public static string ToStringInvariant<T>(this T value, string? format) where T : IFormattable
    {
        if (value is null)
            return "";

        return value.ToString(format, CultureInfo.InvariantCulture) ?? "";
    }
}
