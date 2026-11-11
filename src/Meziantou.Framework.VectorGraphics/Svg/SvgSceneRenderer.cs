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
        private readonly HashSet<string> _elementIds = new(StringComparer.Ordinal);
        private readonly int _precision = options.Precision;
        private Scene _scene = null!;

        // The identifier of the element being written, for the first tag it writes; the element whose effects are on
        // the group that wraps it
        private string? _pendingId;
        private SceneElement? _effectsOnWrapper;

        public void Write(Scene scene)
        {
            _scene = scene;
            CollectIds(scene.Root);
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
            if (_masks.Count > 0 || _patterns.Count > 0)
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
        private void CollectIds(SceneGroup root)
        {
            foreach (var element in SceneTree.EnumerateElements(root))
            {
                if (element.Id is { } id && !_elementIds.Add(id))
                    throw new InvalidOperationException($"Two elements of the scene have the identifier '{id}': the identifiers of the elements must be different.");
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
            var clipCount = 0;
            var filterCount = 0;
            var maskCount = 0;
            var textPathCount = 0;
            var patternCount = 0;
            Visit(root);
            if (definitions.Length > 0)
            {
                output.Write("<defs>\n");
                output.Write(definitions.ToString());
                output.Write("</defs>\n");
            }

            void Visit(SceneElement element)
            {
                if (element is SceneShape shape)
                {
                    AddPaint(shape.Fill);
                    AddPaint(shape.Stroke);
                }

                // The path of a text is referenced by its textPath
                if (element is SceneText { Path: { } textPath } pathText && !_textPathIds.ContainsKey(pathText))
                {
                    var id = NextId("textpath", ref textPathCount);
                    _textPathIds[pathText] = id;
                    definitions.Append("<path id=\"").Append(Escape(id)).Append("\" d=\"").Append(SvgNumber.FormatPath(textPath, _precision)).Append("\"/>\n");
                }

                if (element.Filter is { } filter)
                {
                    var id = NextId("filter", ref filterCount);
                    _filterIds[element] = id;
                    AddFilter(id, element, filter);
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
                        var id = NextId("clip", ref clipCount);
                        _ids[group] = id;
                        definitions.Append("<clipPath id=\"").Append(Escape(id)).Append("\"><rect x=\"").Append(Number(clip.X))
                            .Append("\" y=\"").Append(Number(clip.Y)).Append("\" width=\"").Append(Number(clip.Width))
                            .Append("\" height=\"").Append(Number(clip.Height)).Append("\"/></clipPath>\n");
                    }

                    if (group.ClipPath is { } clipPath)
                    {
                        var id = NextId("clip", ref clipCount);
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

            // The region of a filter contains the element with its blur and its offset: with the bounds of the
            // shapes, so a horizontal line, which has no height, is still drawn; with the bounding box of the element
            // when it has a text, whose size is not known without measuring it
            void AddFilter(string id, SceneElement element, SceneFilter filter)
            {
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

            static double Sanitize(double deviation) => deviation > 0 && double.IsFinite(deviation) ? deviation : 0;

            static double Finite(double value) => double.IsFinite(value) ? value : 0;

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
            _pendingId = element.Id;
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

        // An element with a transform and a mask or a filter is wrapped in a group that has them: they are in the
        // coordinates of its parent, as for a group
        private void WriteLinkedElement(SceneElement element)
        {
            if (element is not SceneGroup && element.Transform is not null && (element.Mask is not null || element.Filter is not null))
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
                    EndElement("path", path);
                    break;

                case SceneText text:
                    WriteText(text);
                    break;

                case SceneImage image:
                    StartTag("image");
                    Attribute("x", image.X);
                    Attribute("y", image.Y);
                    Attribute("width", image.Width);
                    Attribute("height", image.Height);
                    Attribute("preserveAspectRatio", "none");
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
            foreach (var child in group.Children)
            {
                WriteElement(child);
            }

            output.Write("</g>\n");
            for (var i = 0; i < outerClips; i++)
            {
                output.Write("</g>\n");
            }
        }

        private void WriteText(SceneText text)
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

            // The lines of a text are drawn under its spans too, and a span cannot remove them: with spans, each span has its lines
            if (text.Spans.Count is 0 && DecorationValue(text.Decorations) is { } decoration)
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

            Paint("fill", text.Color);
            // The rotation applies before the transform: it is on its right
            var rotation = text.Rotation != 0 && textPathId is null ? "rotate(" + Number(text.Rotation) + " " + Number(text.X) + " " + Number(text.Y) + ")" : null;
            if (text.Transform is { } textTransform)
            {
                Attribute("transform", rotation is null ? Transform(textTransform) : Transform(textTransform) + " " + rotation);
            }
            else if (rotation is not null)
            {
                Attribute("transform", rotation);
            }

            CommonAttributes(text);
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
                output.Write(Escape(text.Text));
            }
            else
            {
                foreach (var span in text.Spans)
                {
                    WriteSpan(text, span);
                }
            }

            if (textPathId is not null)
            {
                output.Write("</textPath>");
            }

            output.Write("</text>\n");
        }

        // A span is a tspan with the attributes that differ from the text, or its characters when it has none
        private void WriteSpan(SceneText text, SceneTextSpan span)
        {
            var font = span.Font ?? text.Font;
            var color = span.Color ?? text.Color;
            var decoration = DecorationValue(span.Decorations ?? text.Decorations);
            var letterSpacing = TextSpacing.Normalize(span.LetterSpacing ?? text.LetterSpacing);
            var wordSpacing = TextSpacing.Normalize(span.WordSpacing ?? text.WordSpacing);
            var hasLetterSpacing = letterSpacing != TextSpacing.Normalize(text.LetterSpacing);
            var hasWordSpacing = wordSpacing != TextSpacing.Normalize(text.WordSpacing);
            var hasStyle = font != text.Font || color != text.Color || decoration is not null || hasLetterSpacing || hasWordSpacing;
            if (!hasStyle)
            {
                output.Write(Escape(span.Text));
                return;
            }

            output.Write("<tspan");
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

            if (color != text.Color)
            {
                Paint("fill", color);

                // A span inherits the opacity of the text: an opaque span of a translucent text resets it
                if (color.IsOpaque && !text.Color.IsOpaque)
                {
                    Attribute("fill-opacity", "1");
                }
            }

            if (decoration is not null)
            {
                Attribute("text-decoration", decoration);
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
            output.Write(Escape(span.Text));
            output.Write("</tspan>");
        }

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
            if (!isLine)
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
            }

            CommonAttributes(shape);
        }

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
            else if (element != _effectsOnWrapper)
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

            if (_maskIds.TryGetValue(element, out var maskId))
            {
                Attribute("mask", "url(#" + maskId + ")");
            }

            if (BlendModeName(element.BlendMode) is { } blendMode)
            {
                Attribute("style", "mix-blend-mode:" + blendMode);
            }
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
