using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

// The GeometryUtils interface of CSSOM View (https://drafts.csswg.org/cssom-view/#the-geometryutils-interface): the quads of the
// boxes of a node, and the conversion of points between the local coordinates of the nodes, from the fragment tree.

/// <summary>https://drafts.csswg.org/cssom-view/#enumdef-cssboxtype</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#enumdef-cssboxtype"/>
[IdlInterface("CSSBoxType", "https://drafts.csswg.org/cssom-view/#enumdef-cssboxtype")]
public enum CssBoxType
{
    /// <summary>The margin box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-cssboxtype-margin"/>
    [IdlMember("margin", "https://drafts.csswg.org/cssom-view/#dom-cssboxtype-margin")]
    Margin,

    /// <summary>The border box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-cssboxtype-border"/>
    [IdlMember("border", "https://drafts.csswg.org/cssom-view/#dom-cssboxtype-border")]
    Border,

    /// <summary>The padding box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-cssboxtype-padding"/>
    [IdlMember("padding", "https://drafts.csswg.org/cssom-view/#dom-cssboxtype-padding")]
    Padding,

    /// <summary>The content box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-cssboxtype-content"/>
    [IdlMember("content", "https://drafts.csswg.org/cssom-view/#dom-cssboxtype-content")]
    Content,
}

/// <summary>
/// The <c>GeometryNode</c> union (https://drafts.csswg.org/cssom-view/#typedefdef-geometrynode): a <see cref="Text"/>, an
/// <see cref="Element"/>, a <see cref="CssPseudoElement"/> or a <see cref="Document"/>.
/// </summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#typedefdef-geometrynode"/>
[IdlInterface("(Text or Element or CSSPseudoElement or Document)", "https://drafts.csswg.org/cssom-view/#typedefdef-geometrynode")]
public readonly struct GeometryNode : IUnionValue, IEquatable<GeometryNode>
{
    private GeometryNode(object value) => Value = value;

    /// <summary>Gets the node: a <see cref="Text"/>, an <see cref="Element"/>, a <see cref="CssPseudoElement"/> or a <see cref="Document"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    object IUnionValue.Value => Value;

    /// <summary>Converts a <c>Text</c> to a <c>GeometryNode</c>.</summary>
    [NotInIdl]
    public static implicit operator GeometryNode(Text value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts an <c>Element</c> to a <c>GeometryNode</c>.</summary>
    [NotInIdl]
    public static implicit operator GeometryNode(Element value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>Document</c> to a <c>GeometryNode</c>.</summary>
    [NotInIdl]
    public static implicit operator GeometryNode(Document value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CSSPseudoElement</c> to a <c>GeometryNode</c>.</summary>
    [NotInIdl]
    public static implicit operator GeometryNode(CssPseudoElement value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>GeometryNode</c> from a <c>Text</c>.</summary>
    [NotInIdl]
    public static GeometryNode FromText(Text value) => value;

    /// <summary>Creates a <c>GeometryNode</c> from an <c>Element</c>.</summary>
    [NotInIdl]
    public static GeometryNode FromElement(Element value) => value;

    /// <summary>Creates a <c>GeometryNode</c> from a <c>Document</c>.</summary>
    [NotInIdl]
    public static GeometryNode FromDocument(Document value) => value;

    /// <summary>Creates a <c>GeometryNode</c> from a <c>CSSPseudoElement</c>.</summary>
    [NotInIdl]
    public static GeometryNode FromCssPseudoElement(CssPseudoElement value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(GeometryNode other) => ReferenceEquals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is GeometryNode other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>GeometryNode</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(GeometryNode left, GeometryNode right) => left.Equals(right);

    /// <summary>Determines whether two <c>GeometryNode</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(GeometryNode left, GeometryNode right) => !left.Equals(right);
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-boxquadoptions</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#dictdef-boxquadoptions"/>
[IdlInterface("BoxQuadOptions", "https://drafts.csswg.org/cssom-view/#dictdef-boxquadoptions")]
public sealed class BoxQuadOptions
{
    /// <summary>Gets the box of the quads.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-boxquadoptions-box"/>
    [IdlMember("box", "https://drafts.csswg.org/cssom-view/#dom-boxquadoptions-box")]
    public CssBoxType Box { get; init; } = CssBoxType.Border;

    /// <summary>Gets the node whose coordinates the quads are expressed in (the viewport by default).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-boxquadoptions-relativeto"/>
    [IdlMember("relativeTo", "https://drafts.csswg.org/cssom-view/#dom-boxquadoptions-relativeto")]
    public GeometryNode? RelativeTo { get; init; }
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-convertcoordinateoptions</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#dictdef-convertcoordinateoptions"/>
[IdlInterface("ConvertCoordinateOptions", "https://drafts.csswg.org/cssom-view/#dictdef-convertcoordinateoptions")]
public sealed class ConvertCoordinateOptions
{
    /// <summary>Gets the box of the node the coordinates are converted from.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-convertcoordinateoptions-frombox"/>
    [IdlMember("fromBox", "https://drafts.csswg.org/cssom-view/#dom-convertcoordinateoptions-frombox")]
    public CssBoxType FromBox { get; init; } = CssBoxType.Border;

    /// <summary>Gets the box of the node the coordinates are converted to.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-convertcoordinateoptions-tobox"/>
    [IdlMember("toBox", "https://drafts.csswg.org/cssom-view/#dom-convertcoordinateoptions-tobox")]
    public CssBoxType ToBox { get; init; } = CssBoxType.Border;
}

public partial class Element
{
    /// <summary>
    /// Returns the quads of the boxes of the element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads): one
    /// per box fragment (or the bounding box of an element of an SVG document fragment), with the transforms, in the coordinates
    /// of the viewport or of <see cref="BoxQuadOptions.RelativeTo"/>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads"/>
    [IdlMember("getBoxQuads", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads")]
    public IReadOnlyList<DomQuad> GetBoxQuads(BoxQuadOptions? options = null) => GeometryUtils.GetBoxQuads(this, options);

    /// <summary>Converts a quad from the coordinates of a node to the coordinates of the element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode"/>
    [IdlMember("convertQuadFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode")]
    public DomQuad ConvertQuadFromNode(DomQuadInit quad, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertQuadFromNode(this, quad, from, options);

    /// <summary>Converts a rectangle from the coordinates of a node to the coordinates of the element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode"/>
    [IdlMember("convertRectFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode")]
    public DomQuad ConvertRectFromNode(DomRectReadOnly rect, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertRectFromNode(this, rect, from, options);

    /// <summary>Converts a point from the coordinates of a node to the coordinates of the element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode"/>
    [IdlMember("convertPointFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode")]
    public DomPoint ConvertPointFromNode(DomPointInit point, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertPointFromNode(this, point, from, options);
}

public partial class Text
{
    /// <summary>
    /// Returns the quads of the text (https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads): the rectangles of
    /// its text fragments (see <see cref="Range.GetClientRects"/>), in the coordinates of the viewport or of
    /// <see cref="BoxQuadOptions.RelativeTo"/>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads"/>
    [IdlMember("getBoxQuads", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads")]
    public IReadOnlyList<DomQuad> GetBoxQuads(BoxQuadOptions? options = null) => GeometryUtils.GetBoxQuads(this, options);

    /// <summary>Converts a quad from the coordinates of a node to the coordinates of the text, which are the ones of its parent element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode"/>
    [IdlMember("convertQuadFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode")]
    public DomQuad ConvertQuadFromNode(DomQuadInit quad, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertQuadFromNode(this, quad, from, options);

    /// <summary>Converts a rectangle from the coordinates of a node to the coordinates of the text (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode"/>
    [IdlMember("convertRectFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode")]
    public DomQuad ConvertRectFromNode(DomRectReadOnly rect, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertRectFromNode(this, rect, from, options);

    /// <summary>Converts a point from the coordinates of a node to the coordinates of the text (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode"/>
    [IdlMember("convertPointFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode")]
    public DomPoint ConvertPointFromNode(DomPointInit point, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertPointFromNode(this, point, from, options);
}

public partial class Document
{
    /// <summary>
    /// Returns the quad of the document (https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads): the layout
    /// viewport, in the coordinates of the viewport or of <see cref="BoxQuadOptions.RelativeTo"/>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads"/>
    [IdlMember("getBoxQuads", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads")]
    public IReadOnlyList<DomQuad> GetBoxQuads(BoxQuadOptions? options = null) => GeometryUtils.GetBoxQuads(this, options);

    /// <summary>Converts a quad from the coordinates of a node to the coordinates of the viewport of the document (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode"/>
    [IdlMember("convertQuadFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode")]
    public DomQuad ConvertQuadFromNode(DomQuadInit quad, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertQuadFromNode(this, quad, from, options);

    /// <summary>Converts a rectangle from the coordinates of a node to the coordinates of the viewport of the document (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode"/>
    [IdlMember("convertRectFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode")]
    public DomQuad ConvertRectFromNode(DomRectReadOnly rect, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertRectFromNode(this, rect, from, options);

    /// <summary>Converts a point from the coordinates of a node to the coordinates of the viewport of the document (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode"/>
    [IdlMember("convertPointFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode")]
    public DomPoint ConvertPointFromNode(DomPointInit point, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertPointFromNode(this, point, from, options);
}

/// <summary>
/// The implementation of GeometryUtils (https://drafts.csswg.org/cssom-view/#the-geometryutils-interface). The local coordinates
/// of a node are the coordinates of the border box of the first fragment of an element or of a pseudo-element (a parallelogram
/// mapped from its bounding box for an element of an SVG document fragment), the ones of the parent element for a text, the
/// ones of the viewport for a document (and for an element without box). The points are mapped through the transforms and the scroll
/// positions (see <see cref="LayoutEngine.MapToRoot(Fragment, PhysicalOffset)"/>) to the viewport of the document, then
/// through the <c>iframe</c>s to the viewport of the common ancestor document of the two nodes.
/// </summary>
internal static class GeometryUtils
{
    /// <param name="node">A <see cref="Node"/> or a <see cref="CssPseudoElement"/>.</param>
    public static IReadOnlyList<DomQuad> GetBoxQuads(object node, BoxQuadOptions? options)
    {
        var document = GetDocument(node);
        GeometryNode relativeTo = options?.RelativeTo ?? document;
        var box = options?.Box ?? CssBoxType.Border;
        var result = new List<DomQuad>();
        switch (node)
        {
            case Document:
            {
                // "the width and height of the layout viewport"
                var size = document.BrowsingContext is null ? default : document.GetLayoutEngine().EnsureLayout()?.Size ?? default;
                result.Add(ConvertQuad(relativeTo.Value, new PhysicalQuad(new PhysicalRect(0, 0, size.Width, size.Height)), document, CssBoxType.Border, CssBoxType.Border));
                break;
            }

            case Text text:
            {
                // The client rectangles of a range that selects the text
                var range = new Range(text, 0, text, text.Length);
                foreach (var quad in range.GetClientQuads())
                {
                    result.Add(ConvertQuad(relativeTo.Value, quad, document, CssBoxType.Border, CssBoxType.Border));
                }

                break;
            }

            case Element element:
                if (CssomViewGeometry.GetLayoutEngine(element) is not { } engine)
                    break;

                if (element is SvgElement svg && CssomViewGeometry.GetOutermostSvgElement(svg) is not null)
                {
                    // The bounding box of an element of an SVG document fragment, with all its boxes
                    foreach (var quad in CssomViewGeometry.GetClientQuads(element))
                    {
                        result.Add(ConvertQuad(relativeTo.Value, quad, document, CssBoxType.Border, CssBoxType.Border));
                    }

                    break;
                }

                // One quad per box fragment, mapped to the viewport, then to the coordinates of relativeTo
                foreach (var fragment in CssomViewGeometry.GetClientFragments(engine, element))
                {
                    var rect = GetBoxRect(fragment, box);
                    var quad = engine.ToViewport(CssomViewGeometry.MapToRoot(fragment, new PhysicalQuad(rect)));
                    result.Add(ConvertQuad(relativeTo.Value, quad, document, CssBoxType.Border, CssBoxType.Border));
                }

                break;

            case CssPseudoElement { BoxName: { } name } pseudoElement:
                if (CssomViewGeometry.GetLayoutEngine(pseudoElement.Element) is not { } pseudoEngine)
                    break;

                foreach (var fragment in pseudoEngine.GetFragments(pseudoElement.Element, name))
                {
                    var quad = pseudoEngine.ToViewport(CssomViewGeometry.MapToRoot(fragment, new PhysicalQuad(GetBoxRect(fragment, box))));
                    result.Add(ConvertQuad(relativeTo.Value, quad, document, CssBoxType.Border, CssBoxType.Border));
                }

                break;
        }

        return result;
    }

    /// <summary>The document of a node or of a pseudo-element.</summary>
    private static Document GetDocument(object node) => node switch
    {
        Document document => document,
        CssPseudoElement pseudoElement => pseudoElement.Element.NodeDocument,
        _ => ((Node)node).NodeDocument,
    };

    public static DomQuad ConvertQuadFromNode(object node, DomQuadInit quad, GeometryNode from, ConvertCoordinateOptions? options)
    {
        ArgumentNullException.ThrowIfNull(quad);
        var converted = DomQuad.FromQuad(quad);
        var input = new PhysicalQuad(ToOffset(converted.P1), ToOffset(converted.P2), ToOffset(converted.P3), ToOffset(converted.P4));
        return ConvertQuad(node, input, from.Value, options?.FromBox ?? CssBoxType.Border, options?.ToBox ?? CssBoxType.Border);
    }

    public static DomQuad ConvertRectFromNode(object node, DomRectReadOnly rect, GeometryNode from, ConvertCoordinateOptions? options)
    {
        ArgumentNullException.ThrowIfNull(rect);
        var quad = DomQuad.FromRect(new DomRectInit { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height });
        var input = new PhysicalQuad(ToOffset(quad.P1), ToOffset(quad.P2), ToOffset(quad.P3), ToOffset(quad.P4));
        return ConvertQuad(node, input, from.Value, options?.FromBox ?? CssBoxType.Border, options?.ToBox ?? CssBoxType.Border);
    }

    public static DomPoint ConvertPointFromNode(object node, DomPointInit point, GeometryNode from, ConvertCoordinateOptions? options)
    {
        ArgumentNullException.ThrowIfNull(point);
        var result = ConvertPoint(node, new PhysicalOffset(point.X, point.Y), from.Value, options?.FromBox ?? CssBoxType.Border, options?.ToBox ?? CssBoxType.Border);

        // As in the browsers, the z and w coordinates of the point are kept
        return new DomPoint(result.X, result.Y, point.Z, point.W);
    }

    private static PhysicalOffset ToOffset(DomPoint point) => new(point.X, point.Y);

    private static DomQuad ConvertQuad(object node, PhysicalQuad quad, object from, CssBoxType fromBox, CssBoxType toBox)
    {
        var p1 = ConvertPoint(node, quad.P1, from, fromBox, toBox);
        var p2 = ConvertPoint(node, quad.P2, from, fromBox, toBox);
        var p3 = ConvertPoint(node, quad.P3, from, fromBox, toBox);
        var p4 = ConvertPoint(node, quad.P4, from, fromBox, toBox);
        return new DomQuad(ToInit(p1), ToInit(p2), ToInit(p3), ToInit(p4));

        static DomPointInit ToInit(PhysicalOffset point) => new() { X = point.X, Y = point.Y };
    }

    /// <summary>https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode</summary>
    private static PhysicalOffset ConvertPoint(object node, PhysicalOffset point, object from, CssBoxType fromBox, CssBoxType toBox)
    {
        // "Check the documents for coordinate conversion": the documents are mapped up to their common ancestor document
        var common = GetCommonDocument(node, from);

        // An element without box has no coordinates (as in the browsers); a pseudo-element without box has the coordinates of
        // the viewport (as in Blink)
        if (node is Element { } element && CssomViewGeometry.GetLayoutEngine(element) is null || from is Element { } fromElement && CssomViewGeometry.GetLayoutEngine(fromElement) is null)
            throw DomException.NotFound("The element does not generate a box");

        var viewportPoint = ToDocument(from, point, fromBox, common);
        return FromDocument(node, viewportPoint, toBox, common);
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom-view/#check-the-documents-for-coordinate-conversion: returns the common ancestor document
    /// (through the containers of the nested browsing contexts) of the documents of two nodes, or null when there is none;
    /// throws a SecurityError when a document on the path is not same origin with the document of <paramref name="node"/>.
    /// </summary>
    private static Document? GetCommonDocument(object node, object from)
    {
        var nodeDocuments = GetDocuments(node);
        var fromDocuments = GetDocuments(from);
        var nodeIndex = -1;
        var fromIndex = -1;
        for (var i = 0; i < nodeDocuments.Count && nodeIndex < 0; i++)
        {
            var index = fromDocuments.IndexOf(nodeDocuments[i]);
            if (index >= 0)
            {
                nodeIndex = i;
                fromIndex = index;
            }
        }

        var documentOfNode = nodeDocuments[0];
        var path = nodeIndex < 0 ? [nodeDocuments[0], fromDocuments[0]] : nodeDocuments.Take(nodeIndex + 1).Concat(fromDocuments.Take(fromIndex + 1));
        foreach (var document in path)
        {
            if (!Document.IsSameOrigin(document, documentOfNode))
                throw new DomException("The nodes are in documents that are not same origin", "SecurityError");
        }

        return nodeIndex < 0 ? null : nodeDocuments[nodeIndex];

        static List<Document> GetDocuments(object node)
        {
            var document = GetDocument(node);
            var result = new List<Document> { document };
            while (result[^1].BrowsingContext?.Container?.NodeDocument is { } containerDocument && !result.Contains(containerDocument))
            {
                result.Add(containerDocument);
            }

            return result;
        }
    }

    /// <summary>Maps a point in the local coordinates of a node (relative to the given box) to the coordinates of the viewport of a document (its document, or an ancestor document).</summary>
    private static PhysicalOffset ToDocument(object node, PhysicalOffset point, CssBoxType box, Document? target)
    {
        var document = GetDocument(node);
        var viewportPoint = GetSpace(node) switch
        {
            FragmentSpace space => space.Engine.ToViewport(LayoutEngine.MapToRoot(space.Fragment, point + GetBoxRect(space.Fragment, box).Offset)),
            AffineSpace space => space.ToViewport(point),
            _ => point,
        };

        // Through the iframes, to the viewport of the target document
        while (!ReferenceEquals(document, target) && document.BrowsingContext?.Container is { } container && GetContainerFragment(container) is var (engine, fragment))
        {
            viewportPoint = engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.ContentBoxRect.Offset + viewportPoint));
            document = container.NodeDocument;
        }

        return viewportPoint;
    }

    /// <summary>Maps a point of the viewport of a document (the document of the node, or an ancestor document) to the local coordinates of a node (relative to the given box).</summary>
    private static PhysicalOffset FromDocument(object node, PhysicalOffset point, CssBoxType box, Document? source)
    {
        // From the viewport of the source document down through the iframes to the viewport of the document of the node
        var document = GetDocument(node);
        var containers = new List<Element>();
        for (var current = document; !ReferenceEquals(current, source) && current.BrowsingContext?.Container is { } container; current = container.NodeDocument)
        {
            containers.Add(container);
        }

        for (var i = containers.Count - 1; i >= 0; i--)
        {
            if (GetContainerFragment(containers[i]) is not var (engine, fragment) || !LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(point), out var local))
                return point;

            point = local - fragment.ContentBoxRect.Offset;
        }

        switch (GetSpace(node))
        {
            case FragmentSpace space:
                return LayoutEngine.TryMapFromRoot(space.Fragment, space.Engine.FromViewport(point), out var local) ? local - GetBoxRect(space.Fragment, box).Offset : point;

            case AffineSpace space:
                return space.FromViewport(point);

            default:
                return point;
        }
    }

    private static (LayoutEngine Engine, BoxFragment Fragment)? GetContainerFragment(Element container)
        => CssomViewGeometry.GetLayoutEngine(container) is { } engine && engine.GetFragments(container) is [var fragment, ..] ? (engine, fragment) : null;

    /// <summary>Returns the rectangle of a box of a fragment in the coordinates of its border box (https://drafts.csswg.org/cssom-view/#adjust-a-point-to-the-border-box).</summary>
    private static PhysicalRect GetBoxRect(BoxFragment fragment, CssBoxType box) => box switch
    {
        CssBoxType.Margin => fragment.Box.IsInlineBox ? GetInlineMarginBoxRect(fragment) : fragment.MarginBoxRect,
        CssBoxType.Padding => fragment.PaddingBoxRect,
        CssBoxType.Content => fragment.ContentBoxRect,
        _ => fragment.BorderBoxRect,
    };

    /// <summary>
    /// The margin box of a fragment of an inline box: its margins in the inline axis (on the fragments with the edges), and its
    /// margins in the block axis, which do not affect the layout of the line (as in the browsers).
    /// </summary>
    private static PhysicalRect GetInlineMarginBoxRect(BoxFragment fragment)
    {
        var containingBlock = fragment.Parent;
        while (containingBlock is not null and not BoxFragment { Box.IsInlineBox: false })
        {
            containingBlock = containingBlock.Parent;
        }

        var writingDirection = fragment.WritingDirection;
        var basis = containingBlock is BoxFragment block ? writingDirection.ToLogical(block.ContentBoxRect.Size).Inline : 0;
        var margins = FormattingContext.ResolveMargins(fragment.Box.Style.Layout.Box, basis);
        var applied = fragment.Margin;
        applied = writingDirection.IsHorizontal ? applied with { Top = margins.Top, Bottom = margins.Bottom } : applied with { Left = margins.Left, Right = margins.Right };
        return fragment.BorderBoxRect.Expand(applied);
    }

    /// <summary>
    /// Returns the local coordinate space of a node: the first fragment of an element or of a pseudo-element, the bounding box
    /// of an element of an SVG document fragment, else the viewport of its document (null).
    /// </summary>
    private static object? GetSpace(object node)
    {
        if (node is CssPseudoElement pseudoElement)
        {
            return pseudoElement.BoxName is { } name && CssomViewGeometry.GetLayoutEngine(pseudoElement.Element) is { } pseudoEngine && pseudoEngine.GetFragments(pseudoElement.Element, name) is [var first, ..]
                ? new FragmentSpace(pseudoEngine, first)
                : null;
        }

        var element = node switch
        {
            Element e => e,
            Text text => text.ParentElement,
            _ => null,
        };

        if (element is null || CssomViewGeometry.GetLayoutEngine(element) is not { } engine)
            return null;

        if (element is SvgElement svg && CssomViewGeometry.GetOutermostSvgElement(svg) is not null)
            return CssomViewGeometry.GetClientQuads(element) is [var quad, ..] ? new AffineSpace(quad) : null;

        return engine.GetFragments(element) is [var fragment, ..] ? new FragmentSpace(engine, fragment) : null;
    }

    private sealed record FragmentSpace(LayoutEngine Engine, BoxFragment Fragment);

    /// <summary>The coordinates of a parallelogram (the transformed bounding box of an SVG element): its P1 is the origin, its P2 is at (width, 0), its P4 at (0, height).</summary>
    private sealed class AffineSpace(PhysicalQuad quad)
    {
        private readonly PhysicalOffset _origin = quad.P1;
        private readonly PhysicalOffset _u = Unit(quad.P1, quad.P2);
        private readonly PhysicalOffset _v = Unit(quad.P1, quad.P4);

        public PhysicalOffset ToViewport(PhysicalOffset point) => new(_origin.X + (point.X * _u.X) + (point.Y * _v.X), _origin.Y + (point.X * _u.Y) + (point.Y * _v.Y));

        public PhysicalOffset FromViewport(PhysicalOffset point)
        {
            var determinant = (_u.X * _v.Y) - (_v.X * _u.Y);
            if (determinant == 0)
                return point - _origin;

            var dx = point.X - _origin.X;
            var dy = point.Y - _origin.Y;
            return new PhysicalOffset(((dx * _v.Y) - (dy * _v.X)) / determinant, ((dy * _u.X) - (dx * _u.Y)) / determinant);
        }

        private static double Distance(PhysicalOffset a, PhysicalOffset b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

        // The unit vector from a to b (the x axis when the points are the same)
        private static PhysicalOffset Unit(PhysicalOffset a, PhysicalOffset b)
        {
            var length = Distance(a, b);
            return length == 0 ? new PhysicalOffset(1, 0) : new PhysicalOffset((b.X - a.X) / length, (b.Y - a.Y) / length);
        }
    }
}
