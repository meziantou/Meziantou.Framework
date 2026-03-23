namespace Meziantou.Framework.Json.Internals;

/// <summary>A literal value in a filter expression (number, string, true, false, null).</summary>
internal sealed class LiteralComparable : Comparable
{
    /// <summary>
    /// Represents JSON null.
    /// </summary>
    public static readonly LiteralComparable Null = new(value: null);

    /// <summary>
    /// Represents JSON true.
    /// </summary>
    public static readonly LiteralComparable True = new(value: true);

    /// <summary>
    /// Represents JSON false.
    /// </summary>
    public static readonly LiteralComparable False = new(value: false);

    public LiteralComparable(object? value)
    {
        Value = value;
    }

    public override ComparableKind Kind => ComparableKind.Literal;

    /// <summary>The literal value: null, bool, string, or double/long.</summary>
    public object? Value { get; }
}
