namespace Meziantou.Framework;

public static partial class StringBuilderExtensions
{
    public static StringBuilder AppendInvariant(this StringBuilder sb, byte value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, byte? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, sbyte value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, sbyte? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, short value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, short? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, ushort value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, ushort? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, int value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, int? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, uint value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, uint? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, long value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, long? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, ulong value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, ulong? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

#if NET6_0_OR_GREATER
    public static StringBuilder AppendInvariant(this StringBuilder sb, Half value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, Half? value)
    {
        if (value != null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }
#endif

    public static StringBuilder AppendInvariant(this StringBuilder sb, float value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, float? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, double value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, double? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, decimal value)
    {
        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, decimal? value)
    {
        if (value is not null)
            return sb.Append(value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, FormattableString? value)
    {
        if (value is not null)
            return sb.Append(value.ToString(CultureInfo.InvariantCulture));

        return sb;
    }

    [SuppressMessage("Performance", "MA0028:Optimize StringBuilder usage", Justification = "Performance")]
    public static StringBuilder AppendInvariant<T>(this StringBuilder sb, T? value)
        where T : IFormattable
    {
        if (value is not null)
            return sb.Append(value.ToString(format: null, CultureInfo.InvariantCulture));

        return sb;
    }

    public static StringBuilder AppendInvariant(this StringBuilder sb, object? value)
    {
        if (value is not null)
            return sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", value);

        return sb;
    }

    public static StringBuilder AppendFormatInvariant(this StringBuilder sb, string format, object? args0)
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args0);
    }

    public static StringBuilder AppendFormatInvariant(this StringBuilder sb, string format, object? args0, object? args1)
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args0, args1);
    }

    public static StringBuilder AppendFormatInvariant(this StringBuilder sb, string format, object? args0, object? args1, object? args2)
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args0, args1, args2);
    }

    public static StringBuilder AppendFormatInvariant(this StringBuilder sb, string format, params object?[] args)
    {
        return sb.AppendFormat(CultureInfo.InvariantCulture, format, args);
    }
}
