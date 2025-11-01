namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="IFormattable"/> types.
/// </summary>
public static class FormattableExtensions
{
    /// <summary>
    /// Converts the value to its string representation using the invariant culture.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The string representation of the value in the invariant culture, or an empty string if the value is <see langword="null"/>.</returns>
    public static string ToStringInvariant<T>(this T value) where T : IFormattable
    {
        return ToStringInvariant(value, format: null);
    }

    /// <summary>
    /// Converts the value to its string representation using the specified format and the invariant culture.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="format">The format string to use, or <see langword="null"/> to use the default format.</param>
    /// <returns>The string representation of the value in the invariant culture, or an empty string if the value is <see langword="null"/>.</returns>
    public static string ToStringInvariant<T>(this T value, string? format) where T : IFormattable
    {
        if (value is null)
            return "";

        return value.ToString(format, CultureInfo.InvariantCulture) ?? "";
    }
}
