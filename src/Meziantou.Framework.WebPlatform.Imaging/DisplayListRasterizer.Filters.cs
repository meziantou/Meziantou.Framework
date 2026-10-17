using Meziantou.Framework.WebPlatform.Rendering;
using ColorMatrix = Meziantou.Framework.Imaging.ColorMatrix;
using D = Meziantou.Framework.Imaging.Drawing;
using PointD = Meziantou.Framework.Imaging.PointD;
using Rgba64 = Meziantou.Framework.Imaging.Rgba64;

namespace Meziantou.Framework.WebPlatform.Imaging;

internal sealed partial class DisplayListRasterizer
{
    /// <summary>The number of coefficients of a color matrix (4 rows of 5 values).</summary>
    private const int ColorMatrixLength = 20;

    /// <summary>
    /// Creates the filter of the drawing API of the filters of a display list, applied one after the other
    /// (https://drafts.fxtf.org/filter-effects/#FilterProperty), or returns null when no filter changes anything.
    /// </summary>
    private D.Filter? CreateFilter(DisplayFilter[] filters)
    {
        var result = new List<D.Filter>(filters.Length);
        foreach (var filter in filters)
        {
            if (CreateFilter(filter) is { } converted)
            {
                result.Add(converted);
            }
        }

        return result.Count switch
        {
            0 => null,
            1 => result[0],
            _ => new D.FilterChain([.. result]),
        };
    }

    private D.Filter? CreateFilter(DisplayFilter filter)
    {
        switch (filter)
        {
            case BlurFilter blur:
                return new D.BlurFilter(ToDeviation(blur.DeviationX), ToDeviation(blur.DeviationY));

            case DropShadowFilter shadow:
                return new D.DropShadowFilter(ToColor(shadow.Color), ToOffset(shadow.Offset.X, shadow.Offset.Y), ToDeviation(shadow.Deviation)) { ShadowOnly = shadow.ShadowOnly };

            case ColorMatrixFilter matrix:
                return matrix.Matrix is { Length: ColorMatrixLength } coefficients ? new D.ColorMatrixFilter(ToColorMatrix(coefficients)) : null;

            case LuminanceToAlphaFilter:
                // The luminance of the colors multiplied by their alpha is the alpha of the result (a luminance mask): the
                // luminance of the straight colors, then multiplied by the alpha of the drawing
                return new D.FilterGraph(
                    new D.ColorMatrixPrimitive(ToColorMatrix([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.2126f, 0.7152f, 0.0722f, 0, 0])) { Input = D.FilterInput.SourceGraphic, ColorSpace = D.FilterColorSpace.Srgb },
                    new D.CompositePrimitive(D.FilterCompositeOperator.In, D.FilterInput.SourceGraphic) { ColorSpace = D.FilterColorSpace.Srgb });

            case FilterGraph graph:
                return CreateFilterGraph(graph);

            default:
                return null;
        }
    }

    /// <summary>Creates the graph of the primitives of an SVG filter element (https://drafts.fxtf.org/filter-effects/#FilterElement).</summary>
    private D.FilterGraph? CreateFilterGraph(FilterGraph graph)
    {
        var primitives = graph.Primitives ?? [];
        if (primitives.Length == 0)
            return null;

        // The transparent input (an input that refers to no result) is the result of a transparent flood before the primitives
        var needsTransparent = false;
        for (var i = 0; i < primitives.Length && !needsTransparent; i++)
        {
            foreach (var input in primitives[i]?.Inputs ?? [])
            {
                if (input == FilterInput.Transparent || input >= i || (input < 0 && input is not (FilterInput.SourceGraphic or FilterInput.SourceAlpha)))
                {
                    needsTransparent = true;
                    break;
                }
            }
        }

        var offset = needsTransparent ? 1 : 0;
        var result = new D.FilterPrimitive[primitives.Length + offset];
        if (needsTransparent)
        {
            result[0] = new D.FloodPrimitive(default(Rgba64));
        }

        for (var i = 0; i < primitives.Length; i++)
        {
            result[i + offset] = CreatePrimitive(primitives[i], i, offset) ?? Transparent(primitives[i]);
        }

        var region = graph.Region;
        return new D.FilterGraph(result) { Region = PathConversions.ToRectangle(region) };
    }

    /// <summary>A primitive whose result is transparent (an invalid primitive).</summary>
    private static D.FloodPrimitive Transparent(FilterPrimitive? primitive)
        => new(default(Rgba64)) { Subregion = primitive is null ? null : PathConversions.ToRectangle(primitive.Subregion) };

    private D.FilterPrimitive? CreatePrimitive(FilterPrimitive? primitive, int index, int offset)
    {
        if (primitive is null)
            return null;

        var colorSpace = primitive.LinearRgb ? D.FilterColorSpace.LinearRgb : D.FilterColorSpace.Srgb;
        var subregion = PathConversions.ToRectangle(primitive.Subregion);
        var input = GetInput(primitive, 0, index, offset);
        switch (primitive)
        {
            case ImagePrimitive image:
            {
                var content = image.Content;
                return new D.ImagePrimitive(canvas => DrawNested(canvas, content, _matrix)) { Subregion = subregion, ColorSpace = colorSpace };
            }

            case FloodPrimitive flood:
                return new D.FloodPrimitive(ToColor(flood.Color)) { Subregion = subregion, ColorSpace = colorSpace };

            case GaussianBlurPrimitive blur:
                if (blur.DeviationX < 0 || blur.DeviationY < 0)
                    return null;

                return new D.GaussianBlurPrimitive(ToDeviation(blur.DeviationX), ToDeviation(blur.DeviationY)) { Input = input, EdgeMode = ToEdgeMode(blur.EdgeMode), Subregion = subregion, ColorSpace = colorSpace };

            case OffsetPrimitive offsetPrimitive:
                return new D.OffsetPrimitive(ToOffset(offsetPrimitive.Dx, offsetPrimitive.Dy)) { Input = input, Subregion = subregion, ColorSpace = colorSpace };

            case DropShadowPrimitive shadow:
                if (shadow.DeviationX < 0 || shadow.DeviationY < 0)
                    return null;

                return new D.DropShadowPrimitive(ToColor(shadow.Color), ToOffset(shadow.Dx, shadow.Dy), ToDeviation(shadow.DeviationX), ToDeviation(shadow.DeviationY)) { Input = input, Subregion = subregion, ColorSpace = colorSpace };

            case ColorMatrixPrimitive matrix:
                if (matrix.Matrix is not { Length: ColorMatrixLength } coefficients)
                    return null;

                return new D.ColorMatrixPrimitive(ToColorMatrix(coefficients)) { Input = input, Subregion = subregion, ColorSpace = colorSpace };

            case ComponentTransferPrimitive transfer:
                return new D.ComponentTransferPrimitive
                {
                    Input = input,
                    Red = ToTransferFunction(transfer.Red),
                    Green = ToTransferFunction(transfer.Green),
                    Blue = ToTransferFunction(transfer.Blue),
                    Alpha = ToTransferFunction(transfer.Alpha),
                    Subregion = subregion,
                    ColorSpace = colorSpace,
                };

            case CompositePrimitive composite:
            {
                var op = composite.Operator switch
                {
                    CompositeOperator.In => D.FilterCompositeOperator.In,
                    CompositeOperator.Out => D.FilterCompositeOperator.Out,
                    CompositeOperator.Atop => D.FilterCompositeOperator.Atop,
                    CompositeOperator.Xor => D.FilterCompositeOperator.Xor,
                    CompositeOperator.Lighter => D.FilterCompositeOperator.Lighter,
                    CompositeOperator.Arithmetic => D.FilterCompositeOperator.Arithmetic,
                    _ => D.FilterCompositeOperator.Over,
                };

                var arithmetic = op == D.FilterCompositeOperator.Arithmetic;
                return new D.CompositePrimitive(op, GetInput(primitive, 1, index, offset))
                {
                    Input = input,
                    K1 = arithmetic ? Finite(composite.K1) : 0,
                    K2 = arithmetic ? Finite(composite.K2) : 0,
                    K3 = arithmetic ? Finite(composite.K3) : 0,
                    K4 = arithmetic ? Finite(composite.K4) : 0,
                    Subregion = subregion,
                    ColorSpace = colorSpace,
                };
            }

            case BlendPrimitive blend:
                return new D.BlendPrimitive(GetModes(blend.Mode).Blend, GetInput(primitive, 1, index, offset)) { Input = input, Subregion = subregion, ColorSpace = colorSpace };

            case MergePrimitive:
            {
                var count = primitive.Inputs?.Length ?? 0;
                var inputs = new D.FilterInput[count];
                for (var i = 0; i < count; i++)
                {
                    inputs[i] = GetInput(primitive, i, index, offset);
                }

                return new D.MergePrimitive(inputs) { Subregion = subregion, ColorSpace = colorSpace };
            }

            case MorphologyPrimitive morphology:
                if (morphology.RadiusX < 0 || morphology.RadiusY < 0 || !double.IsFinite(morphology.RadiusX) || !double.IsFinite(morphology.RadiusY))
                    return null;

                return new D.MorphologyPrimitive(morphology.Dilate ? D.FilterMorphologyOperator.Dilate : D.FilterMorphologyOperator.Erode, Math.Min(morphology.RadiusX, PathConversions.MaxCoordinate), Math.Min(morphology.RadiusY, PathConversions.MaxCoordinate)) { Input = input, Subregion = subregion, ColorSpace = colorSpace };

            case TurbulencePrimitive turbulence:
                return CreateTurbulence(turbulence, subregion, colorSpace);

            case TilePrimitive:
                return new D.TilePrimitive { Input = input, Subregion = subregion, ColorSpace = colorSpace };

            case ConvolveMatrixPrimitive convolve:
            {
                if (convolve.OrderX is < 1 or > 255 || convolve.OrderY is < 1 or > 255 || convolve.Kernel is null || convolve.Kernel.Length != convolve.OrderX * convolve.OrderY
                    || convolve.TargetX < 0 || convolve.TargetX >= convolve.OrderX || convolve.TargetY < 0 || convolve.TargetY >= convolve.OrderY
                    || !double.IsFinite(convolve.Divisor) || convolve.Divisor == 0 || !Array.TrueForAll(convolve.Kernel, float.IsFinite))
                {
                    return null;
                }

                var kernel = new double[convolve.Kernel.Length];
                for (var i = 0; i < kernel.Length; i++)
                {
                    kernel[i] = convolve.Kernel[i];
                }

                return new D.ConvolveMatrixPrimitive(convolve.OrderX, convolve.OrderY, kernel)
                {
                    Input = input,
                    Divisor = convolve.Divisor,
                    Bias = Finite(convolve.Bias),
                    TargetX = convolve.TargetX,
                    TargetY = convolve.TargetY,
                    EdgeMode = ToEdgeMode(convolve.EdgeMode),
                    PreserveAlpha = convolve.PreserveAlpha,
                    Subregion = subregion,
                    ColorSpace = colorSpace,
                };
            }

            case DisplacementMapPrimitive displacement:
                return new D.DisplacementMapPrimitive(PathConversions.Clamp(displacement.Scale), GetInput(primitive, 1, index, offset))
                {
                    Input = input,
                    XChannel = ToChannel(displacement.XChannel),
                    YChannel = ToChannel(displacement.YChannel),
                    Subregion = subregion,
                    ColorSpace = colorSpace,
                };

            case LightingPrimitive lighting:
            {
                if (CreateLight(lighting.Light) is not { } light)
                    return null;

                var color = ToColor(lighting.Color with { A = 1 });
                if (lighting.Specular)
                {
                    return new D.SpecularLightingPrimitive(light, color)
                    {
                        Input = input,
                        SurfaceScale = PathConversions.Clamp(lighting.SurfaceScale),
                        SpecularConstant = Math.Clamp(Finite(lighting.Constant), 0, PathConversions.MaxCoordinate),
                        SpecularExponent = Math.Clamp(double.IsFinite(lighting.SpecularExponent) ? lighting.SpecularExponent : 1, 1, 128),
                        Subregion = subregion,
                        ColorSpace = colorSpace,
                    };
                }

                return new D.DiffuseLightingPrimitive(light, color)
                {
                    Input = input,
                    SurfaceScale = PathConversions.Clamp(lighting.SurfaceScale),
                    DiffuseConstant = Math.Clamp(Finite(lighting.Constant), 0, PathConversions.MaxCoordinate),
                    Subregion = subregion,
                    ColorSpace = colorSpace,
                };
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Creates a turbulence: the noise is a function of the coordinates of the user space of the filter, which the transform of
    /// the primitive maps to the coordinates of the drawing (the noise of another space is computed in it, as an image).
    /// </summary>
    private D.FilterPrimitive? CreateTurbulence(TurbulencePrimitive turbulence, Meziantou.Framework.Imaging.RectangleD subregion, D.FilterColorSpace colorSpace)
    {
        if (!double.IsFinite(turbulence.BaseFrequencyX) || !double.IsFinite(turbulence.BaseFrequencyY) || turbulence.BaseFrequencyX < 0 || turbulence.BaseFrequencyY < 0)
            return null;

        var type = turbulence.FractalNoise ? D.FilterTurbulenceType.FractalNoise : D.FilterTurbulenceType.Turbulence;
        var seed = double.IsFinite(turbulence.Seed) ? (int)Math.Clamp(Math.Round(turbulence.Seed, MidpointRounding.ToZero), int.MinValue, int.MaxValue) : 0;
        var octaves = Math.Max(0, turbulence.Octaves);
        var transform = turbulence.Transform;
        if (transform == DisplayMatrix.Identity)
            return new D.TurbulencePrimitive(type, turbulence.BaseFrequencyX, turbulence.BaseFrequencyY, octaves, seed) { StitchTiles = turbulence.StitchTiles, Subregion = subregion, ColorSpace = colorSpace };

        // The subregion in the user space of the noise
        var inverse = Invert(transform);
        if (inverse is not { } toNoise)
            return null;

        var noiseRegion = MapBounds(toNoise, subregion);
        var noise = new D.FilterGraph(new D.TurbulencePrimitive(type, turbulence.BaseFrequencyX, turbulence.BaseFrequencyY, octaves, seed) { StitchTiles = turbulence.StitchTiles, Subregion = noiseRegion, ColorSpace = colorSpace }) { Region = noiseRegion };
        var affine = PathConversions.ToAffine(transform);
        return new D.ImagePrimitive(canvas => canvas.Transform(affine, noise, static (c, noise) => c.Filter(noise, static _ => { }))) { Subregion = subregion, ColorSpace = colorSpace };

        static DisplayMatrix? Invert(DisplayMatrix m)
        {
            var determinant = (m.A * m.D) - (m.B * m.C);
            if (!double.IsFinite(determinant) || determinant == 0)
                return null;

            return new DisplayMatrix(m.D / determinant, -m.B / determinant, -m.C / determinant, m.A / determinant, ((m.C * m.F) - (m.D * m.E)) / determinant, ((m.B * m.E) - (m.A * m.F)) / determinant);
        }

        static Meziantou.Framework.Imaging.RectangleD MapBounds(DisplayMatrix m, Meziantou.Framework.Imaging.RectangleD rect)
        {
            Span<Css.Layout.PhysicalOffset> corners =
            [
                m.Apply(new(rect.X, rect.Y)),
                m.Apply(new(rect.X + rect.Width, rect.Y)),
                m.Apply(new(rect.X, rect.Y + rect.Height)),
                m.Apply(new(rect.X + rect.Width, rect.Y + rect.Height)),
            ];
            double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
            foreach (var corner in corners)
            {
                left = Math.Min(left, corner.X);
                top = Math.Min(top, corner.Y);
                right = Math.Max(right, corner.X);
                bottom = Math.Max(bottom, corner.Y);
            }

            return Meziantou.Framework.Imaging.RectangleD.FromLTRB(PathConversions.Clamp(left), PathConversions.Clamp(top), PathConversions.Clamp(right), PathConversions.Clamp(bottom));
        }
    }

    private static D.FilterLightSource? CreateLight(FilterLight light)
    {
        switch (light)
        {
            case DistantLight distant:
                return new D.DistantLight(Finite(distant.Azimuth), Finite(distant.Elevation));

            case PointLight point:
                return new D.PointLight(PathConversions.Clamp(point.X), PathConversions.Clamp(point.Y), PathConversions.Clamp(point.Z));

            case SpotLight spot:
                return new D.SpotLight(PathConversions.Clamp(spot.X), PathConversions.Clamp(spot.Y), PathConversions.Clamp(spot.Z), PathConversions.Clamp(spot.PointsAtX), PathConversions.Clamp(spot.PointsAtY), PathConversions.Clamp(spot.PointsAtZ))
                {
                    SpecularExponent = Math.Clamp(Finite(spot.SpecularExponent), 0, 128),
                    LimitingConeAngle = spot.LimitingConeAngle is { } angle && double.IsFinite(angle) ? angle : null,
                };

            default:
                return null;
        }
    }

    /// <summary>The input of a primitive: the drawing, its alpha, the result of a primitive before it, or a transparent image.</summary>
    private static D.FilterInput GetInput(FilterPrimitive primitive, int inputIndex, int primitiveIndex, int offset)
    {
        var input = primitive.Inputs is { } inputs && inputIndex < inputs.Length ? inputs[inputIndex] : FilterInput.SourceGraphic;
        return input switch
        {
            FilterInput.SourceGraphic => D.FilterInput.SourceGraphic,
            FilterInput.SourceAlpha => D.FilterInput.SourceAlpha,
            >= 0 when input < primitiveIndex => D.FilterInput.FromResult(input + offset),
            _ => D.FilterInput.FromResult(0),
        };
    }

    private static D.FilterEdgeMode ToEdgeMode(FilterEdgeMode edgeMode) => edgeMode switch
    {
        FilterEdgeMode.Duplicate => D.FilterEdgeMode.Duplicate,
        FilterEdgeMode.Wrap => D.FilterEdgeMode.Wrap,
        _ => D.FilterEdgeMode.Transparent,
    };

    private static D.FilterChannel ToChannel(int channel) => channel switch
    {
        0 => D.FilterChannel.Red,
        1 => D.FilterChannel.Green,
        2 => D.FilterChannel.Blue,
        _ => D.FilterChannel.Alpha,
    };

    /// <summary>The function of a table of 256 values (the values of the 8-bit components), interpolated between them.</summary>
    private static D.TransferFunction ToTransferFunction(byte[]? table)
    {
        if (table is not { Length: > 0 })
            return D.TransferFunction.Identity;

        var values = new double[table.Length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = table[i] / 255d;
        }

        return D.TransferFunction.CreateTable(values);
    }

    private static ColorMatrix ToColorMatrix(float[] coefficients)
    {
        Span<double> values = stackalloc double[ColorMatrixLength];
        for (var i = 0; i < ColorMatrixLength; i++)
        {
            values[i] = float.IsFinite(coefficients[i]) ? coefficients[i] : 0;
        }

        return new ColorMatrix(values);
    }

    private Rgba64 ToColor(DisplayColor color) => ColorSpaces.ToRgba64(color, _colorSpace);

    private static PointD ToOffset(double x, double y) => new(PathConversions.Clamp(x), PathConversions.Clamp(y));

    private static double ToDeviation(double deviation) => double.IsFinite(deviation) && deviation > 0 ? Math.Min(deviation, MaxDeviation) : 0;

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
