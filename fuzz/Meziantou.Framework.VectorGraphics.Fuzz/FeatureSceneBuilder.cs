using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.VectorGraphics.Fuzz;

/// <summary>
/// Builds a scene of the elements that refer to other content (patterns, markers, uses), of the texts with a spacing or
/// on a path, of the view boxes, and of the elements with a transform, a hyperlink or an identifier. The SVG and the PDF
/// targets share it (the PDF fuzzing project links this file).
/// </summary>
/// <remarks>
/// The input is a list of elements: a byte selects the kind of element and its options, a byte picks a number and more
/// options, and the text up to the next NUL byte gives its text, hyperlink or identifier. The patterns and the markers
/// are kept in pools that later elements reuse; a pattern or a marker can paint or mark its own content, and a use can
/// draw one of its ancestors.
/// </remarks>
internal static class FeatureSceneBuilder
{
    private static readonly double[] Values = [0, 1, -1, 0.5, 2.5, 10, -10, 50, 100, 1e-9, 1e6, -1e6, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 3.3];

    public static Scene Create(ReadOnlySpan<byte> data, bool allowFilters, out bool hasDuplicateIds)
    {
        hasDuplicateIds = false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var scene = new Scene(100, 80, "sans-serif") { Background = SceneColor.White };
        var groups = new Stack<SceneGroup>();
        groups.Push(scene.Root);
        var elements = new List<SceneElement>();
        var patterns = new List<PatternPaint>();
        var markers = new List<SceneMarker>();
        var count = 0;
        while (data.Length >= 2 && count++ < 100)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var option = FuzzInput.ReadByte(ref data);
            var value = FuzzInput.Pick(Values, option);
            var text = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
            var parent = groups.Peek();
            var color = new SceneColor((byte)(kind * 37), (byte)(option * 11), (byte)(kind * 5), (byte)(255 - (option & 0xC0)));
            SceneElement element;
            switch (kind % 12)
            {
                case 0:
                    element = parent.Add(new SceneRectangle(value, 10, 30, 20) { Fill = GetPattern(kind, option, value, color), Stroke = (kind & 16) != 0 ? GetPattern((byte)(kind >> 1), option, value, color) : null, StrokeWidth = option % 5 });
                    break;

                case 1:
                    var builder = new PathBuilder().MoveTo(10, 10).LineTo(value, 30).QuadraticTo(40, value, 60, 20).ArcTo(value, 10, option, largeArc: (option & 1) != 0, sweep: (option & 2) != 0, 80, 60);
                    if ((kind & 16) != 0)
                    {
                        builder.Close().LineTo(5, value);
                    }

                    element = parent.Add(new ScenePath(builder.Build())
                    {
                        Stroke = color,
                        StrokeWidth = option % 4,
                        MarkerStart = (option & 4) != 0 ? GetMarker(kind, option, value, color) : null,
                        MarkerMid = (option & 8) != 0 ? GetMarker((byte)(kind + 1), option, value, color) : null,
                        MarkerEnd = (option & 16) != 0 ? GetMarker((byte)(kind + 2), option, value, color) : null,
                    });
                    break;

                case 2:
                    element = parent.Add(new SceneLine(0, value, 90, 70)
                    {
                        Stroke = color,
                        StrokeWidth = value,
                        MarkerStart = (option & 4) != 0 ? GetMarker(kind, option, value, color) : null,
                        MarkerEnd = (option & 8) != 0 ? GetMarker((byte)(kind + 1), option, value, color) : null,
                    });
                    break;

                case 3:
                    // A text with a spacing and lines, split in two spans that have their own spacing
                    element = parent.Add(new SceneText(value, 40, [new SceneTextSpan(text[..(text.Length / 2)]), new SceneTextSpan(text[(text.Length / 2)..]) { LetterSpacing = (option & 1) != 0 ? -value : null, WordSpacing = (option & 2) != 0 ? value : null, Decorations = (SceneTextDecorations)(option % 8) }], new SceneFont(1 + (kind % 20)), color)
                    {
                        LetterSpacing = value,
                        WordSpacing = (kind & 16) != 0 ? value : 0,
                        Anchor = (SceneTextAnchor)(option % 3),
                        Decorations = (SceneTextDecorations)(kind % 8),
                    });
                    break;

                case 4:
                    // A text along a path, which can be closed, empty or without length
                    var textPath = (option % 4) switch
                    {
                        0 => new PathBuilder().MoveTo(10, 60).ArcTo(40, 40, 0, largeArc: false, sweep: true, 90, 60).Build(),
                        1 => new PathBuilder().AddCircle(50, 40, value).Build(),
                        2 => new PathBuilder().MoveTo(value, value).Build(),
                        _ => new PathBuilder().MoveTo(0, 0).LineTo(value, 50).CubicTo(10, value, 60, 0, 90, 70).Build(),
                    };
                    element = parent.Add(new SceneText(0, 0, text, new SceneFont(1 + (kind % 20)), color)
                    {
                        Path = textPath,
                        PathStartOffset = value,
                        Anchor = (SceneTextAnchor)(kind % 3),
                        LetterSpacing = (option & 16) != 0 ? 2 : 0,
                        Decorations = (SceneTextDecorations)(option % 8),
                    });
                    break;

                case 5:
                    var viewBox = new SceneViewBox(new SceneBounds(0, 0, value, 10 + (option % 7)), new SceneBounds(5, 5, 1 + (kind % 90), 1 + (option % 70)))
                    {
                        Alignment = (SceneAspectRatio)(option % 10),
                        Slice = (option & 16) != 0,
                    };
                    var group = parent.Add(new SceneGroup { ViewBox = viewBox, Transform = (kind & 16) != 0 ? SceneTransform.Rotate(value, 50, 40) : null });
                    groups.Push(group);
                    element = group;
                    break;

                case 6:
                    if (groups.Count > 1)
                    {
                        groups.Pop();
                    }

                    continue;

                case 7:
                    // A use of an element of the scene, of an ancestor (a use in its own content), or of an element that is not in the scene
                    SceneElement content = (kind & 16) != 0 && elements.Count > 0 ? elements[option % elements.Count]
                        : (kind & 32) != 0 ? groups.ElementAt(option % groups.Count)
                        : new SceneCircle(5, 5, value) { Fill = color, Hyperlink = (option & 1) != 0 ? text : null };
                    element = parent.Add(new SceneUse(content) { Transform = SceneTransform.Translate(value, option % 50) });
                    break;

                case 8:
                    element = parent.Add(new SceneCircle(50, 40, 1 + (option % 30)) { Fill = color, Hyperlink = text, Transform = SceneTransform.Scale(value, 2, 50, 40) });
                    break;

                case 9:
                    // A transformed element with a mask, a filter and a hyperlink: the effects are in the coordinates of its parent
                    var mask = new SceneMask { Mode = (SceneMaskMode)(option % 2) };
                    mask.Content.Add(new SceneRectangle(value, 0, 60, 60) { Fill = SceneColor.White });
                    element = parent.Add(new SceneRectangle(0, 0, 20, 20)
                    {
                        Fill = color,
                        Transform = SceneTransform.Rotate(value).Then(SceneTransform.Translate(30, 30)),
                        Mask = (option & 1) != 0 ? mask : null,
                        Filter = allowFilters && (option & 2) != 0 ? new SceneBlurFilter(value) : null,
                        Hyperlink = (option & 4) != 0 ? text : null,
                    });
                    break;

                case 10:
                    element = parent.Add(new SceneRectangle(value, value, 10, 10) { Fill = color, Tooltip = text });
                    break;

                default:
                    element = parent.Add(new SceneText(value, 70, text, new SceneFont(12), color) { Transform = SceneTransform.Translate(option % 20, value), Rotation = value, Hyperlink = (option & 1) != 0 ? text : null });
                    break;
            }

            elements.Add(element);
            element.Opacity = (kind & 0x80) != 0 ? (option % 11) / 10.0 : 1;
            if ((kind & 0x40) != 0 && SceneElement.IsValidId(text))
            {
                hasDuplicateIds |= !ids.Add(text);
                element.Id = text;
            }
        }

        return scene;

        // A pattern of the pool, or a new one; the content of a new pattern can be painted with the pattern itself
        PatternPaint GetPattern(byte kind, byte option, double value, SceneColor color)
        {
            if ((kind & 32) != 0 && patterns.Count > 0)
                return patterns[option % patterns.Count];

            var pattern = new PatternPaint(new SceneBounds(value, 0, 1 + (option % 10), (kind & 1) != 0 ? value : 8)) { Transform = (kind & 8) != 0 ? SceneTransform.Rotate(value) : null };
            pattern.Content.Add(new SceneCircle(2, 2, 2) { Fill = color, Hyperlink = "https://example.com/" });
            if ((kind & 4) != 0)
            {
                pattern.Content.Add(new SceneRectangle(4, 4, 4, 4) { Fill = pattern });
            }

            if ((kind & 2) != 0 && patterns.Count > 0)
            {
                pattern.Content.Add(new SceneRectangle(0, 4, 4, 4) { Fill = patterns[^1] });
            }

            patterns.Add(pattern);
            return pattern;
        }

        // A marker of the pool, or a new one; the content of a new marker can be marked with the marker itself
        SceneMarker GetMarker(byte kind, byte option, double value, SceneColor color)
        {
            if ((kind & 32) != 0 && markers.Count > 0)
                return markers[option % markers.Count];

            var marker = new SceneMarker
            {
                RefX = value,
                RefY = option % 5,
                Orientation = (SceneMarkerOrientation)(option % 3),
                Angle = value,
                Units = (SceneMarkerUnits)(kind % 2),
            };
            marker.Content.Add(new ScenePath(new PathBuilder().AddPolygon((0, 0), (10, 5), (0, 10)).Build()) { Fill = color });
            if ((kind & 4) != 0)
            {
                marker.Content.Add(new SceneLine(0, 0, 5, 5) { Stroke = color, MarkerEnd = marker });
            }

            markers.Add(marker);
            return marker;
        }
    }
}
