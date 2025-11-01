namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="IFormattable"/> types to format values using the invariant culture.
/// </summary>
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
