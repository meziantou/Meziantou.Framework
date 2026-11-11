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
        private readonly int _precision = options.Precision;
        private Scene _scene = null!;

        public void Write(Scene scene)
        {
            _scene = scene;
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

        private void WriteDefinitions(SceneGroup root, string? fontFaceRules)
        {
            var definitions = new StringBuilder();
            if (!string.IsNullOrEmpty(fontFaceRules))
            {
                definitions.Append("<style>").Append(Escape(fontFaceRules)).Append("</style>\n");
            }

            var gradientCount = 0;
            var clipCount = 0;
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
                    AddGradient(shape.Fill);
                    AddGradient(shape.Stroke);
                }

                if (element is SceneGroup group)
                {
                    if (group.Clip is { } clip)
                    {
                        var id = options.IdPrefix + "-clip" + clipCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        clipCount++;
                        _ids[group] = id;
                        definitions.Append("<clipPath id=\"").Append(Escape(id)).Append("\"><rect x=\"").Append(Number(clip.X))
                            .Append("\" y=\"").Append(Number(clip.Y)).Append("\" width=\"").Append(Number(clip.Width))
                            .Append("\" height=\"").Append(Number(clip.Height)).Append("\"/></clipPath>\n");
                    }

                    foreach (var child in group.Children)
                    {
                        Visit(child);
                    }
                }
            }

            void AddGradient(ScenePaint? paint)
            {
                if (paint is not LinearGradientPaint gradient || _ids.ContainsKey(gradient))
                    return;

                var id = options.IdPrefix + "-gradient" + gradientCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                gradientCount++;
                _ids[gradient] = id;
                definitions.Append("<linearGradient id=\"").Append(Escape(id)).Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"").Append(Number(gradient.X1))
                    .Append("\" y1=\"").Append(Number(gradient.Y1)).Append("\" x2=\"").Append(Number(gradient.X2))
                    .Append("\" y2=\"").Append(Number(gradient.Y2)).Append("\">\n");
                foreach (var stop in gradient.Stops)
                {
                    definitions.Append("<stop offset=\"").Append(SvgNumber.Format(stop.Offset, 4)).Append("\" stop-color=\"").Append(stop.Color.ToHexRgb()).Append('"');
                    if (!stop.Color.IsOpaque)
                    {
                        definitions.Append(" stop-opacity=\"").Append(SvgNumber.Format(stop.Color.Opacity, 3)).Append('"');
                    }

                    definitions.Append("/>\n");
                }

                definitions.Append("</linearGradient>\n");
            }
        }

        private void WriteElement(SceneElement element)
        {
            switch (element)
            {
                case SceneGroup group:
                    WriteGroup(group);
                    break;

                case SceneRectangle rectangle:
                    output.Write("<rect");
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
                    output.Write("<circle");
                    Attribute("cx", circle.CenterX);
                    Attribute("cy", circle.CenterY);
                    Attribute("r", circle.Radius);
                    ShapeAttributes(circle);
                    EndElement("circle", circle);
                    break;

                case SceneLine line:
                    output.Write("<line");
                    Attribute("x1", line.X1);
                    Attribute("y1", line.Y1);
                    Attribute("x2", line.X2);
                    Attribute("y2", line.Y2);
                    ShapeAttributes(line, isLine: true);
                    EndElement("line", line);
                    break;

                case ScenePath path:
                    output.Write("<path");
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
            }
        }

        private void WriteGroup(SceneGroup group)
        {
            if (!string.IsNullOrEmpty(group.Hyperlink))
            {
                output.Write("<a");
                Attribute("href", group.Hyperlink);
                output.Write(">\n");
                WriteGroupContent(group);
                output.Write("</a>\n");
                return;
            }

            WriteGroupContent(group);
        }

        private void WriteGroupContent(SceneGroup group)
        {
            var clipped = _ids.TryGetValue(group, out var clipId);
            if (clipped && group.Transform is not null)
            {
                // The clip rectangle is in the coordinates of the parent: clip an outer group, transform an inner one
                output.Write("<g");
                Attribute("clip-path", "url(#" + clipId + ")");
                output.Write(">\n");
            }

            output.Write("<g");
            CommonAttributes(group);
            if (group.Transform is { } transform)
            {
                Attribute("transform", Transform(transform));
            }
            else if (clipped)
            {
                Attribute("clip-path", "url(#" + clipId + ")");
            }

            output.Write(">\n");
            WriteTooltip(group);
            foreach (var child in group.Children)
            {
                WriteElement(child);
            }

            output.Write("</g>\n");
            if (clipped && group.Transform is not null)
            {
                output.Write("</g>\n");
            }
        }

        private void WriteText(SceneText text)
        {
            output.Write("<text");
            Attribute("x", text.X);
            Attribute("y", text.Y);
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
            if (text.Font.Bold)
            {
                Attribute("font-weight", "bold");
            }

            if (text.Font.Italic)
            {
                Attribute("font-style", "italic");
            }

            Paint("fill", text.Color);
            if (text.Rotation != 0)
            {
                Attribute("transform", "rotate(" + Number(text.Rotation) + " " + Number(text.X) + " " + Number(text.Y) + ")");
            }

            CommonAttributes(text);
            output.Write('>');
            if (!string.IsNullOrEmpty(text.Tooltip))
            {
                output.Write("<title>");
                output.Write(Escape(text.Tooltip));
                output.Write("</title>");
            }

            output.Write(Escape(text.Text));
            output.Write("</text>\n");
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
                }
            }

            CommonAttributes(shape);
        }

        private void CommonAttributes(SceneElement element)
        {
            if (element.Opacity < 1)
            {
                Attribute("opacity", SvgNumber.Format(Math.Max(0, element.Opacity), 3));
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
                case LinearGradientPaint gradient:
                    Attribute(name, "url(#" + _ids[gradient] + ")");
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

            var names = CssFontFamily.Parse(requested);
            var others = fonts.Families.Where(family => !names.Contains(family, StringComparer.OrdinalIgnoreCase)).ToList();
            if (others.Count is 0)
                return requested;

            return requested.Trim().Length is 0 ? CssFontFamily.Format(others) : requested + ", " + CssFontFamily.Format(others);
        }

        private static string GetFontFaceRules(Scene scene, SceneFontCollection fonts)
        {
            var used = new HashSet<SceneFontFace>();
            Visit(scene.Root);
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

            void Visit(SceneElement element)
            {
                switch (element)
                {
                    case SceneGroup group:
                        foreach (var child in group.Children)
                        {
                            Visit(child);
                        }

                        break;

                    case SceneText text:
                        foreach (var run in fonts.GetRuns(text.Text, text.Font, scene.FontFamily))
                        {
                            used.Add(run.Face);
                        }

                        break;
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
