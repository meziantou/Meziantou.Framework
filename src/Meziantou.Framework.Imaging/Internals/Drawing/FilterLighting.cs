using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Lights the surface whose height is the alpha of an input, as "Filter Effects Module Level 1" defines
/// <c>feDiffuseLighting</c> and <c>feSpecularLighting</c>.
/// </summary>
/// <remarks>
/// <para>
/// The normal of a pixel is <c>(Nx, Ny, 1)</c> normalized, with <c>Nx = -surfaceScale FACTORx (Kx * A)</c> and the same
/// along y: the Sobel kernels of the specification, and those of its edges and corners where a neighbor is beyond the
/// region of the filter or the surface. They are one rule: along x, the difference of the alphas of the pixels on both
/// sides (of the pixel and the one on the side that exists, on an edge), on the row of the pixel with a weight of 2
/// and on each row above and below that exists with a weight of 1, multiplied by 1 (2 on an edge) over the sum of the
/// weights. A pixel without a neighbor on either side along an axis has a normal without that component.
/// </para>
/// <para>
/// A diffuse light is <c>kd N.L C</c>, opaque; a specular light is <c>ks (N.H)^exponent C</c> with <c>H</c> the half
/// vector of <c>L</c> and of the eye <c>(0, 0, 1)</c>, and its alpha is the largest of its colors (they are its colors
/// multiplied by its alpha). The powers use <see cref="Math.Pow(double, double)"/>. Each value is clamped to the range
/// from 0 to 1. A pixel is at <c>(x, y)</c> on the canvas, its top left corner, and at the height <c>surfaceScale A</c>.
/// </para>
/// </remarks>
internal static class FilterLighting
{
    public static void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion, FilterLightSource source, Rgba64 color, double surfaceScale, bool isSpecular, double constant, double exponent)
    {
        var input = context.GetInput(0);
        var alphas = input.Values;
        var edges = Rectangle.Intersect(context.Region, input.Bounds);
        var light = source.Place(context.Processor);
        var (red, green, blue, _) = context.GetColor(color);
        var values = result.Values;
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            context.Processor.CancellationToken.ThrowIfCancellationRequested();
            for (var x = subregion.Left; x < subregion.Right; x++)
            {
                var hasLeft = x - 1 >= edges.Left && x - 1 < edges.Right;
                var hasRight = x + 1 >= edges.Left && x + 1 < edges.Right;
                var hasTop = y - 1 >= edges.Top && y - 1 < edges.Bottom;
                var hasBottom = y + 1 >= edges.Top && y + 1 < edges.Bottom;

                // Along x: the differences of the rows, weighted 1, 2, 1
                double nx = 0;
                if (hasLeft || hasRight)
                {
                    double sum = 0;
                    double weights = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        if ((dy == -1 && !hasTop) || (dy == 1 && !hasBottom))
                            continue;

                        var weight = dy == 0 ? 2 : 1;
                        var right = hasRight ? Alpha(alphas, input, x + 1, y + dy) : Alpha(alphas, input, x, y + dy);
                        var left = hasLeft ? Alpha(alphas, input, x - 1, y + dy) : Alpha(alphas, input, x, y + dy);
                        sum += weight * (right - left);
                        weights += weight;
                    }

                    nx = -surfaceScale * ((hasLeft && hasRight ? 1 : 2) / weights) * sum;
                }

                double ny = 0;
                if (hasTop || hasBottom)
                {
                    double sum = 0;
                    double weights = 0;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == -1 && !hasLeft) || (dx == 1 && !hasRight))
                            continue;

                        var weight = dx == 0 ? 2 : 1;
                        var bottom = hasBottom ? Alpha(alphas, input, x + dx, y + 1) : Alpha(alphas, input, x + dx, y);
                        var top = hasTop ? Alpha(alphas, input, x + dx, y - 1) : Alpha(alphas, input, x + dx, y);
                        sum += weight * (bottom - top);
                        weights += weight;
                    }

                    ny = -surfaceScale * ((hasTop && hasBottom ? 1 : 2) / weights) * sum;
                }

                var normal = Math.Sqrt((nx * nx) + (ny * ny) + 1);
                nx /= normal;
                ny /= normal;
                var nz = 1 / normal;

                // The unit vector toward the light, and what reaches the pixel of it
                double lx, ly, lz, intensity = 1;
                if (light.Kind == FilterLightKind.Distant)
                {
                    (lx, ly, lz) = (light.X, light.Y, light.Z);
                }
                else
                {
                    lx = light.X - x;
                    ly = light.Y - y;
                    lz = light.Z - (surfaceScale * (x >= edges.Left && x < edges.Right && y >= edges.Top && y < edges.Bottom ? Alpha(alphas, input, x, y) : 0));
                    var length = Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
                    if (length > 0)
                    {
                        lx /= length;
                        ly /= length;
                        lz /= length;
                    }

                    if (light.Kind == FilterLightKind.Spot)
                    {
                        var cosine = -((lx * light.AxisX) + (ly * light.AxisY) + (lz * light.AxisZ));
                        intensity = cosine < light.ConeCosine || !(cosine > 0) ? 0 : Math.Pow(cosine, light.Exponent);
                    }
                }

                double factor;
                if (isSpecular)
                {
                    var hx = lx;
                    var hy = ly;
                    var hz = lz + 1;
                    var length = Math.Sqrt((hx * hx) + (hy * hy) + (hz * hz));
                    var dot = length > 0 ? ((nx * hx) + (ny * hy) + (nz * hz)) / length : 0;
                    factor = dot > 0 ? constant * Math.Pow(dot, exponent) : 0;
                }
                else
                {
                    factor = constant * ((nx * lx) + (ny * ly) + (nz * lz));
                }

                var r = Clamp(factor * intensity * red);
                var g = Clamp(factor * intensity * green);
                var b = Clamp(factor * intensity * blue);
                var offset = result.IndexOf(x, y);
                values[offset] = (float)r;
                values[offset + 1] = (float)g;
                values[offset + 2] = (float)b;
                values[offset + 3] = (float)(isSpecular ? Math.Max(r, Math.Max(g, b)) : 1);
            }
        }

        static double Alpha(Span<float> alphas, FilterSurface input, int x, int y) => alphas[input.IndexOf(x, y) + 3];

        // Written so that a value that is not a number is zero
        static double Clamp(double value) => value > 0 ? (value < 1 ? value : 1) : 0;
    }
}
