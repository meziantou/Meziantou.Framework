namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://drafts.fxtf.org/geometry/#dictdef-dompointinit</summary>
[IdlInterface("DOMPointInit", "https://drafts.fxtf.org/geometry/#dictdef-dompointinit")]
public sealed class DomPointInit
{
    /// <summary>Gets or sets the X coordinate of the point.</summary>
    [IdlMember("x", "https://drafts.fxtf.org/geometry/#dom-dompointinit-x")]
    public double X { get; init; }

    /// <summary>Gets or sets the Y coordinate of the point.</summary>
    [IdlMember("y", "https://drafts.fxtf.org/geometry/#dom-dompointinit-y")]
    public double Y { get; init; }

    /// <summary>Gets or sets the Z coordinate of the point.</summary>
    [IdlMember("z", "https://drafts.fxtf.org/geometry/#dom-dompointinit-z")]
    public double Z { get; init; }

    /// <summary>Gets or sets the W coordinate of the point.</summary>
    [IdlMember("w", "https://drafts.fxtf.org/geometry/#dom-dompointinit-w")]
    public double W { get; init; } = 1;
}

/// <summary>https://drafts.fxtf.org/geometry/#dompointreadonly</summary>
[IdlInterface("DOMPointReadOnly", "https://drafts.fxtf.org/geometry/#dompointreadonly")]
public class DomPointReadOnly : Scripting.IStructuredSerializable
{
    /// <summary>https://drafts.fxtf.org/geometry/#structured-serialization</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => this is DomPoint ? new DomPoint(XValue, YValue, ZValue, WValue) : new DomPointReadOnly(XValue, YValue, ZValue, WValue);

    /// <summary>Creates a point with the given coordinates.</summary>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-dompointreadonly")]
    public DomPointReadOnly(double x = 0, double y = 0, double z = 0, double w = 1)
    {
        XValue = x;
        YValue = y;
        ZValue = z;
        WValue = w;
    }

    private protected double XValue { get; set; }

    private protected double YValue { get; set; }

    private protected double ZValue { get; set; }

    private protected double WValue { get; set; }

    /// <summary>Creates a read-only point from the coordinates of the given dictionary.</summary>
    [IdlMember("fromPoint", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-frompoint")]
    public static DomPointReadOnly FromPoint(DomPointInit? other = null)
    {
        other ??= new DomPointInit();
        return new DomPointReadOnly(other.X, other.Y, other.Z, other.W);
    }

    /// <summary>Gets the X coordinate of the point.</summary>
    [IdlMember("x", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-x")]
    public double X => XValue;

    /// <summary>Gets the Y coordinate of the point.</summary>
    [IdlMember("y", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-y")]
    public double Y => YValue;

    /// <summary>Gets the Z coordinate of the point.</summary>
    [IdlMember("z", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-z")]
    public double Z => ZValue;

    /// <summary>Gets the W coordinate of the point.</summary>
    [IdlMember("w", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-w")]
    public double W => WValue;

    /// <summary>https://drafts.fxtf.org/geometry/#dom-dompointreadonly-matrixtransform</summary>
    [IdlMember("matrixTransform", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-matrixtransform")]
    public DomPoint MatrixTransform(DomMatrixInit? matrix = null)
    {
        var m = DomMatrixReadOnly.FromMatrixInit(matrix);
        return m.TransformPointCore(XValue, YValue, ZValue, WValue);
    }

    /// <summary>Returns a dictionary representation of the point, as used by <c>JSON.stringify()</c>.</summary>
    [IdlMember("toJSON", "https://drafts.fxtf.org/geometry/#dom-dompointreadonly-tojson")]
    public DomPointInit ToJson() => new() { X = XValue, Y = YValue, Z = ZValue, W = WValue };
}

/// <summary>https://drafts.fxtf.org/geometry/#dompoint</summary>
[IdlInterface("DOMPoint", "https://drafts.fxtf.org/geometry/#dompoint")]
public class DomPoint : DomPointReadOnly
{
    /// <summary>Creates a mutable point with the given coordinates.</summary>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dompoint-dompoint")]
    public DomPoint(double x = 0, double y = 0, double z = 0, double w = 1)
        : base(x, y, z, w)
    {
    }

    /// <summary>Creates a mutable point from the coordinates of the given dictionary.</summary>
    [IdlMember("fromPoint", "https://drafts.fxtf.org/geometry/#dom-dompoint-frompoint")]
    public static new DomPoint FromPoint(DomPointInit? other = null)
    {
        other ??= new DomPointInit();
        return new DomPoint(other.X, other.Y, other.Z, other.W);
    }

    /// <summary>Gets or sets the X coordinate of the point.</summary>
    [IdlMember("x", "https://drafts.fxtf.org/geometry/#dom-dompoint-x")]
    public new virtual double X
    {
        get => XValue;
        set => XValue = value;
    }

    /// <summary>Gets or sets the Y coordinate of the point.</summary>
    [IdlMember("y", "https://drafts.fxtf.org/geometry/#dom-dompoint-y")]
    public new virtual double Y
    {
        get => YValue;
        set => YValue = value;
    }

    /// <summary>Gets or sets the Z coordinate of the point.</summary>
    [IdlMember("z", "https://drafts.fxtf.org/geometry/#dom-dompoint-z")]
    public new double Z
    {
        get => ZValue;
        set => ZValue = value;
    }

    /// <summary>Gets or sets the W coordinate of the point.</summary>
    [IdlMember("w", "https://drafts.fxtf.org/geometry/#dom-dompoint-w")]
    public new double W
    {
        get => WValue;
        set => WValue = value;
    }
}

/// <summary>https://drafts.fxtf.org/geometry/#dictdef-domrectinit</summary>
[IdlInterface("DOMRectInit", "https://drafts.fxtf.org/geometry/#dictdef-domrectinit")]
public sealed class DomRectInit
{
    /// <summary>Gets or sets the X coordinate of the origin of the rectangle.</summary>
    [IdlMember("x", "https://drafts.fxtf.org/geometry/#dom-domrectinit-x")]
    public double X { get; init; }

    /// <summary>Gets or sets the Y coordinate of the origin of the rectangle.</summary>
    [IdlMember("y", "https://drafts.fxtf.org/geometry/#dom-domrectinit-y")]
    public double Y { get; init; }

    /// <summary>Gets or sets the width of the rectangle.</summary>
    [IdlMember("width", "https://drafts.fxtf.org/geometry/#dom-domrectinit-width")]
    public double Width { get; init; }

    /// <summary>Gets or sets the height of the rectangle.</summary>
    [IdlMember("height", "https://drafts.fxtf.org/geometry/#dom-domrectinit-height")]
    public double Height { get; init; }
}

/// <summary>https://drafts.fxtf.org/geometry/#domrectreadonly</summary>
[IdlInterface("DOMRectReadOnly", "https://drafts.fxtf.org/geometry/#domrectreadonly")]
public class DomRectReadOnly : Scripting.IStructuredSerializable
{
    /// <summary>https://drafts.fxtf.org/geometry/#structured-serialization</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => this is DomRect ? new DomRect(XValue, YValue, WidthValue, HeightValue) : new DomRectReadOnly(XValue, YValue, WidthValue, HeightValue);

    /// <summary>Creates a rectangle with the given origin and size.</summary>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-domrectreadonly")]
    public DomRectReadOnly(double x = 0, double y = 0, double width = 0, double height = 0)
    {
        XValue = x;
        YValue = y;
        WidthValue = width;
        HeightValue = height;
    }

    private protected double XValue { get; set; }

    private protected double YValue { get; set; }

    private protected double WidthValue { get; set; }

    private protected double HeightValue { get; set; }

    /// <summary>Creates a read-only rectangle from the origin and size of the given dictionary.</summary>
    [IdlMember("fromRect", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-fromrect")]
    public static DomRectReadOnly FromRect(DomRectInit? other = null)
    {
        other ??= new DomRectInit();
        return new DomRectReadOnly(other.X, other.Y, other.Width, other.Height);
    }

    /// <summary>Gets the X coordinate of the origin of the rectangle.</summary>
    [IdlMember("x", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-x")]
    public double X => XValue;

    /// <summary>Gets the Y coordinate of the origin of the rectangle.</summary>
    [IdlMember("y", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-y")]
    public double Y => YValue;

    /// <summary>Gets the width of the rectangle.</summary>
    [IdlMember("width", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-width")]
    public double Width => WidthValue;

    /// <summary>Gets the height of the rectangle.</summary>
    [IdlMember("height", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-height")]
    public double Height => HeightValue;

    /// <summary>Gets the Y coordinate of the top edge of the rectangle.</summary>
    [IdlMember("top", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-top")]
    public double Top => Min(YValue, YValue + HeightValue);

    /// <summary>Gets the X coordinate of the right edge of the rectangle.</summary>
    [IdlMember("right", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-right")]
    public double Right => Max(XValue, XValue + WidthValue);

    /// <summary>Gets the Y coordinate of the bottom edge of the rectangle.</summary>
    [IdlMember("bottom", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-bottom")]
    public double Bottom => Max(YValue, YValue + HeightValue);

    /// <summary>Gets the X coordinate of the left edge of the rectangle.</summary>
    [IdlMember("left", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-left")]
    public double Left => Min(XValue, XValue + WidthValue);

    /// <summary>Returns a dictionary representation of the rectangle, as used by <c>JSON.stringify()</c>.</summary>
    [IdlMember("toJSON", "https://drafts.fxtf.org/geometry/#dom-domrectreadonly-tojson")]
    public DomRectInit ToJson() => new() { X = XValue, Y = YValue, Width = WidthValue, Height = HeightValue };

    // NaN-safe min/max (https://drafts.fxtf.org/geometry/#dom-domrectreadonly-top): NaN propagates
    private static double Min(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Min(a, b);

    private static double Max(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Max(a, b);
}

/// <summary>https://drafts.fxtf.org/geometry/#domrect</summary>
[IdlInterface("DOMRect", "https://drafts.fxtf.org/geometry/#domrect")]
public class DomRect : DomRectReadOnly
{
    /// <summary>Creates a mutable rectangle with the given origin and size.</summary>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-domrect-domrect")]
    public DomRect(double x = 0, double y = 0, double width = 0, double height = 0)
        : base(x, y, width, height)
    {
    }

    /// <summary>Creates a mutable rectangle from the origin and size of the given dictionary.</summary>
    [IdlMember("fromRect", "https://drafts.fxtf.org/geometry/#dom-domrect-fromrect")]
    public static new DomRect FromRect(DomRectInit? other = null)
    {
        other ??= new DomRectInit();
        return new DomRect(other.X, other.Y, other.Width, other.Height);
    }

    /// <summary>Gets or sets the X coordinate of the origin of the rectangle.</summary>
    [IdlMember("x", "https://drafts.fxtf.org/geometry/#dom-domrect-x")]
    public new virtual double X
    {
        get => XValue;
        set => XValue = value;
    }

    /// <summary>Gets or sets the Y coordinate of the origin of the rectangle.</summary>
    [IdlMember("y", "https://drafts.fxtf.org/geometry/#dom-domrect-y")]
    public new virtual double Y
    {
        get => YValue;
        set => YValue = value;
    }

    /// <summary>Gets or sets the width of the rectangle.</summary>
    [IdlMember("width", "https://drafts.fxtf.org/geometry/#dom-domrect-width")]
    public new virtual double Width
    {
        get => WidthValue;
        set => WidthValue = value;
    }

    /// <summary>Gets or sets the height of the rectangle.</summary>
    [IdlMember("height", "https://drafts.fxtf.org/geometry/#dom-domrect-height")]
    public new virtual double Height
    {
        get => HeightValue;
        set => HeightValue = value;
    }
}
