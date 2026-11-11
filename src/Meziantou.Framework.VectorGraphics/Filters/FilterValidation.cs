namespace Meziantou.Framework.VectorGraphics;

/// <summary>Validates the parameters of the primitives of the filter graphs.</summary>
internal static class FilterValidation
{
    public static double Finite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be a finite number.", paramName);

        return value;
    }

    public static double NonNegative(double value, string paramName)
    {
        Finite(value, paramName);
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
        return value;
    }

    public static SceneBounds? Bounds(SceneBounds? value, string paramName)
    {
        if (value is { } bounds)
        {
            if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
                throw new ArgumentException("The coordinates of the rectangle must be finite numbers.", paramName);

            if (bounds.Width < 0 || bounds.Height < 0)
                throw new ArgumentOutOfRangeException(paramName, bounds, "The width and the height of the rectangle must not be negative.");
        }

        return value;
    }

    public static TEnum Defined<TEnum>(TEnum value, TEnum max, string paramName)
        where TEnum : struct, Enum
    {
        var number = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        if (number < 0 || number > Convert.ToInt64(max, System.Globalization.CultureInfo.InvariantCulture))
            throw new ArgumentOutOfRangeException(paramName, value, $"The value is not a defined {typeof(TEnum).Name}.");

        return value;
    }
}
