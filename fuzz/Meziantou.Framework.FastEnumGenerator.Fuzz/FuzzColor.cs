namespace Meziantou.Framework.FastEnumGenerator.Fuzz;

/// <summary>Not [Flags], with names that differ only by case, an alias, and a negative value.</summary>
public enum FuzzColor
{
    Red = 1,
    Green = 2,
    Blue = 4,
    red = 8,
    Purple = -3,
    Zero = 0,
#pragma warning disable CA1069 // Enums values should not be duplicated -- an alias is parsed like the other names
    Alias = 2,
#pragma warning restore CA1069
}
