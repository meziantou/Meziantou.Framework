using System.Runtime.InteropServices;
using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An opcode list of a DNG raw image (DNG specification 1.7.1, chapter 7): a big-endian count, then each opcode (its ID,
/// the DNG version that defined it, its flags, the length of its parameters and the parameters), applied in order.
/// </summary>
/// <remarks>
/// <para>
/// Implemented: WarpRectilinear (1), FixVignetteRadial (3), FixBadPixelsConstant (4), FixBadPixelsList (5), MapTable (7),
/// MapPolynomial (8), GainMap (9), DeltaPerRow (10), DeltaPerColumn (11), ScalePerRow (12) and ScalePerColumn (13). The
/// other opcodes (WarpFisheye, TrimBounds, WarpRectilinear2, and the IDs of later versions) are skipped when the file marks
/// them optional, as the specification allows, and leniently; they are an <see cref="UnsupportedImageFeatureException"/>
/// otherwise.
/// </para>
/// <para>
/// The specification leaves several choices to the reader; this one makes them so:
/// </para>
/// <list type="bullet">
/// <item><description>The opcodes of list 1 see the stored integer values divided by 65,535 (the logical range of the list),
/// and the results are rounded back to integers; lists 2 and 3 work on the linear values. Every result is clipped to the
/// logical range of its list after each opcode, except the stored values of floating-point samples, which have
/// none.</description></item>
/// <item><description>The positions of a warp and of a vignette correction are continuous coordinates: the image goes from
/// (0, 0) to (width, height), the center of a pixel is half a pixel from its corner, and the optical center is (cx * width,
/// cy * height), which is how the DNG SDK reads them (measured: FixVignetteRadial within 1E-6).</description></item>
/// <item><description>A warp resamples its plane bilinearly, the samples outside the image being those of its nearest edge.</description></item>
/// <item><description>A bad pixel is replaced by the mean of the good pixels of its color among its neighbors two pixels away
/// in the four directions, and, for a green pixel, the four pixels next to it diagonally (none: it is left unchanged).</description></item>
/// <item><description>MapTable of lists 2 and 3 maps a value through the entry of its 16-bit encoding (the value times 65,535, rounded), divided by 65,535.</description></item>
/// <item><description>The gain map is sampled at the center of each pixel, (row + 0.5) / height and (column + 0.5) / width of the image the list modifies.</description></item>
/// </list>
/// </remarks>
internal sealed class DngOpcodeList
{
    private const int WarpRectilinearId = 1;
    private const int WarpFisheyeId = 2;
    private const int FixVignetteRadialId = 3;
    private const int FixBadPixelsConstantId = 4;
    private const int FixBadPixelsListId = 5;
    private const int TrimBoundsId = 6;
    private const int MapTableId = 7;
    private const int MapPolynomialId = 8;
    private const int GainMapId = 9;
    private const int DeltaPerRowId = 10;
    private const int DeltaPerColumnId = 11;
    private const int ScalePerRowId = 12;
    private const int ScalePerColumnId = 13;
    private const int WarpRectilinear2Id = 14;

    // The largest number of opcodes of a list, and of bad pixels and rectangles of an opcode
    private const int MaxOpcodes = 1024;

    private readonly List<Opcode> _opcodes;

    private DngOpcodeList(int number, List<Opcode> opcodes)
    {
        Number = number;
        _opcodes = opcodes;
    }

    /// <summary>Gets the number of the list (1, 2 or 3).</summary>
    public int Number { get; }

    /// <summary>Gets the IDs of the opcodes that are applied, in order.</summary>
    public IEnumerable<int> OpcodeIds => _opcodes.Select(opcode => opcode.Id);

    /// <summary>Gets the IDs of the opcodes that are skipped (optional ones the reader does not implement, or any of them leniently).</summary>
    public IReadOnlyList<int> SkippedOpcodeIds { get; private set; } = [];

    /// <summary>Parses an opcode list.</summary>
    /// <param name="data">The bytes of the tag.</param>
    /// <param name="number">The number of the list (1, 2 or 3).</param>
    /// <param name="isLenient">Whether the opcodes that are not implemented are skipped even when they are not optional.</param>
    /// <returns>The list.</returns>
    /// <exception cref="InvalidImageContentException">The list is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A required opcode is not implemented (strict decoding).</exception>
    public static DngOpcodeList Parse(ReadOnlySpan<byte> data, int number, bool isLenient)
    {
        var name = "OpcodeList" + number.ToString(CultureInfo.InvariantCulture);
        if (data.Length < 4)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {name} of the DNG file has {data.Length} bytes; it starts with a count of 4 bytes."));

        var count = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (count > MaxOpcodes)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {name} of the DNG file declares {count} opcodes; at most {MaxOpcodes} are read."));

        var opcodes = new List<Opcode>();
        var skipped = new List<int>();
        var position = 4;
        for (var i = 0; i < count; i++)
        {
            if (data.Length - position < 16)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {name} of the DNG file ends in the header of its opcode {i}."));

            var id = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            var version = data.Slice(position + 4, 4);
            var flags = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 8)..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 12)..]);
            position += 16;
            if (length > (uint)(data.Length - position))
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The parameters of the opcode {i} ({id}) of the {name} of the DNG file run past the end of the list."));

            var parameters = data.Slice(position, (int)length);
            position += (int)length;
            var isOptional = (flags & 1) != 0;

            // An opcode of a later version of the specification is not understood
            var isKnown = DngFormat.CompareVersions(version, DngFormat.SupportedVersion) <= 0 && id is WarpRectilinearId or FixVignetteRadialId or FixBadPixelsConstantId or FixBadPixelsListId or MapTableId or MapPolynomialId or GainMapId or DeltaPerRowId or DeltaPerColumnId or ScalePerRowId or ScalePerColumnId;
            if (!isKnown)
            {
                if (!isOptional && !isLenient)
                    throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The {name} of the DNG file has the required opcode {GetName(id)} ({id}, version {DngFormat.FormatVersion(version)}), which is not supported."), "Opcode: " + GetName(id));

                skipped.Add((int)id);
                continue;
            }

            opcodes.Add(ParseOpcode((int)id, parameters, name));
        }

        return new DngOpcodeList(number, opcodes) { SkippedOpcodeIds = skipped };
    }

    /// <summary>Gets the name of an opcode ID, for messages.</summary>
    public static string GetName(uint id) => id switch
    {
        WarpRectilinearId => "WarpRectilinear",
        WarpFisheyeId => "WarpFisheye",
        FixVignetteRadialId => "FixVignetteRadial",
        FixBadPixelsConstantId => "FixBadPixelsConstant",
        FixBadPixelsListId => "FixBadPixelsList",
        TrimBoundsId => "TrimBounds",
        MapTableId => "MapTable",
        MapPolynomialId => "MapPolynomial",
        GainMapId => "GainMap",
        DeltaPerRowId => "DeltaPerRow",
        DeltaPerColumnId => "DeltaPerColumn",
        ScalePerRowId => "ScalePerRow",
        ScalePerColumnId => "ScalePerColumn",
        WarpRectilinear2Id => "WarpRectilinear2",
        _ => string.Create(CultureInfo.InvariantCulture, $"opcode {id}"),
    };

    /// <summary>Applies the opcodes to an image.</summary>
    /// <param name="image">The image the list modifies.</param>
    /// <param name="values">What the samples of the image are: the logical range of the list and the rounding after each opcode.</param>
    /// <param name="context">The operation context.</param>
    public void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
    {
        foreach (var opcode in _opcodes)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            opcode.Apply(image, values, context);
            if (values == DngSampleValues.StoredFloats)
                continue;

            // The values are clipped to the logical range of the list after each opcode, and stored integers stay integers. A
            // value the opcode cannot compute (its parameters overflow, as a polynomial whose huge terms cancel out) is 0
            var samples = image.Samples;
            for (var i = 0; i < samples.Length; i++)
            {
                var value = float.IsNaN(samples[i]) ? 0f : Math.Clamp(samples[i], 0f, 1f);
                samples[i] = values == DngSampleValues.StoredIntegers ? (float)(Math.Round(value * 65535.0, MidpointRounding.AwayFromZero) / 65535.0) : value;
            }
        }
    }

    private static Opcode ParseOpcode(int id, ReadOnlySpan<byte> parameters, string list)
    {
        var reader = new ParameterReader(parameters, id, list);
        Opcode opcode = id switch
        {
            WarpRectilinearId => WarpRectilinear.Read(ref reader),
            FixVignetteRadialId => FixVignetteRadial.Read(ref reader),
            FixBadPixelsConstantId => FixBadPixels.ReadConstant(ref reader),
            FixBadPixelsListId => FixBadPixels.ReadList(ref reader),
            MapTableId => MapTable.Read(ref reader),
            MapPolynomialId => MapPolynomial.Read(ref reader),
            GainMapId => GainMap.Read(ref reader),
            _ => PerLine.Read(ref reader, isRow: id is DeltaPerRowId or ScalePerRowId, isScale: id is ScalePerRowId or ScalePerColumnId),
        };

        reader.EnsureEnd();
        return opcode;
    }

    private abstract class Opcode(int id)
    {
        public int Id { get; } = id;

        public abstract void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context);
    }

    /// <summary>The area, planes and pitches of the opcodes that modify part of an image.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Area(int Top, int Left, int Bottom, int Right, int Plane, int Planes, int RowPitch, int ColumnPitch)
    {
        public static Area Read(ref ParameterReader reader)
        {
            var area = new Area(reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt());
            if (area.Top > area.Bottom || area.Left > area.Right || area.Planes < 1 || area.RowPitch < 1 || area.ColumnPitch < 1)
                throw reader.Invalid("declares an empty or reversed area, no plane, or a pitch of 0");

            return area;
        }

        /// <summary>Gets the part of the area inside an image: rows and columns, clipped.</summary>
        public (int Top, int Left, int Bottom, int Right, int PlaneEnd) Clip(DngPlanes image)
            => (Math.Max(Top, 0), Math.Max(Left, 0), Math.Min(Bottom, image.Height), Math.Min(Right, image.Width), Math.Min(Plane + Planes, image.Planes));

        /// <summary>Gets the number of rows (or columns) of the area the pitch selects.</summary>
        public static int CountLines(int start, int end, int pitch) => end <= start ? 0 : ((end - start - 1) / pitch) + 1;
    }

    /// <summary>A reader of the big-endian parameters of an opcode.</summary>
    private ref struct ParameterReader(ReadOnlySpan<byte> data, int id, string list)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly int Remaining => _data.Length - _position;

        public int ReadInt()
        {
            var value = ReadUInt();
            if (value > int.MaxValue)
                throw Invalid("has a parameter that is too large");

            return (int)value;
        }

        public uint ReadUInt()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
            _position += 4;
            return value;
        }

        public double ReadDouble()
        {
            Ensure(8);
            var value = BinaryPrimitives.ReadDoubleBigEndian(_data[_position..]);
            _position += 8;
            if (!double.IsFinite(value))
                throw Invalid("has a parameter that is not a finite number");

            return value;
        }

        public float ReadFloat()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadSingleBigEndian(_data[_position..]);
            _position += 4;
            if (!float.IsFinite(value))
                throw Invalid("has a parameter that is not a finite number");

            return value;
        }

        public ushort ReadShort()
        {
            Ensure(2);
            var value = BinaryPrimitives.ReadUInt16BigEndian(_data[_position..]);
            _position += 2;
            return value;
        }

        /// <summary>Checks that a count of elements of <paramref name="size"/> bytes fits in the remaining parameters.</summary>
        public void EnsureCount(long count, int size)
        {
            if (count < 0 || count * size > Remaining)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"declares {count} values, more than its parameters hold"));
        }

        public readonly void EnsureEnd()
        {
            if (_position != _data.Length)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"has {_data.Length - _position} bytes of parameters after its last one"));
        }

        public readonly InvalidImageContentException Invalid(string problem)
            => DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The opcode {GetName((uint)id)} of the {list} of the DNG file {problem}."));

        private readonly void Ensure(int length)
        {
            if (Remaining < length)
                throw Invalid("has truncated parameters");
        }
    }

    private sealed class WarpRectilinear(double[][] coefficients, double centerX, double centerY) : Opcode(WarpRectilinearId)
    {
        public static WarpRectilinear Read(ref ParameterReader reader)
        {
            var count = reader.ReadInt();
            if (count is < 1 or > 4)
                throw reader.Invalid(string.Create(CultureInfo.InvariantCulture, $"declares {count} coefficient sets; 1 to 4 are expected"));

            reader.EnsureCount((count * 6L) + 2, 8);
            var sets = new double[count][];
            for (var i = 0; i < count; i++)
            {
                sets[i] = [reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()];
            }

            return new WarpRectilinear(sets, reader.ReadDouble(), reader.ReadDouble());
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
        {
            if (coefficients.Length != 1 && coefficients.Length != image.Planes)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The opcode WarpRectilinear of the DNG file has {coefficients.Length} coefficient sets for an image of {image.Planes} planes."));

            var (cx, cy, m) = GetCenter(image, centerX, centerY);
            using var source = DngPlanes.Create(context, image.Width, image.Height, image.Planes);
            image.Samples.CopyTo(source.Samples);
            for (var y = 0; y < image.Height; y++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < image.Width; x++)
                {
                    var dx = (x + 0.5 - cx) / m;
                    var dy = (y + 0.5 - cy) / m;
                    var r2 = (dx * dx) + (dy * dy);
                    for (var plane = 0; plane < image.Planes; plane++)
                    {
                        var k = coefficients[coefficients.Length == 1 ? 0 : plane];
                        var f = k[0] + (k[1] * r2) + (k[2] * r2 * r2) + (k[3] * r2 * r2 * r2);
                        var tx = (k[4] * 2 * dx * dy) + (k[5] * (r2 + (2 * dx * dx)));
                        var ty = (k[5] * 2 * dx * dy) + (k[4] * (r2 + (2 * dy * dy)));
                        var sourceX = cx + (m * ((f * dx) + tx));
                        var sourceY = cy + (m * ((f * dy) + ty));
                        image[x, y, plane] = (float)SampleBilinear(source, sourceX - 0.5, sourceY - 0.5, plane);
                    }
                }
            }
        }
    }

    private sealed class FixVignetteRadial(double[] k, double centerX, double centerY) : Opcode(FixVignetteRadialId)
    {
        public static FixVignetteRadial Read(ref ParameterReader reader)
        {
            double[] k = [reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()];
            return new FixVignetteRadial(k, reader.ReadDouble(), reader.ReadDouble());
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
        {
            var (cx, cy, m) = GetCenter(image, centerX, centerY);
            for (var y = 0; y < image.Height; y++)
            {
                var row = image.GetRow(y);
                for (var x = 0; x < image.Width; x++)
                {
                    var r2 = (((x + 0.5 - cx) * (x + 0.5 - cx)) + ((y + 0.5 - cy) * (y + 0.5 - cy))) / (m * m);
                    var gain = 1 + (r2 * (k[0] + (r2 * (k[1] + (r2 * (k[2] + (r2 * (k[3] + (r2 * k[4])))))))));
                    for (var plane = 0; plane < image.Planes; plane++)
                    {
                        row[(x * image.Planes) + plane] = (float)(gain * row[(x * image.Planes) + plane]);
                    }
                }
            }
        }
    }

    private sealed class FixBadPixels(uint? constant, int bayerPhase, (int Row, int Column)[] points, Rectangle[] rectangles, int id) : Opcode(id)
    {
        public static FixBadPixels ReadConstant(ref ParameterReader reader)
        {
            var constant = reader.ReadUInt();
            var phase = ReadPhase(ref reader);
            return new FixBadPixels(constant, phase, [], [], FixBadPixelsConstantId);
        }

        public static FixBadPixels ReadList(ref ParameterReader reader)
        {
            var phase = ReadPhase(ref reader);
            var pointCount = reader.ReadInt();
            var rectangleCount = reader.ReadInt();
            reader.EnsureCount((pointCount * 2L) + (rectangleCount * 4L), 4);
            var points = new (int, int)[pointCount];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = (reader.ReadInt(), reader.ReadInt());
            }

            var rectangles = new Rectangle[rectangleCount];
            for (var i = 0; i < rectangles.Length; i++)
            {
                var (top, left, bottom, right) = (reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt());
                if (bottom < top || right < left)
                    throw reader.Invalid("declares a reversed rectangle");

                rectangles[i] = Rectangle.FromLTRB(left, top, right, bottom);
            }

            return new FixBadPixels(constant: null, phase, points, rectangles, FixBadPixelsListId);
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
        {
            if (image.Planes != 1)
                throw DngFormat.Invalid("The opcode FixBadPixels of the DNG file applies to an image of a Bayer pattern, which has one plane.");

            // The bad pixels, marked first in a bit mask charged to the scope so that a bad pixel is never the neighbor
            // another one is repaired from: a repair reads good pixels only, so it is written in place
            var width = image.Width;
            var height = image.Height;
            using var buffer = context.Scope.Rent((int)(((long)width * height + 7) / 8), AllocationKind.DecoderState, clear: true);
            var mask = buffer.Span;
            var any = false;
            if (constant is { } value)
            {
                var marked = values == DngSampleValues.StoredFloats ? value : (float)(value / 65535.0);
                for (var y = 0; y < height; y++)
                {
                    var row = image.GetRow(y);
                    for (var x = 0; x < width; x++)
                    {
                        if (row[x] == marked)
                        {
                            Mark(mask, x, y);
                        }
                    }
                }
            }

            foreach (var (row, column) in points)
            {
                if (row < height && column < width)
                {
                    Mark(mask, column, row);
                }
            }

            foreach (var rectangle in rectangles)
            {
                for (var y = rectangle.Top; y < Math.Min(rectangle.Bottom, height); y++)
                {
                    for (var x = rectangle.Left; x < Math.Min(rectangle.Right, width); x++)
                    {
                        Mark(mask, x, y);
                    }
                }
            }

            if (!any)
                return;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!IsMarked(mask, x, y))
                        continue;

                    var sum = 0.0;
                    var count = 0;
                    AddNeighbor(mask, x - 2, y);
                    AddNeighbor(mask, x + 2, y);
                    AddNeighbor(mask, x, y - 2);
                    AddNeighbor(mask, x, y + 2);
                    if (IsGreen(x, y))
                    {
                        AddNeighbor(mask, x - 1, y - 1);
                        AddNeighbor(mask, x + 1, y - 1);
                        AddNeighbor(mask, x - 1, y + 1);
                        AddNeighbor(mask, x + 1, y + 1);
                    }

                    if (count > 0)
                    {
                        image[x, y, 0] = (float)(sum / count);
                    }

                    void AddNeighbor(ReadOnlySpan<byte> mask, int nx, int ny)
                    {
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height || IsMarked(mask, nx, ny))
                            return;

                        sum += image[nx, ny, 0];
                        count++;
                    }
                }
            }

            void Mark(Span<byte> mask, int x, int y)
            {
                var index = ((long)y * width) + x;
                mask[(int)(index >> 3)] |= (byte)(1 << (int)(index & 7));
                any = true;
            }

            bool IsMarked(ReadOnlySpan<byte> mask, int x, int y)
            {
                var index = ((long)y * width) + x;
                return (mask[(int)(index >> 3)] & (1 << (int)(index & 7))) != 0;
            }
        }

        private static int ReadPhase(ref ParameterReader reader)
        {
            var phase = reader.ReadInt();
            if (phase > 3)
                throw reader.Invalid(string.Create(CultureInfo.InvariantCulture, $"declares the Bayer phase {phase}; 0 to 3 are expected"));

            return phase;
        }

        // Phases 0 and 3 start a row with red or blue, 1 and 2 with green
        private bool IsGreen(int x, int y) => ((x + y) % 2 == 0) == (bayerPhase is 1 or 2);
    }

    private sealed class MapTable(Area area, ushort[] table) : Opcode(MapTableId)
    {
        public static MapTable Read(ref ParameterReader reader)
        {
            var area = Area.Read(ref reader);
            var size = reader.ReadInt();
            if (size is < 1 or > 65536)
                throw reader.Invalid(string.Create(CultureInfo.InvariantCulture, $"declares a table of {size} entries; 1 to 65,536 are expected"));

            reader.EnsureCount(size, 2);
            var table = new ushort[size];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = reader.ReadShort();
            }

            return new MapTable(area, table);
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
            => ForEachSample(image, area, (_, _, _, value) =>
            {
                var index = (int)Math.Clamp(Math.Round(value * 65535.0, MidpointRounding.AwayFromZero), 0, table.Length - 1);
                return table[index] / 65535.0;
            });
    }

    private sealed class MapPolynomial(Area area, double[] coefficients) : Opcode(MapPolynomialId)
    {
        public static MapPolynomial Read(ref ParameterReader reader)
        {
            var area = Area.Read(ref reader);
            var degree = reader.ReadInt();
            if (degree > 8)
                throw reader.Invalid(string.Create(CultureInfo.InvariantCulture, $"declares a polynomial of degree {degree}; at most 8 is allowed"));

            reader.EnsureCount(degree + 1, 8);
            var coefficients = new double[degree + 1];
            for (var i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = reader.ReadDouble();
            }

            return new MapPolynomial(area, coefficients);
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
            => ForEachSample(image, area, (_, _, _, value) =>
            {
                // Horner's method, from the coefficient of the highest degree
                var result = 0.0;
                for (var i = coefficients.Length - 1; i >= 0; i--)
                {
                    result = (result * value) + coefficients[i];
                }

                return result;
            });
    }

    private sealed class GainMap(Area area, int pointsV, int pointsH, double spacingV, double spacingH, double originV, double originH, int mapPlanes, float[] gains) : Opcode(GainMapId)
    {
        public static GainMap Read(ref ParameterReader reader)
        {
            var area = Area.Read(ref reader);
            var pointsV = reader.ReadInt();
            var pointsH = reader.ReadInt();
            var spacingV = reader.ReadDouble();
            var spacingH = reader.ReadDouble();
            var originV = reader.ReadDouble();
            var originH = reader.ReadDouble();
            var mapPlanes = reader.ReadInt();
            if (pointsV < 1 || pointsH < 1 || mapPlanes < 1 || !(spacingV > 0) || !(spacingH > 0))
                throw reader.Invalid("declares an empty gain map or a spacing that is not positive");

            var count = (long)pointsV * pointsH * mapPlanes;
            reader.EnsureCount(count, 4);
            var gains = new float[count];
            for (var i = 0; i < gains.Length; i++)
            {
                gains[i] = reader.ReadFloat();
            }

            return new GainMap(area, pointsV, pointsH, spacingV, spacingH, originV, originH, mapPlanes, gains);
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
            => ForEachSample(image, area, (x, y, plane, value) =>
            {
                var mapPlane = Math.Min(plane - area.Plane, mapPlanes - 1);
                var row = Math.Clamp(((((y + 0.5) / image.Height) - originV) / spacingV), 0, pointsV - 1);
                var column = Math.Clamp(((((x + 0.5) / image.Width) - originH) / spacingH), 0, pointsH - 1);
                var row0 = (int)Math.Floor(row);
                var column0 = (int)Math.Floor(column);
                var row1 = Math.Min(row0 + 1, pointsV - 1);
                var column1 = Math.Min(column0 + 1, pointsH - 1);
                var fy = row - row0;
                var fx = column - column0;
                var top = (Gain(row0, column0) * (1 - fx)) + (Gain(row0, column1) * fx);
                var bottom = (Gain(row1, column0) * (1 - fx)) + (Gain(row1, column1) * fx);
                return value * ((top * (1 - fy)) + (bottom * fy));

                double Gain(int r, int c) => gains[(((r * pointsH) + c) * mapPlanes) + mapPlane];
            });
    }

    private sealed class PerLine(Area area, float[] lineValues, bool isRow, bool isScale, int id) : Opcode(id)
    {
        public static PerLine Read(ref ParameterReader reader, bool isRow, bool isScale)
        {
            var area = Area.Read(ref reader);
            var count = reader.ReadInt();
            reader.EnsureCount(count, 4);
            var values = new float[count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = reader.ReadFloat();
            }

            var expected = isRow ? Area.CountLines(area.Top, area.Bottom, area.RowPitch) : Area.CountLines(area.Left, area.Right, area.ColumnPitch);
            if (count != expected)
                throw reader.Invalid(string.Create(CultureInfo.InvariantCulture, $"declares {count} values for the {expected} {(isRow ? "rows" : "columns")} of its area"));

            return new PerLine(area, values, isRow, isScale, isRow ? (isScale ? ScalePerRowId : DeltaPerRowId) : (isScale ? ScalePerColumnId : DeltaPerColumnId));
        }

        public override void Apply(DngPlanes image, DngSampleValues values, ImageCodecContext context)
            => ForEachSample(image, area, (x, y, _, value) =>
            {
                var line = isRow ? (y - area.Top) / area.RowPitch : (x - area.Left) / area.ColumnPitch;
                return isScale ? value * lineValues[line] : value + lineValues[line];
            });
    }

    /// <summary>Applies a function to the samples of the area of an opcode, of the planes it selects, every pitch.</summary>
    private static void ForEachSample(DngPlanes image, Area area, Func<int, int, int, double, double> map)
    {
        var (top, left, bottom, right, planeEnd) = area.Clip(image);
        for (var y = top; y < bottom; y++)
        {
            if ((y - area.Top) % area.RowPitch != 0)
                continue;

            var row = image.GetRow(y);
            for (var x = left; x < right; x++)
            {
                if ((x - area.Left) % area.ColumnPitch != 0)
                    continue;

                for (var plane = area.Plane; plane < planeEnd; plane++)
                {
                    var index = (x * image.Planes) + plane;
                    row[index] = (float)map(x, y, plane, row[index]);
                }
            }
        }
    }

    /// <summary>
    /// Gets the optical center of a warp or a vignette correction and the distance from it to the farthest point of the
    /// image, in the continuous coordinates of the image: its rectangle goes from (0, 0) to (width, height), and the center
    /// of the pixel (x, y) is (x + 0.5, y + 0.5) (as the DNG SDK reads the "pixel coordinates" of the bounds).
    /// </summary>
    private static (double X, double Y, double Distance) GetCenter(DngPlanes image, double normalizedX, double normalizedY)
    {
        var cx = normalizedX * image.Width;
        var cy = normalizedY * image.Height;
        var mx = Math.Max(Math.Abs(cx), Math.Abs(image.Width - cx));
        var my = Math.Max(Math.Abs(cy), Math.Abs(image.Height - cy));
        var m = Math.Sqrt((mx * mx) + (my * my));
        return (cx, cy, m > 0 ? m : 1);
    }

    /// <summary>Samples a plane bilinearly; the positions outside the image take the samples of its nearest edge.</summary>
    private static double SampleBilinear(DngPlanes image, double x, double y, int plane)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var top = (image[x0, y0, plane] * (1 - fx)) + (image[x1, y0, plane] * fx);
        var bottom = (image[x0, y1, plane] * (1 - fx)) + (image[x1, y1, plane] * fx);
        return (top * (1 - fy)) + (bottom * fy);
    }
}
