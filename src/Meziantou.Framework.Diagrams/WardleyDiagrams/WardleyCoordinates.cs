namespace Meziantou.Framework.Diagrams;

/// <summary>Checks the coordinates of the elements of a Wardley map.</summary>
internal static class WardleyCoordinates
{
    /// <summary>Returns a coordinate, between 0 and 1.</summary>
    public static double Check(double value, string paramName)
    {
        if (!(value is >= 0 and <= 1))
            throw new ArgumentOutOfRangeException(paramName, value, "A coordinate of a Wardley map is between 0 and 1.");

        return value;
    }

    /// <summary>Returns a name, which cannot be null.</summary>
    public static string CheckName(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }
}
