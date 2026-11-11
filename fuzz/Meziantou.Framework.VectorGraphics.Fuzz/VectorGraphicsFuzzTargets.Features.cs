using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.VectorGraphics.Fuzz;

public static partial class VectorGraphicsFuzzTargets
{
    /// <summary>
    /// The SVG of a scene of patterns, markers, uses, clipping paths, view boxes, texts with a spacing, on a path, painted,
    /// outlined or with moved spans, filter graphs whose image primitives draw elements, the paint options, and elements
    /// with a transform, a hyperlink or an identifier (<see cref="FeatureSceneBuilder"/>) is a well-formed XML document whose root
    /// is <c>svg</c>, and the output is deterministic. Its identifiers are different, and every reference
    /// (<c>href="#id"</c>, <c>url(#id)</c>) is to one of them, and a <c>clipPath</c> has only shapes and texts. A scene where two elements have the same identifier
    /// throws <see cref="InvalidOperationException"/>. The bounds of the scene are computed without looping.
    /// </summary>
    private static void SvgFeaturesTarget(ReadOnlySpan<byte> data)
    {
        var scene = FeatureSceneBuilder.Create(data, allowFilters: true, out var hasDuplicateIds);
        var options = new SvgRenderOptions { IdPrefix = "p" + (data.Length % 3).ToString(CultureInfo.InvariantCulture) };
        if (hasDuplicateIds)
        {
            try
            {
                SvgSceneRenderer.Render(scene, options);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            FuzzAssert.Fail("A scene where two elements have the same identifier was written");
        }

        var svg = SvgSceneRenderer.Render(scene, options);
        FuzzAssert.Equal(svg, SvgSceneRenderer.Render(scene, options), "Rendering the scene twice gives different outputs");

        var document = new XmlDocument { XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException exception)
        {
            FuzzAssert.Fail($"The SVG is not well-formed: {exception.Message}");
        }

        FuzzAssert.Equal("svg", document.DocumentElement?.LocalName, "The root of the SVG is not svg");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlElement element in document.GetElementsByTagName("*"))
        {
            if (element.GetAttributeNode("id") is { } id)
            {
                FuzzAssert.True(ids.Add(id.Value), $"The identifier '{id.Value}' is written twice");
            }
        }

        foreach (XmlElement element in document.GetElementsByTagName("*"))
        {
            foreach (XmlAttribute attribute in element.Attributes)
            {
                // The links of the scene (the a elements) are addresses, and the images embed their bytes: not references
                if (attribute.Name is "href" && element.LocalName is not "a" && !(element.LocalName is "image" && attribute.Value.StartsWith("data:image/", StringComparison.Ordinal)))
                {
                    FuzzAssert.True(attribute.Value.StartsWith('#', StringComparison.Ordinal) && ids.Contains(attribute.Value[1..]), $"The {element.LocalName} refers to '{attribute.Value}', which is not an identifier of the SVG");
                }

                foreach (Match match in UrlReference().Matches(attribute.Value))
                {
                    FuzzAssert.True(ids.Contains(match.Groups["id"].Value), $"The attribute {attribute.Name} refers to '{match.Groups["id"].Value}', which is not an identifier of the SVG");
                }
            }
        }

        // A clipping path has only shapes and texts: SVG ignores the groups, the uses and the links in it
        foreach (XmlElement clipPath in document.GetElementsByTagName("clipPath"))
        {
            foreach (var child in clipPath.ChildNodes.OfType<XmlElement>())
            {
                FuzzAssert.True(child.LocalName is "rect" or "circle" or "path" or "text", $"A clipPath contains a {child.LocalName} element");
            }
        }

        _ = scene.Root.GetBounds(_ => null);
        _ = scene.Root.GetBounds(_ => null, applyClips: false);
    }

    [GeneratedRegex(@"url\(#(?<id>[^)]*)\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlReference();

    /// <summary>
    /// The input picks the commands of a path and their numbers from <see cref="PathValues"/>, then distances, then a view
    /// box. The length of the path is not a number less than 0; the points at two distances along a path of finite
    /// coordinates are finite, their directions are angles of -180 to 180 degrees, and along a single subpath they are no
    /// farther apart than the distances. The path has one vertex per command (and one at the origin when it starts without a move), at the end of
    /// each command. A marker places its reference point on a vertex. A view box maps its box into its viewport (meet),
    /// over it (slice), or onto it (none). The reversed path has the same length, and reversing it twice gives it back.
    /// </summary>
    private static void PathMeasureTarget(ReadOnlySpan<byte> data)
    {
        var builder = new PathBuilder();
        var commandCount = Math.Min(FuzzInput.ReadByte(ref data) % 16, data.Length / 3);
        for (var i = 0; i < commandCount; i++)
        {
            var kind = FuzzInput.ReadByte(ref data);
            switch (kind % 6)
            {
                case 0:
                    builder.MoveTo(ReadPathValue(ref data), ReadPathValue(ref data));
                    break;
                case 1:
                    builder.LineTo(ReadPathValue(ref data), ReadPathValue(ref data));
                    break;
                case 2:
                    builder.CubicTo(ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
                    break;
                case 3:
                    builder.QuadraticTo(ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
                    break;
                case 4:
                    builder.ArcTo(ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), largeArc: (kind & 8) != 0, sweep: (kind & 16) != 0, ReadPathValue(ref data), ReadPathValue(ref data));
                    break;
                default:
                    builder.Close();
                    break;
            }
        }

        var path = builder.Build();
        var length = path.GetLength();
        FuzzAssert.True(length >= 0, $"The length of the path is {length}");

        // The reversed path covers the same points: it has the same length, and reversing it twice gives it back
        var reversed = path.Reverse();
        FuzzAssert.True(reversed.Commands.SequenceEqual(reversed.Reverse().Reverse().Commands), "Reversing a reversed path twice changes it");
        if (double.IsFinite(length) && length < 1e7)
        {
            var reversedLength = reversed.GetLength();
            FuzzAssert.True(Math.Abs(reversedLength - length) <= (1e-6 * length) + 1e-6, $"The reversed path is {reversedLength} long, not {length}");
        }

        var (first, second) = (ReadPathValue(ref data), ReadPathValue(ref data));
        if (path.IsEmpty)
        {
            try
            {
                path.GetPointAtLength(first);
                FuzzAssert.Fail("The point of an empty path did not throw");
            }
            catch (InvalidOperationException)
            {
            }
        }
        else if (double.IsFinite(length) && path.Commands.All(command => double.IsFinite(command.X) && double.IsFinite(command.Y) && double.IsFinite(command.X1) && double.IsFinite(command.Y1) && double.IsFinite(command.X2) && double.IsFinite(command.Y2)) && length < 1e7)
        {
            var a = path.GetPointAtLength(first);
            var b = path.GetPointAtLength(second);
            FuzzAssert.True(double.IsFinite(a.X) && double.IsFinite(a.Y) && double.IsFinite(b.X) && double.IsFinite(b.Y), "A point of the path is not finite");
            FuzzAssert.True(a.Angle is >= -180 and <= 180 && b.Angle is >= -180 and <= 180, $"The directions {a.Angle} and {b.Angle} are not angles");

            // The moves between subpaths have no length: along a single subpath, the points are no farther apart than the distance between them
            if (path.Commands.Skip(1).All(command => command.Kind is not PathCommandKind.MoveTo))
            {
                var along = Math.Abs(Clamp(first) - Clamp(second));
                var distance = Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
                FuzzAssert.True(distance <= (along * (1 + 1e-9)) + 1e-6, $"The points at {first} and {second} are {distance} apart, more than the {along} between them");
            }
        }

        var vertices = path.GetVertices();
        var expected = path.Commands.Count + (path.Commands.Count > 0 && path.Commands[0].Kind is not PathCommandKind.MoveTo ? 1 : 0);
        FuzzAssert.Equal(expected, vertices.Count, "The path has a wrong number of vertices");
        var offset = expected - path.Commands.Count;
        for (var i = 0; i < path.Commands.Count; i++)
        {
            var command = path.Commands[i];
            FuzzAssert.False(double.IsNaN(vertices[i + offset].Angle) || double.IsInfinity(vertices[i + offset].Angle), $"The direction of the vertex {i + offset} is {vertices[i + offset].Angle}");
            if (command.Kind is not PathCommandKind.Close)
            {
                FuzzAssert.True(vertices[i + offset].X.Equals(command.X) && vertices[i + offset].Y.Equals(command.Y), $"The vertex {i + offset} is not at the end of its command");
            }
        }

        // A marker places its reference point on the vertex
        if (vertices.Count > 0)
        {
            var vertex = vertices[^1];
            var marker = new SceneMarker { RefX = first, RefY = second, Orientation = (SceneMarkerOrientation)(commandCount % 3), Angle = first };
            var (x, y) = marker.GetTransform(vertex, 2, isStart: commandCount % 2 == 0).Apply(first, second);
            if (double.IsFinite(x) && double.IsFinite(y) && Math.Abs(first) < 1e6 && Math.Abs(second) < 1e6 && Math.Abs(vertex.X) < 1e6 && Math.Abs(vertex.Y) < 1e6)
            {
                FuzzAssert.True(Math.Abs(x - vertex.X) < 1e-3 && Math.Abs(y - vertex.Y) < 1e-3, $"The marker places its reference point at ({x}, {y}), not on the vertex ({vertex.X}, {vertex.Y})");
            }
        }

        // A view box maps its box into, over or onto its viewport
        var box = new SceneBounds(ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
        var viewport = new SceneBounds(ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
        var alignment = FuzzInput.ReadByte(ref data);
        var viewBox = new SceneViewBox(box, viewport) { Alignment = (SceneAspectRatio)(alignment % 10), Slice = (alignment & 16) != 0 };
        if (viewBox.GetTransform() is { } transform && Math.Max(Math.Max(Math.Abs(box.X), Math.Abs(box.Y)), Math.Max(box.Width, box.Height)) < 1e6 && Math.Max(Math.Max(Math.Abs(viewport.X), Math.Abs(viewport.Y)), Math.Max(viewport.Width, viewport.Height)) < 1e6 && Math.Min(box.Width, box.Height) > 1e-6 && Math.Min(viewport.Width, viewport.Height) > 1e-6)
        {
            var (left, top) = transform.Apply(box.X, box.Y);
            var (right, bottom) = transform.Apply(box.Right, box.Bottom);
            var tolerance = 1e-6 * (1 + Math.Max(Math.Abs(viewport.Right), Math.Abs(viewport.Bottom)) + Math.Max(Math.Abs(right), Math.Abs(bottom)));
            var inside = left >= viewport.X - tolerance && top >= viewport.Y - tolerance && right <= viewport.Right + tolerance && bottom <= viewport.Bottom + tolerance;
            var over = left <= viewport.X + tolerance && top <= viewport.Y + tolerance && right >= viewport.Right - tolerance && bottom >= viewport.Bottom - tolerance;
            if (viewBox.Alignment is SceneAspectRatio.None)
            {
                FuzzAssert.True(inside && over, "A view box without alignment does not map its box onto its viewport");
            }
            else if (viewBox.Slice)
            {
                FuzzAssert.True(over, "A sliced view box does not cover its viewport");
            }
            else
            {
                FuzzAssert.True(inside, "A view box does not fit in its viewport");
            }
        }

        double Clamp(double distance) => double.IsNaN(distance) ? 0 : Math.Clamp(distance, 0, length);
    }

    private static IEnumerable<FuzzSeed> GetSvgFeaturesSeeds()
    {
        // The scenes of the unit tests: patterns, markers, uses, view boxes, spaced and decorated texts, texts along a path,
        // identifiers, clipping paths, paint options
        byte[][] seeds =
        [
            [0, 3, 0, 0 | 4 | 16, 4, 0, 1, 4 | 8 | 16, 0, 2, 4 | 8, 0],
            [3, 5, .. "Spaced and overlined"u8, 0, 4, 0, .. "Text along a path"u8, 0, 4, 1, .. "around"u8, 0, 4, 2, .. "nothing"u8, 0],
            [5, 4, 0, 0, 3, 0, 6, 0, 0, 7 | 32, 1, 0, 7 | 16, 0, 0, 7, 5, .. "https://example.com/"u8, 0],
            [0x40 | 8, 1, .. "north"u8, 0, 0x40 | 10, 2, .. "north"u8, 0],
            [0x40 | 9, 7, .. "https://example.com/"u8, 0, 0x80 | 11, 1, .. "link"u8, 0, 0x40 | 7 | 32, 0, .. "scene-element0"u8, 0],
            [0 | 4, 3, 0, 0 | 32 | 2, 1, 0, 1 | 4, 4 | 8 | 16, 0, 1 | 32, 4 | 8 | 16, 0],

            // Clipping paths: shapes, a text, a group with a use of itself and a shape cut by another clipping path, a
            // clipping path cut by itself, transformed and masked elements; the paint options
            [12, 4 | 16, .. "Clip"u8, 0, 12 | 16, 1 | 2 | 16 | 32, .. "text"u8, 0, 12 | 32, 2 | 64, 0, 12 | 64, 3, 0],
            [13, 0, 0, 13 | 16, 1 | 2, 0, 13, 4 | 8 | 16 | 32 | 64, 0, 13 | 32, 4 | 32, 0, 13 | 64, 8, 0],

            // Filter graphs whose image primitive draws a new element, an element of the scene, or an ancestor
            [0, 3, 0, 9, 2 | 32, 0, 9, 2 | 32 | 64, 0, 5, 4, 0, 9, 2 | 16 | 32 | 128, 0],

            // Texts with paints, outlines, moved and turned spans, baselines, lengths and directions, and texts along reversed paths
            [3, 8 | 16 | 32 | 64 | 128 | 5, .. "Moved and outlined"u8, 0, 3 | 32, 8 | 4 | 2, .. "Shifted"u8, 0, 3 | 16, 128 | 1, .. "Stretched"u8, 0],
            [4 | 32, 32 | 64 | 2, .. "Reversed path"u8, 0, 4, 64 | 128 | 1, .. "Fitted to the path"u8, 0, 3 | 64, 16, .. "Pattern"u8, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("svg-features-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetPathMeasureSeeds()
    {
        // The paths of the unit tests: a polyline, a closed square, a half circle, curves, a path without a move, and empty paths
        byte[][] seeds =
        [
            [3, 0, 0, 0, 1, 5, 0, 1, 5, 5, 3, 7, 4, 7, 0, 0, 5, 5, 0, 0, 5, 7, 1],
            [5, 0, 0, 0, 1, 7, 0, 1, 7, 7, 1, 0, 7, 5, 0, 7, 0, 0, 0, 7, 7, 0, 0, 7, 7, 5 | 16],
            [2, 0, 0, 0, 4 | 16, 4, 4, 0, 5, 0, 1, 5, 0, 0, 5, 5, 0, 0, 5, 5, 0],
            [4, 0, 0, 0, 2, 0, 5, 5, 5, 5, 0, 1, 5, 0, 3, 7, 0, 7, 5, 0, 0, 0, 0],
            [1, 1, 5, 0, 1, 0, 5, 5, 0, 0, 5, 5, 0],
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [2, 0, 13, 0, 1, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("path-measure-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
