using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://drafts.fxtf.org/geometry/#dictdef-domquadinit</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#dictdef-domquadinit"/>
[IdlInterface("DOMQuadInit", "https://drafts.fxtf.org/geometry/#dictdef-domquadinit")]
public sealed class DomQuadInit
{
    /// <summary>Gets the first point of the quad as a point dictionary.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquadinit-p1"/>
    [IdlMember("p1", "https://drafts.fxtf.org/geometry/#dom-domquadinit-p1")]
    public DomPointInit? P1 { get; init; }

    /// <summary>Gets the second point of the quad as a point dictionary.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquadinit-p2"/>
    [IdlMember("p2", "https://drafts.fxtf.org/geometry/#dom-domquadinit-p2")]
    public DomPointInit? P2 { get; init; }

    /// <summary>Gets the third point of the quad as a point dictionary.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquadinit-p3"/>
    [IdlMember("p3", "https://drafts.fxtf.org/geometry/#dom-domquadinit-p3")]
    public DomPointInit? P3 { get; init; }

    /// <summary>Gets the fourth point of the quad as a point dictionary.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquadinit-p4"/>
    [IdlMember("p4", "https://drafts.fxtf.org/geometry/#dom-domquadinit-p4")]
    public DomPointInit? P4 { get; init; }
}

/// <summary>https://drafts.fxtf.org/geometry/#domquad</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#domquad"/>
[IdlInterface("DOMQuad", "https://drafts.fxtf.org/geometry/#domquad")]
public sealed class DomQuad : Scripting.IStructuredSerializable
{
    /// <summary>https://drafts.fxtf.org/geometry/#structured-serialization: the points are serialized as DOMPoint objects.</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => new DomQuad(P1.ToJson(), P2.ToJson(), P3.ToJson(), P4.ToJson());

    /// <summary>https://drafts.fxtf.org/geometry/#dom-domquad-domquad</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-domquad"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-domquad-domquad")]
    public DomQuad(DomPointInit? p1 = null, DomPointInit? p2 = null, DomPointInit? p3 = null, DomPointInit? p4 = null)
    {
        P1 = DomPoint.FromPoint(p1);
        P2 = DomPoint.FromPoint(p2);
        P3 = DomPoint.FromPoint(p3);
        P4 = DomPoint.FromPoint(p4);
    }

    /// <summary>https://drafts.fxtf.org/geometry/#create-a-domquad-from-the-domrectinit-dictionary</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-fromrect"/>
    [IdlMember("fromRect", "https://drafts.fxtf.org/geometry/#dom-domquad-fromrect")]
    public static DomQuad FromRect(DomRectInit? other = null)
    {
        other ??= new DomRectInit();
        var x = other.X;
        var y = other.Y;
        var width = other.Width;
        var height = other.Height;
        return new DomQuad(
            new DomPointInit { X = x, Y = y },
            new DomPointInit { X = x + width, Y = y },
            new DomPointInit { X = x + width, Y = y + height },
            new DomPointInit { X = x, Y = y + height });
    }

    /// <summary>https://drafts.fxtf.org/geometry/#create-a-domquad-from-the-domquadinit-dictionary</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-fromquad"/>
    [IdlMember("fromQuad", "https://drafts.fxtf.org/geometry/#dom-domquad-fromquad")]
    public static DomQuad FromQuad(DomQuadInit? other = null)
    {
        other ??= new DomQuadInit();
        return new DomQuad(other.P1, other.P2, other.P3, other.P4);
    }

    /// <summary>Gets the first corner point of the quadrilateral.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-p1"/>
    [IdlMember("p1", "https://drafts.fxtf.org/geometry/#dom-domquad-p1")]
    public DomPoint P1 { get; }

    /// <summary>Gets the second corner point of the quadrilateral.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-p2"/>
    [IdlMember("p2", "https://drafts.fxtf.org/geometry/#dom-domquad-p2")]
    public DomPoint P2 { get; }

    /// <summary>Gets the third corner point of the quadrilateral.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-p3"/>
    [IdlMember("p3", "https://drafts.fxtf.org/geometry/#dom-domquad-p3")]
    public DomPoint P3 { get; }

    /// <summary>Gets the fourth corner point of the quadrilateral.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-p4"/>
    [IdlMember("p4", "https://drafts.fxtf.org/geometry/#dom-domquad-p4")]
    public DomPoint P4 { get; }

    /// <summary>https://drafts.fxtf.org/geometry/#dom-domquad-getbounds</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-getbounds"/>
    [IdlMember("getBounds", "https://drafts.fxtf.org/geometry/#dom-domquad-getbounds")]
    public DomRect GetBounds()
    {
        var left = Min(P1.X, P2.X, P3.X, P4.X);
        var top = Min(P1.Y, P2.Y, P3.Y, P4.Y);
        var right = Max(P1.X, P2.X, P3.X, P4.X);
        var bottom = Max(P1.Y, P2.Y, P3.Y, P4.Y);
        return new DomRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Returns a dictionary that represents the quadrilateral, with its four points converted to point dictionaries.
    /// </summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domquad-tojson"/>
    [IdlMember("toJSON", "https://drafts.fxtf.org/geometry/#dom-domquad-tojson")]
    public DomQuadInit ToJson() => new() { P1 = P1.ToJson(), P2 = P2.ToJson(), P3 = P3.ToJson(), P4 = P4.ToJson() };

    // NaN-safe minimum/maximum: NaN propagates
    private static double Min(double a, double b, double c, double d) => double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) || double.IsNaN(d) ? double.NaN : Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static double Max(double a, double b, double c, double d) => double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) || double.IsNaN(d) ? double.NaN : Math.Max(Math.Max(a, b), Math.Max(c, d));
}

/// <summary>https://drafts.fxtf.org/geometry/#domrectlist</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#domrectlist"/>
[IdlInterface("DOMRectList", "https://drafts.fxtf.org/geometry/#domrectlist")]
public sealed class DomRectList : IReadOnlyList<DomRect>
{
    private readonly DomRect[] _items;

    /// <summary>Creates a list that contains the given rectangles.</summary>
    [NotInIdl]
    public DomRectList(IEnumerable<DomRect> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>Gets the number of rectangles in the list.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domrectlist-length"/>
    [IdlMember("length", "https://drafts.fxtf.org/geometry/#dom-domrectlist-length")]
    public uint Length => (uint)_items.Length;

    /// <summary>Gets the number of rectangles in the list (the .NET counterpart of <c>length</c>).</summary>
    [NotInIdl]
    public int Count => _items.Length;

    /// <summary>Returns the rectangle at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domrectlist-item"/>
    [IdlMember("item", "https://drafts.fxtf.org/geometry/#dom-domrectlist-item")]
    public DomRect? Item(uint index) => index < (uint)_items.Length ? _items[index] : null;

    /// <summary>Gets the rectangle at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-domrectlist-item"/>
    [IdlMember("item", "https://drafts.fxtf.org/geometry/#dom-domrectlist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public DomRect? this[uint index] => Item(index);

    /// <summary>Gets the rectangle at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public DomRect this[int index] => _items[index];

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<DomRect> GetEnumerator() => ((IEnumerable<DomRect>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
