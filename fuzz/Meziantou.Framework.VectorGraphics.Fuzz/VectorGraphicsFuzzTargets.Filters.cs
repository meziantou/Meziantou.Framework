using System.Globalization;
using System.Xml;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.VectorGraphics.Fuzz;

public static partial class VectorGraphicsFuzzTargets
{
    private static readonly double[] FilterValues = [0, 1, -1, 0.5, 2.5, 10, -10, 128, 300, 1e-9, 1e9, -1e9, double.NaN, double.PositiveInfinity, 3.3, 255];

    // A property: the list of the targets reads it, and the static fields of the partial files have no defined order
    private static string[] FilterTokens => ["\0", "\u0001", "\u0002", "\u0010", "\u0011", "ÿ", "\u0080"];

    /// <summary>
    /// The input picks the primitives of a filter graph, their inputs and their numbers. A primitive whose numbers are not
    /// valid throws <see cref="ArgumentException"/>, and the graph throws it exactly when a primitive reads the result of
    /// a primitive that is not before it. The SVG of a scene filtered by the graph is a well-formed XML document: in each
    /// <c>filter</c>, the <c>result</c> names are different, every <c>in</c> and <c>in2</c> is <c>SourceGraphic</c>,
    /// <c>SourceAlpha</c> or the result of an earlier primitive, and every <c>href</c> is an identifier of the SVG or the
    /// data of an image. The default region of the graph contains the bounds of the element.
    /// </summary>
    private static void FilterGraphTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return;

        var header = FuzzInput.ReadByte(ref data);
        var regionValue = FuzzInput.Pick(FilterValues, FuzzInput.ReadByte(ref data));
        var scene = new Scene(100, 80, "sans-serif");
        var target = new SceneCircle(30, 30, 10) { Fill = SceneColor.Black, Id = (header & 1) != 0 ? "target" : null };
        scene.Root.Add(target);

        var primitives = new List<SceneFilterPrimitive>();
        var expectInvalid = false;
        while (data.Length >= 3 && primitives.Count < 12)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var option = FuzzInput.ReadByte(ref data);
            var value = FuzzInput.Pick(FilterValues, FuzzInput.ReadByte(ref data));
            var index = primitives.Count;
            var input = ReadInput(option, index);
            var input2 = ReadInput((byte)(option >> 3), index);
            SceneFilterPrimitive primitive;
            try
            {
                primitive = CreatePrimitive(kind, option, value, input, input2, target);
            }
            catch (ArgumentException)
            {
                continue;
            }

            expectInvalid |= GetPrimitiveInputs(primitive).Any(value => value.Kind is SceneFilterInputKind.Result && value.ResultIndex >= index);
            primitives.Add(primitive);
        }

        if (primitives.Count is 0)
            return;

        SceneFilterGraph graph;
        try
        {
            graph = new SceneFilterGraph(primitives);
        }
        catch (ArgumentException)
        {
            FuzzAssert.True(expectInvalid, "A valid graph threw");
            return;
        }

        FuzzAssert.False(expectInvalid, "A graph whose primitive reads a later result was created");
        if ((header & 2) != 0)
        {
            try
            {
                graph = new SceneFilterGraph(primitives) { Region = new SceneBounds(regionValue, 0, 60, 60) };
            }
            catch (ArgumentException)
            {
                FuzzAssert.False(double.IsFinite(regionValue), "A graph with a valid region threw");
            }
        }
        if (graph.Region is null)
        {
            var bounds = new SceneBounds(10, 10, 40, 40);
            var region = graph.GetRegion(bounds);
            FuzzAssert.True(region.X <= bounds.X && region.Y <= bounds.Y && region.Right >= bounds.Right && region.Bottom >= bounds.Bottom, "The default region of the graph does not contain the bounds of the element");
        }

        var filtered = scene.Root.Add(new SceneRectangle(10, 10, 40, 40) { Fill = SceneColor.Black, Filter = graph, Transform = (header & 4) != 0 ? SceneTransform.Rotate(30) : null });
        if ((header & 8) != 0)
        {
            filtered.Opacity = 0.5;
        }

        var svg = SvgSceneRenderer.Render(scene);
        FuzzAssert.Equal(svg, SvgSceneRenderer.Render(scene), "Rendering the scene twice gives different outputs");

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

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlElement element in document.GetElementsByTagName("*"))
        {
            if (element.GetAttributeNode("id") is { } id)
            {
                FuzzAssert.True(ids.Add(id.Value), $"The identifier '{id.Value}' is written twice");
            }
        }

        foreach (XmlElement filter in document.GetElementsByTagName("filter"))
        {
            var results = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement element in filter.GetElementsByTagName("*"))
            {
                foreach (var name in (ReadOnlySpan<string>)["in", "in2"])
                {
                    if (element.GetAttributeNode(name) is { } reference)
                    {
                        FuzzAssert.True(reference.Value is "SourceGraphic" or "SourceAlpha" || results.Contains(reference.Value), $"The {element.LocalName} reads '{reference.Value}', which is not a result before it");
                    }
                }

                if (element.GetAttributeNode("href") is { } href)
                {
                    FuzzAssert.True(href.Value.StartsWith("data:", StringComparison.Ordinal) || (href.Value.StartsWith('#', StringComparison.Ordinal) && ids.Contains(href.Value[1..])), $"The feImage refers to '{href.Value}', which is not an identifier of the SVG");
                }

                if (element.GetAttributeNode("result") is { } result)
                {
                    FuzzAssert.True(results.Add(result.Value), $"The result '{result.Value}' is written twice");
                }
            }
        }

        _ = scene.Root.GetBounds(_ => null);

        static SceneFilterInput ReadInput(byte option, int index) => (option % 4) switch
        {
            0 => SceneFilterInput.Previous,
            1 => SceneFilterInput.SourceGraphic,
            2 => SceneFilterInput.SourceAlpha,
            _ => SceneFilterInput.Result((option >> 2) % (index + 2)),
        };

    }

    // The inputs a primitive reads, from its public properties
    private static IEnumerable<SceneFilterInput> GetPrimitiveInputs(SceneFilterPrimitive primitive) => primitive switch
    {
        SceneGaussianBlurPrimitive value => [value.Input],
        SceneOffsetPrimitive value => [value.Input],
        SceneCompositePrimitive value => [value.Input, value.Input2],
        SceneBlendPrimitive value => [value.Input, value.Input2],
        SceneMergePrimitive value => value.Inputs,
        SceneMorphologyPrimitive value => [value.Input],
        SceneDisplacementMapPrimitive value => [value.Input, value.Map],
        SceneConvolveMatrixPrimitive value => [value.Input],
        SceneDiffuseLightingPrimitive value => [value.Input],
        SceneSpecularLightingPrimitive value => [value.Input],
        SceneComponentTransferPrimitive value => [value.Input],
        SceneTilePrimitive value => [value.Input],
        SceneColorMatrixPrimitive value => [value.Input],
        SceneDropShadowPrimitive value => [value.Input],
        _ => [],
    };

    private static SceneFilterPrimitive CreatePrimitive(byte kind, byte option, double value, SceneFilterInput input, SceneFilterInput input2, SceneElement target)
    {
        var color = new SceneColor((byte)(kind * 37), option, (byte)(kind * 5), (byte)(255 - (option & 0xC0)));
        var subregion = (kind & 0x80) != 0 ? new SceneBounds(value, 0, 50, Math.Abs(value)) : (SceneBounds?)null;
        var space = (kind & 0x40) != 0 ? SceneFilterColorSpace.Srgb : SceneFilterColorSpace.LinearRgb;
        SceneLightSource light = (option % 3) switch
        {
            0 => new SceneDistantLight(value, 45),
            1 => new ScenePointLight(value, 10, 20),
            _ => new SceneSpotLight(10, 10, value, 20, 20, 0) { SpecularExponent = Math.Abs(value), LimitingConeAngle = (option & 4) != 0 ? value : null },
        };

        return (kind % 18) switch
        {
            0 => new SceneGaussianBlurPrimitive(value, Math.Abs(value)) { Input = input, EdgeMode = (SceneFilterEdgeMode)(option % 5), Subregion = subregion, ColorSpace = space },
            1 => new SceneOffsetPrimitive(value, option) { Input = input, Subregion = subregion, ColorSpace = space },
            2 => new SceneFloodPrimitive(color) { Subregion = subregion, ColorSpace = space },
            3 => new SceneCompositePrimitive((SceneCompositeOperator)(option % 8), input2) { Input = input, K1 = value, K2 = 1, K3 = -value, K4 = 0.5, Subregion = subregion, ColorSpace = space },
            4 => new SceneBlendPrimitive((SceneBlendMode)(option % 17), input2) { Input = input, Subregion = subregion, ColorSpace = space },
            5 => new SceneMergePrimitive(input, input2) { Subregion = subregion, ColorSpace = space },
            6 => new SceneMorphologyPrimitive((SceneMorphologyOperator)(option % 3), value, Math.Abs(value)) { Input = input, Subregion = subregion, ColorSpace = space },
            7 => new SceneTurbulencePrimitive((SceneTurbulenceType)(option % 3), Math.Abs(value), value, option % 5 - 1, option) { StitchTiles = (option & 8) != 0, Subregion = subregion, ColorSpace = space },
            8 => new SceneDisplacementMapPrimitive(value, input2) { Input = input, XChannel = (SceneColorChannel)(option % 5), YChannel = (SceneColorChannel)(kind % 4), Subregion = subregion, ColorSpace = space },
            9 => new SceneConvolveMatrixPrimitive(1 + (option % 3), 1 + (kind % 3), [.. Enumerable.Repeat(value, (1 + (option % 3)) * (1 + (kind % 3)) + ((option & 64) != 0 ? 1 : 0))])
            {
                Divisor = (option & 8) != 0 ? value : null,
                Bias = (option & 16) != 0 ? value : 0,
                TargetX = (option & 32) != 0 ? option % 4 : null,
                EdgeMode = (SceneFilterEdgeMode)(kind % 5),
                PreserveAlpha = (option & 1) != 0,
                Input = input,
                Subregion = subregion,
                ColorSpace = space,
            },
            10 => new SceneDiffuseLightingPrimitive(light, color) { SurfaceScale = value, DiffuseConstant = (option & 8) != 0 ? value : 1, Input = input, Subregion = subregion, ColorSpace = space },
            11 => new SceneSpecularLightingPrimitive(light, color) { SurfaceScale = value, SpecularExponent = (option & 8) != 0 ? value : 1, Input = input, Subregion = subregion, ColorSpace = space },
            12 => new SceneComponentTransferPrimitive
            {
                Red = SceneTransferFunction.CreateTable(value, 1),
                Green = SceneTransferFunction.CreateDiscrete(),
                Blue = SceneTransferFunction.CreateLinear(value, 0),
                Alpha = (option & 1) != 0 ? SceneTransferFunction.CreateGamma(1, value, 0) : SceneTransferFunction.Identity,
                Input = input,
                Subregion = subregion,
                ColorSpace = space,
            },
            13 => new SceneTilePrimitive { Input = input, Subregion = subregion, ColorSpace = space },
            14 => new SceneImagePrimitive((option & 1) != 0 ? target : new SceneImage(0, 0, 10, 10, OnePixelPng) { Opacity = (option & 2) != 0 ? 0.5 : 1 }) { Subregion = subregion, ColorSpace = space },
            15 => new SceneColorMatrixPrimitive([value, .. new double[19]]) { Input = input, Subregion = subregion, ColorSpace = space },
            16 => SceneColorMatrixPrimitive.LuminanceToAlpha,
            _ => new SceneDropShadowPrimitive(value, 2, Math.Abs(value), value, color) { Input = input, Subregion = subregion, ColorSpace = space },
        };
    }

    private static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");

    private static IEnumerable<FuzzSeed> GetFilterGraphSeeds()
    {
        // The graphs of the unit tests: a glow, a rough edge in a region, every primitive, and the images of the primitives
        byte[][] seeds =
        [
            [0, 0, 6, 2 | 8, 2, 0, 0, 4, 2, 0, 5, 3, 3 | (1 << 2), 7, 5, 1 << 3, 0],
            [2, 5, 7, 1 | 8, 3, 8, 1 | (0 << 3), 5],
            [0, 0, 0 | 0x80 | 0x40, 2, 3, 1, 0, 1, 2, 0, 0, 3, 3, 1, 4, 1 << 3, 0, 6, 0, 1, 7, 9, 3, 8, 3, 1, 9, 1, 1, 10, 0, 1, 11, 2, 2, 12, 1, 1, 13, 3, 0, 14, 0, 0, 15, 0, 1, 16, 0, 0, 17, 1, 2],
            [1, 0, 14, 1, 0, 14, 0, 0, 14, 2, 0, 5, 1, 0],
            [6, 11, 0, 3 | (5 << 2), 3, 9, 32 | 64, 4, 11, 8, 8],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("filter-graph-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
