using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The captures of a view transition (https://drafts.csswg.org/css-view-transitions-1/#algorithms): the named elements of the
/// old and of the new state in paint order, their geometry in the snapshot containing block, the images of the old elements,
/// and the dynamic view transition style sheet that sizes, places and animates the groups.
/// </summary>
internal static class ViewTransitionCapture
{
    /// <summary>The largest side of the bitmap of a snapshot, in device pixels: the ink overflow beyond it is not captured.</summary>
    private const double MaxSnapshotSide = 4096;

    /// <summary>The largest scale of the transform of an element its snapshot is rasterized at.</summary>
    private const double MaxRasterScale = 8;

    public static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static double[] Translation(double x, double y) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, 0, 1];

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-1/#snapshot-containing-block-size: the snapshot containing block covers the
    /// viewport with its scrollbar gutters (there is no user agent interface that shows and hides in the viewport).
    /// </summary>
    public static PhysicalSize GetSnapshotContainingBlockSize(Document document)
    {
        var environment = document.GetStyleEngine().Environment;
        return new PhysicalSize(environment.Width, environment.Height);
    }

    /// <summary>A named element of the document: its view transition name and its box fragments.</summary>
    private readonly record struct NamedElement(Element Element, string? PseudoElement, string Name, IReadOnlyList<BoxFragment> Fragments, ComputedStyle Style);

    /// <summary>
    /// Returns whether an element can be the root element of a scoped view transition
    /// (https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition): it is rendered in the document, and its
    /// principal box is not "an internal table box other than table-cell, [...] an internal ruby box or a non-atomic inline-level
    /// box" ("this ensures that this has a principal box and qualifies for layout containment").
    /// </summary>
    public static bool CanHostScopedViewTransition(Document document, Element root)
    {
        if (!root.IsConnected || !ReferenceEquals(root.NodeDocument, document) || document.BrowsingContext is null)
            return false;

        if (!BoxGeneration.Get(root).IsBeingRendered)
            return false;

        var style = document.GetStyleEngine().GetStyle(root);
        var display = style.Layout.Display;
        if (display.Internal is not (DisplayInternal.None or DisplayInternal.TableCell or DisplayInternal.TableCaption) || display.Inside == DisplayInside.Ruby)
            return false;

        return !display.IsInlineBox || BoxGeneration.IsReplaced(root, style);
    }

    /// <summary>
    /// Returns whether an element is not discovered by a transition because of view transition scoping
    /// (https://drafts.csswg.org/css-view-transitions-2/#view-transition-scope-prop): "any flat tree inclusive ancestor of this
    /// element, up to but not including transition's root element, has view-transition-scope: all" (the root element of an
    /// active scoped view transition has it too), or the element is not a flat tree inclusive descendant of the root element
    /// of a scoped view transition.
    /// </summary>
    private static bool IsOutOfScope(ViewTransition transition, StyleEngine styleEngine, Element element)
    {
        var root = transition.RootElement;
        for (var current = element; current is not null; current = StyleEngine.GetInheritanceParent(current))
        {
            if (ReferenceEquals(current, root))
                return false;

            if (styleEngine.GetStyle(current).GetComputedValue("view-transition-scope") == "all")
                return true;
        }

        // The root element is not an ancestor of the element
        return transition.IsScoped;
    }

    /// <summary>
    /// Returns the elements that have a view transition name and are rendered, in paint order
    /// (https://drafts.csswg.org/css-view-transitions-1/#capture-the-old-state: the elements in the skipped contents of an
    /// ancestor and the fragmented elements are ignored, and so are the elements that view transition scoping hides from the
    /// transition). Returns null when two elements have the same name.
    /// </summary>
    private static List<NamedElement>? GetNamedElements(ViewTransition transition, LayoutEngine engine)
    {
        var document = transition.Document;
        var result = new List<NamedElement>();
        if (engine.EnsureLayout() is null)
            return result;

        var styleEngine = document.GetStyleEngine();
        var seen = new HashSet<(Element, string?)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in engine.GetPaintOrder())
        {
            if (item.Fragment is not BoxFragment { Box: { Element: { } element, IsAnonymous: false } box } || box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid)
                continue;

            // The elements and the pseudo-elements that generate boxes (e.g. ::backdrop) have a name; the pseudo-elements of a view
            // transition and the elements of the other documents have none
            var pseudoElement = box.PseudoElement;
            if (ViewTransitionPseudoTree.IsViewTransitionPseudoElement(pseudoElement) || !ReferenceEquals(element.NodeDocument, document) || !seen.Add((element, pseudoElement)))
                continue;

            var style = box.Style;
            if (GetViewTransitionName(element, style) is not { } name)
                continue;

            if (IsOutOfScope(transition, styleEngine, element))
                continue;

            if (ContentRelevancy.IsInSkippedContents(element))
                continue;

            var fragments = engine.GetFragments(element, pseudoElement);
            if (fragments.Count == 0 || (fragments.Count > 1 && !fragments[0].Box.IsInlineBox))
                continue;

            if (!names.Add(name))
                return null;

            result.Add(new NamedElement(element, pseudoElement, name, fragments, style));
        }

        return result;
    }

    /// <summary>
    /// Returns the view transition name of an element (https://drafts.csswg.org/css-view-transitions-2/#document-scoped-view-transition-name),
    /// as the argument of the pseudo-elements names it (the serialization of the identifier), or null for <c>none</c>. The
    /// names of <c>auto</c> and <c>match-element</c> are generated: they cannot be selected by name.
    /// </summary>
    internal static string? GetViewTransitionName(Element element, ComputedStyle style)
    {
        var value = style.GetComputedValue("view-transition-name");
        switch (value)
        {
            case "none" or "":
                return null;

            case "match-element":
                return ViewTransitionGeneratedNames.Get(element);

            case "auto":
                // "If computed is auto, element has an associated id [...] return a unique string starting with -ua-. Two elements
                // with the same id must return the same string"
                return element.GetAttributeValue("id") is { Length: > 0 } id ? "-ua-id-" + CssSerializer.SerializeIdentifier(id) : ViewTransitionGeneratedNames.Get(element);

            default:
                return value;
        }
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#capture-the-old-state. Returns false on failure.</summary>
    public static bool CaptureOldState(ViewTransition transition)
    {
        var document = transition.Document;
        var engine = document.GetLayoutEngine();
        transition.InitialSnapshotContainingBlockSize = GetSnapshotContainingBlockSize(document);
        if (GetNamedElements(transition, engine) is not { } elements)
            return false;

        // "If any other active view transition contains element in its captured elements, then for each such active view
        // transition, in tree order of their corresponding root elements, skip that view transition with an "AbortError""
        if (document.HasScopedViewTransitions && SkipTransitionsThatCapture(transition, elements))
        {
            // The styles and the boxes changed: the pseudo-elements of the skipped transitions were removed
            if (GetNamedElements(transition, engine) is not { } remaining)
                return false;

            elements = remaining;
        }

        if (engine.EnsureLayout() is not { } root)
            return true;

        // "Set element's captured in a view transition to true": the captured descendants of an element are not painted in its image
        foreach (var named in elements)
        {
            document.SetCapturedInViewTransition(named.Element, named.PseudoElement, true);
        }

        // The painting order of the document, shared by the images of the elements
        var order = new List<PaintItem>();
        PaintOrder.Collect(root, order, markers: true);
        try
        {
            foreach (var named in elements)
            {
                var geometry = GetGeometry(transition, engine, named);
                var style = named.Style;
                var captured = new ViewTransitionCapturedElement
                {
                    Name = named.Name,
                    OldImage = CaptureImage(document, engine, root, geometry, order),
                    HasOldImage = true,
                    OldWidth = geometry.Size.Width,
                    OldHeight = geometry.Size.Height,
                    OldTransform = geometry.Transform,
                    OldBorderWidth = geometry.Border,
                    OldBorderShape = GetBorderShape(style),
                    OldWritingMode = style.GetComputedValue("writing-mode"),
                    OldDirection = style.GetComputedValue("direction"),
                    OldTextOrientation = style.GetComputedValue("text-orientation"),
                    OldMixBlendMode = style.GetComputedValue("mix-blend-mode"),
                    OldBackdropFilter = style.GetComputedValue("backdrop-filter"),
                    OldColorScheme = style.GetComputedValue("color-scheme"),
                    ClassList = GetClassList(style),
                    ContainingGroupName = ResolveUsedGroupName(transition, named.Element, named.PseudoElement, style),
                };
                transition.NamedElements.Add(captured);
            }
        }
        finally
        {
            foreach (var named in elements)
            {
                document.SetCapturedInViewTransition(named.Element, named.PseudoElement, false);
            }
        }

        return true;
    }

    /// <summary>https://drafts.csswg.org/css-view-transitions-1/#capture-the-new-state. Returns false on failure.</summary>
    public static bool CaptureNewState(ViewTransition transition)
    {
        var document = transition.Document;
        var engine = document.GetLayoutEngine();
        if (GetNamedElements(transition, engine) is not { } elements)
            return false;

        foreach (var named in elements)
        {
            document.SetCapturedInViewTransition(named.Element, named.PseudoElement, true);
        }

        try
        {
            foreach (var named in elements)
            {
                var captured = transition.NamedElements.Get(named.Name);
                if (captured is null)
                {
                    // "This implies that names which only exist in the new DOM (entry animations) will be painted on top"
                    captured = new ViewTransitionCapturedElement { Name = named.Name };
                    transition.NamedElements.Add(captured);
                }

                captured.NewElement = named.Element;
                captured.NewPseudoElement = named.PseudoElement;

                // https://drafts.csswg.org/css-view-transitions-2/#vt-class-algorithms: the classes of the new element
                captured.ClassList = GetClassList(named.Style);
                captured.ContainingGroupName = ResolveUsedGroupName(transition, named.Element, named.PseudoElement, named.Style);
            }
        }
        finally
        {
            foreach (var named in elements)
            {
                document.SetCapturedInViewTransition(named.Element, named.PseudoElement, false);
            }
        }

        return true;
    }

    /// <summary>
    /// Skips the other active view transitions that captured one of the elements (their new elements) with an
    /// <c>AbortError</c> (https://drafts.csswg.org/css-view-transitions-2/#capture-the-old-state). Returns whether a transition was skipped.
    /// </summary>
    private static bool SkipTransitionsThatCapture(ViewTransition transition, List<NamedElement> elements)
    {
        List<ViewTransition>? conflicts = null;
        foreach (var other in transition.Document.GetActiveViewTransitions())
        {
            if (ReferenceEquals(other, transition) || other.NamedElements.Count == 0)
                continue;

            foreach (var captured in other.NamedElements.Values)
            {
                if (captured.NewElement is { } element && elements.Exists(named => ReferenceEquals(named.Element, element) && named.PseudoElement == captured.NewPseudoElement))
                {
                    (conflicts ??= []).Add(other);
                    break;
                }
            }
        }

        if (conflicts is null)
            return false;

        // "In tree order of their corresponding root elements"
        conflicts.Sort(static (a, b) => a.RootElement is { } rootA && b.RootElement is { } rootB ? global::Meziantou.Framework.WebPlatform.Animations.DeclarativeAnimations.CompareTreeOrder(rootA, null, rootB, null) : 0);
        foreach (var other in conflicts)
        {
            other.Skip(new DomException("An element of the view transition was captured by another view transition.", "AbortError"));
        }

        return true;
    }

    /// <summary>
    /// Paints the frame of the root element of a scoped view transition displayed while its rendering is suppressed: its
    /// stacking context, with its effects and without its transform, as its image is painted
    /// (https://drafts.csswg.org/css-view-transitions-1/#capture-the-image) but with the elements captured by the transition.
    /// </summary>
    public static ViewTransitionFrozenFrame? CaptureFrozenFrame(Document document, Element scope)
    {
        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is null || engine.GetFragments(scope) is not [BoxFragment fragment, ..])
            return null;

        var origin = GetSnapOrigin(engine, fragment, GetDevicePixelRatio(document));
        var content = Painter.PaintViewTransitionCapture(engine, document, fragment, origin);
        return new ViewTransitionFrozenFrame(content, origin, fragment.ContentBoxRect.Size);
    }

    /// <summary>The computed <c>view-transition-class</c> of an element, as a list of identifiers.</summary>
    private static string[] GetClassList(ComputedStyle style)
    {
        var value = style.GetComputedValue("view-transition-class");
        return value is "none" or "" ? [] : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-2/#resolve-the-used-group-name: the name of the group that contains the group
    /// of an element, from its <c>view-transition-group</c>: <c>normal</c> and <c>contain</c> are the nearest containing group
    /// name (the name of the nearest ancestor that participates in the transition and whose <c>view-transition-group</c> is not
    /// <c>normal</c>), <c>nearest</c> is the nearest ancestor that participates in the transition, and a name is an ancestor
    /// with this name that participates in the transition (else the nearest containing group name).
    /// </summary>
    private static string? ResolveUsedGroupName(ViewTransition transition, Element element, string? pseudoElement, ComputedStyle style)
    {
        var document = transition.Document;
        var styleEngine = document.GetStyleEngine();
        var scope = transition.IsScoped ? transition.RootElement : null;
        var group = style.GetComputedValue("view-transition-group");
        string? nearest = null;
        string? containing = null;
        var hasNamedAncestor = false;
        for (var ancestor = pseudoElement is null ? StyleEngine.GetInheritanceParent(element) : element; ancestor is not null; ancestor = StyleEngine.GetInheritanceParent(ancestor))
        {
            if (!document.IsCapturedInViewTransition(ancestor))
                continue;

            var ancestorStyle = styleEngine.GetStyle(ancestor);
            var name = GetViewTransitionName(ancestor, ancestorStyle);
            nearest ??= name;
            if (containing is null && ancestorStyle.GetComputedValue("view-transition-group") is not ("normal" or ""))
            {
                containing = name;
            }

            hasNamedAncestor |= name is not null && string.Equals(name, group, StringComparison.Ordinal);

            // The elements outside of the root element of a scoped view transition do not participate in it
            if (ReferenceEquals(ancestor, scope))
                break;
        }

        return group switch
        {
            "normal" or "contain" or "" => containing,
            "nearest" => nearest,
            _ => hasNamedAncestor ? group : containing,
        };
    }

    private static double GetDevicePixelRatio(Document document)
    {
        var ratio = document.GetStyleEngine().Environment.DevicePixelRatio;
        return ratio > 0 && double.IsFinite(ratio) ? ratio : 1;
    }

    /// <summary>The geometry of a captured element: the size of its border box and the transform that maps it from the origin of the snapshot containing block.</summary>
    private readonly record struct Geometry(PhysicalSize Size, double[] Transform, Sides<double> Border, BoxFragment? Fragment);

    private static Geometry GetGeometry(ViewTransition transition, LayoutEngine engine, NamedElement named)
    {
        var document = transition.Document;

        // "Let originalRect be snapshot containing block if element is the document element, otherwise, the element's border box"
        if (named.PseudoElement is null && ReferenceEquals(named.Element, document.DocumentElement))
            return new Geometry(GetSnapshotContainingBlockSize(document), Identity(), default, Fragment: null);

        var fragments = named.Fragments;
        var fragment = fragments[0];

        // https://drafts.csswg.org/css-view-transitions-2/#scoped-vt-layout: the ::view-transition pseudo-element of a scoped view
        // transition covers the padding box of its root element (its containing block), in its coordinates: the geometry of the
        // elements is relative to it, without the transforms of the root element and of its ancestors
        var scopeElement = transition.IsScoped ? transition.RootElement : null;
        if (scopeElement is not null && named.PseudoElement is null && ReferenceEquals(named.Element, scopeElement))
        {
            // (the padding box is after the border and the scrollbars of the start edges)
            var paddingBox = fragment.PaddingBoxRect;
            return new Geometry(fragment.Size, Translation(-paddingBox.X, -paddingBox.Y), fragment.Border, fragment);
        }

        if (fragments.Count == 1)
            return new Geometry(fragment.Size, GetTransformToSnapshotContainingBlock(engine, fragment, scopeElement), fragment.Border, fragment);

        // An inline box on several lines: the bounding box of its fragments
        var bounds = LayoutEngine.MapToRoot(fragment, fragment.BorderBoxRect).Bounds;
        for (var i = 1; i < fragments.Count; i++)
        {
            bounds = bounds.Union(LayoutEngine.MapToRoot(fragments[i], fragments[i].BorderBoxRect).Bounds);
        }

        if (scopeElement is not null && FindScopeFragment(fragment, scopeElement) is { } scope)
        {
            var origin = LayoutEngine.MapToRoot(scope, scope.PaddingBoxRect.Offset);
            return new Geometry(bounds.Size, Translation(bounds.X - origin.X, bounds.Y - origin.Y), fragment.Border, fragment);
        }

        var scroll = engine.ViewportScrollState.Offset;
        return new Geometry(bounds.Size, Translation(bounds.X - scroll.X, bounds.Y - scroll.Y), fragment.Border, fragment);
    }

    /// <summary>
    /// Returns the matrix that maps the coordinates of a fragment (its border box origin) to the coordinates of the snapshot
    /// containing block (the viewport, or the padding box of the root element of a scoped view transition): the offsets, the
    /// scroll positions and the transforms of the fragment and of its ancestors (up to the root element of a scoped view transition)
    /// (https://drafts.csswg.org/css-transforms-2/#accumulated-3d-transformation-matrix-computation: the transforms are flattened
    /// outside of the 3D rendering contexts).
    /// </summary>
    private static double[] GetTransformToSnapshotContainingBlock(LayoutEngine engine, Fragment fragment, Element? scopeElement)
    {
        var matrix = Identity();
        var reachedScope = false;
        for (var current = fragment; current is not null; current = current.Parent)
        {
            // The origin of the snapshot containing block of a scoped view transition is the padding box of its root element
            if (scopeElement is not null && IsPrincipalFragment(current, scopeElement) && current is BoxFragment scope)
            {
                reachedScope = true;
                var paddingBox = scope.PaddingBoxRect;
                if (paddingBox.X != 0 || paddingBox.Y != 0)
                {
                    matrix = CssTransforms.Multiply(Translation(-paddingBox.X, -paddingBox.Y), matrix);
                }

                break;
            }

            if (LayoutEngine.GetTransform(current) is { } transform)
            {
                matrix = CssTransforms.Multiply(transform, matrix);
                if (current is BoxFragment box && !LayoutTransforms.ParticipatesIn3DRenderingContext(box) && LayoutTransforms.HasZOutput(matrix))
                {
                    // The flattening: the third row and the third column are those of the identity
                    matrix[2] = 0;
                    matrix[6] = 0;
                    matrix[8] = 0;
                    matrix[9] = 0;
                    matrix[10] = 1;
                    matrix[11] = 0;
                    matrix[14] = 0;
                }
            }

            var offset = LayoutEngine.GetScrolledOffset(current);
            if (offset.X != 0 || offset.Y != 0)
            {
                matrix = CssTransforms.Multiply(Translation(offset.X, offset.Y), matrix);
            }
        }

        if (reachedScope)
            return matrix;

        var scroll = engine.ViewportScrollState.Offset;
        if (scroll.X != 0 || scroll.Y != 0)
        {
            matrix = CssTransforms.Multiply(Translation(-scroll.X, -scroll.Y), matrix);
        }

        return matrix;
    }

    /// <summary>Whether a fragment is a fragment of the principal box of an element.</summary>
    private static bool IsPrincipalFragment(Fragment fragment, Element element)
        => fragment is BoxFragment { Box: { IsAnonymous: false, PseudoElement: null } box } && ReferenceEquals(box.Element, element) && box.Kind != ContainerBoxKind.TableGrid;

    /// <summary>
    /// Returns the fragment of the root element of a scoped view transition that contains a fragment (the ancestor in the
    /// fragment tree of the fragment: the layout containment of the root element makes it the containing block of its descendants).
    /// </summary>
    private static BoxFragment? FindScopeFragment(Fragment fragment, Element scopeElement)
    {
        for (var current = fragment.Parent; current is not null; current = current.Parent)
        {
            if (IsPrincipalFragment(current, scopeElement))
                return (BoxFragment)current;
        }

        return null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-1/#capture-the-image: paints the element into a display list in the
    /// coordinates of its image, and rasterizes it with the rendering backend of the document (without backend, the image is the
    /// display list).
    /// </summary>
    private static ViewTransitionSnapshot CaptureImage(Document document, LayoutEngine engine, BoxFragment root, Geometry geometry, List<PaintItem> order)
    {
        var ratio = document.GetStyleEngine().Environment.DevicePixelRatio;
        ratio = ratio > 0 && double.IsFinite(ratio) ? ratio : 1;
        var origin = GetSnapOrigin(engine, geometry.Fragment, ratio);
        var content = Paint(document, engine, root, geometry.Fragment, origin, order);
        var natural = new PhysicalRect(origin.X, origin.Y, geometry.Size.Width, geometry.Size.Height);

        // "Return the portion of this canvas that includes element's ink overflow rectangle as an image"; the image of the document
        // element is "a transparent canvas the size of the snapshot containing block"
        var bounds = natural;
        if (geometry.Fragment is not null)
        {
            if (GetInkBounds(content) is { IsEmpty: false } ink)
            {
                bounds = bounds.IsEmpty ? ink : bounds.Union(ink);
            }

            bounds = ClampBounds(document, bounds.Translate(new PhysicalOffset(-origin.X, -origin.Y)), geometry, ratio).Translate(origin);
        }

        // The edges are on device pixels, so that the pixels of the image are the pixels of the element
        var left = Math.Floor(bounds.Left * ratio) / ratio;
        var top = Math.Floor(bounds.Top * ratio) / ratio;
        var right = Math.Ceiling(bounds.Right * ratio) / ratio;
        var bottom = Math.Ceiling(bounds.Bottom * ratio) / ratio;
        bounds = new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

        // "Implementations may adjust the rasterization quality": an element that is scaled up by its transform is rasterized at
        // the scale it is displayed at (the image is drawn with the same transform by its group), up to the maximum size
        var matrix = geometry.Transform;
        var ratioX = ratio * Math.Clamp(Math.Sqrt((matrix[0] * matrix[0]) + (matrix[1] * matrix[1])), 1, MaxRasterScale);
        var ratioY = ratio * Math.Clamp(Math.Sqrt((matrix[4] * matrix[4]) + (matrix[5] * matrix[5])), 1, MaxRasterScale);
        if (bounds.Width * ratioX > MaxSnapshotSide || bounds.Height * ratioY > MaxSnapshotSide)
        {
            ratioX = ratio;
            ratioY = ratio;
        }

        RenderingImage? bitmap = null;
        PaintImage? image = null;
        var width = (int)Math.Round(bounds.Width * ratioX, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(bounds.Height * ratioY, MidpointRounding.AwayFromZero);
        if (document.RenderingBackend is { } backend && width > 0 && height > 0)
        {
            using var surface = backend.CreateSurface(width, height, RenderingSurfaceOptions.Default);
            if (surface is not null)
            {
                surface.Draw(content.WithTransform(DisplayMatrix.Scale(ratioX, ratioY).PreConcat(DisplayMatrix.Translation(-bounds.X, -bounds.Y))));
                bitmap = surface.CreateSnapshot();
                image = new PaintImage("", backend, bitmap);
            }
        }

        return new ViewTransitionSnapshot { NaturalSize = geometry.Size, Origin = origin, Bounds = bounds, Content = content, Image = image, Bitmap = bitmap };
    }

    /// <summary>
    /// Limits the captured area of a large element (https://drafts.csswg.org/css-view-transitions-1/#capture-rendering-characteristics:
    /// "Implementations may clip the rendered contents if the ink overflow rectangle exceeds some implementation-defined maximum.
    /// However, the captured image should include, at the very least, the contents of element that intersect with the snapshot
    /// containing block"): the area is the part of the snapshot containing block the element covers, extended by its size.
    /// </summary>
    private static PhysicalRect ClampBounds(Document document, PhysicalRect bounds, Geometry geometry, double ratio)
    {
        var max = MaxSnapshotSide / ratio;
        if (bounds.Width <= max && bounds.Height <= max)
            return bounds;

        // The snapshot containing block in the coordinates of the image (the transform is a translation for most elements)
        var snapshot = GetSnapshotContainingBlockSize(document);
        var visible = new PhysicalRect(-geometry.Transform[12], -geometry.Transform[13], snapshot.Width, snapshot.Height);
        if (LayoutTransforms.IsInvertibleOnPlane(geometry.Transform)
            && LayoutTransforms.TryInverseTransformPoint(geometry.Transform, new PhysicalOffset(0, 0), out var p1)
            && LayoutTransforms.TryInverseTransformPoint(geometry.Transform, new PhysicalOffset(snapshot.Width, 0), out var p2)
            && LayoutTransforms.TryInverseTransformPoint(geometry.Transform, new PhysicalOffset(snapshot.Width, snapshot.Height), out var p3)
            && LayoutTransforms.TryInverseTransformPoint(geometry.Transform, new PhysicalOffset(0, snapshot.Height), out var p4))
        {
            var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
            var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
            var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
            var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
            visible = new PhysicalRect(minX, minY, maxX - minX, maxY - minY);
        }

        return new PhysicalRect(ClampAxis(bounds.Left, bounds.Right, visible.Left, visible.Right, max, out var width), ClampAxis(bounds.Top, bounds.Bottom, visible.Top, visible.Bottom, max, out var height), width, height);

        static double ClampAxis(double start, double end, double visibleStart, double visibleEnd, double max, out double size)
        {
            if (end - start <= max)
            {
                size = end - start;
                return start;
            }

            // The visible part, extended on both sides up to the maximum; the start of the element when it is not visible
            var from = Math.Max(start, visibleStart);
            var to = Math.Min(end, visibleEnd);
            if (to <= from)
            {
                from = visibleEnd <= start ? start : end - max;
                size = max;
                return Math.Max(start, from);
            }

            var extra = Math.Max(0, max - (to - from)) / 2;
            from = Math.Max(start, from - extra);
            to = Math.Min(end, from + max);
            size = to - from;
            return from;
        }
    }

    /// <summary>
    /// Paints an element captured by a view transition into a display list in the coordinates of its image: the viewport for the
    /// document element (the whole document, without the view transition layer), the border box for another element (its
    /// stacking context, without its transform nor the effects of its ancestors). The other captured elements are not painted.
    /// </summary>
    /// <param name="origin">The position of the origin of the border box of the element in the display list (see <see cref="GetSnapOrigin(LayoutEngine, BoxFragment?, double)"/>).</param>
    /// <param name="order">The painting order of the document with the markers of the stacking contexts, or null to compute it.</param>
    internal static DisplayList Paint(Document document, LayoutEngine engine, BoxFragment root, BoxFragment? fragment, PhysicalOffset origin, List<PaintItem>? order = null)
    {
        if (fragment is null)
            return Painter.PaintViewTransitionRootCapture(engine, document, root, engine.ViewportScrollState.Offset, engine.GetCanvasBaseColor());

        return Painter.PaintViewTransitionCapture(engine, document, fragment, origin, order);
    }

    /// <summary>
    /// Returns the position of the border box of a captured element in its image: the fractional part (in device pixels) of
    /// its position in the viewport, so that its painting is snapped to the same device pixels as in the document (the image is
    /// drawn back at this position by its group); the origin when the element or one of its ancestors is transformed (the
    /// painting is then snapped in the coordinates of the transformed box). A transform that only translates the plane of its
    /// box moves the position instead: the painter snaps the boxes of such a box at their translated position (see
    /// <see cref="Painter.IsPlaneTranslation(BoxFragment, out PhysicalOffset)"/>), where the group draws the image.
    /// </summary>
    internal static PhysicalOffset GetSnapOrigin(LayoutEngine engine, BoxFragment? fragment, double ratio)
    {
        if (fragment is null)
            return PhysicalOffset.Zero;

        var translation = PhysicalOffset.Zero;
        for (Fragment? current = fragment; current is not null; current = current.Parent)
        {
            if (LayoutEngine.GetTransform(current) is null)
                continue;

            if (current is not BoxFragment transformed || !Painter.IsPlaneTranslation(transformed, out var offset))
                return PhysicalOffset.Zero;

            translation += offset;
        }

        var position = engine.ToViewport(fragment.GetOffsetInRoot() + translation);
        var x = ((position.X * ratio) - Math.Floor(position.X * ratio)) / ratio;
        var y = ((position.Y * ratio) - Math.Floor(position.Y * ratio)) / ratio;
        return new PhysicalOffset(x < 1e-6 ? 0 : x, y < 1e-6 ? 0 : y);
    }

    /// <summary>
    /// Returns the bounds of what a display list paints (the ink overflow of a captured element), or null when it paints
    /// nothing. The filters and the blurs extend the bounds of what they apply to.
    /// </summary>
    internal static PhysicalRect? GetInkBounds(DisplayList list)
    {
        PhysicalRect? result = null;
        var transform = DisplayMatrix.Identity;
        PhysicalRect? clip = null;
        var inflate = 0d;
        var stack = new Stack<(DisplayMatrix Transform, PhysicalRect? Clip, double Inflate)>();
        foreach (var item in list.Items)
        {
            switch (item)
            {
                case SaveItem:
                    stack.Push((transform, clip, inflate));
                    continue;

                case SaveLayerItem layer:
                    stack.Push((transform, clip, inflate));
                    if (layer.Filters is { } filters)
                    {
                        foreach (var filter in filters)
                        {
                            inflate += filter switch
                            {
                                BlurFilter blur => 3 * Math.Max(blur.DeviationX, blur.DeviationY),
                                DropShadowFilter shadow => (3 * shadow.Deviation) + Math.Max(Math.Abs(shadow.Offset.X), Math.Abs(shadow.Offset.Y)),
                                FilterGraph => 64,
                                _ => 0,
                            };
                        }
                    }

                    continue;

                case RestoreItem:
                    if (stack.Count > 0)
                    {
                        (transform, clip, inflate) = stack.Pop();
                    }

                    continue;

                case TransformItem transformItem:
                    transform = transform.PreConcat(transformItem.Matrix);
                    continue;

                case ClipRectItem clipRect:
                    clip = Intersect(clip, ContentfulPaintScanner.TransformBounds(transform, clipRect.Rect));
                    continue;

                case ClipRoundedRectItem clipRounded:
                    clip = Intersect(clip, ContentfulPaintScanner.TransformBounds(transform, clipRounded.Rect.Rect));
                    continue;

                case ClipPathItem clipPath:
                    clip = Intersect(clip, ContentfulPaintScanner.TransformBounds(transform, clipPath.Path.GetBounds()));
                    continue;
            }

            if (ContentfulPaintScanner.GetBounds(item) is not { } local)
                continue;

            // The blur of a shadow and the width of a stroke extend the shape
            var paint = ContentfulPaintScanner.GetPaint(item);
            var extent = ((paint?.BlurSigma ?? 0) * 3) + ((paint?.Stroke?.Width ?? 0) / 2);
            if (extent > 0)
            {
                local = new PhysicalRect(local.X - extent, local.Y - extent, local.Width + (2 * extent), local.Height + (2 * extent));
            }

            var bounds = ContentfulPaintScanner.TransformBounds(transform, local);
            if (inflate > 0)
            {
                bounds = new PhysicalRect(bounds.X - inflate, bounds.Y - inflate, bounds.Width + (2 * inflate), bounds.Height + (2 * inflate));
            }

            if (clip is { } clipBounds)
            {
                bounds = Intersect(clipBounds, bounds);
            }

            if (bounds.IsEmpty)
                continue;

            result = result is { } previous ? previous.Union(bounds) : bounds;
        }

        return result;

        static PhysicalRect Intersect(PhysicalRect? clip, PhysicalRect rect)
        {
            if (clip is not { } c)
                return rect;

            var left = Math.Max(c.Left, rect.Left);
            var top = Math.Max(c.Top, rect.Top);
            var right = Math.Min(c.Right, rect.Right);
            var bottom = Math.Min(c.Bottom, rect.Bottom);
            return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-view-transitions-1/#update-pseudo-element-styles: computes the geometry of the new elements and
    /// replaces the dynamic view transition style sheet of the document (with the rules of
    /// https://drafts.csswg.org/css-view-transitions-1/#setup-transition-pseudo-elements). Returns false on failure.
    /// </summary>
    public static bool UpdatePseudoElementStyles(ViewTransition transition)
    {
        var document = transition.Document;
        var engine = document.GetLayoutEngine();
        var styleEngine = document.GetStyleEngine();
        var hasLayout = engine.EnsureLayout() is not null;
        var builder = new StringBuilder();

        // The rules of a transition only apply to the pseudo-elements of its root element: ":root" for the document element; the
        // root element of a scoped view transition matches :active-view-transition (the same specificity)
        var rootSelector = transition.IsScoped ? ":active-view-transition" : ":root";
        foreach (var captured in transition.NamedElements.Values)
        {
            double width;
            double height;
            double[] transform;
            string? writingMode;
            string? direction;
            string? textOrientation;
            string? mixBlendMode;
            string? backdropFilter;
            string? colorScheme;
            if (captured.NewElement is not { } element)
            {
                width = captured.OldWidth;
                height = captured.OldHeight;
                transform = captured.OldTransform;
                writingMode = captured.OldWritingMode;
                direction = captured.OldDirection;
                textOrientation = captured.OldTextOrientation;
                mixBlendMode = captured.OldMixBlendMode;
                backdropFilter = captured.OldBackdropFilter;
                colorScheme = captured.OldColorScheme;
            }
            else
            {
                // "Return failure if [...] capturedElement's new element has a flat tree ancestor that skips its contents, [...] is
                // not rendered, [...] has more than one box fragment"
                if (!hasLayout || !element.IsConnected || !ReferenceEquals(element.NodeDocument, document) || ContentRelevancy.IsInSkippedContents(element))
                    return false;

                var fragments = engine.GetFragments(element, captured.NewPseudoElement);
                if (fragments.Count == 0 || (fragments.Count > 1 && !fragments[0].Box.IsInlineBox))
                    return false;

                var style = styleEngine.GetStyle(element, captured.NewPseudoElement);
                var geometry = GetGeometry(transition, engine, new NamedElement(element, captured.NewPseudoElement, captured.Name, fragments, style));
                width = geometry.Size.Width;
                height = geometry.Size.Height;
                transform = geometry.Transform;
                captured.NewSize = geometry.Size;
                captured.NewBorderWidth = geometry.Border;
                captured.NewOrigin = GetSnapOrigin(engine, geometry.Fragment, GetDevicePixelRatio(document));
                captured.NewBorderShape = GetBorderShape(style);
                writingMode = style.GetComputedValue("writing-mode");
                direction = style.GetComputedValue("direction");
                textOrientation = style.GetComputedValue("text-orientation");
                mixBlendMode = style.GetComputedValue("mix-blend-mode");
                backdropFilter = style.GetComputedValue("backdrop-filter");
                colorScheme = style.GetComputedValue("color-scheme");
            }

            // https://drafts.csswg.org/css-view-transitions-2/#vt-group-transform-adjust: the transform of a nested group is relative
            // to its containing group
            captured.TransformFromSnapshotContainingBlock = transform;
            var oldTransform = captured.OldTransform;
            var containing = captured.ContainingGroupName is { } containingName ? transition.NamedElements.Get(containingName) : null;
            if (containing is not null && !ReferenceEquals(containing, captured))
            {
                // The geometry of a group without new element is the one of the old state: it is relative to the old state of its container
                transform = AdjustNestedTransform(transform, containing, useOld: captured.NewElement is null);
                oldTransform = AdjustNestedTransform(oldTransform, containing, useOld: true);
            }

            // The transform of the group is applied around its transform origin, the center of its box: the function "that would
            // map element's border box from the snapshot containing block origin to its current visual position" is relative to it
            transform = ToTransformAroundCenter(transform, width, height);
            oldTransform = ToTransformAroundCenter(oldTransform, captured.OldWidth, captured.OldHeight);

            var name = captured.Name;
            builder.Append(rootSelector).Append("::view-transition-group(").Append(name).Append(") { width: ").Append(FormatNumber(width)).Append("px; height: ").Append(FormatNumber(height)).Append("px; transform: ").Append(FormatMatrix(transform)).Append(';');
            AppendDeclaration(builder, "writing-mode", writingMode);
            AppendDeclaration(builder, "direction", direction);
            AppendDeclaration(builder, "text-orientation", textOrientation);
            AppendDeclaration(builder, "mix-blend-mode", mixBlendMode);
            AppendDeclaration(builder, "backdrop-filter", backdropFilter);
            AppendDeclaration(builder, "color-scheme", colorScheme);
            builder.Append(" }\n");

            if (captured.HasOldImage && captured.NewElement is not null)
            {
                builder.Append("@keyframes -ua-view-transition-group-anim-").Append(name).Append(" { from { transform: ").Append(FormatMatrix(oldTransform)).Append("; width: ").Append(FormatNumber(captured.OldWidth)).Append("px; height: ")
                    .Append(FormatNumber(captured.OldHeight)).Append("px;");
                AppendDeclaration(builder, "backdrop-filter", captured.OldBackdropFilter);
                builder.Append(" } }\n");
                builder.Append(rootSelector).Append("::view-transition-group(").Append(name).Append(") { animation-name: -ua-view-transition-group-anim-").Append(name).Append("; }\n");
                builder.Append(rootSelector).Append("::view-transition-image-pair(").Append(name).Append(") { isolation: isolate; }\n");
                builder.Append(rootSelector).Append("::view-transition-old(").Append(name).Append(") { animation-name: -ua-view-transition-fade-out, -ua-mix-blend-mode-plus-lighter; }\n");
                builder.Append(rootSelector).Append("::view-transition-new(").Append(name).Append(") { animation-name: -ua-view-transition-fade-in, -ua-mix-blend-mode-plus-lighter; }\n");
            }
            else if (captured.HasOldImage)
            {
                builder.Append(rootSelector).Append("::view-transition-old(").Append(name).Append(") { animation-name: -ua-view-transition-fade-out; }\n");
            }
            else
            {
                builder.Append(rootSelector).Append("::view-transition-new(").Append(name).Append(") { animation-name: -ua-view-transition-fade-in; }\n");
            }

            // https://drafts.csswg.org/css-view-transitions-2/#selectordef-view-transition-group-children: "it has a transparent
            // border that matches the size and shape of the border on the element that caused this structure to be created"
            if (transition.PseudoTree?.FindGroup(name) is { Children.Count: > 0 })
            {
                var border = captured.NewElement is not null ? captured.NewBorderWidth : captured.OldBorderWidth;
                builder.Append(rootSelector).Append("::view-transition-group-children(").Append(name).Append(") { ").Append(FormatBorderWidth(border)).Append(captured.NewElement is not null ? captured.NewBorderShape : captured.OldBorderShape).Append(" }\n");
                if (captured.HasOldImage && captured.NewElement is not null)
                {
                    // The border of the children container animates from the border of the old element
                    builder.Append("@keyframes -ua-view-transition-group-children-anim-").Append(name).Append(" { from { ").Append(FormatBorderWidth(captured.OldBorderWidth)).Append(captured.OldBorderShape).Append(" } }\n");
                    builder.Append(rootSelector).Append("::view-transition-group-children(").Append(name).Append(") { animation-name: -ua-view-transition-group-children-anim-").Append(name).Append("; }\n");
                }
            }
        }

        document.SetViewTransitionStyleSheet(transition, builder.ToString());
        return true;
    }

    private static readonly string[] BorderShapeProperties =
    [
        "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius",
        "corner-top-left-shape", "corner-top-right-shape", "corner-bottom-right-shape", "corner-bottom-left-shape",
    ];

    /// <summary>The declarations of the radii and of the corner shapes of the border of an element.</summary>
    private static string GetBorderShape(ComputedStyle style)
    {
        var builder = new StringBuilder();
        foreach (var property in BorderShapeProperties)
        {
            AppendDeclaration(builder, property, style.GetComputedValue(property));
        }

        return builder.ToString();
    }

    private static string FormatBorderWidth(Sides<double> border)
        => "border-width: " + FormatNumber(border.Top) + "px " + FormatNumber(border.Right) + "px " + FormatNumber(border.Bottom) + "px " + FormatNumber(border.Left) + "px;";

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#adjust-nested-view-transition-group-transform</summary>
    private static double[] AdjustNestedTransform(double[] transform, ViewTransitionCapturedElement container, bool useOld)
    {
        var childrenTransform = useOld || container.TransformFromSnapshotContainingBlock is null ? container.OldTransform : container.TransformFromSnapshotContainingBlock;
        var border = useOld || container.NewElement is null ? container.OldBorderWidth : container.NewBorderWidth;
        childrenTransform = CssTransforms.Multiply(childrenTransform, Translation(border.Left, border.Top));
        return Animations.CssMatrixDecomposition.Inverse(childrenTransform) is { } inverse ? CssTransforms.Multiply(inverse, transform) : transform;
    }

    /// <summary>
    /// Returns the transform that, applied around the center of a box of the given size at the origin (the default
    /// <c>transform-origin</c> of the groups), is the matrix: T(-center) × matrix × T(center).
    /// </summary>
    private static double[] ToTransformAroundCenter(double[] matrix, double width, double height)
    {
        if (IsTranslation(matrix) || (width == 0 && height == 0))
            return matrix;

        return CssTransforms.Multiply(CssTransforms.Multiply(Translation(-width / 2, -height / 2), matrix), Translation(width / 2, height / 2));

        static bool IsTranslation(double[] m)
            => m[0] == 1 && m[1] == 0 && m[2] == 0 && m[3] == 0 && m[4] == 0 && m[5] == 1 && m[6] == 0 && m[7] == 0 && m[8] == 0 && m[9] == 0 && m[10] == 1 && m[11] == 0 && m[15] == 1;
    }

    private static void AppendDeclaration(StringBuilder builder, string property, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            builder.Append(' ').Append(property).Append(": ").Append(value).Append(';');
        }
    }

    private static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            return "0";

        // The rounding errors of the geometry are removed (the browsers compute it with fixed-point layout units)
        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatMatrix(double[] m)
    {
        var is2D = m[2] == 0 && m[3] == 0 && m[6] == 0 && m[7] == 0 && m[8] == 0 && m[9] == 0 && m[10] == 1 && m[11] == 0 && m[14] == 0 && m[15] == 1;
        var builder = new StringBuilder();
        if (is2D)
        {
            builder.Append("matrix(").Append(FormatNumber(m[0])).Append(", ").Append(FormatNumber(m[1])).Append(", ").Append(FormatNumber(m[4])).Append(", ").Append(FormatNumber(m[5])).Append(", ")
                .Append(FormatNumber(m[12])).Append(", ").Append(FormatNumber(m[13])).Append(')');
            return builder.ToString();
        }

        builder.Append("matrix3d(");
        for (var i = 0; i < 16; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(FormatNumber(m[i]));
        }

        return builder.Append(')').ToString();
    }
}
