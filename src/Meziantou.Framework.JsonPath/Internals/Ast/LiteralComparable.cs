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
        IsScalar = ScalarValue.TryFromLiteral(value, out var scalar);
        Scalar = scalar;
    }

    public override ComparableKind Kind => ComparableKind.Literal;

    /// <summary>The literal value: null, bool, string, or <see cref="NumberLiteral"/>.</summary>
    public object? Value { get; }

    /// <summary>Gets whether <see cref="Value"/> is a literal, which <see cref="Scalar"/> holds.</summary>
    public bool IsScalar { get; }

    /// <summary>Gets the value of the literal, converted once rather than for each node a filter tests.</summary>
    public ScalarValue Scalar { get; }
}
