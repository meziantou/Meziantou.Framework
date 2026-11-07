namespace Sample;

public readonly struct Distance(double value)
{
    public double Value => value;

    #region TaggedValues_Operator
    [return: ValueTag("Meters")]
    public static Distance operator +([ValueTag("Meters")] Distance left, [ValueTag("Meters")] Distance right) => new(left.Value + right.Value);
    #endregion
}
