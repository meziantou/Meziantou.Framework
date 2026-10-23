using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Shadings;

/// <summary>
/// Renders a shading (ISO 32000-2, 8.7.4.5) to an image of an area of the device: the color of each pixel is computed
/// at its center in shading space (function-based, axial and radial shadings), or the triangles of the mesh are filled
/// with the colors interpolated between their vertices (the meshes, the patches being divided into triangles).
/// </summary>
internal static class ShadingRasterizer
{
    // The entries of the table of the colors of a function of one input
    private const int LookupSize = 1024;

    // The vertices and patches of a mesh
    private const int MaxMeshItems = 1 << 20;

    // The pixels of the area a shading is rendered to
    private const long MaxPixels = 64L * 1024 * 1024;

    public static Image<Rgba32>? Rasterize(PdfObject shadingObject, PdfMatrix shadingToDevice, RectangleD area, RenderCache cache, bool includeBackground)
    {
        var resolved = PdfReference.ResolveIfReference(shadingObject);
        var dictionary = resolved switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary d => d,
            _ => null,
        };
        if (dictionary is null)
            return null;

        var left = (int)Math.Floor(area.X);
        var top = (int)Math.Floor(area.Y);
        var width = (int)Math.Ceiling(area.X + area.Width) - left;
        var height = (int)Math.Ceiling(area.Y + area.Height) - top;
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels || !shadingToDevice.TryInvert(out var deviceToShading))
            return null;

        cache.Charge((long)width * height / 256);
        var colorSpace = RenderColorSpace.Read(dictionary.GetResolved(PdfNames.ColorSpace), resources: null, cache);
        var function = dictionary.GetResolved(PdfName.Create("Function")) is { } functionObject ? cache.GetFunction(functionObject) : null;
        var pixels = new Rgba32[width * height];
        if (includeBackground && dictionary.Get<PdfArray>(PdfName.Create("Background")) is { } background)
        {
            pixels.AsSpan().Fill(ToPixel(colorSpace.ToRgb([.. Enumerable.Range(0, background.Count).Select(i => background.GetNumber(i))])));
        }

        var bbox = PdfRectangle.TryRead(dictionary.Get<PdfArray>(PdfNames.BBox), out var box) ? box : (PdfRectangle?)null;
        var context = new Context(pixels, width, height, left, top, deviceToShading, shadingToDevice, bbox, colorSpace, function);
        switch (dictionary.GetInteger(PdfName.Create("ShadingType"), 0))
        {
            case 1:
                RasterizeFunctionBased(context, dictionary);
                break;
            case 2:
                RasterizeAxial(context, dictionary, cache);
                break;
            case 3:
                RasterizeRadial(context, dictionary, cache);
                break;
            case 4 or 5 or 6 or 7 when resolved is PdfStream meshStream:
                RasterizeMesh(context, meshStream, cache);
                break;
            default:
                return null;
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height, configuration: cache.Options.Configuration);
    }

    private static void RasterizeFunctionBased(Context context, PdfDictionary dictionary)
    {
        var function = context.Function ?? throw new InvalidPdfContentException("A function-based shading has no function");
        if (function.InputCount != 2)
            throw new InvalidPdfContentException("The function of a function-based shading does not have two inputs");
        var domain = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Domain"))) is { Length: 4 } d ? d : [0, 1, 0, 1];
        var matrix = Graphics.ContentRenderer.ReadMatrix(dictionary.Get<PdfArray>(PdfNames.Matrix));
        if (!matrix.TryInvert(out var inverse))
            return;

        context.ForEachPixel((x, y) =>
        {
            var (u, v) = inverse.Transform(x, y);
            if (u < Math.Min(domain[0], domain[1]) || u > Math.Max(domain[0], domain[1]) || v < Math.Min(domain[2], domain[3]) || v > Math.Max(domain[2], domain[3]))
                return null;

            Span<double> inputs = [u, v];
            Span<double> outputs = stackalloc double[Math.Max(function.OutputCount, context.ColorSpace.ComponentCount)];
            function.Evaluate(inputs, outputs);
            return ToPixel(context.ColorSpace.ToRgb(outputs[..context.ColorSpace.ComponentCount]));
        });
    }

    private static void RasterizeAxial(Context context, PdfDictionary dictionary, RenderCache cache)
    {
        var coordinates = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Coords")));
        if (coordinates is not { Length: >= 4 })
            return;

        var (x0, y0, x1, y1) = (coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
        var (extendStart, extendEnd) = ReadExtend(dictionary);
        var lookup = cache.GetShadingColors(dictionary, () => CreateLookup(context, dictionary));
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = (dx * dx) + (dy * dy);
        if (length == 0)
            return;

        context.ForEachPixel((x, y) =>
        {
            var s = (((x - x0) * dx) + ((y - y0) * dy)) / length;
            if (s < 0)
            {
                if (!extendStart)
                    return null;

                s = 0;
            }
            else if (s > 1)
            {
                if (!extendEnd)
                    return null;

                s = 1;
            }

            return lookup[(int)Math.Round(s * (LookupSize - 1), MidpointRounding.AwayFromZero)];
        });
    }

    private static void RasterizeRadial(Context context, PdfDictionary dictionary, RenderCache cache)
    {
        var coordinates = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Coords")));
        if (coordinates is not { Length: >= 6 })
            return;

        var (x0, y0, r0, x1, y1, r1) = (coordinates[0], coordinates[1], coordinates[2], coordinates[3], coordinates[4], coordinates[5]);
        var (extendStart, extendEnd) = ReadExtend(dictionary);
        var lookup = cache.GetShadingColors(dictionary, () => CreateLookup(context, dictionary));
        var cdx = x1 - x0;
        var cdy = y1 - y0;
        var dr = r1 - r0;
        var a = (cdx * cdx) + (cdy * cdy) - (dr * dr);
        context.ForEachPixel((x, y) =>
        {
            // The largest s whose circle, of a radius that is not negative, goes through the point (8.7.4.5.4)
            var px = x - x0;
            var py = y - y0;
            var b = -2 * ((px * cdx) + (py * cdy) + (r0 * dr));
            var c = (px * px) + (py * py) - (r0 * r0);
            double? best = null;
            void Consider(double s)
            {
                if (!double.IsFinite(s) || r0 + (s * dr) < 0)
                    return;

                if ((s < 0 && !extendStart) || (s > 1 && !extendEnd))
                    return;

                if (best is null || s > best)
                {
                    best = s;
                }
            }

            if (Math.Abs(a) < 1e-12)
            {
                if (b != 0)
                {
                    Consider(-c / b);
                }
            }
            else
            {
                var discriminant = (b * b) - (4 * a * c);
                if (discriminant >= 0)
                {
                    var root = Math.Sqrt(discriminant);
                    Consider((-b + root) / (2 * a));
                    Consider((-b - root) / (2 * a));
                }
            }

            if (best is not { } value)
                return null;

            return lookup[(int)Math.Round(Math.Clamp(value, 0, 1) * (LookupSize - 1), MidpointRounding.AwayFromZero)];
        });
    }

    /// <summary>The colors of the positions from 0 to 1 of an axial or radial shading: the function at the matching value of the domain.</summary>
    private static Rgba32[] CreateLookup(Context context, PdfDictionary dictionary)
    {
        var function = context.Function ?? throw new InvalidPdfContentException("An axial or radial shading has no function");
        if (function.InputCount != 1)
            throw new InvalidPdfContentException("The function of an axial or radial shading does not have one input");
        var domain = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Domain"))) is { Length: 2 } d ? d : [0, 1];
        var lookup = new Rgba32[LookupSize];
        Span<double> outputs = stackalloc double[Math.Max(function.OutputCount, context.ColorSpace.ComponentCount)];
        for (var i = 0; i < LookupSize; i++)
        {
            Span<double> input = [domain[0] + ((domain[1] - domain[0]) * i / (LookupSize - 1))];
            function.Evaluate(input, outputs);
            lookup[i] = ToPixel(context.ColorSpace.ToRgb(outputs[..context.ColorSpace.ComponentCount]));
        }

        return lookup;
    }

    private static (bool Start, bool End) ReadExtend(PdfDictionary dictionary)
    {
        var extend = dictionary.Get<PdfArray>(PdfName.Create("Extend"));
        return (extend is { Count: 2 } && extend.GetResolved(0) is PdfBoolean { Value: true }, extend is { Count: 2 } && extend.GetResolved(1) is PdfBoolean { Value: true });
    }

    private static Rgba32 ToPixel(RgbColor color) => new(RenderColorSpace.ToByte(color.R), RenderColorSpace.ToByte(color.G), RenderColorSpace.ToByte(color.B), 255);

    /// <summary>Renders a mesh shading (types 4 to 7): its triangles, or its patches divided into triangles.</summary>
    private static void RasterizeMesh(Context context, PdfStream stream, RenderCache cache)
    {
        var mesh = MeshReader.Read(stream, context.ColorSpace.ComponentCount, context.Function is not null, MaxMeshItems);
        var colorCount = context.Function is not null ? 1 : context.ColorSpace.ComponentCount;
        var lookup = context.Function is not null ? CreateMeshLookup(context, mesh.Domain) : null;
        foreach (var triangle in mesh.Triangles)
        {
            cache.Charge(4);
            context.FillTriangle(triangle, colorCount, values =>
            {
                if (lookup is not null)
                {
                    var t = (values[0] - mesh.Domain[0]) / (mesh.Domain[1] - mesh.Domain[0]);
                    return lookup[(int)Math.Round(Math.Clamp(double.IsFinite(t) ? t : 0, 0, 1) * (LookupSize - 1), MidpointRounding.AwayFromZero)];
                }

                return ToPixel(context.ColorSpace.ToRgb(values));
            });
        }
    }

    private static Rgba32[] CreateMeshLookup(Context context, double[] domain)
    {
        var function = context.Function!;
        var lookup = new Rgba32[LookupSize];
        Span<double> outputs = stackalloc double[Math.Max(function.OutputCount, context.ColorSpace.ComponentCount)];
        for (var i = 0; i < LookupSize; i++)
        {
            Span<double> input = [domain[0] + ((domain[1] - domain[0]) * i / (LookupSize - 1))];
            function.Evaluate(input, outputs);
            lookup[i] = ToPixel(context.ColorSpace.ToRgb(outputs[..context.ColorSpace.ComponentCount]));
        }

        return lookup;
    }

    /// <summary>The image being rendered and the transformations between its pixels and shading space.</summary>
    private sealed class Context(Rgba32[] pixels, int width, int height, int left, int top, PdfMatrix deviceToShading, PdfMatrix shadingToDevice, PdfRectangle? bbox, RenderColorSpace colorSpace, RenderFunction? function)
    {
        public RenderColorSpace ColorSpace { get; } = colorSpace;

        public RenderFunction? Function { get; } = function;

        /// <summary>Sets each pixel whose center is in the bounding box to the color computed at its center in shading space; <see langword="null"/> leaves the pixel.</summary>
        public void ForEachPixel(Func<double, double, Rgba32?> color)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var (sx, sy) = deviceToShading.Transform(left + x + 0.5, top + y + 0.5);
                    if (bbox is { } box && (sx < box.Left || sx > box.Right || sy < box.Bottom || sy > box.Top))
                        continue;

                    if (color(sx, sy) is { } value)
                    {
                        pixels[(y * width) + x] = value;
                    }
                }
            }
        }

        /// <summary>Fills a triangle of shading space with the colors interpolated between its vertices (Gouraud shading).</summary>
        public void FillTriangle(MeshTriangle triangle, int colorCount, Func<double[], Rgba32> color)
        {
            var (ax, ay) = shadingToDevice.Transform(triangle.A.X, triangle.A.Y);
            var (bx, by) = shadingToDevice.Transform(triangle.B.X, triangle.B.Y);
            var (cx, cy) = shadingToDevice.Transform(triangle.C.X, triangle.C.Y);
            ax -= left;
            bx -= left;
            cx -= left;
            ay -= top;
            by -= top;
            cy -= top;
            var area = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
            if (Math.Abs(area) < 1e-9)
                return;

            var minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            var values = new double[colorCount];
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x + 0.5;
                    var py = y + 0.5;

                    // The barycentric coordinates of the center of the pixel; a small tolerance closes the seams between triangles
                    var wa = (((bx - px) * (cy - py)) - ((cx - px) * (by - py))) / area;
                    var wb = (((cx - px) * (ay - py)) - ((ax - px) * (cy - py))) / area;
                    var wc = 1 - wa - wb;
                    const double Tolerance = -1e-6;
                    if (wa < Tolerance || wb < Tolerance || wc < Tolerance)
                        continue;

                    if (bbox is { } box)
                    {
                        var (sx, sy) = deviceToShading.Transform(left + px, top + py);
                        if (sx < box.Left || sx > box.Right || sy < box.Bottom || sy > box.Top)
                            continue;
                    }

                    for (var i = 0; i < colorCount; i++)
                    {
                        values[i] = (wa * triangle.A.Color[i]) + (wb * triangle.B.Color[i]) + (wc * triangle.C.Color[i]);
                    }

                    pixels[(y * width) + x] = color(values);
                }
            }
        }
    }
}
