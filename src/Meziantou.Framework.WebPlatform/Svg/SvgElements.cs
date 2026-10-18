using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class SvgElement
{
    /// <summary>https://svgwg.org/svg2-draft/types.html#__svg__SVGElement__ownerSVGElement: the nearest ancestor svg element.</summary>
    [IdlMember("ownerSVGElement", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGElement__ownerSVGElement")]
    public SvgSvgElement? OwnerSvgElement
    {
        get
        {
            // The ancestors in the same SVG document fragment: an svg element in a foreignObject is an outermost svg element
            for (var ancestor = ParentNode; ancestor is SvgElement and not SvgForeignObjectElement; ancestor = ancestor.ParentNode)
            {
                if (ancestor is SvgSvgElement svg)
                    return svg;
            }

            return null;
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/types.html#__svg__SVGElement__viewportElement: the element that establishes the viewport (the nearest ancestor svg element).</summary>
    [IdlMember("viewportElement", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGElement__viewportElement")]
    public SvgElement? ViewportElement => OwnerSvgElement;
}

public partial class SvgGraphicsElement
{
    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGraphicsElement__getBBox: the bounding box of the geometry of the
    /// element in its user space (see <see cref="SvgBoundingBox"/>): the shapes and their stroke, the glyph cells of the texts,
    /// the children of the containers; the markers and the clipping are ignored.
    /// </summary>
    [IdlMember("getBBox", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGraphicsElement__getBBox")]
    public DomRect GetBBox(SvgBoundingBoxOptions? options = null) => SvgBoundingBox.Compute(this, options?.Fill ?? true, options?.Stroke ?? false);

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGraphicsElement__getCTM: the transform from the user space of the
    /// element to the coordinate system of its nearest viewport element (see <see cref="SvgUserSpace.GetCtm"/>). As in the
    /// browsers, an element that is not rendered gets the transforms of its attributes and styles.
    /// </summary>
    [IdlMember("getCTM", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGraphicsElement__getCTM")]
    public DomMatrix? GetCtm() => SvgUserSpace.GetCtm(this).ToDomMatrix();

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGraphicsElement__getScreenCTM: the transform from the user space of
    /// the element to the coordinates of the viewport (the layout of the outermost svg element, see
    /// <see cref="SvgUserSpace.GetScreenCtm"/>). As in the browsers, an element that has no layout gets its <c>getCTM()</c>.
    /// </summary>
    [IdlMember("getScreenCTM", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGraphicsElement__getScreenCTM")]
    public DomMatrix? GetScreenCtm() => (SvgUserSpace.GetScreenCtm(this) ?? SvgUserSpace.GetCtm(this)).ToDomMatrix();
}

/// <summary>https://svgwg.org/svg2-draft/types.html#DictionarySVGBoundingBoxOptions</summary>
[IdlInterface("SVGBoundingBoxOptions", "https://w3c.github.io/svgwg/svg2-draft/types.html#DictionarySVGBoundingBoxOptions")]
public sealed class SvgBoundingBoxOptions
{
    /// <summary>
    /// Gets a value indicating whether the fill is included in the bounding box (<c>true</c> by default).
    /// </summary>
    [IdlMember("fill", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGBoundingBoxOptions__fill")]
    public bool Fill { get; init; } = true;

    /// <summary>Gets a value indicating whether the stroke is included in the bounding box.</summary>
    [IdlMember("stroke", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGBoundingBoxOptions__stroke")]
    public bool Stroke { get; init; }

    /// <summary>Gets a value indicating whether the markers are included in the bounding box.</summary>
    [IdlMember("markers", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGBoundingBoxOptions__markers")]
    public bool Markers { get; init; }

    /// <summary>
    /// Gets a value indicating whether the clipping applied to the element is included in the bounding box.
    /// </summary>
    [IdlMember("clipped", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGBoundingBoxOptions__clipped")]
    public bool Clipped { get; init; }
}

public partial class SvgGeometryElement
{
    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGeometryElement__isPointInFill: whether the point (in the user space of
    /// the element) is inside the fill of its geometry, with its <c>fill-rule</c> (the points of the boundary are inside). The
    /// fill paint, <c>visibility</c> and <c>pointer-events</c> have no effect.
    /// </summary>
    [IdlMember("isPointInFill", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGeometryElement__isPointInFill")]
    public bool IsPointInFill(DomPointInit? point = null)
    {
        var geometry = SvgShapeGeometry.Get(this);
        return !geometry.IsEmpty && geometry.IsPointInFill(new SvgPathData.SvgPoint(point?.X ?? 0, point?.Y ?? 0), SvgUserSpace.GetComputedValue(this, "fill-rule") == "evenodd");
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGeometryElement__isPointInStroke: whether the point (in the user space
    /// of the element) is inside the stroke shape of its geometry (the stroke width, the joins, the caps, the miter limit and the
    /// dashes; <c>vector-effect: non-scaling-stroke</c> applies the stroke width in the coordinates of the viewport). The stroke
    /// paint, <c>visibility</c> and <c>pointer-events</c> have no effect.
    /// </summary>
    [IdlMember("isPointInStroke", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGeometryElement__isPointInStroke")]
    public bool IsPointInStroke(DomPointInit? point = null)
    {
        var geometry = SvgShapeGeometry.Get(this);
        if (geometry.IsEmpty)
            return false;

        var stroke = SvgStrokeStyle.FromElement(this);
        var transform = stroke.NonScaling ? GetNonScalingStrokeTransform() : SvgAffine.Identity;
        return geometry.IsPointInStroke(new SvgPathData.SvgPoint(point?.X ?? 0, point?.Y ?? 0), stroke, transform);
    }

    /// <summary>
    /// The transform to the space where a non-scaling stroke has its width (https://svgwg.org/svg2-draft/coords.html#VectorEffects):
    /// the linear part of the transform to the viewport (or to the nearest viewport element when the element has no layout).
    /// </summary>
    internal SvgAffine GetNonScalingStrokeTransform()
    {
        var transform = SvgUserSpace.GetScreenCtm(this) ?? SvgUserSpace.GetCtm(this);
        return transform with { E = 0, F = 0 };
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGeometryElement__getTotalLength: the length of the path of the element
    /// (the equivalent path of a basic shape, https://svgwg.org/svg2-draft/shapes.html); pathLength does not change it.
    /// </summary>
    [IdlMember("getTotalLength", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGeometryElement__getTotalLength")]
    public float GetTotalLength() => ComputeTotalLength();

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGeometryElement__getPointAtLength: the point at a distance along the
    /// path of the element, clamped to [0, total length]. Throws an <c>InvalidStateError</c> when the path is empty.
    /// </summary>
    [IdlMember("getPointAtLength", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGGeometryElement__getPointAtLength")]
    public DomPoint GetPointAtLength(float distance) => ComputePointAtLength(distance);

    private protected virtual float ComputeTotalLength() => (float)SvgShapeGeometry.Get(this).PathGeometry.TotalLength;

    private protected virtual DomPoint ComputePointAtLength(float distance)
    {
        var geometry = SvgShapeGeometry.Get(this).PathGeometry;
        if (geometry.IsEmpty)
            throw DomException.InvalidState("The path is empty");

        var point = geometry.GetPointAtLength(distance);
        return new DomPoint((float)point.X, (float)point.Y);
    }
}

public partial class SvgPathElement
{
    private string? _pathDataSource;
    private List<SvgPathCommand> _pathData = [];
    private string? _computedPathDataSource;
    private List<SvgPathCommand> _computedPathData = [];
    private List<SvgPathCommand>? _computedPathDataAttribute;
    private List<SvgPathCommand>? _geometryPathData;
    private SvgPathGeometry? _geometry;

    /// <summary>The path data of the d attribute, up to the first error (not affected by the d property).</summary>
    private List<SvgPathCommand> PathData
    {
        get
        {
            var value = GetAttributeValue("d");
            if (!string.Equals(value, _pathDataSource, StringComparison.Ordinal))
            {
                _pathData = value is null ? [] : SvgPathData.Parse(value);
                _pathDataSource = value;
            }

            return _pathData;
        }
    }

    /// <summary>
    /// The path data that defines the geometry of the element: the computed value of the d property
    /// (https://svgwg.org/svg2-draft/paths.html#TheDProperty) when the element is connected, so the d property wins over the d
    /// attribute (its presentation attribute); the path data of the d attribute otherwise.
    /// </summary>
    private List<SvgPathCommand> UsedPathData
    {
        get
        {
            var pathData = PathData;
            if (!IsConnected)
                return pathData;

            var computed = NodeDocument.GetStyleEngine().GetStyle(this).GetComputedValue("d");
            if (!string.Equals(computed, _computedPathDataSource, StringComparison.Ordinal) || !ReferenceEquals(pathData, _computedPathDataAttribute))
            {
                // The path data of the attribute keeps its relative commands (for getPathSegmentAtLength())
                if (pathData.Count > 0 && string.Equals(computed, CssPath.Serialize(fillRule: null, SvgPathData.ToAbsolute(pathData)), StringComparison.Ordinal))
                {
                    _computedPathData = pathData;
                }
                else
                {
                    _computedPathData = CssParser.ParseListOfComponentValues(computed) is [CssFunction function] && CssPath.TryParse(function, out _, out var commands) ? commands : [];
                }

                _computedPathDataSource = computed;
                _computedPathDataAttribute = pathData;
            }

            return _computedPathData;
        }
    }

    /// <summary>The path data that defines the geometry of the element (see <see cref="UsedPathData"/>).</summary>
    internal List<SvgPathCommand> GetUsedPathData() => UsedPathData;

    private SvgPathGeometry Geometry
    {
        get
        {
            var pathData = UsedPathData;
            if (_geometry is null || !ReferenceEquals(pathData, _geometryPathData))
            {
                _geometry = new SvgPathGeometry(pathData);
                _geometryPathData = pathData;
            }

            return _geometry;
        }
    }

    /// <summary>
    /// https://svgwg.org/specs/paths/#InterfaceSVGPathData: the segments of the path data of the d attribute, up to the first
    /// error. When <see cref="SvgPathDataSettings.Normalize"/> is true, the segments are converted to the absolute M, L, C and Z
    /// commands (the arcs are approximated by cubic Béziers).
    /// </summary>
    [IdlMember("getPathData", "https://svgwg.org/specs/paths/#InterfaceSVGPathData")]
    public IReadOnlyList<SvgPathSegment> GetPathData(SvgPathDataSettings? settings = null)
    {
        var pathData = PathData;
        if (settings?.Normalize == true)
        {
            pathData = SvgPathData.Normalize(pathData);
        }

        var result = new SvgPathSegment[pathData.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = pathData[i].ToSegment();
        }

        return result;
    }

    /// <summary>
    /// https://svgwg.org/specs/paths/#InterfaceSVGPathData: sets the d attribute to the serialization of the segments, up to the
    /// first invalid segment (unknown type, wrong number of values, non-finite value, or a first segment that is not a moveto).
    /// The attribute is removed when there is no valid segment.
    /// </summary>
    [IdlMember("setPathData", "https://svgwg.org/specs/paths/#InterfaceSVGPathData")]
    public void SetPathData(IEnumerable<SvgPathSegment> pathData)
    {
        ArgumentNullException.ThrowIfNull(pathData);
        var commands = SvgPathData.FromSegments(pathData);
        if (commands.Count == 0)
        {
            RemoveAttributeByLocalName("d");
            return;
        }

        SetAttributeValue("d", SvgPathData.Serialize(commands));
    }

    /// <summary>
    /// https://svgwg.org/specs/paths/#InterfaceSVGPathElement: the segment of the path data at the distance along the path
    /// (clamped to [0, total length]), or null if the path is empty.
    /// </summary>
    [IdlMember("getPathSegmentAtLength", "https://svgwg.org/specs/paths/#InterfaceSVGPathElement")]
    public SvgPathSegment? GetPathSegmentAtLength(float distance)
    {
        var geometry = Geometry;
        return geometry.IsEmpty ? null : _geometryPathData![geometry.GetCommandIndexAtLength(distance)].ToSegment();
    }

    /// <summary>The length of the path (see <see cref="UsedPathData"/>); the pathLength attribute does not change it.</summary>
    private protected override float ComputeTotalLength() => (float)Geometry.TotalLength;

    /// <summary>The point at the distance along the path, clamped to [0, total length]. Throws an InvalidStateError if the path is empty.</summary>
    private protected override DomPoint ComputePointAtLength(float distance)
    {
        var geometry = Geometry;
        if (geometry.IsEmpty)
            throw DomException.InvalidState("The path is empty");

        var point = geometry.GetPointAtLength(distance);
        return new DomPoint((float)point.X, (float)point.Y);
    }
}

public partial class SvgSvgElement
{
    private SvgCurrentTranslate? _currentTranslate;
    private float _currentScale = 1;

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        var document = NodeDocument;
        document.HasSvgSvgElements = true;

        // https://svgwg.org/specs/animations/#TimingAttributes: the timeline of a document fragment inserted after the load event
        // of the document begins when it is inserted
        if (document.LoadEventFired && TimeContainer is null && TimelineBeginTime is null && document.BrowsingContext is not null)
        {
            TimelineBeginTime = document.RelevantWindow?.GetCurrentHighResolutionTime();
        }
    }

    /// <summary>The time of the clock of the document when the element was inserted after the load event of the document, or null.</summary>
    internal double? TimelineBeginTime { get; private set; }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);

        // The natural size of an embed or an object element is the one of the root svg element of the document it displays
        // (https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS, see ReplacedContent): the layout of the document of the
        // element changes with the sizing attributes of that root
        if (localName is "width" or "height" or "viewBox" && @namespace is null && ReferenceEquals(NodeDocument.DocumentElement, this)
            && NodeDocument.BrowsingContext?.Container is HtmlEmbedElement or HtmlObjectElement)
        {
            NodeDocument.BrowsingContext.Container.NodeDocument.InvalidateLayout();
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__currentScale</summary>
    [IdlMember("currentScale", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__currentScale")]
    public float CurrentScale
    {
        get => IsOutermost ? _currentScale : 1;
        set
        {
            if (IsOutermost)
            {
                _currentScale = value;
            }
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__currentTranslate</summary>
    [IdlMember("currentTranslate", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__currentTranslate")]
    public DomPoint CurrentTranslate => _currentTranslate ??= new SvgCurrentTranslate(this);

    // The outermost svg element of an SVG document fragment (https://svgwg.org/svg2-draft/struct.html#TermOutermostSVGElement)
    private bool IsOutermost => ParentNode is not SvgElement;

    /// <summary>
    /// https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__getIntersectionList: the graphics elements of the subtree
    /// of the element (or of <paramref name="referenceElement"/>) whose rendered bounding box, in the user space of this element,
    /// intersects the rectangle, in document order (as in the browsers, the bounding boxes are tested and the clipping is ignored).
    /// </summary>
    [IdlMember("getIntersectionList", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__getIntersectionList")]
    public NodeList GetIntersectionList(DomRectReadOnly rect, SvgElement? referenceElement)
    {
        ArgumentNullException.ThrowIfNull(rect);
        return new StaticNodeList(SvgIntersection.GetElements(this, rect, referenceElement, enclosure: false).Cast<Node>().ToList());
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__getEnclosureList: the graphics elements whose rendered
    /// bounding box is entirely inside the rectangle (see <see cref="GetIntersectionList"/>).
    /// </summary>
    [IdlMember("getEnclosureList", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__getEnclosureList")]
    public NodeList GetEnclosureList(DomRectReadOnly rect, SvgElement? referenceElement)
    {
        ArgumentNullException.ThrowIfNull(rect);
        return new StaticNodeList(SvgIntersection.GetElements(this, rect, referenceElement, enclosure: true).Cast<Node>().ToList());
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__checkIntersection: whether the rendered bounding box of the element intersects the rectangle.</summary>
    [IdlMember("checkIntersection", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__checkIntersection")]
    public bool CheckIntersection(SvgElement element, DomRectReadOnly rect)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(rect);
        return SvgIntersection.Check(this, element, rect, enclosure: false);
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__checkEnclosure: whether the rendered bounding box of the element is inside the rectangle.</summary>
    [IdlMember("checkEnclosure", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__checkEnclosure")]
    public bool CheckEnclosure(SvgElement element, DomRectReadOnly rect)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(rect);
        return SvgIntersection.Check(this, element, rect, enclosure: true);
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__deselectAll: clears the selection of the document.</summary>
    [IdlMember("deselectAll", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__deselectAll")]
    public void DeselectAll() => NodeDocument.GetSelection()?.RemoveAllRanges();

    /// <summary>Creates a number with the value 0.</summary>
    [IdlMember("createSVGNumber", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGNumber")]
    public SvgNumber CreateSvgNumber() => new();

    /// <summary>Creates a length with the value 0.</summary>
    [IdlMember("createSVGLength", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGLength")]
    public SvgLength CreateSvgLength() => new();

    /// <summary>Creates an angle with the value 0.</summary>
    [IdlMember("createSVGAngle", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGAngle")]
    public SvgAngle CreateSvgAngle() => new();

    /// <summary>Creates a point at the origin (0, 0).</summary>
    [IdlMember("createSVGPoint", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGPoint")]
    public DomPoint CreateSvgPoint() => new();

    /// <summary>Creates an identity matrix.</summary>
    [IdlMember("createSVGMatrix", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGMatrix")]
    public DomMatrix CreateSvgMatrix() => new();

    /// <summary>Creates a rectangle whose position and size are all zero.</summary>
    [IdlMember("createSVGRect", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGRect")]
    public DomRect CreateSvgRect() => new();

    /// <summary>Creates a transform of type <c>SVG_TRANSFORM_MATRIX</c> with the identity matrix.</summary>
    [IdlMember("createSVGTransform", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGTransform")]
    public SvgTransform CreateSvgTransform() => new();

    /// <summary>Creates a transform of type <c>SVG_TRANSFORM_MATRIX</c> from the given matrix.</summary>
    [IdlMember("createSVGTransformFromMatrix", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__createSVGTransformFromMatrix")]
    public SvgTransform CreateSvgTransformFromMatrix(DomMatrix2DInit? matrix = null)
    {
        var transform = new SvgTransform();
        transform.SetMatrix(matrix);
        return transform;
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__getElementById: the first descendant element with the id.</summary>
    [IdlMember("getElementById", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__getElementById")]
    public Element? GetElementById(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        for (var node = FirstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is Element element && element.IdValue == elementId)
                return element;
        }

        return null;
    }

    /// <summary>Suspends the redrawing of the element; as nothing is rendered, it only returns a handle.</summary>
    [IdlMember("suspendRedraw", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__suspendRedraw")]
    public uint SuspendRedraw(uint maxWaitMilliseconds)
    {
        _ = maxWaitMilliseconds;
        return 1;
    }

    /// <summary>
    /// Cancels a suspension returned by <c>suspendRedraw()</c>; it has no effect because nothing is rendered.
    /// </summary>
    [IdlMember("unsuspendRedraw", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__unsuspendRedraw")]
    public void UnsuspendRedraw(uint suspendHandleId) => _ = suspendHandleId;

    /// <summary>Cancels all the suspensions of redrawing; it has no effect because nothing is rendered.</summary>
    [IdlMember("unsuspendRedrawAll", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__unsuspendRedrawAll")]
    public void UnsuspendRedrawAll()
    {
    }

    /// <summary>Forces a redraw of the element; it has no effect because nothing is rendered.</summary>
    [IdlMember("forceRedraw", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__SVGSVGElement__forceRedraw")]
    public void ForceRedraw()
    {
    }

    /// <summary>The currentTranslate point: it is read only when the element is not the outermost svg element.</summary>
    private sealed class SvgCurrentTranslate(SvgSvgElement element) : DomPoint
    {
        public override double X
        {
            get => base.X;
            set
            {
                CheckWritable();
                base.X = value;
            }
        }

        public override double Y
        {
            get => base.Y;
            set
            {
                CheckWritable();
                base.Y = value;
            }
        }

        private void CheckWritable()
        {
            if (!element.IsOutermost)
                throw DomException.NoModificationAllowed("The currentTranslate of an svg element that is not the outermost one is read only");
        }
    }
}

public partial class SvgMarkerElement
{
    /// <summary>https://svgwg.org/svg2-draft/painting.html#__svg__SVGMarkerElement__orient</summary>
    [IdlMember("orient", "https://w3c.github.io/svgwg/svg2-draft/painting.html#__svg__SVGMarkerElement__orient")]
    public string Orient
    {
        get => GetAttributeValue("orient") ?? "";
        set => SetAttributeValue("orient", value);
    }

    /// <summary>Sets the <c>orient</c> attribute to <c>auto</c>.</summary>
    [IdlMember("setOrientToAuto", "https://w3c.github.io/svgwg/svg2-draft/painting.html#__svg__SVGMarkerElement__setOrientToAuto")]
    public void SetOrientToAuto() => SetAttributeValue("orient", "auto");

    /// <summary>Sets the <c>orient</c> attribute to the given angle.</summary>
    [IdlMember("setOrientToAngle", "https://w3c.github.io/svgwg/svg2-draft/painting.html#__svg__SVGMarkerElement__setOrientToAngle")]
    public void SetOrientToAngle(SvgAngle angle)
    {
        ArgumentNullException.ThrowIfNull(angle);
        SetAttributeValue("orient", angle.ValueAsString);
    }
}

public partial class SvgFEDropShadowElement
{
    /// <summary>
    /// Sets the <c>stdDeviation</c> attribute to the given horizontal and vertical standard deviations.
    /// </summary>
    [IdlMember("setStdDeviation", "https://drafts.fxtf.org/filter-effects-1/#dom-svgfedropshadowelement-setstddeviation")]
    public void SetStdDeviation(float stdDeviationX, float stdDeviationY) => SetAttributeValue("stdDeviation", SvgParsing.FormatNumber(stdDeviationX) + " " + SvgParsing.FormatNumber(stdDeviationY));
}

public partial class SvgFEGaussianBlurElement
{
    /// <summary>
    /// Sets the <c>stdDeviation</c> attribute to the given horizontal and vertical standard deviations.
    /// </summary>
    [IdlMember("setStdDeviation", "https://drafts.fxtf.org/filter-effects-1/#dom-svgfegaussianblurelement-setstddeviation")]
    public void SetStdDeviation(float stdDeviationX, float stdDeviationY) => SetAttributeValue("stdDeviation", SvgParsing.FormatNumber(stdDeviationX) + " " + SvgParsing.FormatNumber(stdDeviationY));
}

public partial class SvgImageElement
{
    private int _fetchGeneration;
    private Document? _documentWhoseLoadEventIsDelayed;

    /// <summary>The natural size of the image (https://svgwg.org/svg2-draft/embedded.html#ImageElement: the size of an auto width or height), or null before it is loaded.</summary>
    internal ImageNaturalSize? NaturalSize { get; private set; }

    /// <summary>The image painted by the element once it is loaded (see <see cref="Document.CreatePaintImage"/>), or null.</summary>
    internal Rendering.PaintImage? Image { get; private set; }

    /// <summary>
    /// Whether the image is broken (the fetch failed or the response is not an image): as for an <c>img</c> element, a canvas
    /// throws an <c>InvalidStateError</c> when it draws it (https://html.spec.whatwg.org/multipage/canvas.html#check-the-usability-of-the-image-argument).
    /// </summary>
    internal bool IsImageBroken { get; private set; }

    /// <summary>
    /// Returns the used size of the image from its width and height (null for auto): the concrete object size of the default
    /// sizing algorithm (https://drafts.csswg.org/css-images-3/#default-sizing) from the natural dimensions of the image and the
    /// default object size 300x150 (https://svgwg.org/svg2-draft/embedded.html#Placement), or null when an auto size is not
    /// known (the image is not loaded). The natural dimensions are the ones of the image without its EXIF orientation when
    /// <c>image-orientation</c> is <c>none</c> (<paramref name="fromImageOrientation"/> is false) and the image is CORS-same-origin.
    /// </summary>
    internal (double Width, double Height)? GetUsedSize(double? width, double? height, bool fromImageOrientation = true)
    {
        if (width is { } w && height is { } h)
            return (w, h);

        if (NaturalSize is not { } natural)
            return null;

        if (!fromImageOrientation && Image is { IsCorsSameOrigin: true })
        {
            natural = natural.WithoutOrientation();
        }

        var ratio = natural.AspectRatio ?? (natural is { Width: > 0, Height: > 0 } ? natural.Width / natural.Height : null);
        if (width is { } specifiedWidth)
            return (specifiedWidth, ratio is { } r and > 0 ? specifiedWidth / r : natural.Height ?? 150);

        if (height is { } specifiedHeight)
            return (ratio is { } r ? specifiedHeight * r : natural.Width ?? 300, specifiedHeight);

        if (natural.Width is { } naturalWidth && natural.Height is { } naturalHeight)
            return (naturalWidth, naturalHeight);

        if (ratio is { } aspectRatio and > 0)
        {
            if (natural.Width is { } onlyWidth)
                return (onlyWidth, onlyWidth / aspectRatio);

            if (natural.Height is { } onlyHeight)
                return (onlyHeight * aspectRatio, onlyHeight);

            // The contain fit of the ratio into the default object size
            return 300 / 150d > aspectRatio ? (150 * aspectRatio, 150) : (300, 300 / aspectRatio);
        }

        return (natural.Width ?? 300, natural.Height ?? 150);
    }

    /// <summary>https://svgwg.org/svg2-draft/embedded.html#__svg__SVGImageElement__crossOrigin</summary>
    [IdlMember("crossOrigin", "https://w3c.github.io/svgwg/svg2-draft/embedded.html#__svg__SVGImageElement__crossOrigin")]
    public string? CrossOrigin
    {
        get => Html.Reflection.GetNullableEnumerated(this, "crossorigin", Html.EnumeratedAttributes.Crossorigin);
        set => Html.Reflection.SetNullableString(this, "crossorigin", value);
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if ((localName == "href" && @namespace is null or Namespaces.XLink) || (localName == "crossorigin" && @namespace is null))
        {
            FetchImage();
        }
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();

        // An image whose href was set in a document without browsing context (e.g. a document of createDocument()) is fetched
        // when the element is inserted into a document that has one (e.g. after its adoption)
        if (Image is null && !IsImageBroken && _documentWhoseLoadEventIsDelayed is null && NodeDocument.BrowsingContext is not null
            && (GetAttributeValue("href") ?? GetAttributeValue(Namespaces.XLink, "href")) is { Length: > 0 })
        {
            FetchImage();
        }
    }

    /// <summary>Loads the image of an element of the document of an SVG image (once the document is known to be the document of an image).</summary>
    internal void LoadImageOfSvgImage() => FetchImage();

    /// <summary>
    /// Fetches the image referenced by <c>href</c> (or <c>xlink:href</c>) when it changes, as the <c>img</c> elements do
    /// (https://svgwg.org/svg2-draft/embedded.html#ImageElement): the <c>load</c> event is fired when the response is an
    /// image, the <c>error</c> event otherwise. The element delays the load event of its document while the image is fetched.
    /// </summary>
    private void FetchImage()
    {
        var generation = ++_fetchGeneration;
        IsImageBroken = false;
        StopDelayingTheLoadEvent();
        var document = NodeDocument;
        var href = GetAttributeValue("href") ?? GetAttributeValue(Namespaces.XLink, "href");
        if (href is not { Length: > 0 } || Html.Reflection.EncodingParseUrl(document, href) is not { } url)
        {
            // Without a valid URL, nothing is rendered
            if (Image is not null || NaturalSize is not null)
            {
                Image = null;
                NaturalSize = null;
                document.InvalidateLayout();
            }

            return;
        }

        // The document of an SVG image has no browsing context: only its data: URLs are loaded, synchronously
        // (https://svgwg.org/svg2-draft/conform.html#secure-animated-mode: no external resource)
        if (document.BrowsingContext is null)
        {
            if (document.SvgImage is not null && url.Scheme == "data" && Urls.DataUrl.Process(url) is var (mimeType, body))
            {
                OnFetched(DocumentResource.Create(body, mimeType));
            }

            return;
        }

        var request = SubresourceFetching.CreateRequest(document, url, RequestDestination.Image, cors: HasAttribute("crossorigin"), referrerPolicy: "", this, useCredentials: SubresourceFetching.UsesCredentials(this));
        if (!SubresourceFetching.Fetch(document, request, OnFetched))
            return;

        _documentWhoseLoadEventIsDelayed = document;
        document.ImagesDelayingTheLoadEvent++;

        void OnFetched(DocumentResource? resource)
        {
            if (generation != _fetchGeneration)
                return;

            StopDelayingTheLoadEvent();
            var bytes = resource is null || !resource.IsOkStatus ? (ReadOnlyMemory<byte>?)null : resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
            var naturalSize = default(ImageNaturalSize);
            var isImage = bytes is { } data && ImageHeaders.TryGetImageInfo(data.Span, resource!.MimeType?.Essence ?? "", out naturalSize);
            NaturalSize = isImage ? naturalSize : null;
            IsImageBroken = !isImage;

            // The image is CORS-same-origin (it does not taint the canvases) unless its response is opaque: a no-CORS request redirected
            // through another origin or to a cross-origin response (https://fetch.spec.whatwg.org/#concept-request-response-tainting)
            var corsSameOrigin = isImage && resource!.ResponseType is not (FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect);
            Image = isImage ? document.CreatePaintImage(url.Serialize(), naturalSize, bytes!.Value, resource!.MimeType?.Essence, corsSameOrigin) : null;
            if (isImage)
            {
                // https://w3c.github.io/paint-timing/#sec-modifications-HTML
                document.ProcessImageThatFinishedLoading(this, url.Serialize(), naturalSize, bytes!.Value.Length);
            }

            document.InvalidateLayout();
            FireEvent(new Event(isImage ? "load" : "error"));
        }
    }

    private void StopDelayingTheLoadEvent()
    {
        if (_documentWhoseLoadEventIsDelayed is not { } document)
            return;

        _documentWhoseLoadEventIsDelayed = null;
        document.ImagesDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }
}

public partial class SvgFEImageElement
{
    private int _fetchGeneration;
    private Document? _documentWhoseLoadEventIsDelayed;

    /// <summary>
    /// The image painted by the filter primitive when <c>href</c> references an image resource
    /// (https://drafts.fxtf.org/filter-effects/#feImageElement), once it is loaded, or null (also for the references to an
    /// element of the document and the <c>data:</c> URLs, which are resolved when the filter is built).
    /// </summary>
    internal Rendering.PaintImage? Image { get; private set; }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if ((localName == "href" && @namespace is null or Namespaces.XLink) || (localName == "crossorigin" && @namespace is null))
        {
            FetchImage();
        }
    }

    /// <summary>
    /// Fetches the image resource referenced by <c>href</c> (or <c>xlink:href</c>) when it changes, as the SVG <c>image</c>
    /// elements do (https://drafts.fxtf.org/filter-effects/#feImageElement: "the image resource is rendered according to the
    /// behavior of the image element"). The element delays the load event of its document while the image is fetched, and the
    /// filters that use it are painted again when it is loaded.
    /// </summary>
    private void FetchImage()
    {
        var generation = ++_fetchGeneration;
        StopDelayingTheLoadEvent();
        var document = NodeDocument;
        var href = GetAttributeValue("href") ?? GetAttributeValue(Namespaces.XLink, "href");
        if (Image is not null)
        {
            Image = null;
            document.InvalidateLayout();
        }

        // A reference to an element of the document and a data: URL need no fetch; the document of an SVG image has no browsing
        // context (https://svgwg.org/svg2-draft/conform.html#secure-animated-mode: no external resource)
        if (href is not { Length: > 0 } || href.StartsWith('#', StringComparison.Ordinal) || document.BrowsingContext is null
            || Html.Reflection.EncodingParseUrl(document, href) is not { } url || url.Scheme == "data")
        {
            return;
        }

        var request = SubresourceFetching.CreateRequest(document, url, RequestDestination.Image, cors: HasAttribute("crossorigin"), referrerPolicy: "", this, useCredentials: SubresourceFetching.UsesCredentials(this));
        if (!SubresourceFetching.Fetch(document, request, OnFetched))
            return;

        _documentWhoseLoadEventIsDelayed = document;
        document.ImagesDelayingTheLoadEvent++;

        void OnFetched(DocumentResource? resource)
        {
            if (generation != _fetchGeneration)
                return;

            StopDelayingTheLoadEvent();
            var bytes = resource is null || !resource.IsOkStatus ? (ReadOnlyMemory<byte>?)null : resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
            var naturalSize = default(ImageNaturalSize);
            if (bytes is not { } data || !ImageHeaders.TryGetImageInfo(data.Span, resource!.MimeType?.Essence ?? "", out naturalSize))
                return;

            var corsSameOrigin = resource.ResponseType is not (FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect);
            Image = document.CreatePaintImage(url.Serialize(), naturalSize, data, resource.MimeType?.Essence, corsSameOrigin);
            document.InvalidateLayout();
        }
    }

    private void StopDelayingTheLoadEvent()
    {
        if (_documentWhoseLoadEventIsDelayed is not { } document)
            return;

        _documentWhoseLoadEventIsDelayed = null;
        document.ImagesDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }
}

public partial class SvgUseElement
{
    /// <summary>The maximum depth of the resource documents that reference resource documents.</summary>
    private const int MaxResourceDepth = 4;

    private int _resourceGeneration;
    private string? _resourceUrl;
    private Document? _resourceDocument;
    private Document? _documentWhoseLoadEventIsDelayed;

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        UpdateExternalResource();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (localName == "href" && @namespace is null or Namespaces.XLink)
        {
            UpdateExternalResource();
        }
    }

    /// <summary>
    /// Returns the element referenced by the element (https://svgwg.org/svg2-draft/struct.html#UseElementHrefAttribute): an
    /// element of its tree for a same-document reference, else the element of the external resource document that its URL
    /// identifies once the document is loaded (its root element for a URL without fragment), or null.
    /// </summary>
    internal Element? GetReferencedElement()
    {
        var href = SvgLinking.GetHref(this);
        if (SvgLinking.IsSameDocumentReference(this, href))
            return SvgLinking.GetReferencedElement(this);

        if (_resourceDocument is not { } resource || href is null)
            return null;

        var hash = href.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0 || hash == href.Length - 1)
            return resource.DocumentElement as SvgElement;

        var id = href[(hash + 1)..];
        if (id.Contains('%', StringComparison.Ordinal))
        {
            id = System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(id));
        }

        return resource.GetElementById(id);
    }

    /// <summary>
    /// Fetches the external resource document referenced by <c>href</c> (https://svgwg.org/svg2-draft/linking.html#processingURL-fetch)
    /// when the element is connected and the reference is not a same-document reference: a same-origin request, whose response
    /// is parsed as an XML document without browsing context (no script runs; the external resources of its own use elements are
    /// fetched by the document of the element). The <c>load</c> event is fired when the document is parsed, the <c>error</c>
    /// event otherwise (also for a <c>data:</c> URL, which is not loaded, as in the browsers). The element delays the load event
    /// of its document while the resource is fetched.
    /// </summary>
    internal void UpdateExternalResource()
    {
        var document = NodeDocument;
        var host = document.BrowsingContext is not null ? document : document.SvgResourceHost;
        var href = SvgLinking.GetHref(this);
        Urls.UrlRecord? url = null;
        if (IsConnected && host is not null && document.SvgResourceDepth < MaxResourceDepth && href is { Length: > 0 } && !SvgLinking.IsSameDocumentReference(this, href))
        {
            url = Html.Reflection.EncodingParseUrl(document, href);
        }

        var resourceUrl = url?.Serialize();
        if (resourceUrl == _resourceUrl)
            return;

        var generation = ++_resourceGeneration;
        _resourceUrl = resourceUrl;
        StopDelayingTheLoadEvent();
        if (_resourceDocument is not null)
        {
            _resourceDocument = null;
            host?.InvalidateLayout();
        }

        if (url is null || host is null)
            return;

        if (url.Scheme == "data")
        {
            host.QueueElementTask(() =>
            {
                if (generation == _resourceGeneration)
                {
                    FireEvent(new Event("error"));
                }
            });
            return;
        }

        // The use elements of a document that reference the same resource share its fetch, and the ones that reference the same
        // element share its resource document
        var cache = host.SvgResourceDocuments ??= new SvgResourceDocumentCache();
        var depth = document.SvgResourceDepth + 1;
        _documentWhoseLoadEventIsDelayed = host;
        host.ImagesDelayingTheLoadEvent++;
        if (!cache.Load(host, this, url, depth, OnLoaded))
        {
            StopDelayingTheLoadEvent();
        }

        void OnLoaded(Document? parsed)
        {
            if (generation != _resourceGeneration)
                return;

            _resourceDocument = parsed;
            StopDelayingTheLoadEvent();
            if (parsed is null)
            {
                FireEvent(new Event("error"));
                return;
            }

            host.InvalidateLayout();
            FireEvent(new Event("load"));
        }
    }

    private void StopDelayingTheLoadEvent()
    {
        if (_documentWhoseLoadEventIsDelayed is not { } document)
            return;

        _documentWhoseLoadEventIsDelayed = null;
        document.ImagesDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }
}

/// <summary>
/// The external resource documents of a document (https://svgwg.org/svg2-draft/linking.html#processingURL-fetch): the
/// documents referenced by its use elements, and by the properties of its rendered elements that reference an element of
/// another document (<c>fill: url(other.svg#gradient)</c>, see <see cref="LoadForProperty"/>). A resource is fetched once per
/// URL without fragment (and per request of a property: its request URL modifiers). The resource of a use element is parsed
/// once per URL (the URL of a resource document has the fragment of the reference, so that the referenced element matches
/// <c>:target</c>); the resource of a property is parsed once.
/// </summary>
internal sealed class SvgResourceDocumentCache
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public bool IsComplete;
        public string? Text;
        public string ContentType = "";
        public List<Action>? Waiters;
        public Dictionary<string, Document?> Documents { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Loads the resource document of a URL for a use element: <paramref name="onComplete"/> is called from a task with the
    /// document, or null when the resource cannot be fetched or is not an XML document. Returns false when nothing is fetched (the
    /// callback is not called).
    /// </summary>
    public bool Load(Document host, SvgUseElement initiator, Urls.UrlRecord url, int depth, Action<Document?> onComplete)
    {
        var key = url.Serialize(excludeFragment: true);
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.IsComplete)
            {
                host.QueueElementTask(Complete);
            }
            else
            {
                (entry.Waiters ??= []).Add(Complete);
            }

            return true;
        }

        entry = new Entry();
        var request = SubresourceFetching.CreateRequest(host, url, RequestDestination.Image, cors: true, referrerPolicy: "", initiator);
        request.Mode = RequestMode.SameOrigin;
        if (!SubresourceFetching.Fetch(host, request, resource => OnFetched(entry, resource), initiatorType: "use"))
            return false;

        _entries[key] = entry;
        (entry.Waiters ??= []).Add(Complete);
        return true;

        void Complete() => onComplete(GetDocument(host, entry, url.Serialize(), depth));
    }

    /// <summary>
    /// Fetches the resource document of a URL referenced by a property of a rendered element (e.g. <c>fill</c>, <c>filter</c>,
    /// <c>clip-path</c>), unless it is already fetched or being fetched: a CORS request ("for a reference from a style property
    /// or presentation attribute, the anonymous state"), with the request URL modifiers of the URL
    /// (https://drafts.csswg.org/css-values-5/#request-url-modifiers). <paramref name="onComplete"/> is called from a task when
    /// the fetch started by this call completes. Returns false when no fetch is started.
    /// </summary>
    public bool LoadForProperty(Document host, Element initiator, Urls.UrlRecord url, Css.Values.CssRequestModifiers modifiers, Action onComplete)
    {
        var key = GetPropertyKey(url, modifiers);
        if (_entries.ContainsKey(key))
            return false;

        var entry = new Entry();
        var request = SubresourceFetching.CreateRequest(host, url, RequestDestination.Image, cors: true, modifiers.ReferrerPolicy, initiator, useCredentials: modifiers.UsesCredentials, integrity: modifiers.IntegrityMetadata);
        if (!SubresourceFetching.Fetch(host, request, resource => OnFetched(entry, resource), initiatorType: "css"))
            return false;

        _entries[key] = entry;
        (entry.Waiters ??= []).Add(onComplete);
        return true;
    }

    /// <summary>
    /// Records the response of the request of a mask image as the resource document of its URL: a <c>mask-image</c> whose URL
    /// has a fragment references a mask element of the document when the fragment identifies one
    /// (https://drafts.fxtf.org/css-masking/#the-mask-image), else the URL is the URL of an image.
    /// </summary>
    public void AddFetchedForProperty(Urls.UrlRecord url, Css.Values.CssRequestModifiers modifiers, DocumentResource? resource)
    {
        var key = GetPropertyKey(url, modifiers);
        if (_entries.ContainsKey(key))
            return;

        var entry = new Entry();
        _entries[key] = entry;
        OnFetched(entry, resource);
    }

    /// <summary>
    /// Returns the resource document of a URL referenced by a property (see <see cref="LoadForProperty"/>), or null: the
    /// resource is not fetched yet, cannot be fetched (a network error, a response that the CORS check or the integrity
    /// metadata rejects) or is not an XML document.
    /// </summary>
    public Document? GetForProperty(Document host, Urls.UrlRecord url, Css.Values.CssRequestModifiers modifiers)
        => _entries.TryGetValue(GetPropertyKey(url, modifiers), out var entry) && entry.IsComplete ? GetDocument(host, entry, url.Serialize(excludeFragment: true), depth: 1) : null;

    private static string GetPropertyKey(Urls.UrlRecord url, Css.Values.CssRequestModifiers modifiers)
        => string.Concat("css ", modifiers.CrossOrigin, " ", modifiers.Integrity, " ", modifiers.ReferrerPolicy, "\n", url.Serialize(excludeFragment: true));

    private static void OnFetched(Entry entry, DocumentResource? resource)
    {
        if (resource is { IsOkStatus: true, MimeType.IsXml: true })
        {
            var text = resource.Content ?? (resource.ContentBytes is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes.Span) : "");
            entry.Text = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
            entry.ContentType = resource.MimeType.Essence;
        }

        entry.IsComplete = true;
        var waiters = entry.Waiters;
        entry.Waiters = null;
        if (waiters is not null)
        {
            foreach (var waiter in waiters)
            {
                waiter();
            }
        }
    }

    /// <summary>Returns the resource document of an entry for a URL of the document (parsed on first use), or null.</summary>
    private static Document? GetDocument(Document host, Entry entry, string documentUrl, int depth)
    {
        if (!entry.Documents.TryGetValue(documentUrl, out var document))
        {
            document = entry.Text is null ? null : Parse(entry.Text, documentUrl, entry.ContentType);
            entry.Documents[documentUrl] = document;
            if (document is not null)
            {
                // The external resources of the use elements of the resource document are fetched by the host document
                document.SvgResourceHost = host;
                document.SvgResourceDepth = depth;
                foreach (var use in document.Descendants().OfType<SvgUseElement>().ToList())
                {
                    use.UpdateExternalResource();
                }
            }
        }

        return document;
    }

    private static Document? Parse(string text, string url, string contentType)
    {
        try
        {
            var document = XmlDocumentParser.ParseDocument(text, new XmlParserOptions { Url = url, ContentType = contentType });
            return document.DocumentElement is null ? null : document;
        }
#pragma warning disable CA1031 // A document that cannot be parsed is not a resource document
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}

public partial class SvgScriptElement
{
    /// <summary>https://svgwg.org/svg2-draft/interact.html#__svg__SVGScriptElement__crossOrigin</summary>
    [IdlMember("crossOrigin", "https://w3c.github.io/svgwg/svg2-draft/interact.html#__svg__SVGScriptElement__crossOrigin")]
    public string? CrossOrigin
    {
        get => Html.Reflection.GetNullableEnumerated(this, "crossorigin", Html.EnumeratedAttributes.Crossorigin);
        set => Html.Reflection.SetNullableString(this, "crossorigin", value);
    }

    private bool _alreadyProcessed;

    /// <summary>
    /// Whether the element was inserted by a parser, which processes it when it reaches its end tag (set by the HTML and XML
    /// parsers, as the parser document of the HTML script elements).
    /// </summary>
    internal bool IsParserInserted { get; set; }

    /// <summary>
    /// The script text (https://w3c.github.io/trusted-types/dist/spec/#svgscriptelement-script-text): the child text content
    /// set by the parser. When it differs from the child text content, the default policy is applied to the child text
    /// content when the element is processed.
    /// </summary>
    internal string ScriptText { get; set; } = "";

    /// <summary>Marks the element as already processed: it never runs (e.g. the scripts parsed by <c>innerHTML</c>).</summary>
    internal void MarkAsAlreadyProcessed() => _alreadyProcessed = true;

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);

        // As for the src attribute of the HTML script elements, setting href processes a connected script
        if (localName == "href" && @namespace is null or Namespaces.XLink && value is not null && IsConnected && !IsParserInserted)
        {
            Process();
        }
    }

    internal override void CloningSteps(Node copy, bool subtree)
    {
        base.CloningSteps(copy, subtree);
        ((SvgScriptElement)copy)._alreadyProcessed = _alreadyProcessed;
    }

    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        if (!IsParserInserted)
        {
            Process();
        }
    }

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);
        if (childrenInserted && IsConnected && !IsParserInserted)
        {
            Process();
        }
    }

    /// <summary>
    /// Processes the SVG script element (https://svgwg.org/svg2-draft/interact.html#ScriptElement), when the parser reaches
    /// its end tag (https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign) or, as in browsers, as the
    /// HTML script elements are prepared (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element)
    /// when a script that is not parser-inserted becomes connected, a child is inserted or <c>href</c> is set: the inline
    /// script, or the script referenced by <c>href</c>, runs when the browsing context of the document has a script engine.
    /// The <c>type</c> attribute must be a JavaScript MIME type (the <c>language</c> attribute is ignored, as in browsers).
    /// </summary>
    /// <param name="pendingScripts">
    /// The list of the scripts that wait for the script that wrote this element (with <c>document.write()</c>) to return, or
    /// null: an external script whose fetch completed synchronously is added to it instead of running.
    /// </param>
    internal void Process(List<Action>? pendingScripts = null)
    {
        if (_alreadyProcessed)
            return;

        var parserInserted = IsParserInserted;
        IsParserInserted = false;

        // https://w3c.github.io/trusted-types/dist/spec/#slot-value-verification
        if (!PrepareScriptText())
            return;

        var href = GetAttributeValue("href") ?? GetAttributeValue(Namespaces.XLink, "href");
        var sourceText = ScriptText;
        if ((href is null && sourceText.Length == 0) || !IsConnected)
            return;

        var type = GetAttributeValue("type");
        if (type is { Length: > 0 } && !HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(Infra.InfraAscii.StripLeadingAndTrailingAsciiWhitespace(type).ToString()))
            return;

        _alreadyProcessed = true;
        var document = NodeDocument;
        if (document.BrowsingContext is not { Options: { ScriptEngine: not null, ScriptingEnabled: true } } browsingContext || !document.ScriptingEnabled || document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document))
            return;

        if (href is null)
        {
            // https://w3c.github.io/webappsec-csp/#should-block-inline
            if (ContentSecurityPolicy.ShouldBlockInline(this, ContentSecurityPolicyInlineType.Script, sourceText, parserInserted))
                return;

            var position = (RareDataOrNull as ElementRareData)?.InlineSourcePosition ?? (1, 0);
            Run(new Scripting.ClassicScript(window, sourceText, document.BaseUri) { Url = document.Url, Element = this, SourceTextLine = position.Line, SourceTextColumn = position.Column });
            return;
        }

        // As for the src attribute of the HTML script elements, an empty or invalid href fires an error event from a task (an
        // empty href does not fetch the document itself)
        var url = href.Length == 0 ? null : Html.Reflection.EncodingParseAndSerializeUrl(document, href);
        if (url is null)
        {
            document.QueueElementTask(() => FireEvent(new Event("error")));
            return;
        }

        // The external script delays the load event of the document until it runs
        document.ScriptsDelayingTheLoadEvent++;
        if (Scripting.ScriptFetching.FetchClassicScript(this, browsingContext, window, url, OnFetched, out var result))
        {
            // As the HTML script elements that are not parser-inserted, a script inserted by a script runs from a task
            if (parserInserted && pendingScripts is not null)
            {
                pendingScripts.Add(() => OnFetched(result));
            }
            else if (parserInserted)
            {
                OnFetched(result);
            }
            else
            {
                browsingContext.EventLoop.QueueTask(() => OnFetched(result));
            }
        }

        void OnFetched(Scripting.ClassicScript? script)
        {
            try
            {
                if (script is null)
                {
                    FireEvent(new Event("error"));
                    return;
                }

                Run(script);
                FireEvent(new Event("load"));
            }
            finally
            {
                document.ScriptsDelayingTheLoadEvent--;
                document.TryQueueLoadEvent();
            }
        }
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#prepare-the-script-text</summary>
    private bool PrepareScriptText()
    {
        var childTextContent = GetChildTextContent();
        if (ScriptText == childTextContent)
            return true;

        try
        {
            ScriptText = TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, NodeDocument, childTextContent, "SVGScriptElement text");
            return true;
        }
#pragma warning disable CA1031 // The exceptions of the default policy are reported and the script does not run
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (ex is not TypeErrorException)
            {
                Scripting.ScriptErrors.Report(ex);
            }

            return false;
        }
    }

    private void Run(Scripting.ClassicScript script)
    {
        var document = NodeDocument;
        if (document.BrowsingContext?.Options.ScriptEngine is not { } engine)
            return;

        var oldCurrentScript = document.CurrentScript;
        document.CurrentScript = GetRoot() is ShadowRoot ? null : this;
        try
        {
            Scripting.ScriptExecution.RunClassicScript(engine, script);
        }
        finally
        {
            document.CurrentScript = oldCurrentScript;
        }
    }
}

public partial class SvgStyleElement
{
    /// <summary>https://svgwg.org/svg2-draft/styling.html#__svg__SVGStyleElement__disabled: the disabled flag of the associated style sheet.</summary>
    [IdlMember("disabled", "https://w3c.github.io/svgwg/svg2-draft/styling.html#__svg__SVGStyleElement__disabled")]
    public bool Disabled
    {
        get => GetLinkStyleSheet()?.Disabled ?? false;
        set
        {
            if (GetLinkStyleSheet() is { } sheet)
            {
                sheet.Disabled = value;
            }
        }
    }
}

public partial class SvgSvgElement
{
    /// <summary>The time container of the document fragment, when the element is an outermost svg element (see <see cref="Svg.Animations.SmilTimeContainer"/>).</summary>
    internal Svg.Animations.SmilTimeContainer? TimeContainer { get; set; }

    // The document timeline of the SVG animations (https://svgwg.org/specs/animations/#TimingAttributes): the time container
    // of the outermost svg element of the document fragment
    private Svg.Animations.SmilTimeContainer GetTimeContainer() => NodeDocument.Smil.GetContainer(SvgUserSpace.GetOutermostSvgElement(this) ?? this);

    /// <summary>Pauses the animations of the document fragment: its timeline stops.</summary>
    [IdlMember("pauseAnimations", "https://svgwg.org/specs/animations/#__svg__SVGSVGElement__pauseAnimations")]
    public void PauseAnimations()
    {
        var container = GetTimeContainer();
        NodeDocument.Smil.Flush();
        container.Pause();
    }

    /// <summary>Resumes the animations of the document fragment.</summary>
    [IdlMember("unpauseAnimations", "https://svgwg.org/specs/animations/#__svg__SVGSVGElement__unpauseAnimations")]
    public void UnpauseAnimations()
    {
        GetTimeContainer().Unpause();
        NodeDocument.Smil.SetDirty();
    }

    /// <summary>Returns whether the animations are paused.</summary>
    [IdlMember("animationsPaused", "https://svgwg.org/specs/animations/#__svg__SVGSVGElement__animationsPaused")]
    public bool AnimationsPaused() => (SvgUserSpace.GetOutermostSvgElement(this) ?? this).TimeContainer is { IsPaused: true };

    /// <summary>Returns the current time, in seconds, of the timeline of the document fragment.</summary>
    [IdlMember("getCurrentTime", "https://svgwg.org/specs/animations/#__svg__SVGSVGElement__getCurrentTime")]
    public float GetCurrentTime() => (float)GetTimeContainer().Elapsed.Seconds;

    /// <summary>
    /// Sets the current time, in seconds, of the timeline of the document fragment: the animations are sampled at once. Before
    /// the timeline begins (the load event of the document), the time is the time it begins at.
    /// </summary>
    [IdlMember("setCurrentTime", "https://svgwg.org/specs/animations/#__svg__SVGSVGElement__setCurrentTime")]
    public void SetCurrentTime(float seconds)
    {
        if (!float.IsFinite(seconds))
            return;

        var smil = NodeDocument.Smil;
        var container = GetTimeContainer();
        smil.PrepareSeek();
        container.SetElapsed(Svg.Animations.SmilTime.FromSeconds(Math.Max(seconds, 0)));
        smil.OnSeek();
    }
}

public partial class SvgTextContentElement
{
    /// <summary>
    /// https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getNumberOfChars: the number of addressable
    /// characters of the element (UTF-16 code units after the white space processing, see <see cref="SvgTextLayout"/>); 0 when
    /// the element is not rendered.
    /// </summary>
    [IdlMember("getNumberOfChars", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getNumberOfChars")]
    public int GetNumberOfChars() => GetCharacters().Length;

    /// <summary>https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getComputedTextLength: the sum of the advances of the characters (after textLength and lengthAdjust).</summary>
    [IdlMember("getComputedTextLength", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getComputedTextLength")]
    public float GetComputedTextLength()
    {
        var length = 0d;
        foreach (var character in GetCharacters())
        {
            length += character.Advance;
        }

        return (float)length;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getSubStringLength: the sum of the advances of the
    /// characters from <paramref name="charnum"/> (at most <paramref name="nchars"/>). Throws an <c>IndexSizeError</c> when
    /// <paramref name="charnum"/> is not the index of a character.
    /// </summary>
    [IdlMember("getSubStringLength", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getSubStringLength")]
    public float GetSubStringLength(uint charnum, uint nchars)
    {
        var characters = GetCharacters();
        CheckIndex(characters, charnum);
        var end = (int)Math.Min((long)charnum + nchars, characters.Length);
        var length = 0d;
        for (var i = (int)charnum; i < end; i++)
        {
            length += characters[i].Advance;
        }

        return (float)length;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getStartPositionOfChar: the position of the
    /// character on its baseline (the alignment point, not moved by dominant-baseline), in the user space of the text element.
    /// </summary>
    [IdlMember("getStartPositionOfChar", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getStartPositionOfChar")]
    public DomPoint GetStartPositionOfChar(uint charnum)
    {
        var characters = GetCharacters();
        CheckIndex(characters, charnum);
        var position = characters[(int)charnum].Position;
        return new DomPoint((float)position.X, (float)position.Y);
    }

    /// <summary>https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getEndPositionOfChar: the position of the character moved by its advance along its progression.</summary>
    [IdlMember("getEndPositionOfChar", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getEndPositionOfChar")]
    public DomPoint GetEndPositionOfChar(uint charnum)
    {
        var characters = GetCharacters();
        CheckIndex(characters, charnum);
        var position = characters[(int)charnum].EndPosition;
        return new DomPoint((float)position.X, (float)position.Y);
    }

    /// <summary>https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getExtentOfChar: the bounding box of the glyph cell of the character.</summary>
    [IdlMember("getExtentOfChar", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getExtentOfChar")]
    public DomRect GetExtentOfChar(uint charnum)
    {
        var characters = GetCharacters();
        CheckIndex(characters, charnum);
        var (p1, p2, p3, p4) = characters[(int)charnum].GetCell();
        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new DomRect((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getRotationOfChar: the rotation of the character in
    /// degrees: its rotate attribute, the tangent of its text path, and the rotation of a sideways glyph of vertical text.
    /// </summary>
    [IdlMember("getRotationOfChar", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getRotationOfChar")]
    public float GetRotationOfChar(uint charnum)
    {
        var characters = GetCharacters();
        CheckIndex(characters, charnum);
        return (float)characters[(int)charnum].Rotation;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__getCharNumAtPosition: the index of the character
    /// whose glyph cell contains the point (in the user space of the text element; the last one when they overlap), or -1.
    /// </summary>
    [IdlMember("getCharNumAtPosition", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__getCharNumAtPosition")]
    public int GetCharNumAtPosition(DomPointInit? point = null)
    {
        var characters = GetCharacters();
        var target = new SvgPathData.SvgPoint(point?.X ?? 0, point?.Y ?? 0);
        for (var i = characters.Length - 1; i >= 0; i--)
        {
            ref readonly var character = ref characters[i];
            if (character.IsClusterStart && !character.IsHidden && character.Contains(target))
                return i;
        }

        return -1;
    }

    /// <summary>https://svgwg.org/svg2-draft/text.html#__svg__SVGTextContentElement__selectSubString (deprecated): selects the characters (the range of their DOM positions) in the selection of the document.</summary>
    [IdlMember("selectSubString", "https://w3c.github.io/svgwg/svg2-draft/text.html#__svg__SVGTextContentElement__selectSubString")]
    public void SelectSubString(uint charnum, uint nchars)
    {
        var characters = GetCharacters();
        CheckIndex(characters, charnum);
        if (NodeDocument.GetSelection() is not { } selection)
            return;

        // The characters are mapped to their positions in the text nodes
        var last = characters[(int)Math.Min((long)charnum + Math.Max(nchars, 1) - 1, characters.Length - 1)];
        var first = characters[(int)charnum];
        var range = new Range(first.TextNode, (uint)first.TextOffset, last.TextNode, (uint)(last.TextOffset + (nchars == 0 ? 0 : 1)));
        selection.RemoveAllRanges();
        selection.AddRange(range);
    }

    /// <summary>Returns the characters of the element in the layout of its text element (empty when it is not rendered).</summary>
    private ReadOnlySpan<SvgTextCharacter> GetCharacters()
    {
        if (SvgTextLayout.Get(this) is not { } layout || !layout.TryGetRange(this, out var start, out var count))
            return default;

        return layout.Characters.AsSpan(start, count);
    }

    private static void CheckIndex(ReadOnlySpan<SvgTextCharacter> characters, uint charnum)
    {
        if (charnum >= characters.Length)
            throw DomException.IndexSize("The index is not the index of a character of the element");
    }
}

public partial class SvgTextElement
{
    /// <summary>The last layout of the text (see <see cref="SvgTextLayout.Get"/>), reused until the styles change.</summary>
    internal SvgTextLayout? CachedTextLayout { get; set; }
}
