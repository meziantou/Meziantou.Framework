using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A smooth shading (ISO 32000-2, 8.7.4.5): a gradient painted with <see cref="PdfContentBuilder.PaintShading"/> or
/// used by a <see cref="PdfShadingPattern"/>. A shading does not belong to a document: each document that uses it gets
/// its own object. The instances are immutable.
/// </summary>
public abstract class PdfShading
{
    private protected PdfShading(PdfDeviceColorSpace colorSpace)
    {
        if (!Enum.IsDefined(colorSpace))
            throw new ArgumentOutOfRangeException(nameof(colorSpace), colorSpace, "Unknown color space");

        ColorSpace = colorSpace;
    }

    /// <summary>Gets the color space of the colors of the shading.</summary>
    public PdfDeviceColorSpace ColorSpace { get; }

    /// <summary>Gets or initializes the background color, painted outside the shading when it is used by a pattern; <see langword="null"/> for none.</summary>
    public PdfColor? Background { get; init; }

    /// <summary>Gets or initializes whether the shading is antialiased.</summary>
    public bool AntiAlias { get; init; }

    internal int ComponentCount => ColorSpace switch
    {
        PdfDeviceColorSpace.Gray => 1,
        PdfDeviceColorSpace.Rgb => 3,
        _ => 4,
    };

    /// <summary>Creates a function-based shading (type 1, 8.7.4.5.2): the color of each point of the domain is the output of a function of two inputs.</summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="function">The function: two inputs, one output per component of the color space.</param>
    /// <param name="domain">The rectangle of the inputs <c>[xmin xmax ymin ymax]</c>; <see langword="null"/> for <c>[0 1 0 1]</c>.</param>
    /// <param name="matrix">The matrix that maps the domain to the shading space; <see langword="null"/> for the identity.</param>
    /// <returns>The shading.</returns>
    public static PdfShading FunctionBased(PdfDeviceColorSpace colorSpace, PdfFunction function, double[]? domain = null, PdfMatrix? matrix = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (domain is not null && domain.Length != 4)
            throw new ArgumentException("The domain must have four values", nameof(domain));

        var shading = new FunctionShading(colorSpace, function, domain ?? [0, 1, 0, 1], matrix);
        CheckFunction(shading, function, inputs: 2);
        return shading;
    }

    /// <summary>Creates an axial shading (type 2, 8.7.4.5.3): the colors vary along the line from the start point to the end point.</summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="x0">The x coordinate of the start point.</param>
    /// <param name="y0">The y coordinate of the start point.</param>
    /// <param name="x1">The x coordinate of the end point.</param>
    /// <param name="y1">The y coordinate of the end point.</param>
    /// <param name="function">The function from the position on the line (0 to 1) to the components of the color.</param>
    /// <param name="extendStart">Whether the shading extends beyond the start point.</param>
    /// <param name="extendEnd">Whether the shading extends beyond the end point.</param>
    /// <returns>The shading.</returns>
    public static PdfShading Axial(PdfDeviceColorSpace colorSpace, double x0, double y0, double x1, double y1, PdfFunction function, bool extendStart = false, bool extendEnd = false)
    {
        ArgumentNullException.ThrowIfNull(function);
        var shading = new GradientShading(2, colorSpace, [x0, y0, x1, y1], function, extendStart, extendEnd);
        CheckFunction(shading, function, inputs: 1);
        return shading;
    }

    /// <summary>Creates a radial shading (type 3, 8.7.4.5.4): the colors vary between two circles.</summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="x0">The x coordinate of the center of the start circle.</param>
    /// <param name="y0">The y coordinate of the center of the start circle.</param>
    /// <param name="r0">The radius of the start circle.</param>
    /// <param name="x1">The x coordinate of the center of the end circle.</param>
    /// <param name="y1">The y coordinate of the center of the end circle.</param>
    /// <param name="r1">The radius of the end circle.</param>
    /// <param name="function">The function from the position between the circles (0 to 1) to the components of the color.</param>
    /// <param name="extendStart">Whether the shading extends beyond the start circle.</param>
    /// <param name="extendEnd">Whether the shading extends beyond the end circle.</param>
    /// <returns>The shading.</returns>
    public static PdfShading Radial(PdfDeviceColorSpace colorSpace, double x0, double y0, double r0, double x1, double y1, double r1, PdfFunction function, bool extendStart = false, bool extendEnd = false)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (r0 < 0 || r1 < 0)
            throw new ArgumentOutOfRangeException(nameof(r0), "The radii must not be negative");

        var shading = new GradientShading(3, colorSpace, [x0, y0, r0, x1, y1, r1], function, extendStart, extendEnd);
        CheckFunction(shading, function, inputs: 1);
        return shading;
    }

    /// <summary>Creates a free-form Gouraud-shaded triangle mesh (type 4, 8.7.4.5.5).</summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="triangles">The triangles: three vertices each, whose colors are interpolated inside the triangle.</param>
    /// <returns>The shading.</returns>
    public static PdfShading TriangleMesh(PdfDeviceColorSpace colorSpace, IReadOnlyList<(PdfMeshVertex A, PdfMeshVertex B, PdfMeshVertex C)> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Count == 0)
            throw new ArgumentException("A mesh needs at least one triangle", nameof(triangles));

        var shading = new MeshShading(4, colorSpace);
        foreach (var (a, b, c) in triangles)
        {
            shading.AddVertex(0, a);
            shading.AddVertex(0, b);
            shading.AddVertex(0, c);
        }

        return shading;
    }

    /// <summary>Creates a lattice-form Gouraud-shaded triangle mesh (type 5, 8.7.4.5.6): rows of vertices, each two consecutive rows forming the triangles of a strip.</summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="verticesPerRow">The number of vertices of each row, at least 2.</param>
    /// <param name="vertices">The vertices, row by row; at least two rows.</param>
    /// <returns>The shading.</returns>
    public static PdfShading LatticeMesh(PdfDeviceColorSpace colorSpace, int verticesPerRow, IReadOnlyList<PdfMeshVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (verticesPerRow < 2)
            throw new ArgumentOutOfRangeException(nameof(verticesPerRow), verticesPerRow, "A row needs at least two vertices");

        if (vertices.Count < verticesPerRow * 2 || vertices.Count % verticesPerRow != 0)
            throw new ArgumentException("The vertices must be whole rows, at least two", nameof(vertices));

        var shading = new MeshShading(5, colorSpace) { VerticesPerRow = verticesPerRow };
        foreach (var vertex in vertices)
        {
            shading.AddVertex(flag: null, vertex);
        }

        return shading;
    }

    /// <summary>
    /// Creates a Coons patch mesh (type 6, 8.7.4.5.7): patches bounded by four cubic Bézier curves, with a color at each
    /// corner.
    /// </summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="patches">The patches.</param>
    /// <returns>The shading.</returns>
    public static PdfShading CoonsPatchMesh(PdfDeviceColorSpace colorSpace, IReadOnlyList<PdfPatch> patches) => CreatePatchMesh(6, colorSpace, patches);

    /// <summary>
    /// Creates a tensor-product patch mesh (type 7, 8.7.4.5.8): the patches of <see cref="CoonsPatchMesh"/> with four more
    /// control points inside each one (<see cref="PdfPatch.InnerPoints"/>).
    /// </summary>
    /// <param name="colorSpace">The color space.</param>
    /// <param name="patches">The patches.</param>
    /// <returns>The shading.</returns>
    public static PdfShading TensorPatchMesh(PdfDeviceColorSpace colorSpace, IReadOnlyList<PdfPatch> patches) => CreatePatchMesh(7, colorSpace, patches);

    /// <summary>Gets the reference to the shading object in a document, creating it on first use.</summary>
    internal PdfReference GetReference(PdfDocument document)
    {
        if (document.SharedResources.TryGetValue(this, out var reference))
            return reference;

        var value = CreateObject(document);
        var dictionary = value is PdfStream stream ? stream.Dictionary : (PdfDictionary)value;
        dictionary[PdfName.Create("ShadingType")] = PdfInteger.Get(ShadingType);
        dictionary[PdfNames.ColorSpace] = ColorSpace switch
        {
            PdfDeviceColorSpace.Gray => PdfNames.DeviceGray,
            PdfDeviceColorSpace.Rgb => PdfNames.DeviceRGB,
            _ => PdfNames.DeviceCMYK,
        };
        if (Background is { } background)
        {
            if (background.ColorSpace != ColorSpace)
                throw new InvalidOperationException("The background must be in the color space of the shading");

            dictionary[PdfName.Create("Background")] = PdfArray.FromNumbers(background.GetComponents());
        }

        if (AntiAlias)
        {
            dictionary[PdfName.Create("AntiAlias")] = PdfBoolean.True;
        }

        reference = document.Objects.Add(value);
        document.SharedResources[this] = reference;
        return reference;
    }

    private protected abstract int ShadingType { get; }

    private protected abstract PdfObject CreateObject(PdfDocument document);

    private static void CheckFunction(PdfShading shading, PdfFunction function, int inputs)
    {
        if (function.InputCount != inputs || function.OutputCount != shading.ComponentCount)
            throw new ArgumentException($"The function must have {inputs} input(s) and {shading.ComponentCount} output(s)", nameof(function));
    }

    private static MeshShading CreatePatchMesh(int type, PdfDeviceColorSpace colorSpace, IReadOnlyList<PdfPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(patches);
        if (patches.Count == 0)
            throw new ArgumentException("A mesh needs at least one patch", nameof(patches));

        var shading = new MeshShading(type, colorSpace);
        foreach (var patch in patches)
        {
            ArgumentNullException.ThrowIfNull(patch, nameof(patches));
            shading.AddPatch(patch);
        }

        return shading;
    }

    private sealed class FunctionShading(PdfDeviceColorSpace colorSpace, PdfFunction function, double[] domain, PdfMatrix? matrix) : PdfShading(colorSpace)
    {
        private protected override int ShadingType => 1;

        private protected override PdfObject CreateObject(PdfDocument document)
        {
            var dictionary = new PdfDictionary
            {
                [PdfName.Create("Domain")] = PdfArray.FromNumbers(domain),
                [PdfName.Create("Function")] = function.GetObject(document),
            };
            if (matrix is { } value)
            {
                dictionary[PdfNames.Matrix] = PdfArray.FromNumbers(value.A, value.B, value.C, value.D, value.E, value.F);
            }

            return dictionary;
        }
    }

    private sealed class GradientShading(int type, PdfDeviceColorSpace colorSpace, double[] coordinates, PdfFunction function, bool extendStart, bool extendEnd) : PdfShading(colorSpace)
    {
        private protected override int ShadingType => type;

        private protected override PdfObject CreateObject(PdfDocument document)
        {
            var dictionary = new PdfDictionary
            {
                [PdfName.Create("Coords")] = PdfArray.FromNumbers(coordinates),
                [PdfName.Create("Function")] = function.GetObject(document),
            };
            if (extendStart || extendEnd)
            {
                dictionary[PdfName.Create("Extend")] = new PdfArray(PdfBoolean.Get(extendStart), PdfBoolean.Get(extendEnd));
            }

            return dictionary;
        }
    }

    /// <summary>The shadings whose data is a stream of vertices or patches: 32-bit coordinates and 16-bit color components, mapped by <c>/Decode</c> (8.7.4.5.5).</summary>
    private sealed class MeshShading(int type, PdfDeviceColorSpace colorSpace) : PdfShading(colorSpace)
    {
        private const int CoordinateBits = 32;
        private const int ComponentBits = 16;
        private const int FlagBits = 8;

        private readonly List<(int? Flag, double[] Values, PdfColor[] Colors)> _items = [];

        public int VerticesPerRow { get; init; }

        private protected override int ShadingType => type;

        public void AddVertex(int? flag, PdfMeshVertex vertex)
        {
            _items.Add((flag, [vertex.X, vertex.Y], [vertex.Color]));
        }

        public void AddPatch(PdfPatch patch)
        {
            var values = new List<double>(32);
            foreach (var point in patch.GetPoints(includeInnerPoints: type == 7))
            {
                values.Add(point.X);
                values.Add(point.Y);
            }

            _items.Add((0, [.. values], [.. patch.Colors]));
        }

        private protected override PdfObject CreateObject(PdfDocument document)
        {
            // The rectangle of the coordinates, which the 32-bit integers map to
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            foreach (var (_, values, colors) in _items)
            {
                for (var i = 0; i < values.Length; i += 2)
                {
                    minX = Math.Min(minX, values[i]);
                    maxX = Math.Max(maxX, values[i]);
                    minY = Math.Min(minY, values[i + 1]);
                    maxY = Math.Max(maxY, values[i + 1]);
                }

                foreach (var color in colors)
                {
                    if (color.ColorSpace != ColorSpace)
                        throw new InvalidOperationException("The colors of the mesh must be in the color space of the shading");
                }
            }

            if (maxX == minX)
            {
                maxX = minX + 1;
            }

            if (maxY == minY)
            {
                maxY = minY + 1;
            }

            var writer = new BitWriter();
            foreach (var (flag, values, colors) in _items)
            {
                if (flag is { } value)
                {
                    writer.Write((uint)value, FlagBits);
                }

                for (var i = 0; i < values.Length; i += 2)
                {
                    writer.Write(Quantize(values[i], minX, maxX, CoordinateBits), CoordinateBits);
                    writer.Write(Quantize(values[i + 1], minY, maxY, CoordinateBits), CoordinateBits);
                }

                foreach (var color in colors)
                {
                    foreach (var component in color.GetComponents())
                    {
                        writer.Write(Quantize(component, 0, 1, ComponentBits), ComponentBits);
                    }
                }

                // Each vertex or patch starts on a byte (8.7.4.5.5)
                writer.Flush();
            }

            var stream = new PdfStream();
            stream.Dictionary[PdfName.Create("BitsPerCoordinate")] = PdfInteger.Get(CoordinateBits);
            stream.Dictionary[PdfName.Create("BitsPerComponent")] = PdfInteger.Get(ComponentBits);
            if (type == 5)
            {
                stream.Dictionary[PdfName.Create("VerticesPerRow")] = PdfInteger.Get(VerticesPerRow);
            }
            else
            {
                stream.Dictionary[PdfName.Create("BitsPerFlag")] = PdfInteger.Get(FlagBits);
            }

            var decode = new List<double> { minX, maxX, minY, maxY };
            for (var i = 0; i < ComponentCount; i++)
            {
                decode.Add(0);
                decode.Add(1);
            }

            stream.Dictionary[PdfNames.Decode] = PdfArray.FromNumbers([.. decode]);
            stream.SetData(writer.ToArray());
            return stream;
        }

        private static uint Quantize(double value, double min, double max, int bits)
        {
            var maxValue = bits == 32 ? uint.MaxValue : (1u << bits) - 1;
            var scaled = Math.Round((value - min) / (max - min) * maxValue, MidpointRounding.AwayFromZero);
            return (uint)Math.Clamp(scaled, 0, maxValue);
        }
    }
}
