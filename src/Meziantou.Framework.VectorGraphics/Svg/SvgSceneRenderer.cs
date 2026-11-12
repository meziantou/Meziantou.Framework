using System.Text;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>Writes a <see cref="Scene"/> as SVG.</summary>
/// <remarks>
/// The output is deterministic: numbers use the invariant culture and <see cref="SvgRenderOptions.Precision"/>
/// decimals, identifiers are numbered in document order, and each element is on its own line. The root element has
/// <c>role="img"</c>, and the title and the description of the scene become its <c>title</c> and <c>desc</c>. The
/// <see cref="SceneElement.DataAttributes"/> of an element are written in the order of their names.
/// </remarks>
public static class SvgSceneRenderer
{
    /// <summary>Returns the SVG of a scene.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    public static string Render(Scene scene, SvgRenderOptions? options = null)
    {
        using var writer = new StringWriter();
        Render(scene, writer, options);
        return writer.ToString();
    }

    /// <summary>Writes the SVG of a scene.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public static void Render(Scene scene, TextWriter writer, SvgRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(writer);
        new Writer(writer, options ?? new SvgRenderOptions()).Write(scene);
    }

    private sealed class Writer(TextWriter output, SvgRenderOptions options)
    {
        private readonly Dictionary<object, string> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SceneGroup, string> _clipPathIds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SceneElement, string> _filterIds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SceneElement, string> _maskIds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SceneText, string> _textPathIds = new(ReferenceEqualityComparer.Instance);
        private readonly List<(string Id, SceneMask Mask)> _masks = [];
        private readonly List<(string Id, PatternPaint Pattern)> _patterns = [];
        private readonly List<(string Id, SceneMarker Marker)> _markers = [];

        // The clipping paths of the elements, by the transform from the coordinates of their content to the user space
        // that refers to them (null for the parent of a clipped element); the ones to write, in order
        private readonly Dictionary<(SceneClipPath ClipPath, SceneTransform? Base), string> _clipPathDefinitionIds = [];
        private readonly List<(string Id, SceneClipPath ClipPath, SceneTransform? Base)> _clipPathDefinitions = [];
        private int _clipCount;

        // While a clipping path is written: the transform of the group of its content being written, and of the text being written
        private bool _inClipPath;
        private SceneTransform? _clipTextTransform;
        private readonly HashSet<string> _elementIds = new(StringComparer.Ordinal);

        // The generated identifiers of the contents of the uses that have none; the contents that are only drawn by uses
        private readonly Dictionary<SceneElement, string> _contentIds = new(ReferenceEqualityComparer.Instance);
        private List<SceneElement> _detached = [];
        private List<SceneElement> _roots = [];
        private readonly HashSet<SceneElement> _useTargets = new(ReferenceEqualityComparer.Instance);
        private readonly int _precision = options.Precision;
        private Scene _scene = null!;

        // The identifier of the element being written, for the first tag it writes; the element whose effects are on
        // the group that wraps it
        private string? _pendingId;
        private SceneElement? _effectsOnWrapper;

        public void Write(Scene scene)
        {
            _scene = scene;
            var walk = SceneTree.Walk(scene.Root);
            _detached = walk.Detached;
            _roots = walk.Roots;
            CollectIds(walk.Elements);
            output.Write("<svg");
            if (options.IncludeNamespace)
            {
                output.Write(" xmlns=\"http://www.w3.org/2000/svg\"");
            }

            Attribute("width", scene.Width);
            Attribute("height", scene.Height);
            Attribute("viewBox", "0 0 " + Number(scene.Width) + " " + Number(scene.Height));
            Attribute("role", "img");
            Attribute("font-family", FamilyList(scene.FontFamily));
            output.Write(">\n");
            if (!string.IsNullOrEmpty(scene.Title))
            {
                output.Write("<title>");
                output.Write(Escape(scene.Title));
                output.Write("</title>\n");
            }

            if (!string.IsNullOrEmpty(scene.Description))
            {
                output.Write("<desc>");
                output.Write(Escape(scene.Description));
                output.Write("</desc>\n");
            }

            WriteDefinitions(scene.Root, options.EmbedFonts && scene.Fonts is { } fonts ? GetFontFaceRules(scene, fonts) : null);
            if (_masks.Count > 0 || _patterns.Count > 0 || _markers.Count > 0 || _detached.Count > 0 || _clipPathDefinitions.Count > 0)
            {
                // The content of a mask is in the coordinates of the parent of the masked element, on the whole plane
                output.Write("<defs>\n");
                foreach (var (id, mask) in _masks)
                {
                    output.Write("<mask");
                    Attribute("id", id);
                    output.Write(" maskUnits=\"userSpaceOnUse\" x=\"-100000\" y=\"-100000\" width=\"200000\" height=\"200000\"");
                    if (mask.Mode is SceneMaskMode.Alpha)
                    {
                        output.Write(" mask-type=\"alpha\"");
                    }

                    output.Write(">\n");
                    WriteElement(mask.Content);
                    output.Write("</mask>\n");
                }

                // The content of a pattern is in the coordinates of its tile: its view box is the tile, of the same size
                foreach (var (id, pattern) in _patterns)
                {
                    var tile = pattern.Tile;
                    output.Write("<pattern");
                    Attribute("id", id);
                    output.Write(" patternUnits=\"userSpaceOnUse\"");
                    Attribute("x", tile.X);
                    Attribute("y", tile.Y);
                    Attribute("width", tile.Width);
                    Attribute("height", tile.Height);
                    if (tile.X != 0 || tile.Y != 0)
                    {
                        Attribute("viewBox", Number(tile.X) + " " + Number(tile.Y) + " " + Number(tile.Width) + " " + Number(tile.Height));
                    }

                    if (pattern.Transform is { } patternTransform)
                    {
                        Attribute("patternTransform", Transform(patternTransform));
                    }

                    output.Write(">\n");
                    WriteElement(pattern.Content);
                    output.Write("</pattern>\n");
                }

                // The content of a marker is not cut: its size does not matter
                foreach (var (id, marker) in _markers)
                {
                    output.Write("<marker");
                    Attribute("id", id);
                    if (marker.Units is SceneMarkerUnits.UserSpace)
                    {
                        output.Write(" markerUnits=\"userSpaceOnUse\"");
                    }

                    if (marker.RefX != 0)
                    {
                        Attribute("refX", marker.RefX);
                    }

                    if (marker.RefY != 0)
                    {
                        Attribute("refY", marker.RefY);
                    }

                    Attribute("orient", marker.Orientation switch
                    {
                        SceneMarkerOrientation.Auto => "auto",
                        SceneMarkerOrientation.AutoStartReverse => "auto-start-reverse",
                        _ => Number(double.IsFinite(marker.Angle) ? marker.Angle : 0),
                    });
                    output.Write(" overflow=\"visible\">\n");
                    WriteElement(marker.Content);
                    output.Write("</marker>\n");
                }

                // The contents of the uses that are not drawn otherwise
                foreach (var element in _detached)
                {
                    WriteElement(element);
                }

                // The clipping paths; writing one adds the clipping paths of its content to the list
                for (var i = 0; i < _clipPathDefinitions.Count; i++)
                {
                    WriteClipPathDefinition(_clipPathDefinitions[i].Id, _clipPathDefinitions[i].ClipPath, _clipPathDefinitions[i].Base);
                }

                output.Write("</defs>\n");
            }

            if (scene.Background is { } background)
            {
                output.Write("<rect");
                Attribute("width", scene.Width);
                Attribute("height", scene.Height);
                Paint("fill", background);
                output.Write("/>\n");
            }

            foreach (var child in scene.Root.Children)
            {
                WriteElement(child);
            }

            output.Write("</svg>\n");
        }

        // The identifiers of the elements, which must be different: the generated identifiers avoid them
        private void CollectIds(List<SceneElement> elements)
        {
            foreach (var element in elements)
            {
                if (element.Id is { } id && !_elementIds.Add(id))
                    throw new InvalidOperationException($"Two elements of the scene have the identifier '{id}': the identifiers of the elements must be different.");
            }

            // The content of a use is referenced by its identifier
            var elementCount = 0;
            foreach (var element in elements)
            {
                if (element is SceneUse { Content: var target })
                {
                    _useTargets.Add(target);
                }

                if (element is SceneUse { Content: { Id: null } content } use && !_contentIds.ContainsKey(content) && !use.IsCircular())
                {
                    _contentIds[content] = NextId("element", ref elementCount);
                }

                // The elements of the image primitives of a filter are referenced by their identifiers too
                foreach (var reference in SceneTree.GetFilterImageReferences(element))
                {
                    _useTargets.Add(reference);
                    if (reference.Id is null && !_contentIds.ContainsKey(reference))
                    {
                        _contentIds[reference] = NextId("element", ref elementCount);
                    }
                }
            }
        }

        // An identifier the renderer generates: the prefix, the kind and a number, skipping the identifiers of the elements
        private string NextId(string kind, ref int count)
        {
            while (true)
            {
                var id = options.IdPrefix + "-" + kind + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                count++;
                if (!_elementIds.Contains(id))
                    return id;
            }
        }

        // Writes the start of a tag, with the identifier of the element being written when it is its first tag
        private void StartTag(string name)
        {
            output.Write('<');
            output.Write(name);
            if (_pendingId is { } id)
            {
                _pendingId = null;
                Attribute("id", id);
            }
        }

        private void WriteDefinitions(SceneGroup root, string? fontFaceRules)
        {
            var definitions = new StringBuilder();
            if (!string.IsNullOrEmpty(fontFaceRules))
            {
                definitions.Append("<style>").Append(Escape(fontFaceRules)).Append("</style>\n");
            }

            var gradientCount = 0;
            var filterCount = 0;
            var maskCount = 0;
            var textPathCount = 0;
            var patternCount = 0;
            var markerCount = 0;
            var visited = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
            var visitedClipPaths = new HashSet<SceneClipPath>(ReferenceEqualityComparer.Instance);
            var visitedClipContents = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
            Visit(root);
            foreach (var element in _roots)
            {
                Visit(element);
            }

            if (definitions.Length > 0)
            {
                output.Write("<defs>\n");
                output.Write(definitions.ToString());
                output.Write("</defs>\n");
            }

            // An element is visited once: it is written with the same definitions wherever it is
            void Visit(SceneElement element)
            {
                if (!visited.Add(element))
                    return;

                if (element is SceneShape shape)
                {
                    AddPaint(shape.Fill);
                    AddPaint(shape.Stroke);
                }

                switch (element)
                {
                    case ScenePath path:
                        AddMarker(path.MarkerStart);
                        AddMarker(path.MarkerMid);
                        AddMarker(path.MarkerEnd);
                        break;

                    case SceneLine line:
                        AddMarker(line.MarkerStart);
                        AddMarker(line.MarkerEnd);
                        break;
                }

                AddTextPaints(element);
                AddTextPath(element);

                if (element.Filter is { } filter)
                {
                    var id = NextId("filter", ref filterCount);
                    _filterIds[element] = id;
                    AddFilter(id, element, filter);
                }

                if (element.ClippingPath is { } clippingPath)
                {
                    GetClipPathId(clippingPath, transform: null);
                    VisitClipPath(clippingPath);
                }

                if (element.Mask is { } mask)
                {
                    var id = NextId("mask", ref maskCount);
                    _maskIds[element] = id;
                    _masks.Add((id, mask));
                    Visit(mask.Content);
                }

                if (element is SceneGroup group)
                {
                    if (group.Clip is { } clip)
                    {
                        var id = NextId("clip", ref _clipCount);
                        _ids[group] = id;
                        definitions.Append("<clipPath id=\"").Append(Escape(id)).Append("\"><rect x=\"").Append(Number(clip.X))
                            .Append("\" y=\"").Append(Number(clip.Y)).Append("\" width=\"").Append(Number(clip.Width))
                            .Append("\" height=\"").Append(Number(clip.Height)).Append("\"/></clipPath>\n");
                    }

                    if (group.ClipPath is { } clipPath)
                    {
                        var id = NextId("clip", ref _clipCount);
                        _clipPathIds[group] = id;
                        definitions.Append("<clipPath id=\"").Append(Escape(id)).Append("\"><path d=\"").Append(SvgNumber.FormatPath(clipPath, _precision)).Append('"');
                        if (group.ClipRule is SceneFillRule.EvenOdd)
                        {
                            definitions.Append(" clip-rule=\"evenodd\"");
                        }

                        definitions.Append("/></clipPath>\n");
                    }

                    foreach (var child in group.Children)
                    {
                        Visit(child);
                    }
                }
            }

            // The paints of a text and of its spans
            void AddTextPaints(SceneElement element)
            {
                if (element is SceneText paintedText)
                {
                    AddPaint(paintedText.Fill);
                    AddPaint(paintedText.Stroke);
                    AddPaint(paintedText.DecorationFill);
                    foreach (var span in paintedText.Spans)
                    {
                        AddPaint(span.Fill);
                        AddPaint(span.Stroke);
                        AddPaint(span.DecorationFill);
                    }
                }
            }

            // The path of a text is referenced by its textPath
            void AddTextPath(SceneElement element)
            {
                if (element is SceneText { Path: { } textPath } pathText && !_textPathIds.ContainsKey(pathText))
                {
                    var id = NextId("textpath", ref textPathCount);
                    _textPathIds[pathText] = id;
                    definitions.Append("<path id=\"").Append(Escape(id)).Append("\" d=\"").Append(SvgNumber.FormatPath(textPath, _precision)).Append("\"/>\n");
                }
            }

            // The texts of a clipping path, of the clipping paths that cut it, and of the clipping paths of its content,
            // are written with it: their paths are referenced by their textPath
            void VisitClipPath(SceneClipPath clipPath)
            {
                for (var current = clipPath; current is not null && visitedClipPaths.Add(current); current = current.ClippingPath)
                {
                    VisitClipContent(current.Content);
                }
            }

            void VisitClipContent(SceneElement element)
            {
                if (!visitedClipContents.Add(element))
                    return;

                AddTextPaints(element);
                AddTextPath(element);
                switch (element)
                {
                    case SceneGroup group:
                        foreach (var child in group.Children)
                        {
                            VisitClipContent(child);
                        }

                        break;

                    case SceneUse use when !use.IsCircular():
                        VisitClipContent(use.Content);
                        break;
                }

                // Only the clipping paths of the shapes and the texts apply in a clipping path: the ones of its groups and its uses
                // are not written
                if (element is SceneRectangle or SceneCircle or ScenePath or SceneText && element.ClippingPath is { } clippingPath)
                {
                    VisitClipPath(clippingPath);
                }
            }

            // The region of a filter contains the element with its blur and its offset: with the bounds of the
            // shapes, so a horizontal line, which has no height, is still drawn; with the bounding box of the element
            // when it has a text, whose size is not known without measuring it
            void AddFilter(string id, SceneElement element, SceneFilter filter)
            {
                if (filter is SceneFilterGraph graph)
                {
                    AddFilterGraph(id, element, graph);
                    return;
                }

                var (deviation, offsetX, offsetY) = filter switch
                {
                    SceneBlurFilter blur => (Sanitize(blur.StandardDeviation), 0.0, 0.0),
                    SceneDropShadowFilter shadow => (Sanitize(shadow.StandardDeviation), Finite(shadow.OffsetX), Finite(shadow.OffsetY)),
                    _ => (0.0, 0.0, 0.0),
                };

                definitions.Append("<filter id=\"").Append(Escape(id)).Append('"');
                // The filter applies before the clips of the element, which wrap its output
                var hasText = false;
                var bounds = element.GetBounds(
                    _ =>
                    {
                        hasText = true;
                        return null;
                    },
                    applyClips: false);
                if (!hasText)
                {
                    var region = SceneGeometry.Inflate(bounds ?? default, (3 * deviation) + Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) + 1);
                    definitions.Append(" filterUnits=\"userSpaceOnUse\" x=\"").Append(Number(region.X)).Append("\" y=\"").Append(Number(region.Y))
                        .Append("\" width=\"").Append(Number(region.Width)).Append("\" height=\"").Append(Number(region.Height)).Append('"');
                }
                else
                {
                    definitions.Append(" x=\"-50%\" y=\"-50%\" width=\"200%\" height=\"200%\"");
                }

                definitions.Append(" color-interpolation-filters=\"sRGB\">");
                switch (filter)
                {
                    case SceneBlurFilter:
                        definitions.Append("<feGaussianBlur stdDeviation=\"").Append(Number(deviation)).Append("\"/>");
                        break;

                    case SceneColorMatrixFilter colorMatrix:
                        definitions.Append("<feColorMatrix type=\"matrix\" values=\"").Append(string.Join(' ', colorMatrix.Values.Select(value => SvgNumber.Format(value, 6)))).Append("\"/>");
                        break;

                    case SceneDropShadowFilter shadow:
                        definitions.Append("<feGaussianBlur in=\"SourceAlpha\" stdDeviation=\"").Append(Number(deviation)).Append("\"/>")
                            .Append("<feOffset dx=\"").Append(Number(offsetX)).Append("\" dy=\"").Append(Number(offsetY)).Append("\" result=\"shadow\"/>")
                            .Append("<feFlood flood-color=\"").Append(shadow.Color.ToHexRgb()).Append('"');
                        if (!shadow.Color.IsOpaque)
                        {
                            definitions.Append(" flood-opacity=\"").Append(SvgNumber.Format(shadow.Color.Opacity, 3)).Append('"');
                        }

                        definitions.Append("/><feComposite in2=\"shadow\" operator=\"in\"/><feMerge><feMergeNode/><feMergeNode in=\"SourceGraphic\"/></feMerge>");
                        break;
                }

                definitions.Append("</filter>\n");
            }

            // The region of a graph is its own, or the bounds of the element enlarged by the reach of the primitives
            // (with a margin of a pixel, as the other filters); the percentages of the bounding box for a text
            void AddFilterGraph(string id, SceneElement element, SceneFilterGraph graph)
            {
                definitions.Append("<filter id=\"").Append(Escape(id)).Append('"');
                var hasText = false;
                var bounds = element.GetBounds(
                    _ =>
                    {
                        hasText = true;
                        return null;
                    },
                    applyClips: false);
                if (graph.Region is { } region)
                {
                    AppendRegion(" filterUnits=\"userSpaceOnUse\"", region);
                }
                else if (!hasText)
                {
                    AppendRegion(" filterUnits=\"userSpaceOnUse\"", SceneGeometry.Inflate(graph.GetRegion(bounds ?? default), 1));
                }
                else
                {
                    definitions.Append(" x=\"-50%\" y=\"-50%\" width=\"200%\" height=\"200%\"");
                }

                definitions.Append(" primitiveUnits=\"userSpaceOnUse\">");

                // The results the other primitives read are named after the index of their primitive
                var referenced = new HashSet<int>();
                foreach (var primitive in graph.Primitives)
                {
                    foreach (var input in primitive.GetInputs())
                    {
                        if (input.Kind is SceneFilterInputKind.Result)
                        {
                            referenced.Add(input.ResultIndex);
                        }
                    }
                }

                for (var i = 0; i < graph.Primitives.Count; i++)
                {
                    AppendPrimitive(graph.Primitives[i], referenced.Contains(i) ? "r" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
                }

                definitions.Append("</filter>\n");
            }

            void AppendRegion(string prefix, SceneBounds region)
            {
                definitions.Append(prefix).Append(" x=\"").Append(Number(region.X)).Append("\" y=\"").Append(Number(region.Y))
                    .Append("\" width=\"").Append(Number(region.Width)).Append("\" height=\"").Append(Number(region.Height)).Append('"');
            }

            void AppendPrimitive(SceneFilterPrimitive primitive, string? result)
            {
                var (name, input) = primitive switch
                {
                    SceneGaussianBlurPrimitive blur => ("feGaussianBlur", blur.Input),
                    SceneOffsetPrimitive offset => ("feOffset", offset.Input),
                    SceneFloodPrimitive => ("feFlood", SceneFilterInput.Previous),
                    SceneCompositePrimitive composite => ("feComposite", composite.Input),
                    SceneBlendPrimitive blend => ("feBlend", blend.Input),
                    SceneMergePrimitive => ("feMerge", SceneFilterInput.Previous),
                    SceneMorphologyPrimitive morphology => ("feMorphology", morphology.Input),
                    SceneTurbulencePrimitive => ("feTurbulence", SceneFilterInput.Previous),
                    SceneDisplacementMapPrimitive displacement => ("feDisplacementMap", displacement.Input),
                    SceneConvolveMatrixPrimitive convolve => ("feConvolveMatrix", convolve.Input),
                    SceneDiffuseLightingPrimitive diffuse => ("feDiffuseLighting", diffuse.Input),
                    SceneSpecularLightingPrimitive specular => ("feSpecularLighting", specular.Input),
                    SceneComponentTransferPrimitive transfer => ("feComponentTransfer", transfer.Input),
                    SceneTilePrimitive tile => ("feTile", tile.Input),
                    SceneImagePrimitive => ("feImage", SceneFilterInput.Previous),
                    SceneColorMatrixPrimitive matrix => ("feColorMatrix", matrix.Input),
                    SceneDropShadowPrimitive shadow => ("feDropShadow", shadow.Input),
                    _ => throw new NotSupportedException($"The filter primitive '{primitive.GetType()}' is not supported."),
                };

                definitions.Append('<').Append(name);
                AppendInput("in", input);
                if (primitive.Subregion is { } subregion)
                {
                    AppendRegion("", subregion);
                }

                if (primitive.ColorSpace is SceneFilterColorSpace.Srgb)
                {
                    definitions.Append(" color-interpolation-filters=\"sRGB\"");
                }

                if (result is not null)
                {
                    definitions.Append(" result=\"").Append(result).Append('"');
                }

                var children = new StringBuilder();
                switch (primitive)
                {
                    case SceneGaussianBlurPrimitive blur:
                        AppendPair("stdDeviation", blur.StandardDeviationX, blur.StandardDeviationY);
                        AppendEdgeMode(blur.EdgeMode, SceneFilterEdgeMode.None);
                        break;

                    case SceneOffsetPrimitive offset:
                        AppendNumber("dx", offset.OffsetX);
                        AppendNumber("dy", offset.OffsetY);
                        break;

                    case SceneFloodPrimitive flood:
                        AppendColor("flood", flood.Color);
                        break;

                    case SceneCompositePrimitive composite:
                        AppendInput("in2", composite.Input2);
                        definitions.Append(" operator=\"").Append(composite.Operator switch
                        {
                            SceneCompositeOperator.In => "in",
                            SceneCompositeOperator.Out => "out",
                            SceneCompositeOperator.Atop => "atop",
                            SceneCompositeOperator.Xor => "xor",
                            SceneCompositeOperator.Lighter => "lighter",
                            SceneCompositeOperator.Arithmetic => "arithmetic",
                            _ => "over",
                        }).Append('"');
                        if (composite.Operator is SceneCompositeOperator.Arithmetic)
                        {
                            AppendFactor("k1", composite.K1);
                            AppendFactor("k2", composite.K2);
                            AppendFactor("k3", composite.K3);
                            AppendFactor("k4", composite.K4);
                        }

                        break;

                    case SceneBlendPrimitive blend:
                        AppendInput("in2", blend.Input2);
                        definitions.Append(" mode=\"").Append(BlendModeName(blend.Mode) ?? "normal").Append('"');
                        break;

                    case SceneMergePrimitive merge:
                        foreach (var mergeInput in merge.Inputs)
                        {
                            children.Append("<feMergeNode");
                            AppendInputTo(children, "in", mergeInput);
                            children.Append("/>");
                        }

                        break;

                    case SceneMorphologyPrimitive morphology:
                        if (morphology.Operator is SceneMorphologyOperator.Dilate)
                        {
                            definitions.Append(" operator=\"dilate\"");
                        }

                        AppendPair("radius", morphology.RadiusX, morphology.RadiusY);
                        break;

                    case SceneTurbulencePrimitive turbulence:
                        if (turbulence.Type is SceneTurbulenceType.FractalNoise)
                        {
                            definitions.Append(" type=\"fractalNoise\"");
                        }

                        definitions.Append(" baseFrequency=\"").Append(Factor(turbulence.BaseFrequencyX));
                        if (turbulence.BaseFrequencyY != turbulence.BaseFrequencyX)
                        {
                            definitions.Append(' ').Append(Factor(turbulence.BaseFrequencyY));
                        }

                        definitions.Append("\" numOctaves=\"").Append(turbulence.Octaves.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
                        definitions.Append(" seed=\"").Append(turbulence.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
                        if (turbulence.StitchTiles)
                        {
                            definitions.Append(" stitchTiles=\"stitch\"");
                        }

                        break;

                    case SceneDisplacementMapPrimitive displacement:
                        AppendInput("in2", displacement.Map);
                        AppendNumber("scale", displacement.Scale);
                        if (displacement.XChannel is not SceneColorChannel.Alpha)
                        {
                            definitions.Append(" xChannelSelector=\"").Append(ChannelName(displacement.XChannel)).Append('"');
                        }

                        if (displacement.YChannel is not SceneColorChannel.Alpha)
                        {
                            definitions.Append(" yChannelSelector=\"").Append(ChannelName(displacement.YChannel)).Append('"');
                        }

                        break;

                    case SceneConvolveMatrixPrimitive convolve:
                        definitions.Append(" order=\"").Append(convolve.OrderX.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        if (convolve.OrderY != convolve.OrderX)
                        {
                            definitions.Append(' ').Append(convolve.OrderY.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }

                        definitions.Append("\" kernelMatrix=\"").Append(string.Join(' ', convolve.Kernel.Select(Factor))).Append('"');
                        if (convolve.Divisor is { } divisor)
                        {
                            AppendFactor("divisor", divisor);
                        }

                        if (convolve.Bias != 0)
                        {
                            AppendFactor("bias", convolve.Bias);
                        }

                        if (convolve.TargetX is { } targetX)
                        {
                            definitions.Append(" targetX=\"").Append(targetX.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
                        }

                        if (convolve.TargetY is { } targetY)
                        {
                            definitions.Append(" targetY=\"").Append(targetY.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
                        }

                        AppendEdgeMode(convolve.EdgeMode, SceneFilterEdgeMode.Duplicate);
                        if (convolve.PreserveAlpha)
                        {
                            definitions.Append(" preserveAlpha=\"true\"");
                        }

                        break;

                    case SceneDiffuseLightingPrimitive diffuse:
                        AppendFactor("surfaceScale", diffuse.SurfaceScale);
                        AppendFactor("diffuseConstant", diffuse.DiffuseConstant);
                        definitions.Append(" lighting-color=\"").Append(diffuse.Color.ToHexRgb()).Append('"');
                        AppendLight(children, diffuse.Light);
                        break;

                    case SceneSpecularLightingPrimitive specular:
                        AppendFactor("surfaceScale", specular.SurfaceScale);
                        AppendFactor("specularConstant", specular.SpecularConstant);
                        AppendFactor("specularExponent", specular.SpecularExponent);
                        definitions.Append(" lighting-color=\"").Append(specular.Color.ToHexRgb()).Append('"');
                        AppendLight(children, specular.Light);
                        break;

                    case SceneComponentTransferPrimitive transfer:
                        AppendTransferFunction(children, "feFuncR", transfer.Red);
                        AppendTransferFunction(children, "feFuncG", transfer.Green);
                        AppendTransferFunction(children, "feFuncB", transfer.Blue);
                        AppendTransferFunction(children, "feFuncA", transfer.Alpha);
                        break;

                    case SceneImagePrimitive { IsInlineImage: true, Content: SceneImage image }:
                        definitions.Append(" preserveAspectRatio=\"").Append(AspectRatioValue(image.Alignment, image.Slice) ?? "xMidYMid").Append("\" href=\"").Append(Escape("data:" + image.MediaType + ";base64," + Convert.ToBase64String(image.Data.Span))).Append('"');
                        break;

                    case SceneImagePrimitive reference:
                        definitions.Append(" href=\"#").Append(Escape(reference.Content.Id ?? _contentIds[reference.Content])).Append('"');
                        break;

                    case SceneColorMatrixPrimitive matrix:
                        definitions.Append(" type=\"matrix\" values=\"").Append(string.Join(' ', matrix.Values.Select(Factor))).Append('"');
                        break;

                    case SceneDropShadowPrimitive shadow:
                        AppendNumber("dx", shadow.OffsetX);
                        AppendNumber("dy", shadow.OffsetY);
                        AppendPair("stdDeviation", shadow.StandardDeviationX, shadow.StandardDeviationY);
                        AppendColor("flood", shadow.Color);
                        break;
                }

                if (children.Length is 0)
                {
                    definitions.Append("/>");
                }
                else
                {
                    definitions.Append('>').Append(children).Append("</").Append(name).Append('>');
                }
            }

            void AppendInput(string attribute, SceneFilterInput input) => AppendInputTo(definitions, attribute, input);

            static void AppendInputTo(StringBuilder builder, string attribute, SceneFilterInput input)
            {
                var value = input.Kind switch
                {
                    SceneFilterInputKind.SourceGraphic => "SourceGraphic",
                    SceneFilterInputKind.SourceAlpha => "SourceAlpha",
                    SceneFilterInputKind.Result => "r" + input.ResultIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => null,
                };

                if (value is not null)
                {
                    builder.Append(' ').Append(attribute).Append("=\"").Append(value).Append('"');
                }
            }

            void AppendNumber(string attribute, double value) => definitions.Append(' ').Append(attribute).Append("=\"").Append(Number(value)).Append('"');

            void AppendFactor(string attribute, double value) => definitions.Append(' ').Append(attribute).Append("=\"").Append(Factor(value)).Append('"');

            // One value when both are equal, as SVG reads it
            void AppendPair(string attribute, double x, double y)
            {
                definitions.Append(' ').Append(attribute).Append("=\"").Append(Number(x));
                if (y != x)
                {
                    definitions.Append(' ').Append(Number(y));
                }

                definitions.Append('"');
            }

            void AppendColor(string prefix, SceneColor color)
            {
                definitions.Append(' ').Append(prefix).Append("-color=\"").Append(color.ToHexRgb()).Append('"');
                if (!color.IsOpaque)
                {
                    definitions.Append(' ').Append(prefix).Append("-opacity=\"").Append(SvgNumber.Format(color.Opacity, 3)).Append('"');
                }
            }

            void AppendEdgeMode(SceneFilterEdgeMode mode, SceneFilterEdgeMode defaultMode)
            {
                if (mode == defaultMode)
                    return;

                definitions.Append(" edgeMode=\"").Append(mode switch
                {
                    SceneFilterEdgeMode.Duplicate => "duplicate",
                    SceneFilterEdgeMode.Wrap => "wrap",
                    SceneFilterEdgeMode.Mirror => "mirror",
                    _ => "none",
                }).Append('"');
            }

            void AppendLight(StringBuilder builder, SceneLightSource light)
            {
                switch (light)
                {
                    case SceneDistantLight distant:
                        builder.Append("<feDistantLight azimuth=\"").Append(Factor(distant.Azimuth)).Append("\" elevation=\"").Append(Factor(distant.Elevation)).Append("\"/>");
                        break;

                    case ScenePointLight point:
                        builder.Append("<fePointLight x=\"").Append(Number(point.X)).Append("\" y=\"").Append(Number(point.Y)).Append("\" z=\"").Append(Number(point.Z)).Append("\"/>");
                        break;

                    case SceneSpotLight spot:
                        builder.Append("<feSpotLight x=\"").Append(Number(spot.X)).Append("\" y=\"").Append(Number(spot.Y)).Append("\" z=\"").Append(Number(spot.Z))
                            .Append("\" pointsAtX=\"").Append(Number(spot.PointsAtX)).Append("\" pointsAtY=\"").Append(Number(spot.PointsAtY)).Append("\" pointsAtZ=\"").Append(Number(spot.PointsAtZ)).Append('"');
                        if (spot.SpecularExponent != 1)
                        {
                            builder.Append(" specularExponent=\"").Append(Factor(spot.SpecularExponent)).Append('"');
                        }

                        if (spot.LimitingConeAngle is { } cone)
                        {
                            builder.Append(" limitingConeAngle=\"").Append(Factor(cone)).Append('"');
                        }

                        builder.Append("/>");
                        break;
                }
            }

            static void AppendTransferFunction(StringBuilder builder, string name, SceneTransferFunction function)
            {
                if (function.Kind is SceneTransferFunctionKind.Identity)
                    return;

                builder.Append('<').Append(name).Append(" type=\"");
                switch (function.Kind)
                {
                    case SceneTransferFunctionKind.Table or SceneTransferFunctionKind.Discrete:
                        builder.Append(function.Kind is SceneTransferFunctionKind.Table ? "table" : "discrete").Append("\" tableValues=\"").Append(string.Join(' ', function.Values.Select(Factor)));
                        break;

                    case SceneTransferFunctionKind.Linear:
                        builder.Append("linear\" slope=\"").Append(Factor(function.Slope)).Append("\" intercept=\"").Append(Factor(function.Intercept));
                        break;

                    case SceneTransferFunctionKind.Gamma:
                        builder.Append("gamma\" amplitude=\"").Append(Factor(function.Amplitude)).Append("\" exponent=\"").Append(Factor(function.Exponent)).Append("\" offset=\"").Append(Factor(function.Offset));
                        break;
                }

                builder.Append("\"/>");
            }

            static string ChannelName(SceneColorChannel channel) => channel switch
            {
                SceneColorChannel.Red => "R",
                SceneColorChannel.Green => "G",
                SceneColorChannel.Blue => "B",
                _ => "A",
            };

            // The factors of the primitives (weights, constants, frequencies, angles) have six decimals, as the color matrices
            static string Factor(double value) => SvgNumber.Format(value, 6);

            static double Sanitize(double deviation) => deviation > 0 && double.IsFinite(deviation) ? deviation : 0;

            static double Finite(double value) => double.IsFinite(value) ? value : 0;

            // The identifier is set before the content is visited: a marker drawn on its own content is not visited again
            void AddMarker(SceneMarker? marker)
            {
                if (marker is null || _ids.ContainsKey(marker))
                    return;

                var markerId = NextId("marker", ref markerCount);
                _ids[marker] = markerId;
                _markers.Add((markerId, marker));
                Visit(marker.Content);
            }

            void AddPaint(ScenePaint? paint)
            {
                if (paint is PatternPaint pattern)
                {
                    // The identifier is set before the content is visited: a pattern that paints with itself is not visited again
                    if (_ids.ContainsKey(pattern))
                        return;

                    var patternId = NextId("pattern", ref patternCount);
                    _ids[pattern] = patternId;
                    _patterns.Add((patternId, pattern));
                    Visit(pattern.Content);
                    return;
                }

                if (paint is not SceneGradientPaint || _ids.ContainsKey(paint))
                    return;

                var id = NextId("gradient", ref gradientCount);
                _ids[paint] = id;
                IReadOnlyList<SceneGradientStop> stops;
                string elementName;
                if (paint is LinearGradientPaint gradient)
                {
                    elementName = "linearGradient";
                    stops = gradient.Stops;
                    definitions.Append("<linearGradient id=\"").Append(Escape(id)).Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"").Append(Number(gradient.X1))
                        .Append("\" y1=\"").Append(Number(gradient.Y1)).Append("\" x2=\"").Append(Number(gradient.X2))
                        .Append("\" y2=\"").Append(Number(gradient.Y2)).Append('"');
                }
                else
                {
                    var radial = (RadialGradientPaint)paint;
                    elementName = "radialGradient";
                    stops = radial.Stops;
                    definitions.Append("<radialGradient id=\"").Append(Escape(id)).Append("\" gradientUnits=\"userSpaceOnUse\" cx=\"").Append(Number(radial.CenterX))
                        .Append("\" cy=\"").Append(Number(radial.CenterY)).Append("\" r=\"").Append(Number(radial.Radius)).Append('"');
                    if (radial.FocalX != radial.CenterX || radial.FocalY != radial.CenterY)
                    {
                        definitions.Append(" fx=\"").Append(Number(radial.FocalX)).Append("\" fy=\"").Append(Number(radial.FocalY)).Append('"');
                    }

                    if (radial.FocalRadius > 0 && double.IsFinite(radial.FocalRadius))
                    {
                        definitions.Append(" fr=\"").Append(Number(radial.FocalRadius)).Append('"');
                    }
                }

                var gradientPaint = (SceneGradientPaint)paint;
                switch (gradientPaint.Spread)
                {
                    case SceneGradientSpread.Repeat:
                        definitions.Append(" spreadMethod=\"repeat\"");
                        break;
                    case SceneGradientSpread.Reflect:
                        definitions.Append(" spreadMethod=\"reflect\"");
                        break;
                }

                if (gradientPaint.Transform is { } gradientTransform)
                {
                    definitions.Append(" gradientTransform=\"").Append(Escape(Transform(gradientTransform))).Append('"');
                }

                definitions.Append(">\n");

                foreach (var stop in stops)
                {
                    definitions.Append("<stop offset=\"").Append(SvgNumber.Format(stop.Offset, 4)).Append("\" stop-color=\"").Append(stop.Color.ToHexRgb()).Append('"');
                    if (!stop.Color.IsOpaque)
                    {
                        definitions.Append(" stop-opacity=\"").Append(SvgNumber.Format(stop.Color.Opacity, 3)).Append('"');
                    }

                    definitions.Append("/>\n");
                }

                definitions.Append("</").Append(elementName).Append(">\n");
            }
        }

        private void WriteElement(SceneElement element)
        {
            _pendingId = element.Id ?? _contentIds.GetValueOrDefault(element);
            if (!string.IsNullOrEmpty(element.Hyperlink))
            {
                StartTag("a");
                Attribute("href", element.Hyperlink);
                output.Write(">\n");
                WriteLinkedElement(element);
                output.Write("</a>\n");
                return;
            }

            WriteLinkedElement(element);
        }

        // An element with a transform (or a rotated text) and a mask, a filter or a clipping path is wrapped in a group
        // that has them: they are in the coordinates of its parent, as for a group
        private void WriteLinkedElement(SceneElement element)
        {
            var hasTransform = element.Transform is not null || element is SceneText { Path: null } text && text.Rotation != 0;
            if (element is not SceneGroup && hasTransform && (element.Mask is not null || element.Filter is not null || element.ClippingPath is not null))
            {
                StartTag("g");
                EffectAttributes(element);
                output.Write(">\n");
                _effectsOnWrapper = element;
                WriteElementContent(element);
                _effectsOnWrapper = null;
                output.Write("</g>\n");
                return;
            }

            WriteElementContent(element);
        }

        private void WriteElementContent(SceneElement element)
        {
            switch (element)
            {
                case SceneGroup group:
                    WriteGroupContent(group);
                    break;

                case SceneRectangle rectangle:
                    StartTag("rect");
                    Attribute("x", rectangle.X);
                    Attribute("y", rectangle.Y);
                    Attribute("width", rectangle.Width);
                    Attribute("height", rectangle.Height);
                    if (rectangle.CornerRadius > 0)
                    {
                        Attribute("rx", rectangle.CornerRadius);
                    }

                    ShapeAttributes(rectangle);
                    EndElement("rect", rectangle);
                    break;

                case SceneCircle circle:
                    StartTag("circle");
                    Attribute("cx", circle.CenterX);
                    Attribute("cy", circle.CenterY);
                    Attribute("r", circle.Radius);
                    ShapeAttributes(circle);
                    EndElement("circle", circle);
                    break;

                case SceneLine line:
                    StartTag("line");
                    Attribute("x1", line.X1);
                    Attribute("y1", line.Y1);
                    Attribute("x2", line.X2);
                    Attribute("y2", line.Y2);
                    ShapeAttributes(line, isLine: true);
                    MarkerAttributes(line.MarkerStart, null, line.MarkerEnd);
                    EndElement("line", line);
                    break;

                case ScenePath path:
                    StartTag("path");
                    Attribute("d", SvgNumber.FormatPath(path.Data, _precision));
                    if (path.FillRule is SceneFillRule.EvenOdd)
                    {
                        Attribute("fill-rule", "evenodd");
                    }

                    ShapeAttributes(path);
                    MarkerAttributes(path.MarkerStart, path.MarkerMid, path.MarkerEnd);
                    EndElement("path", path);
                    break;

                case SceneText text:
                    WriteText(text);
                    break;

                // A use in its own content draws nothing: it is an empty group, which the uses that refer to it can draw
                case SceneUse use when use.IsCircular():
                    if (_pendingId is not null)
                    {
                        StartTag("g");
                        output.Write("/>\n");
                    }

                    break;

                case SceneUse use:
                    StartTag("use");
                    Attribute("href", "#" + (use.Content.Id ?? _contentIds[use.Content]));
                    CommonAttributes(use);
                    EndElement("use", use);
                    break;

                case SceneImage image:
                    StartTag("image");
                    Attribute("x", image.X);
                    Attribute("y", image.Y);
                    Attribute("width", image.Width);
                    Attribute("height", image.Height);
                    Attribute("preserveAspectRatio", AspectRatioValue(image.Alignment, image.Slice) ?? "xMidYMid");
                    Attribute("href", "data:" + image.MediaType + ";base64," + Convert.ToBase64String(image.Data.Span));
                    CommonAttributes(image);
                    EndElement("image", image);
                    break;
            }
        }

        private void WriteGroupContent(SceneGroup group)
        {
            // The clips are in the coordinates of the parent: an outer group clips, an inner one transforms. The last
            // clip is on the group itself when it has no transform.
            // The blend mode, the opacity, the mask, the clips and the filter apply in this order, from the outside. The
            // opacity is on the group itself, unless the group has a filter: it applies after the filter, which can
            // change the alpha (a color matrix)
            List<(string Name, string Value)> layers = [];
            if (BlendModeName(group.BlendMode) is { } blendMode)
            {
                layers.Add(("style", "mix-blend-mode:" + blendMode));
            }

            if (HasOuterOpacity(group))
            {
                layers.Add(("opacity", SvgNumber.Format(Math.Max(0, group.Opacity), 3)));
            }

            if (_maskIds.TryGetValue(group, out var maskId))
            {
                layers.Add(("mask", "url(#" + maskId + ")"));
            }

            if (_ids.TryGetValue(group, out var clipId))
            {
                layers.Add(("clip-path", "url(#" + clipId + ")"));
            }

            if (_clipPathIds.TryGetValue(group, out var clipPathId))
            {
                layers.Add(("clip-path", "url(#" + clipPathId + ")"));
            }

            if (group.ClippingPath is { } clippingPath)
            {
                layers.Add(("clip-path", "url(#" + GetClipPathId(clippingPath, transform: null) + ")"));
            }

            if (_filterIds.TryGetValue(group, out var filterId))
            {
                layers.Add(("filter", "url(#" + filterId + ")"));
            }

            var outerClips = group.Transform is null && layers.Count > 0 ? layers.Count - 1 : layers.Count;
            for (var i = 0; i < outerClips; i++)
            {
                StartTag("g");
                Attribute(layers[i].Name, layers[i].Value);
                output.Write(">\n");
            }

            StartTag("g");
            CommonAttributes(group);
            if (group.Transform is { } transform)
            {
                Attribute("transform", Transform(transform));
            }
            else if (layers.Count > 0)
            {
                Attribute(layers[^1].Name, layers[^1].Value);
            }

            output.Write(">\n");
            WriteTooltip(group);
            if (group.ViewBox is { } viewBox)
            {
                // A nested svg element maps its view box to its viewport, and cuts what is outside of it. A view box that
                // draws nothing has no children, except in definitions when a use draws one of them.
                if (viewBox.GetTransform() is null)
                {
                    if (group.Children.SelectMany(SceneTree.GetDescendants).Any(_useTargets.Contains))
                    {
                        output.Write("<defs>\n");
                        foreach (var child in group.Children)
                        {
                            WriteElement(child);
                        }

                        output.Write("</defs>\n");
                    }
                }
                else
                {
                    output.Write("<svg");
                    Attribute("x", viewBox.Viewport.X);
                    Attribute("y", viewBox.Viewport.Y);
                    Attribute("width", viewBox.Viewport.Width);
                    Attribute("height", viewBox.Viewport.Height);
                    Attribute("viewBox", Number(viewBox.Box.X) + " " + Number(viewBox.Box.Y) + " " + Number(viewBox.Box.Width) + " " + Number(viewBox.Box.Height));
                    if (AspectRatioValue(viewBox) is { } aspectRatio)
                    {
                        Attribute("preserveAspectRatio", aspectRatio);
                    }

                    output.Write(">\n");
                    foreach (var child in group.Children)
                    {
                        WriteElement(child);
                    }

                    output.Write("</svg>\n");
                }
            }
            else
            {
                foreach (var child in group.Children)
                {
                    WriteElement(child);
                }
            }

            output.Write("</g>\n");
            for (var i = 0; i < outerClips; i++)
            {
                output.Write("</g>\n");
            }
        }

        private void WriteText(SceneText text, string? clipPathId = null)
        {
            StartTag("text");
            var textPathId = _textPathIds.GetValueOrDefault(text);
            if (textPathId is null)
            {
                Attribute("x", text.X);
                Attribute("y", text.Y);
            }
            switch (text.Anchor)
            {
                case SceneTextAnchor.Middle:
                    Attribute("text-anchor", "middle");
                    break;
                case SceneTextAnchor.End:
                    Attribute("text-anchor", "end");
                    break;
            }

            if (text.Direction is SceneTextDirection.RightToLeft)
            {
                Attribute("direction", "rtl");
            }

            if (BaselineValue(text.Baseline) is { } baseline)
            {
                Attribute("dominant-baseline", baseline);
            }

            if (text.Font.Family is { } family && !string.Equals(family, _scene.FontFamily, StringComparison.Ordinal))
            {
                Attribute("font-family", FamilyList(family));
            }

            Attribute("font-size", text.Font.Size);
            // 700 is written "bold", as before the weights; the other weights are numbers
            switch (text.Font.ClampedWeight)
            {
                case SceneFont.NormalWeight:
                    break;
                case SceneFont.BoldWeight:
                    Attribute("font-weight", "bold");
                    break;
                case var weight:
                    Attribute("font-weight", weight.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }

            // The lines of a text are drawn under its spans too, and a span cannot remove them: with spans, each span has
            // its lines. SVG paints the lines with the fill of the element that has them: lines of another paint are on
            // the text, and the glyphs in a tspan with their own fill.
            var (fill, fillColor) = TextPlacement.GetFill(text, null);
            var decoration = text.Spans.Count is 0 ? DecorationValue(text.Decorations) : null;
            var decorationFill = decoration is not null && text.DecorationFill is { } linePaint && !TextPlacement.IsSamePaint((linePaint, default), (fill, fillColor)) ? linePaint : null;
            if (decoration is not null)
            {
                Attribute("text-decoration", decoration);
            }

            if (text.Font.Italic)
            {
                Attribute("font-style", "italic");
            }

            if (TextSpacing.Normalize(text.LetterSpacing) is not 0 and var letterSpacing)
            {
                Attribute("letter-spacing", letterSpacing);
            }

            if (TextSpacing.Normalize(text.WordSpacing) is not 0 and var wordSpacing)
            {
                Attribute("word-spacing", wordSpacing);
            }

            if (text.TextLength is { } length && length >= 0 && double.IsFinite(length))
            {
                Attribute("textLength", length);
                if (text.LengthAdjust is SceneTextLengthAdjust.SpacingAndGlyphs)
                {
                    Attribute("lengthAdjust", "spacingAndGlyphs");
                }
            }

            if (decorationFill is not null)
            {
                Paint("fill", decorationFill);
            }
            else
            {
                Fill(fill, fillColor);
            }

            if (TextPlacement.HasStroke(text))
            {
                if (text.Stroke is not null)
                {
                    Paint("stroke", text.Stroke);
                }

                TextStrokeAttributes(text);
                if (TextPlacement.IsStrokeFirst(text.PaintOrder))
                {
                    Attribute("paint-order", "stroke");
                }
            }

            // The rotation applies before the transform: it is on its right. In a clipping path, the transform has the
            // transforms of the groups of its content.
            var rotation = text.Rotation != 0 && textPathId is null ? "rotate(" + Number(text.Rotation) + " " + Number(text.X) + " " + Number(text.Y) + ")" : null;
            if ((_inClipPath ? _clipTextTransform : text.Transform) is { } textTransform)
            {
                Attribute("transform", rotation is null ? Transform(textTransform) : Transform(textTransform) + " " + rotation);
            }
            else if (rotation is not null)
            {
                Attribute("transform", rotation);
            }

            CommonAttributes(text);
            if (clipPathId is not null)
            {
                Attribute("clip-path", "url(#" + clipPathId + ")");
            }

            output.Write('>');
            if (!string.IsNullOrEmpty(text.Tooltip))
            {
                output.Write("<title>");
                output.Write(Escape(text.Tooltip));
                output.Write("</title>");
            }

            if (textPathId is not null)
            {
                output.Write("<textPath");
                Attribute("href", "#" + textPathId);
                if (text.PathStartOffset != 0 && double.IsFinite(text.PathStartOffset))
                {
                    Attribute("startOffset", text.PathStartOffset);
                }

                output.Write('>');
            }

            if (text.Spans.Count is 0)
            {
                if (decorationFill is not null)
                {
                    output.Write("<tspan");
                    Fill(fill, fillColor);
                    ResetOpacity("fill-opacity", fill, fillColor, decorationFill, default);
                    output.Write('>');
                    output.Write(Escape(text.Text));
                    output.Write("</tspan>");
                }
                else
                {
                    output.Write(Escape(text.Text));
                }
            }
            else
            {
                foreach (var span in text.Spans)
                {
                    WriteSpan(text, span, onPath: textPathId is not null);
                }
            }

            if (textPathId is not null)
            {
                output.Write("</textPath>");
            }

            output.Write("</text>\n");
        }

        // A span is a tspan with the attributes that differ from the text, or its characters when it has none. A span
        // whose lines have another paint than its glyphs has them, and its glyphs are in a tspan with their own fill.
        private void WriteSpan(SceneText text, SceneTextSpan span, bool onPath)
        {
            var font = span.Font ?? text.Font;
            var (fill, fillColor) = TextPlacement.GetFill(text, span);
            var (textFill, textFillColor) = TextPlacement.GetFill(text, null);
            var hasFill = !TextPlacement.IsSamePaint((fill, fillColor), (textFill, textFillColor));
            var stroke = TextPlacement.HasStrokeWidth(text) ? span.Stroke : null;
            var hasStroke = stroke is not null && !ReferenceEquals(stroke, text.Stroke);
            var decoration = DecorationValue(span.Decorations ?? text.Decorations);
            var decorationFill = decoration is not null && (span.DecorationFill ?? text.DecorationFill) is { } linePaint && !TextPlacement.IsSamePaint((linePaint, default), (fill, fillColor)) ? linePaint : null;
            var letterSpacing = TextSpacing.Normalize(span.LetterSpacing ?? text.LetterSpacing);
            var wordSpacing = TextSpacing.Normalize(span.WordSpacing ?? text.WordSpacing);
            var hasLetterSpacing = letterSpacing != TextSpacing.Normalize(text.LetterSpacing);
            var hasWordSpacing = wordSpacing != TextSpacing.Normalize(text.WordSpacing);
            var x = !onPath && span.X is { } spanX && double.IsFinite(spanX) ? spanX : (double?)null;
            var y = !onPath && span.Y is { } spanY && double.IsFinite(spanY) ? spanY : (double?)null;
            var dx = double.IsFinite(span.Dx) ? span.Dx : 0;
            var dy = double.IsFinite(span.Dy) ? span.Dy : 0;
            var rotate = onPath ? 0 : TextPlacement.GetGlyphRotation(span);
            var shift = double.IsFinite(span.BaselineShift) ? span.BaselineShift : 0;
            var hasPosition = x is not null || y is not null || dx != 0 || dy != 0 || rotate != 0 || shift != 0;
            var hasStyle = font != text.Font || hasFill || hasStroke || decoration is not null || hasLetterSpacing || hasWordSpacing || hasPosition;
            if (!hasStyle)
            {
                output.Write(Escape(span.Text));
                return;
            }

            output.Write("<tspan");
            if (x is { } xValue)
            {
                Attribute("x", xValue);
            }

            if (y is { } yValue)
            {
                Attribute("y", yValue);
            }

            if (dx != 0)
            {
                Attribute("dx", dx);
            }

            if (dy != 0)
            {
                Attribute("dy", dy);
            }

            if (rotate != 0)
            {
                Attribute("rotate", rotate);
            }

            if (shift != 0)
            {
                Attribute("baseline-shift", shift);
            }

            if (!string.Equals(font.Family, text.Font.Family, StringComparison.Ordinal))
            {
                Attribute("font-family", FamilyList(font.Family ?? _scene.FontFamily));
            }

            if (font.Size != text.Font.Size)
            {
                Attribute("font-size", font.Size);
            }

            if (font.ClampedWeight != text.Font.ClampedWeight)
            {
                Attribute("font-weight", font.ClampedWeight switch
                {
                    SceneFont.NormalWeight => "normal",
                    SceneFont.BoldWeight => "bold",
                    var weight => weight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }

            if (font.Italic != text.Font.Italic)
            {
                Attribute("font-style", font.Italic ? "italic" : "normal");
            }

            if (decorationFill is not null)
            {
                // The lines have the fill of the tspan, the glyphs the fill of the inner tspan
                Paint("fill", decorationFill);
                ResetOpacity("fill-opacity", decorationFill, default, textFill, textFillColor);
            }
            else if (hasFill)
            {
                // A span inherits the opacity of the text: an opaque span of a translucent text resets it
                Fill(fill, fillColor);
                ResetOpacity("fill-opacity", fill, fillColor, textFill, textFillColor);
            }

            if (decoration is not null)
            {
                Attribute("text-decoration", decoration);
            }

            if (hasStroke)
            {
                Paint("stroke", stroke);
                ResetOpacity("stroke-opacity", stroke, default, text.Stroke, default);
            }

            if (hasLetterSpacing)
            {
                Attribute("letter-spacing", letterSpacing);
            }

            if (hasWordSpacing)
            {
                Attribute("word-spacing", wordSpacing);
            }

            output.Write('>');
            if (decorationFill is not null)
            {
                output.Write("<tspan");
                Fill(fill, fillColor);
                ResetOpacity("fill-opacity", fill, fillColor, decorationFill, default);
                output.Write('>');
                output.Write(Escape(span.Text));
                output.Write("</tspan>");
            }
            else
            {
                output.Write(Escape(span.Text));
            }

            output.Write("</tspan>");
        }

        // The fill of a text: its paint, or its color
        private void Fill(ScenePaint? paint, SceneColor color)
        {
            if (paint is null)
            {
                Paint("fill", color);
            }
            else
            {
                Paint("fill", paint);
            }
        }

        // An element inherits the opacity of the paint of its parent when its own paint has none: it is reset to 1
        private void ResetOpacity(string name, ScenePaint? paint, SceneColor color, ScenePaint? parentPaint, SceneColor parentColor)
        {
            if (WrittenOpacity(paint, color) >= 1 && WrittenOpacity(parentPaint, parentColor) < 1)
            {
                Attribute(name, "1");
            }

            // The opacity a paint attribute is written with: the one of a color; the other paints have none
            static double WrittenOpacity(ScenePaint? paint, SceneColor color) => paint switch
            {
                null => color.Opacity,
                SolidPaint solid => solid.Color.Opacity,
                _ => 1,
            };
        }

        // The width, the dashes, the caps and the joins of the outline of a text
        private void TextStrokeAttributes(SceneText text)
        {
            if (text.StrokeWidth != 1)
            {
                Attribute("stroke-width", text.StrokeWidth);
            }

            if (text.StrokeDashArray is { Count: > 0 } dashes)
            {
                Attribute("stroke-dasharray", string.Join(' ', dashes.Select(Number)));
                if (text.StrokeDashOffset != 0 && double.IsFinite(text.StrokeDashOffset))
                {
                    Attribute("stroke-dashoffset", text.StrokeDashOffset);
                }
            }

            switch (text.LineCap)
            {
                case SceneLineCap.Round:
                    Attribute("stroke-linecap", "round");
                    break;
                case SceneLineCap.Square:
                    Attribute("stroke-linecap", "square");
                    break;
            }

            switch (text.LineJoin)
            {
                case SceneLineJoin.Round:
                    Attribute("stroke-linejoin", "round");
                    break;
                case SceneLineJoin.Bevel:
                    Attribute("stroke-linejoin", "bevel");
                    break;
                case SceneLineJoin.Miter when text.MiterLimit != 4 && text.MiterLimit >= 1 && double.IsFinite(text.MiterLimit):
                    Attribute("stroke-miterlimit", text.MiterLimit);
                    break;
            }
        }

        private static string? BaselineValue(SceneTextBaseline baseline) => baseline switch
        {
            SceneTextBaseline.Middle => "middle",
            SceneTextBaseline.Central => "central",
            SceneTextBaseline.Hanging => "hanging",
            SceneTextBaseline.Mathematical => "mathematical",
            SceneTextBaseline.Ideographic => "ideographic",
            SceneTextBaseline.TextTop => "text-top",
            SceneTextBaseline.TextBottom => "text-bottom",
            _ => null,
        };

        private static string? DecorationValue(SceneTextDecorations decorations)
        {
            var values = new List<string>(3);
            if (decorations.HasFlag(SceneTextDecorations.Underline))
            {
                values.Add("underline");
            }

            if (decorations.HasFlag(SceneTextDecorations.Overline))
            {
                values.Add("overline");
            }

            if (decorations.HasFlag(SceneTextDecorations.Strikethrough))
            {
                values.Add("line-through");
            }

            return values.Count is 0 ? null : string.Join(' ', values);
        }

        private void ShapeAttributes(SceneShape shape, bool isLine = false)
        {
            // A line is not filled; its fill is the context-fill of its markers
            if (!isLine || shape is SceneLine { MarkerStart: not null } or SceneLine { MarkerEnd: not null })
            {
                Paint("fill", shape.Fill);
            }

            if (shape.Stroke is not null)
            {
                Paint("stroke", shape.Stroke);
                if (shape.StrokeWidth != 1)
                {
                    Attribute("stroke-width", shape.StrokeWidth);
                }

                if (shape.StrokeDashArray is { Count: > 0 } dashes)
                {
                    Attribute("stroke-dasharray", string.Join(' ', dashes.Select(Number)));
                    if (shape.StrokeDashOffset != 0 && double.IsFinite(shape.StrokeDashOffset))
                    {
                        Attribute("stroke-dashoffset", shape.StrokeDashOffset);
                    }
                }

                switch (shape.LineCap)
                {
                    case SceneLineCap.Round:
                        Attribute("stroke-linecap", "round");
                        break;
                    case SceneLineCap.Square:
                        Attribute("stroke-linecap", "square");
                        break;
                }

                switch (shape.LineJoin)
                {
                    case SceneLineJoin.Round:
                        Attribute("stroke-linejoin", "round");
                        break;
                    case SceneLineJoin.Bevel:
                        Attribute("stroke-linejoin", "bevel");
                        break;
                    case SceneLineJoin.Miter when shape.MiterLimit != 4 && shape.MiterLimit >= 1 && double.IsFinite(shape.MiterLimit):
                        Attribute("stroke-miterlimit", shape.MiterLimit);
                        break;
                }

                if (shape.NonScalingStroke)
                {
                    Attribute("vector-effect", "non-scaling-stroke");
                }
            }

            if (PaintOrderValue(shape.PaintOrder) is { } paintOrder)
            {
                Attribute("paint-order", paintOrder);
            }

            CommonAttributes(shape);
        }

        // The shortest value of paint-order: the parts that are not named follow in the order of normal
        private static string? PaintOrderValue(ScenePaintOrder order) => order switch
        {
            ScenePaintOrder.FillMarkersStroke => "fill markers",
            ScenePaintOrder.StrokeFillMarkers => "stroke",
            ScenePaintOrder.StrokeMarkersFill => "stroke markers",
            ScenePaintOrder.MarkersFillStroke => "markers",
            ScenePaintOrder.MarkersStrokeFill => "markers stroke",
            _ => null,
        };

        private bool HasOuterOpacity(SceneElement element) => element is SceneGroup && element.Opacity < 1 && _filterIds.ContainsKey(element);

        private void CommonAttributes(SceneElement element)
        {
            // The transform of a group is written by WriteGroupContent, the one of a text with its rotation
            if (element is not (SceneGroup or SceneText) && element.Transform is { } transform)
            {
                Attribute("transform", Transform(transform));
            }

            // The layers of a group are written by WriteGroupContent
            if (element is SceneGroup)
            {
                if (element.Opacity < 1 && !HasOuterOpacity(element))
                {
                    Attribute("opacity", SvgNumber.Format(Math.Max(0, element.Opacity), 3));
                }
            }
            else if (element != _effectsOnWrapper && !_inClipPath)
            {
                EffectAttributes(element);
            }

            if (!string.IsNullOrEmpty(element.CssClass))
            {
                Attribute("class", element.CssClass);
            }

            if (element.DataAttributes.Count > 0)
            {
                foreach (var (name, value) in element.DataAttributes.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    Attribute("data-" + name, value);
                }
            }
        }

        private static string? AspectRatioValue(SceneViewBox viewBox) => AspectRatioValue(viewBox.Alignment, viewBox.Slice);

        // The value of preserveAspectRatio, or null for the default (xMidYMid meet)
        private static string? AspectRatioValue(SceneAspectRatio aspectRatio, bool slice)
        {
            var alignment = aspectRatio switch
            {
                SceneAspectRatio.None => "none",
                SceneAspectRatio.XMinYMin => "xMinYMin",
                SceneAspectRatio.XMidYMin => "xMidYMin",
                SceneAspectRatio.XMaxYMin => "xMaxYMin",
                SceneAspectRatio.XMinYMid => "xMinYMid",
                SceneAspectRatio.XMaxYMid => "xMaxYMid",
                SceneAspectRatio.XMinYMax => "xMinYMax",
                SceneAspectRatio.XMidYMax => "xMidYMax",
                SceneAspectRatio.XMaxYMax => "xMaxYMax",
                _ => "xMidYMid",
            };

            if (slice && aspectRatio is not SceneAspectRatio.None)
                return alignment + " slice";

            return alignment is "xMidYMid" ? null : alignment;
        }

        private void MarkerAttributes(SceneMarker? start, SceneMarker? middle, SceneMarker? end)
        {
            if (start is not null)
            {
                Attribute("marker-start", "url(#" + _ids[start] + ")");
            }

            if (middle is not null)
            {
                Attribute("marker-mid", "url(#" + _ids[middle] + ")");
            }

            if (end is not null)
            {
                Attribute("marker-end", "url(#" + _ids[end] + ")");
            }
        }

        // The opacity, the filter, the mask and the blend mode of an element that is not a group
        private void EffectAttributes(SceneElement element)
        {
            if (element.Opacity < 1)
            {
                Attribute("opacity", SvgNumber.Format(Math.Max(0, element.Opacity), 3));
            }

            if (_filterIds.TryGetValue(element, out var filterId))
            {
                Attribute("filter", "url(#" + filterId + ")");
            }

            if (element.ClippingPath is { } clippingPath)
            {
                Attribute("clip-path", "url(#" + GetClipPathId(clippingPath, transform: null) + ")");
            }

            if (_maskIds.TryGetValue(element, out var maskId))
            {
                Attribute("mask", "url(#" + maskId + ")");
            }

            if (BlendModeName(element.BlendMode) is { } blendMode)
            {
                Attribute("style", "mix-blend-mode:" + blendMode);
            }
        }

        // The identifier of the definition of a clipping path whose content is mapped by a transform, created the first
        // time it is needed. The clipping paths that cut it have the same transform.
        private string GetClipPathId(SceneClipPath clipPath, SceneTransform? transform)
        {
            if (_clipPathDefinitionIds.TryGetValue((clipPath, transform), out var id))
                return id;

            id = NextId("clip", ref _clipCount);
            _clipPathDefinitionIds[(clipPath, transform)] = id;
            _clipPathDefinitions.Add((id, clipPath, transform));
            return id;
        }

        // A clipping path cut by a chain of clipping paths that loops (back to itself, or to one of the chain) leaves
        // nothing visible: it is written empty
        private void WriteClipPathDefinition(string id, SceneClipPath clipPath, SceneTransform? transform)
        {
            output.Write("<clipPath");
            Attribute("id", id);
            var isCircular = false;
            var chain = new HashSet<SceneClipPath>(ReferenceEqualityComparer.Instance);
            for (var current = clipPath; current is not null; current = current.ClippingPath)
            {
                if (!chain.Add(current))
                {
                    isCircular = true;
                    break;
                }
            }

            if (isCircular)
            {
                output.Write("/>\n");
                return;
            }

            if (clipPath.ClippingPath is { } outerClipPath)
            {
                Attribute("clip-path", "url(#" + GetClipPathId(outerClipPath, transform) + ")");
            }

            output.Write(">\n");
            _inClipPath = true;
            try
            {
                WriteClipContent(clipPath.Content, transform, new HashSet<SceneUse>(ReferenceEqualityComparer.Instance));
            }
            finally
            {
                _inClipPath = false;
            }

            output.Write("</clipPath>\n");
        }

        // The content of a clipping path is made of shapes and texts: the groups and the uses are flattened, their
        // transforms on the shapes. The clipping path of a shape is in the coordinates of its parent, outside of its
        // transform: it is written for the space of the shape.
        private void WriteClipContent(SceneElement element, SceneTransform? parentTransform, HashSet<SceneUse> uses)
        {
            var transform = element.Transform is { } own ? (parentTransform is { } parent ? own.Then(parent) : own) : parentTransform;
            switch (element)
            {
                case SceneGroup group:
                    foreach (var child in group.Children)
                    {
                        WriteClipContent(child, transform, uses);
                    }

                    return;

                case SceneUse use when !use.IsCircular() && uses.Add(use):
                    WriteClipContent(use.Content, transform, uses);
                    uses.Remove(use);
                    return;

                case SceneRectangle or SceneCircle or ScenePath or SceneText:
                    break;

                default:
                    return;
            }

            string? clipPathId = null;
            if (element.ClippingPath is { } clippingPath)
            {
                SceneTransform? inverse = null;
                if (element.Transform is { } elementTransform && (inverse = Invert(elementTransform)) is null)
                    return;

                clipPathId = GetClipPathId(clippingPath, inverse);
            }

            // The identifiers are written by the definitions of the elements: the content of a clipping path is its geometry
            _pendingId = null;
            switch (element)
            {
                case SceneRectangle rectangle:
                    StartTag("rect");
                    Attribute("x", rectangle.X);
                    Attribute("y", rectangle.Y);
                    Attribute("width", rectangle.Width);
                    Attribute("height", rectangle.Height);
                    if (rectangle.CornerRadius > 0)
                    {
                        Attribute("rx", rectangle.CornerRadius);
                    }

                    break;

                case SceneCircle circle:
                    StartTag("circle");
                    Attribute("cx", circle.CenterX);
                    Attribute("cy", circle.CenterY);
                    Attribute("r", circle.Radius);
                    break;

                case ScenePath path:
                    StartTag("path");
                    Attribute("d", SvgNumber.FormatPath(path.Data, _precision));
                    if (path.FillRule is SceneFillRule.EvenOdd)
                    {
                        Attribute("clip-rule", "evenodd");
                    }

                    break;

                case SceneText text:
                    _clipTextTransform = transform;
                    try
                    {
                        WriteText(text, clipPathId);
                    }
                    finally
                    {
                        _clipTextTransform = null;
                    }

                    return;
            }

            if (transform is { } shapeTransform)
            {
                Attribute("transform", Transform(shapeTransform));
            }

            if (clipPathId is not null)
            {
                Attribute("clip-path", "url(#" + clipPathId + ")");
            }

            output.Write("/>\n");
        }

        private static SceneTransform? Invert(SceneTransform transform)
        {
            var determinant = (transform.A * transform.D) - (transform.B * transform.C);
            if (determinant == 0 || !double.IsFinite(determinant))
                return null;

            var inverse = new SceneTransform(
                transform.D / determinant,
                -transform.B / determinant,
                -transform.C / determinant,
                transform.A / determinant,
                ((transform.C * transform.F) - (transform.D * transform.E)) / determinant,
                ((transform.B * transform.E) - (transform.A * transform.F)) / determinant);
            return double.IsFinite(inverse.A) && double.IsFinite(inverse.B) && double.IsFinite(inverse.C) && double.IsFinite(inverse.D) && double.IsFinite(inverse.E) && double.IsFinite(inverse.F) ? inverse : null;
        }

        private void EndElement(string name, SceneElement element)
        {
            if (string.IsNullOrEmpty(element.Tooltip))
            {
                output.Write("/>\n");
                return;
            }

            output.Write('>');
            WriteTooltip(element);
            output.Write("</");
            output.Write(name);
            output.Write(">\n");
        }

        private void WriteTooltip(SceneElement element)
        {
            if (string.IsNullOrEmpty(element.Tooltip))
                return;

            output.Write("<title>");
            output.Write(Escape(element.Tooltip));
            output.Write("</title>");
            if (element is SceneGroup)
            {
                output.Write('\n');
            }
        }

        private void Paint(string name, ScenePaint? paint)
        {
            switch (paint)
            {
                case null:
                    Attribute(name, "none");
                    break;
                case SolidPaint solid:
                    Paint(name, solid.Color);
                    break;
                case SceneGradientPaint or PatternPaint:
                    Attribute(name, "url(#" + _ids[paint] + ")");
                    break;
                case ContextPaint context:
                    Attribute(name, context.IsStroke ? "context-stroke" : "context-fill");
                    break;
            }
        }

        private void Paint(string name, SceneColor color)
        {
            Attribute(name, color.ToHexRgb());
            if (!color.IsOpaque)
            {
                Attribute(name + "-opacity", SvgNumber.Format(color.Opacity, 3));
            }
        }

        private static string? BlendModeName(SceneBlendMode mode) => mode switch
        {
            SceneBlendMode.Multiply => "multiply",
            SceneBlendMode.Screen => "screen",
            SceneBlendMode.Overlay => "overlay",
            SceneBlendMode.Darken => "darken",
            SceneBlendMode.Lighten => "lighten",
            SceneBlendMode.ColorDodge => "color-dodge",
            SceneBlendMode.ColorBurn => "color-burn",
            SceneBlendMode.HardLight => "hard-light",
            SceneBlendMode.SoftLight => "soft-light",
            SceneBlendMode.Difference => "difference",
            SceneBlendMode.Exclusion => "exclusion",
            SceneBlendMode.Hue => "hue",
            SceneBlendMode.Saturation => "saturation",
            SceneBlendMode.Color => "color",
            SceneBlendMode.Luminosity => "luminosity",
            _ => null,
        };

        private string Transform(SceneTransform transform)
        {
            if (transform.IsTranslation)
                return "translate(" + Number(transform.E) + " " + Number(transform.F) + ")";

            return "matrix(" + string.Join(' ', new[] { transform.A, transform.B, transform.C, transform.D, transform.E, transform.F }.Select(value => SvgNumber.Format(value, Math.Max(_precision, 6)))) + ")";
        }

        // With fonts, the other families of the collection follow the requested ones, so a viewer looks for the
        // characters a family does not have in the same faces as the layout
        private string FamilyList(string requested)
        {
            if (_scene.Fonts is not { } fonts)
                return requested;

            // The families the collection has come first, in the order of the request (the default family when it has
            // none of them), then the other families of the collection: a family it does not have would draw the texts
            // with other widths than the measured ones. A generic family (sans-serif) stays last, for a viewer without
            // the fonts.
            var names = CssFontFamily.Parse(requested);
            var resolved = names.Select(name => fonts.Families.FirstOrDefault(family => string.Equals(family, name, StringComparison.OrdinalIgnoreCase))).OfType<string>().ToList();
            if (resolved.Count is 0)
            {
                resolved.Add(fonts.DefaultFamily);
            }

            resolved.AddRange(fonts.Families.Where(family => !resolved.Contains(family, StringComparer.OrdinalIgnoreCase)));
            var generic = names.Where(CssFontFamily.IsGenericFamily).Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", resolved.Distinct(StringComparer.OrdinalIgnoreCase).Select(CssFontFamily.Format).Concat(generic));
        }

        private static string GetFontFaceRules(Scene scene, SceneFontCollection fonts)
        {
            var used = new HashSet<SceneFontFace>();
            foreach (var element in SceneTree.EnumerateElements(scene.Root))
            {
                switch (element)
                {
                    case SceneText { Spans.Count: > 0 } text:
                        foreach (var span in text.Spans)
                        {
                            AddRuns(span.Text, span.Font ?? text.Font);
                        }

                        break;

                    case SceneText text:
                        AddRuns(text.Text, text.Font);
                        break;
                }
            }

            var rules = new StringBuilder();
            foreach (var face in fonts.Faces)
            {
                if (!used.Contains(face) || face.FaceCount > 1 || GetMediaType(face.Data.Span) is not { } mediaType)
                    continue;

                rules.Append("@font-face{font-family:").Append(CssString(face.FamilyName))
                    .Append(";font-weight:").Append(face.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(";font-style:").Append(face.IsItalic ? "italic" : "normal")
                    .Append(";src:url(data:").Append(mediaType).Append(";base64,").Append(Convert.ToBase64String(face.Data.Span)).Append(")}\n");
            }

            return rules.ToString();

            void AddRuns(string text, SceneFont font)
            {
                foreach (var run in fonts.GetRuns(text, font, scene.FontFamily))
                {
                    used.Add(run.Face);
                }
            }
        }

        private static string? GetMediaType(ReadOnlySpan<byte> data)
        {
            return data.Length < 4 ? null : (data[0], data[1], data[2], data[3]) switch
            {
                ((byte)'w', (byte)'O', (byte)'F', (byte)'2') => "font/woff2",
                ((byte)'w', (byte)'O', (byte)'F', (byte)'F') => "font/woff",
                ((byte)'O', (byte)'T', (byte)'T', (byte)'O') => "font/otf",
                (0, 1, 0, 0) or ((byte)'t', (byte)'r', (byte)'u', (byte)'e') => "font/ttf",
                _ => null,
            };
        }

        // A CSS string: the quotes, the backslashes and the control characters are escaped. The XML escaping of the
        // style element handles the other characters
        private static string CssString(string value)
        {
            var builder = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                if (c is '"' or '\\')
                {
                    builder.Append('\\').Append(c);
                }
                else if (char.IsControl(c))
                {
                    builder.Append('\\').Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.Append('"').ToString();
        }

        private void Attribute(string name, double value) => Attribute(name, Number(value));

        private void Attribute(string name, string value)
        {
            output.Write(' ');
            output.Write(name);
            output.Write("=\"");
            output.Write(Escape(value));
            output.Write('"');
        }

        private string Number(double value) => SvgNumber.Format(value, _precision);

        private static string Escape(string value)
        {
            StringBuilder? builder = null;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                string? replacement = c switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => null,
                };

                // The characters XML forbids: controls, unpaired surrogates, U+FFFE and U+FFFF
                var isValid = true;
                if (replacement is null)
                {
                    if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            builder?.Append(c).Append(value[i + 1]);
                            i++;
                            continue;
                        }

                        isValid = false;
                    }
                    else
                    {
                        isValid = c is '\t' or '\n' or '\r' || (c >= 0x20 && !char.IsLowSurrogate(c) && c is not '￾' and not '￿');
                    }
                }

                if (replacement is null && isValid)
                {
                    builder?.Append(c);
                    continue;
                }

                builder ??= new StringBuilder(value.Length + 16).Append(value, 0, i);
                if (replacement is not null)
                {
                    builder.Append(replacement);
                }
            }

            return builder?.ToString() ?? value;
        }
    }
}
