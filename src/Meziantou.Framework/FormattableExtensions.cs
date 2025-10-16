namespace Meziantou.Framework;

public static class FormattableExtensions
{
    public static string ToStringInvariant<T>(this T value) where T : IFormattable
    {
        return ToStringInvariant(value, format: null);
    }

    public static string ToStringInvariant<T>(this T value, string? format) where T : IFormattable
    {
        if (value is null)
            return "";

        return value.ToString(format, CultureInfo.InvariantCulture) ?? "";
    }
}
