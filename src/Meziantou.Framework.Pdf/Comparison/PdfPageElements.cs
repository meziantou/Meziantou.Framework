using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// What a page shows, as elements that can be compared: its lines of text, the images, the paths and the shadings it
/// paints, with their positions in default user space. <see cref="PdfDocumentComparer"/> compares the elements of two
/// documents.
/// </summary>
public sealed class PdfPageElements
{
    private const int MaxElements = 100_000;
    private const int MaxFormDepth = 16;
    private const int MaxOperations = 2_000_000;

    private PdfPageElements(double width, double height, int rotation, IReadOnlyList<PdfTextElement> text, IReadOnlyList<PdfImageElement> images, IReadOnlyList<PdfPathElement> paths, IReadOnlyList<PdfShadingElement> shadings)
    {
        Width = width;
        Height = height;
        Rotation = rotation;
        Text = text;
        Images = images;
        Paths = paths;
        Shadings = shadings;
    }

    /// <summary>Gets the width of the crop box of the page.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the crop box of the page.</summary>
    public double Height { get; }

    /// <summary>Gets the rotation of the page, in degrees.</summary>
    public int Rotation { get; }

    /// <summary>Gets the lines of text, in the order of the content.</summary>
    public IReadOnlyList<PdfTextElement> Text { get; }

    /// <summary>Gets the images painted, in the order of the content.</summary>
    public IReadOnlyList<PdfImageElement> Images { get; }

    /// <summary>Gets the paths painted (filled or stroked; the clipping paths are not), in the order of the content, forms included.</summary>
    public IReadOnlyList<PdfPathElement> Paths { get; }

    /// <summary>Gets the shadings painted, in the order of the content, forms included.</summary>
    public IReadOnlyList<PdfShadingElement> Shadings { get; }

    // The entries of an image dictionary that change what it shows (8.9.5), inline abbreviations included (8.9.7), and
    // its optional content group (8.11.3.3), which can hide it
    private static readonly PdfName[] ImageDisplayKeys = [.. new[] { "ColorSpace", "CS", "BitsPerComponent", "BPC", "Decode", "D", "ImageMask", "IM", "Mask", "SMask", "SMaskInData", "Filter", "F", "DecodeParms", "DP", "OC" }.Select(PdfName.Create)];

    /// <summary>Reads the elements of a page. The parts of the content that cannot be read are skipped.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The elements.</returns>
    public static PdfPageElements Create(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var crop = page.CropBox;
        var text = new List<PdfTextElement>();
        try
        {
            foreach (var line in page.ExtractTextLayout().Lines)
            {
                text.Add(new PdfTextElement(line.Text, line.Bounds));
            }
        }
        catch (PdfException)
        {
        }

        var images = new List<PdfImageElement>();
        try
        {
            foreach (var image in page.GetImages())
            {
                images.Add(new PdfImageElement(image.Bounds, image.Width, image.Height, GetImageDigest(image)));
            }
        }
        catch (PdfException)
        {
        }

        var walker = new ContentWalker();
        try
        {
            walker.Walk(page.GetContentData(), page.Resources, PdfMatrix.Identity, depth: 0);
        }
        catch (PdfException)
        {
        }

        return new PdfPageElements(crop.Width, crop.Height, page.Rotation, text, images, walker.Paths, walker.Shadings);
    }

    /// <summary>
    /// The digest of the data of an image and of the entries of its dictionary that change what it shows, with the objects
    /// they refer to: its metadata, its name or the order of its entries do not change it.
    /// </summary>
    private static string GetImageDigest(PdfImageOccurrence image)
    {
        var display = new PdfDictionary();
        foreach (var key in ImageDisplayKeys)
        {
            if (image.Dictionary[key] is { } value)
            {
                display[key] = value;
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(image.EncodedData.Span);
        hash.AppendData(Encoding.ASCII.GetBytes(ObjectDigest.Compute(display)));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Gets the line segments a page paints, in default user space: the segments of the stroked paths (the edges of their
    /// rectangles included), and the center lines of the filled rectangles thinner than <paramref name="maxThickness"/>,
    /// which tables use as rules.
    /// </summary>
    internal static List<(double X1, double Y1, double X2, double Y2)> GetRules(PdfPage page, double maxThickness)
    {
        var walker = new ContentWalker { Rules = [], MaxRuleThickness = maxThickness };
        try
        {
            walker.Walk(page.GetContentData(), page.Resources, PdfMatrix.Identity, depth: 0);
        }
        catch (PdfException)
        {
        }

        return walker.Rules;
    }

    /// <summary>Follows the graphics state of a content (8.4): the transformation, the colors and the paths it paints.</summary>
    private sealed class ContentWalker
    {
        private readonly HashSet<PdfStream> _activeForms = new(ReferenceEqualityComparer.Instance);
        private readonly List<(double X1, double Y1, double X2, double Y2)> _segments = [];
        private readonly List<(double X1, double Y1, double X2, double Y2)> _thinRectangles = [];
        private (double X, double Y) _current;
        private (double X, double Y) _start;
        private int _operations;

        public List<(double X1, double Y1, double X2, double Y2)>? Rules { get; init; }

        public double MaxRuleThickness { get; init; }

        public List<PdfPathElement> Paths { get; } = [];

        public List<PdfShadingElement> Shadings { get; } = [];

        /// <summary>Walks a content. A form inherits the colors of the content that paints it (8.10.1).</summary>
        public void Walk(ReadOnlyMemory<byte> content, PdfDictionary? resources, PdfMatrix ctm, int depth, string? fill = null, string? stroke = null, PdfObject? fillPattern = null)
        {
            var reader = new ContentStreamReader(content);
            var operands = new List<PdfObject>();
            var stack = new Stack<(PdfMatrix Ctm, string? Fill, string? Stroke, PdfObject? FillPattern)>();
            var points = new List<(double X, double Y)>();
            while (reader.TryRead(operands, out var name) && _operations++ < MaxOperations)
            {
                switch (name)
                {
                    case "q":
                        if (stack.Count < 256)
                        {
                            stack.Push((ctm, fill, stroke, fillPattern));
                        }

                        break;
                    case "Q":
                        if (stack.TryPop(out var saved))
                        {
                            (ctm, fill, stroke, fillPattern) = saved;
                        }

                        break;
                    case "cm" when operands.Count >= 6 && TryGetNumbers(operands, 6, out var m):
                        ctm = new PdfMatrix(m[0], m[1], m[2], m[3], m[4], m[5]) * ctm;
                        break;

                    case "g" or "rg" or "k" or "sc" or "scn" or "cs":
                        fill = Format(name, operands);
                        fillPattern = name == "scn" && operands is [.., PdfName pattern] ? GetResource(resources, PdfNames.Pattern, pattern) : null;
                        break;
                    case "G" or "RG" or "K" or "SC" or "SCN" or "CS":
                        stroke = Format(name, operands);
                        break;

                    case "m" or "l" when TryGetNumbers(operands, 2, out var p):
                        AddPoint(points, ctm, p[0], p[1]);
                        if (Rules is not null)
                        {
                            var point = ctm.Transform(p[0], p[1]);
                            if (name == "l")
                            {
                                AddSegment(_current, point);
                            }
                            else
                            {
                                _start = point;
                            }

                            _current = point;
                        }

                        break;
                    case "h" when Rules is not null:
                        AddSegment(_current, _start);
                        _current = _start;
                        break;
                    case "c" when TryGetNumbers(operands, 6, out var c):
                        AddPoint(points, ctm, c[0], c[1]);
                        AddPoint(points, ctm, c[2], c[3]);
                        AddPoint(points, ctm, c[4], c[5]);
                        break;
                    case "v" or "y" when TryGetNumbers(operands, 4, out var v):
                        AddPoint(points, ctm, v[0], v[1]);
                        AddPoint(points, ctm, v[2], v[3]);
                        break;
                    case "re" when TryGetNumbers(operands, 4, out var r):
                        AddPoint(points, ctm, r[0], r[1]);
                        AddPoint(points, ctm, r[0] + r[2], r[1]);
                        AddPoint(points, ctm, r[0] + r[2], r[1] + r[3]);
                        AddPoint(points, ctm, r[0], r[1] + r[3]);
                        if (Rules is not null)
                        {
                            var a = ctm.Transform(r[0], r[1]);
                            var b = ctm.Transform(r[0] + r[2], r[1]);
                            var c2 = ctm.Transform(r[0] + r[2], r[1] + r[3]);
                            var d = ctm.Transform(r[0], r[1] + r[3]);
                            AddSegment(a, b);
                            AddSegment(b, c2);
                            AddSegment(c2, d);
                            AddSegment(d, a);
                            _start = _current = a;

                            // A thin filled rectangle is a rule: its center line, along its longer side
                            var width = Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
                            var height = Math.Abs(d.X - a.X) + Math.Abs(d.Y - a.Y);
                            if (Math.Min(width, height) <= MaxRuleThickness)
                            {
                                _thinRectangles.Add(width >= height
                                    ? ((a.X + d.X) / 2, (a.Y + d.Y) / 2, (b.X + c2.X) / 2, (b.Y + c2.Y) / 2)
                                    : ((a.X + b.X) / 2, (a.Y + b.Y) / 2, (d.X + c2.X) / 2, (d.Y + c2.Y) / 2));
                            }
                        }

                        break;

                    case "f" or "F" or "f*":
                        PaintRules(stroke: false);
                        AddPath(points, PdfPathPaint.Fill, fill, stroke);
                        AddPatternShading(fillPattern);
                        break;
                    case "S" or "s":
                        if (name == "s")
                        {
                            AddSegment(_current, _start);
                        }

                        PaintRules(stroke: true);
                        AddPath(points, PdfPathPaint.Stroke, fill, stroke);
                        break;
                    case "B" or "B*" or "b" or "b*":
                        if (name is "b" or "b*")
                        {
                            AddSegment(_current, _start);
                        }

                        PaintRules(stroke: true);
                        AddPath(points, PdfPathPaint.FillAndStroke, fill, stroke);
                        AddPatternShading(fillPattern);
                        break;
                    case "n":
                        PaintRules(stroke: false, paint: false);
                        points.Clear();
                        break;

                    case "sh" when operands is [PdfName shadingName, ..] && GetResource(resources, PdfNames.Shading, shadingName) is { } shading:
                        AddShading(shading, ctm);
                        break;

                    case "Do" when operands is [.., PdfName xobjectName] && GetResource(resources, PdfNames.XObject, xobjectName) is PdfStream form
                        && form.Dictionary.GetName(PdfNames.Subtype)?.Value == "Form" && depth < MaxFormDepth && _activeForms.Add(form):
                        try
                        {
                            var matrix = form.Dictionary.GetResolved(PdfNames.Matrix) is PdfArray array && array.Count == 6
                                ? new PdfMatrix(array.GetNumber(0), array.GetNumber(1), array.GetNumber(2), array.GetNumber(3), array.GetNumber(4), array.GetNumber(5))
                                : PdfMatrix.Identity;
                            Walk(form.GetDecodedData(), form.Dictionary.GetResolved(PdfNames.Resources) as PdfDictionary ?? resources, matrix * ctm, depth + 1, fill, stroke, fillPattern);
                        }
                        catch (PdfException)
                        {
                        }
                        finally
                        {
                            _activeForms.Remove(form);
                        }

                        break;
                }
            }
        }

        private void AddSegment((double X, double Y) from, (double X, double Y) to)
        {
            if (Rules is not null && _segments.Count < MaxElements && double.IsFinite(from.X) && double.IsFinite(from.Y) && double.IsFinite(to.X) && double.IsFinite(to.Y))
            {
                _segments.Add((from.X, from.Y, to.X, to.Y));
            }
        }

        /// <summary>The path is painted: its segments are rules when it is stroked, its thin rectangles when it is filled.</summary>
        private void PaintRules(bool stroke, bool paint = true)
        {
            if (Rules is not null && paint && Rules.Count < MaxElements)
            {
                Rules.AddRange(stroke ? _segments : _thinRectangles);
            }

            _segments.Clear();
            _thinRectangles.Clear();
        }

        private void AddPath(List<(double X, double Y)> points, PdfPathPaint paint, string? fill, string? stroke)
        {
            if (points.Count > 0 && Paths.Count < MaxElements)
            {
                var left = points.Min(point => point.X);
                var bottom = points.Min(point => point.Y);
                var right = points.Max(point => point.X);
                var top = points.Max(point => point.Y);
                Paths.Add(new PdfPathElement(new PdfRectangle(left, bottom, right, top), paint, paint == PdfPathPaint.Stroke ? null : fill, paint == PdfPathPaint.Fill ? null : stroke));
            }

            points.Clear();
        }

        /// <summary>A path filled with a shading pattern paints the shading of the pattern, in the space of the pattern (8.7.3.1).</summary>
        private void AddPatternShading(PdfObject? pattern)
        {
            var dictionary = pattern is PdfStream stream ? stream.Dictionary : pattern as PdfDictionary;
            if (dictionary?.GetInteger(PdfName.Create("PatternType")) == 2 && dictionary.GetResolved(PdfNames.Shading) is { } shading)
            {
                var matrix = dictionary.GetResolved(PdfNames.Matrix) is PdfArray array && array.Count == 6
                    ? new PdfMatrix(array.GetNumber(0), array.GetNumber(1), array.GetNumber(2), array.GetNumber(3), array.GetNumber(4), array.GetNumber(5))
                    : PdfMatrix.Identity;
                AddShading(shading, matrix);
            }
        }

        private void AddShading(PdfObject shading, PdfMatrix ctm)
        {
            var dictionary = shading is PdfStream stream ? stream.Dictionary : shading as PdfDictionary;
            if (dictionary is null || Shadings.Count >= MaxElements)
                return;

            Shadings.Add(new PdfShadingElement((int)dictionary.GetInteger(PdfName.Create("ShadingType")), ctm, ObjectDigest.Compute(shading)));
        }

        private static void AddPoint(List<(double X, double Y)> points, PdfMatrix ctm, double x, double y)
        {
            if (points.Count < MaxElements)
            {
                var (tx, ty) = ctm.Transform(x, y);
                if (double.IsFinite(tx) && double.IsFinite(ty))
                {
                    points.Add((tx, ty));
                }
            }
        }

        private static bool TryGetNumbers(List<PdfObject> operands, int count, out double[] values)
        {
            values = new double[count];
            if (operands.Count < count)
                return false;

            var start = operands.Count - count;
            for (var i = 0; i < count; i++)
            {
                if (operands[start + i] is not PdfNumber number)
                    return false;

                values[i] = number.DoubleValue;
            }

            return true;
        }

        private static string Format(string name, List<PdfObject> operands)
        {
            var builder = new StringBuilder();
            foreach (var operand in operands.Take(32))
            {
                builder.Append(operand switch
                {
                    PdfNumber number => number.DoubleValue.ToString("0.###", CultureInfo.InvariantCulture),
                    PdfName value => "/" + value.Value,
                    _ => operand.ToString(),
                }).Append(' ');
            }

            return builder.Append(name).ToString();
        }

        private static PdfObject? GetResource(PdfDictionary? resources, PdfName category, PdfName name)
        {
            try
            {
                return resources?.GetResolved(category) is PdfDictionary dictionary ? dictionary.GetResolved(name) : null;
            }
            catch (PdfException)
            {
                return null;
            }
        }
    }
}
