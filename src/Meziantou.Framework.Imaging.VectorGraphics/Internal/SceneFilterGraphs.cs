using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.VectorGraphics.Internal;

/// <summary>Turns the filter graphs of a scene into the filter graphs of the canvas.</summary>
/// <remarks>
/// The canvas filters in the coordinates of the drawing at its <c>Filter</c> call: the renderer calls it outside the
/// transform of the element, in the coordinates of its parent, where the scene places the region and the primitives.
/// The values the canvas does not accept are brought within its limits: the coordinates within 16777216, the standard
/// deviations within 10000, the specular exponents of the spot lights within 128.
/// </remarks>
internal static class SceneFilterGraphs
{
    private const double MaxCoordinate = 16777216;
    private const double MaxDeviation = 10000;

    /// <summary>Creates the filter of an element, or returns <see langword="null"/> when it draws nothing: an empty region, or no region and nothing to filter.</summary>
    public static FilterGraph? Create(SceneElement element, SceneFilterGraph graph, SceneRenderer.DrawContext context)
    {
        SceneBounds region;
        if (graph.Region is { } explicitRegion)
        {
            region = explicitRegion;
        }
        else
        {
            // The filter applies inside the clips of the element: its region is what it draws before them, enlarged by
            // the reach of the primitives and a margin of a pixel, as in SVG
            if (SceneRenderer.GetBounds(element, context, applyClips: false) is not { } bounds)
                return null;

            var enlarged = graph.GetRegion(new SceneBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            region = new SceneBounds(enlarged.X - 1, enlarged.Y - 1, enlarged.Width + 2, enlarged.Height + 2);
        }

        var rectangle = ToRectangle(region);
        if (!(rectangle.Width > 0 && rectangle.Height > 0))
            return null;

        var primitives = new FilterPrimitive[graph.Primitives.Count];
        for (var i = 0; i < primitives.Length; i++)
        {
            primitives[i] = Create(graph.Primitives[i], context);
        }

        return new FilterGraph(primitives) { Region = rectangle };
    }

    // A rectangle within the limits of the canvas
    private static RectangleD ToRectangle(SceneBounds bounds)
    {
        var left = Coordinate(bounds.X);
        var top = Coordinate(bounds.Y);
        var right = Coordinate(bounds.Right);
        var bottom = Coordinate(bounds.Bottom);
        return new RectangleD(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static FilterPrimitive Create(SceneFilterPrimitive primitive, SceneRenderer.DrawContext context)
    {
        var subregion = primitive.Subregion is { } bounds ? ToRectangle(bounds) : (RectangleD?)null;
        var space = primitive.ColorSpace is SceneFilterColorSpace.Srgb ? FilterColorSpace.Srgb : FilterColorSpace.LinearRgb;
        return primitive switch
        {
            SceneGaussianBlurPrimitive blur => new GaussianBlurPrimitive(Deviation(blur.StandardDeviationX), Deviation(blur.StandardDeviationY))
            {
                EdgeMode = ToEdgeMode(blur.EdgeMode),
                Input = ToInput(blur.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneOffsetPrimitive offset => new OffsetPrimitive(new PointD(Coordinate(offset.OffsetX), Coordinate(offset.OffsetY)))
            {
                Input = ToInput(offset.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneFloodPrimitive flood => new FloodPrimitive(ToColor(flood.Color))
            {
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneCompositePrimitive composite => new CompositePrimitive((FilterCompositeOperator)composite.Operator, ToInput(composite.Input2))
            {
                Input = ToInput(composite.Input),
                K1 = composite.K1,
                K2 = composite.K2,
                K3 = composite.K3,
                K4 = composite.K4,
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneBlendPrimitive blend => new BlendPrimitive(SceneRenderer.ToBlendMode(blend.Mode), ToInput(blend.Input2))
            {
                Input = ToInput(blend.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneMergePrimitive merge => new MergePrimitive([.. merge.Inputs.Select(ToInput)])
            {
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneMorphologyPrimitive morphology => new MorphologyPrimitive((FilterMorphologyOperator)morphology.Operator, Math.Min(morphology.RadiusX, MaxCoordinate), Math.Min(morphology.RadiusY, MaxCoordinate))
            {
                Input = ToInput(morphology.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneTurbulencePrimitive turbulence => new TurbulencePrimitive((FilterTurbulenceType)turbulence.Type, turbulence.BaseFrequencyX, turbulence.BaseFrequencyY, turbulence.Octaves, turbulence.Seed)
            {
                StitchTiles = turbulence.StitchTiles,
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneDisplacementMapPrimitive displacement => new DisplacementMapPrimitive(Coordinate(displacement.Scale), ToInput(displacement.Map))
            {
                Input = ToInput(displacement.Input),
                XChannel = (FilterChannel)displacement.XChannel,
                YChannel = (FilterChannel)displacement.YChannel,
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneConvolveMatrixPrimitive convolve => new ConvolveMatrixPrimitive(convolve.OrderX, convolve.OrderY, [.. convolve.Kernel])
            {
                Divisor = convolve.Divisor,
                Bias = convolve.Bias,
                TargetX = convolve.TargetX ?? (convolve.OrderX / 2),
                TargetY = convolve.TargetY ?? (convolve.OrderY / 2),
                EdgeMode = ToEdgeMode(convolve.EdgeMode),
                PreserveAlpha = convolve.PreserveAlpha,
                Input = ToInput(convolve.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneDiffuseLightingPrimitive diffuse => new DiffuseLightingPrimitive(ToLight(diffuse.Light), ToLightColor(diffuse.Color))
            {
                SurfaceScale = Coordinate(diffuse.SurfaceScale),
                DiffuseConstant = Math.Min(diffuse.DiffuseConstant, MaxCoordinate),
                Input = ToInput(diffuse.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneSpecularLightingPrimitive specular => new SpecularLightingPrimitive(ToLight(specular.Light), ToLightColor(specular.Color))
            {
                SurfaceScale = Coordinate(specular.SurfaceScale),
                SpecularConstant = Math.Min(specular.SpecularConstant, MaxCoordinate),
                SpecularExponent = specular.SpecularExponent,
                Input = ToInput(specular.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneComponentTransferPrimitive transfer => new ComponentTransferPrimitive
            {
                Red = ToTransferFunction(transfer.Red),
                Green = ToTransferFunction(transfer.Green),
                Blue = ToTransferFunction(transfer.Blue),
                Alpha = ToTransferFunction(transfer.Alpha),
                Input = ToInput(transfer.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneTilePrimitive tile => new TilePrimitive
            {
                Input = ToInput(tile.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneImagePrimitive image => new ImagePrimitive(canvas => DrawImage(canvas, image, context))
            {
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneColorMatrixPrimitive matrix => new ColorMatrixPrimitive(new ColorMatrix([.. matrix.Values]))
            {
                Input = ToInput(matrix.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            SceneDropShadowPrimitive shadow => new DropShadowPrimitive(ToColor(shadow.Color), new PointD(Coordinate(shadow.OffsetX), Coordinate(shadow.OffsetY)), Deviation(shadow.StandardDeviationX), Deviation(shadow.StandardDeviationY))
            {
                Input = ToInput(shadow.Input),
                Subregion = subregion,
                ColorSpace = space,
            },
            _ => throw new NotSupportedException($"The filter primitive '{primitive.GetType()}' is not supported."),
        };
    }

    // The element of an image primitive is drawn with the context of the scene; an element that draws itself through its
    // own filter draws nothing there
    private static void DrawImage(DrawingCanvas canvas, SceneImagePrimitive primitive, SceneRenderer.DrawContext context)
    {
        if (!context.EnterFilterImage(primitive))
            return;

        try
        {
            SceneRenderer.DrawSceneElement(canvas, primitive.Content, context);
        }
        finally
        {
            context.ExitFilterImage(primitive);
        }
    }

    private static FilterInput ToInput(SceneFilterInput input) => input.Kind switch
    {
        SceneFilterInputKind.SourceGraphic => FilterInput.SourceGraphic,
        SceneFilterInputKind.SourceAlpha => FilterInput.SourceAlpha,
        SceneFilterInputKind.Result => FilterInput.FromResult(input.ResultIndex),
        _ => FilterInput.Previous,
    };

    private static FilterEdgeMode ToEdgeMode(SceneFilterEdgeMode mode) => mode switch
    {
        SceneFilterEdgeMode.Duplicate => FilterEdgeMode.Duplicate,
        SceneFilterEdgeMode.Wrap => FilterEdgeMode.Wrap,
        SceneFilterEdgeMode.Mirror => FilterEdgeMode.Mirror,
        _ => FilterEdgeMode.Transparent,
    };

    private static FilterLightSource ToLight(SceneLightSource light) => light switch
    {
        SceneDistantLight distant => new DistantLight(distant.Azimuth, distant.Elevation),
        ScenePointLight point => new PointLight(Coordinate(point.X), Coordinate(point.Y), Coordinate(point.Z)),
        SceneSpotLight spot => new SpotLight(Coordinate(spot.X), Coordinate(spot.Y), Coordinate(spot.Z), Coordinate(spot.PointsAtX), Coordinate(spot.PointsAtY), Coordinate(spot.PointsAtZ))
        {
            SpecularExponent = Math.Min(spot.SpecularExponent, 128),
            LimitingConeAngle = spot.LimitingConeAngle,
        },
        _ => throw new NotSupportedException($"The light '{light.GetType()}' is not supported."),
    };

    private static TransferFunction ToTransferFunction(SceneTransferFunction function) => function.Kind switch
    {
        SceneTransferFunctionKind.Table => TransferFunction.CreateTable([.. function.Values]),
        SceneTransferFunctionKind.Discrete => TransferFunction.CreateDiscrete([.. function.Values]),
        SceneTransferFunctionKind.Linear => TransferFunction.CreateLinear(function.Slope, function.Intercept),
        SceneTransferFunctionKind.Gamma => TransferFunction.CreateGamma(function.Amplitude, function.Exponent, function.Offset),
        _ => TransferFunction.Identity,
    };

    private static Rgba64 ToColor(SceneColor color) => Rgba64.FromRgba32(new Rgba32(color.R, color.G, color.B, color.A));

    // The color of a light has no alpha, as the lighting-color of SVG
    private static Rgba64 ToLightColor(SceneColor color) => Rgba64.FromRgba32(new Rgba32(color.R, color.G, color.B, 255));

    private static double Coordinate(double value) => Math.Clamp(value, -MaxCoordinate, MaxCoordinate);

    private static double Deviation(double value) => Math.Min(value, MaxDeviation);
}
