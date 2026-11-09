namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>A circle of the layout of a Venn diagram: a set, with its position, its radius and its size.</summary>
internal sealed class VennCircle
{
    public VennCircle(double x, double y, double radius)
    {
        X = x;
        Y = y;
        Radius = radius;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public double Radius { get; set; }

    /// <summary>Gets or sets the size of the set, the area of the circle before the layout is scaled.</summary>
    public double Size { get; set; }

    /// <summary>Gets or sets the identifier of the set.</summary>
    public string SetId { get; set; } = "";

    /// <summary>Gets or sets the parent of the circle in the union-find of the disjoint clusters.</summary>
    public VennCircle? Parent { get; set; }

    public VennCircle Clone() => new(X, Y, Radius) { Size = Size, SetId = SetId };
}
