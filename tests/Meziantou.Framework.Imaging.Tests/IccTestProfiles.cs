using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Synthetic ICC profiles for the color conversion tests, built field by field from ICC.1:2001-04 (version 2) and
/// ICC.1:2022 (version 4): header, tag table and tag data. Nothing here uses the library to produce profile bytes.
/// </summary>
internal static class IccTestProfiles
{
    /// <summary>The sRGB primaries adapted to D50 (columns red, green, blue; each X, Y, Z).</summary>
    public static readonly double[][] SrgbColorants = [[0.4360747, 0.2225045, 0.0139322], [0.3850649, 0.7168786, 0.0971045], [0.1430804, 0.0606169, 0.7141733]];

    /// <summary>The Display P3 primaries adapted to D50.</summary>
    public static readonly double[][] DisplayP3Colorants = [[0.5151, 0.2412, -0.0011], [0.2919, 0.6922, 0.0419], [0.1572, 0.0666, 0.7841]];

    /// <summary>A <c>curveType</c>: no entry is the identity, one entry is a u8Fixed8 gamma, more are samples.</summary>
    public static byte[] Curve(params ushort[] entries)
    {
        var data = new byte[12 + (2 * entries.Length)];
        "curv"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + (2 * i)), entries[i]);
        }

        return data;
    }

    /// <summary>A <c>curveType</c> with one entry: the gamma as a u8Fixed8Number (<paramref name="gamma256"/> / 256).</summary>
    public static byte[] Gamma(ushort gamma256) => Curve(gamma256);

    /// <summary>A <c>parametricCurveType</c>; the parameters are rounded to s15Fixed16Number.</summary>
    public static byte[] Parametric(ushort function, params double[] parameters)
    {
        var data = new byte[12 + (4 * parameters.Length)];
        "para"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), function);
        for (var i = 0; i < parameters.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12 + (4 * i)), ToS15Fixed16(parameters[i]));
        }

        return data;
    }

    /// <summary>The sRGB decoding function as a parametric curve of type 3.</summary>
    public static byte[] SrgbCurve() => Parametric(3, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045);

    /// <summary>An <c>XYZType</c> with one value, rounded to s15Fixed16Number.</summary>
    public static byte[] Xyz(double x, double y, double z)
    {
        var data = new byte[20];
        "XYZ "u8.CopyTo(data);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), ToS15Fixed16(x));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), ToS15Fixed16(y));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), ToS15Fixed16(z));
        return data;
    }

    /// <summary>
    /// A <c>lut8Type</c> (ICC.1:2022 section 10.10): matrix, 256-entry input tables, color lookup table with the first
    /// input varying least rapidly, 256-entry output tables. All values are normalized to [0, 1] and rounded to 8 bits.
    /// </summary>
    /// <param name="inputs">The number of input channels.</param>
    /// <param name="outputs">The number of output channels.</param>
    /// <param name="gridPoints">The number of grid points of each input.</param>
    /// <param name="clut">The outputs of a grid point, given its inputs.</param>
    /// <param name="inputTable">The input table of a channel (channel, input); the identity when <see langword="null"/>.</param>
    /// <param name="outputTable">The output table of a channel (channel, input); the identity when <see langword="null"/>.</param>
    /// <param name="matrix">The 3x3 matrix in row-major order; the identity when <see langword="null"/>.</param>
    public static byte[] Lut8(int inputs, int outputs, int gridPoints, Func<double[], double[]> clut, Func<int, double, double>? inputTable = null, Func<int, double, double>? outputTable = null, double[]? matrix = null)
    {
        var data = new List<byte>();
        data.AddRange("mft1"u8);
        data.AddRange([0, 0, 0, 0, (byte)inputs, (byte)outputs, (byte)gridPoints, 0]);
        AddMatrix(data, matrix);
        AddTables(data, inputs, 256, inputTable, bytesPerEntry: 1);
        AddClut(data, [.. Enumerable.Repeat(gridPoints, inputs)], outputs, clut, bytesPerEntry: 1);
        AddTables(data, outputs, 256, outputTable, bytesPerEntry: 1);
        return [.. data];
    }

    /// <summary>A <c>lut16Type</c> (ICC.1:2022 section 10.11): like <see cref="Lut8"/> with 16-bit values and tables of 2 to 4096 entries.</summary>
    public static byte[] Lut16(int inputs, int outputs, int gridPoints, Func<double[], double[]> clut, int inputEntries = 2, int outputEntries = 2, Func<int, double, double>? inputTable = null, Func<int, double, double>? outputTable = null, double[]? matrix = null)
    {
        var data = new List<byte>();
        data.AddRange("mft2"u8);
        data.AddRange([0, 0, 0, 0, (byte)inputs, (byte)outputs, (byte)gridPoints, 0]);
        AddMatrix(data, matrix);
        AddUInt16(data, inputEntries);
        AddUInt16(data, outputEntries);
        AddTables(data, inputs, inputEntries, inputTable, bytesPerEntry: 2);
        AddClut(data, [.. Enumerable.Repeat(gridPoints, inputs)], outputs, clut, bytesPerEntry: 2);
        AddTables(data, outputs, outputEntries, outputTable, bytesPerEntry: 2);
        return [.. data];
    }

    /// <summary>
    /// A <c>lutAToBType</c> or <c>lutBToAType</c> (ICC.1:2022 sections 10.12 and 10.13). Each element is optional; curves
    /// are <c>curveType</c> or <c>parametricCurveType</c> elements, the matrix has 9 coefficients then 3 offsets.
    /// </summary>
    /// <param name="type">"mAB " or "mBA ".</param>
    /// <param name="inputs">The number of input channels.</param>
    /// <param name="outputs">The number of output channels.</param>
    /// <param name="curvesA">The A curves (device side).</param>
    /// <param name="clut">The color lookup table: grid points of each input, bytes per entry (1 or 2) and outputs of a grid point.</param>
    /// <param name="curvesM">The M curves.</param>
    /// <param name="matrix">The matrix.</param>
    /// <param name="curvesB">The B curves (connection space side).</param>
    public static byte[] LutAToB(string type, int inputs, int outputs, byte[][]? curvesA = null, (int[] GridPoints, int BytesPerEntry, Func<double[], double[]> Values)? clut = null, byte[][]? curvesM = null, double[]? matrix = null, byte[][]? curvesB = null)
    {
        var data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes(type));
        data.AddRange([0, 0, 0, 0, (byte)inputs, (byte)outputs, 0, 0]);
        data.AddRange(new byte[20]);

        // Element offsets: B curves at 12, matrix at 16, M curves at 20, color lookup table at 24, A curves at 28
        if (curvesB is not null)
        {
            SetOffset(12);
            AddCurves(curvesB);
        }

        if (matrix is not null)
        {
            SetOffset(16);
            foreach (var value in matrix)
            {
                AddInt32(data, ToS15Fixed16(value));
            }
        }

        if (curvesM is not null)
        {
            SetOffset(20);
            AddCurves(curvesM);
        }

        if (clut is { } table)
        {
            SetOffset(24);
            var header = new byte[20];
            for (var i = 0; i < table.GridPoints.Length; i++)
            {
                header[i] = (byte)table.GridPoints[i];
            }

            header[16] = (byte)table.BytesPerEntry;
            data.AddRange(header);
            AddClut(data, table.GridPoints, outputs, table.Values, table.BytesPerEntry);
            Pad();
        }

        if (curvesA is not null)
        {
            SetOffset(28);
            AddCurves(curvesA);
        }

        return [.. data];

        void SetOffset(int position)
        {
            Span<byte> offset = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(offset, (uint)data.Count);
            for (var i = 0; i < 4; i++)
            {
                data[position + i] = offset[i];
            }
        }

        void AddCurves(byte[][] curves)
        {
            foreach (var curve in curves)
            {
                data.AddRange(curve);
                Pad();
            }
        }

        void Pad()
        {
            while (data.Count % 4 != 0)
            {
                data.Add(0);
            }
        }
    }

    /// <summary>A lookup-table profile: tags for one or several rendering intents and directions.</summary>
    public static IccProfile Lut(string colorSpace, string connectionSpace, params (string Signature, byte[] Data)[] tags)
        => Build(colorSpace, connectionSpace, tags);

    // -----------------------------------------------------------------------------------------------------------------
    // Synthetic lookup-table profiles. Their tables sample simple device models; the tests never rely on the models
    // being realistic, only on the bytes of the profiles.
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An RGB profile with the CIELAB connection space and <c>lut8Type</c> tables: device values squared (input tables),
    /// then linear RGB with the sRGB primaries to CIELAB (5 grid points); the reverse with 9 grid points.
    /// </summary>
    public static IccProfile RgbLabLut8()
        => Lut(
            "RGB ",
            "Lab ",
            ("A2B0", Lut8(3, 3, 5, static rgb => EncodeLab(XyzToLab(RgbToXyz(rgb))), inputTable: static (_, x) => x * x)),
            ("B2A0", Lut8(3, 3, 9, static lab => XyzToRgb(LabToXyz(DecodeLab(lab))), outputTable: static (_, x) => Math.Sqrt(x))));

    /// <summary>
    /// An RGB profile with the CIEXYZ connection space and <c>lut16Type</c> tables. The BToA table uses the matrix of the
    /// tag (CIEXYZ to linear RGB, halved to stay in range), which only applies because the tag input is CIEXYZ.
    /// </summary>
    public static IccProfile RgbXyzLut16()
        => Lut(
            "RGB ",
            "XYZ ",
            ("A2B0", Lut16(3, 3, 3, static rgb => EncodeXyz(RgbToXyz(rgb)), inputEntries: 256, inputTable: static (_, x) => Math.Pow(x, 2.2))),
            ("B2A0", Lut16(3, 3, 2, static halfRgb => [Math.Min(1, 2 * halfRgb[0]), Math.Min(1, 2 * halfRgb[1]), Math.Min(1, 2 * halfRgb[2])], outputEntries: 1024, outputTable: static (_, x) => Math.Pow(x, 1 / 2.2), matrix: HalfXyzToRgbMatrix())));

    /// <summary>
    /// A CMYK output profile with the CIELAB connection space and <c>lut16Type</c> tables (legacy CIELAB encoding), with
    /// different tables for the perceptual (0), colorimetric (1) and saturation (2) intents: the colorimetric tables
    /// darken, the saturation tables swap magenta and yellow.
    /// </summary>
    public static IccProfile CmykLabLut16(bool withColorimetricTables = true, bool withSaturationTables = true)
    {
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("A2B0", Lut16(4, 3, 5, static cmyk => EncodeLabLegacy(XyzToLab(RgbToXyz(CmykToRgb(cmyk)))))),
            ("B2A0", Lut16(3, 4, 9, static lab => RgbToCmyk(XyzToRgb(LabToXyz(DecodeLabLegacy(lab)))))),
        };

        if (withColorimetricTables)
        {
            tags.Add(("A2B1", Lut16(4, 3, 4, static cmyk => EncodeLabLegacy(XyzToLab(RgbToXyz(CmykToRgb(cmyk).Select(static v => 0.8 * v).ToArray()))))));
            tags.Add(("B2A1", Lut16(3, 4, 7, static lab => RgbToCmyk(XyzToRgb(LabToXyz(DecodeLabLegacy(lab))).Select(static v => Math.Min(1, v / 0.8)).ToArray()))));
        }

        if (withSaturationTables)
        {
            tags.Add(("A2B2", Lut16(4, 3, 3, static cmyk => EncodeLabLegacy(XyzToLab(RgbToXyz(CmykToRgb([cmyk[0], cmyk[2], cmyk[1], cmyk[3]])))))));
            tags.Add(("B2A2", Lut16(3, 4, 5, static lab =>
            {
                var cmyk = RgbToCmyk(XyzToRgb(LabToXyz(DecodeLabLegacy(lab))));
                return [cmyk[0], cmyk[2], cmyk[1], cmyk[3]];
            })));
        }

        return new IccProfile(new MetadataBlob(BuildBytes("CMYK", "Lab ", [.. tags], static data =>
        {
            data[8] = 2;
            data[9] = 0x40;
            "prtr"u8.CopyTo(data.AsSpan(12));
        })));
    }

    /// <summary>A monochrome profile with a <c>lut8Type</c> table (one input) and the CIELAB connection space.</summary>
    public static IccProfile GrayLabLut8()
        => Lut(
            "GRAY",
            "Lab ",
            ("A2B0", Lut8(1, 3, 17, static gray => EncodeLab([100 * Math.Pow(gray[0], 1.5), 0, 0]))),
            ("B2A0", Lut8(3, 1, 9, static lab => [Math.Pow(lab[0], 1 / 1.5)])));

    /// <summary>
    /// A version 4 RGB profile with the CIELAB connection space and every element of <c>lutAToBType</c> and
    /// <c>lutBToAType</c>: A curves, a color lookup table with a different grid per input, M curves, a matrix with offsets
    /// and B curves. The elements do not commute, so a wrong order changes the result.
    /// </summary>
    public static IccProfile RgbLabLutAToB()
        => Lut(
            "RGB ",
            "Lab ",
            ("A2B0", LutAToB(
                "mAB ",
                3,
                3,
                curvesA: [Parametric(0, 2.0), Gamma(461), Curve(0, 20000, 65535)],
                clut: ([3, 4, 5], 2, static rgb => EncodeLab(XyzToLab(RgbToXyz(rgb)))),
                curvesM: [Parametric(0, 1.25), Curve(), Parametric(3, 1.5, 0.9, 0.1, 0.5, 0.1)],
                matrix: [0.9, 0.05, 0.0, -0.05, 1.0, 0.03, 0.02, 0.0, 0.95, 0.01, 0.02, -0.01],
                curvesB: [Curve(0, 30000, 65535), Parametric(0, 0.9), Curve()])),
            ("B2A0", LutAToB(
                "mBA ",
                3,
                3,
                curvesB: [Parametric(0, 1.1), Curve(0, 32000, 65535), Curve()],
                matrix: [1.0, 0.02, -0.02, 0.0, 0.97, 0.03, 0.04, 0.0, 0.96, -0.01, 0.0, 0.02],
                curvesM: [Curve(), Parametric(0, 0.95), Curve(1000, 40000, 65000)],
                clut: ([5, 5, 5], 1, static lab => XyzToRgb(LabToXyz(DecodeLab(lab)))),
                curvesA: [Parametric(0, 0.5), Parametric(0, 0.4545), Curve(0, 50000, 65535)])));

    /// <summary>
    /// A version 4 RGB profile with the CIEXYZ connection space whose <c>lutAToBType</c> has no color lookup table: M
    /// curves, matrix and B curves only (a matrix-based profile written as a lookup table), and the reverse.
    /// </summary>
    public static IccProfile RgbXyzMatrixLutAToB()
    {
        // The matrix works on encoded CIEXYZ: 1.0 is 32768 / 65535
        var toXyz = new double[12];
        var toRgb = new double[12];
        var inverse = XyzToRgbMatrix();
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                toXyz[(3 * row) + column] = SrgbColorants[column][row] * 32768 / 65535;
                toRgb[(3 * row) + column] = inverse[(3 * row) + column] * 65535 / 32768;
            }
        }

        return Lut(
            "RGB ",
            "XYZ ",
            ("A2B0", LutAToB("mAB ", 3, 3, curvesM: [Parametric(0, 2.2), Parametric(0, 2.2), Parametric(0, 2.2)], matrix: toXyz, curvesB: [Curve(), Curve(), Curve()])),
            ("B2A0", LutAToB("mBA ", 3, 3, curvesB: [Curve(), Curve(), Curve()], matrix: toRgb, curvesM: [Parametric(0, 1 / 2.2), Parametric(0, 1 / 2.2), Parametric(0, 1 / 2.2)])));
    }

    /// <summary>
    /// A version 4 CMYK profile with the CIELAB connection space: a <c>lutAToBType</c> with A curves, a four-input color
    /// lookup table of 8-bit entries and B curves, and a <c>lutBToAType</c> with four A curves.
    /// </summary>
    public static IccProfile CmykLabLutAToB()
        => new(new MetadataBlob(BuildBytes(
            "CMYK",
            "Lab ",
            [
                ("A2B0", LutAToB(
                    "mAB ",
                    4,
                    3,
                    curvesA: [Curve(), Parametric(0, 1.2), Curve(0, 30000, 65535), Parametric(0, 0.8)],
                    clut: ([3, 4, 3, 5], 1, static cmyk => EncodeLab(XyzToLab(RgbToXyz(CmykToRgb(cmyk))))),
                    curvesB: [Curve(), Curve(), Curve()])),
                ("B2A0", LutAToB(
                    "mBA ",
                    3,
                    4,
                    curvesB: [Curve(), Curve(), Curve()],
                    clut: ([7, 6, 5], 2, static lab => RgbToCmyk(XyzToRgb(LabToXyz(DecodeLab(lab))))),
                    curvesA: [Curve(), Parametric(0, 1 / 1.2), Curve(0, 40000, 65535), Parametric(0, 1.25)])),
            ],
            static data => "prtr"u8.CopyTo(data.AsSpan(12)))));

    /// <summary>Linear RGB with the sRGB primaries to CIEXYZ relative to D50.</summary>
    public static double[] RgbToXyz(double[] rgb)
    {
        var xyz = new double[3];
        for (var i = 0; i < 3; i++)
        {
            xyz[i] = (SrgbColorants[0][i] * rgb[0]) + (SrgbColorants[1][i] * rgb[1]) + (SrgbColorants[2][i] * rgb[2]);
        }

        return xyz;
    }

    /// <summary>CIEXYZ relative to D50 to linear RGB with the sRGB primaries, clipped to [0, 1].</summary>
    public static double[] XyzToRgb(double[] xyz)
    {
        var m = XyzToRgbMatrix();
        var rgb = new double[3];
        for (var i = 0; i < 3; i++)
        {
            rgb[i] = Math.Clamp((m[3 * i] * xyz[0]) + (m[(3 * i) + 1] * xyz[1]) + (m[(3 * i) + 2] * xyz[2]), 0, 1);
        }

        return rgb;
    }

    public static double[] XyzToLab(double[] xyz)
    {
        var fx = LabFunction(xyz[0] / 0.9642);
        var fy = LabFunction(xyz[1]);
        var fz = LabFunction(xyz[2] / 0.8249);
        return [(116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz)];

        static double LabFunction(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : ((24389.0 / 27 * t) + 16) / 116;
    }

    public static double[] LabToXyz(double[] lab)
    {
        var fy = (lab[0] + 16) / 116;
        var fx = fy + (lab[1] / 500);
        var fz = fy - (lab[2] / 200);
        return [0.9642 * Inverse(fx), Inverse(fy), 0.8249 * Inverse(fz)];

        static double Inverse(double f) => f > 6.0 / 29 ? f * f * f : ((116 * f) - 16) * 27 / 24389;
    }

    /// <summary>CIELAB to the normalized encoding of version 4 (and of 8-bit tables): L* / 100, (a* + 128) / 255.</summary>
    public static double[] EncodeLab(double[] lab) => [lab[0] / 100, (lab[1] + 128) / 255, (lab[2] + 128) / 255];

    public static double[] DecodeLab(double[] encoded) => [encoded[0] * 100, (encoded[1] * 255) - 128, (encoded[2] * 255) - 128];

    /// <summary>CIELAB to the normalized legacy 16-bit encoding of <c>lut16Type</c>: L* * 652.8 / 65535, (a* + 128) * 256 / 65535.</summary>
    public static double[] EncodeLabLegacy(double[] lab) => [lab[0] * 652.8 / 65535, (lab[1] + 128) * 256 / 65535, (lab[2] + 128) * 256 / 65535];

    public static double[] DecodeLabLegacy(double[] encoded) => [encoded[0] * 65535 / 652.8, (encoded[1] * 65535 / 256) - 128, (encoded[2] * 65535 / 256) - 128];

    /// <summary>CIEXYZ to its normalized 16-bit encoding (u1Fixed15: 1.0 is 32768 / 65535).</summary>
    public static double[] EncodeXyz(double[] xyz) => [xyz[0] * 32768 / 65535, xyz[1] * 32768 / 65535, xyz[2] * 32768 / 65535];

    /// <summary>A simple subtractive model: each ink removes its complementary light, black removes all.</summary>
    public static double[] CmykToRgb(double[] cmyk) => [(1 - cmyk[0]) * (1 - cmyk[3]), (1 - cmyk[1]) * (1 - cmyk[3]), (1 - cmyk[2]) * (1 - cmyk[3])];

    public static double[] RgbToCmyk(double[] rgb)
    {
        var black = 1 - Math.Max(rgb[0], Math.Max(rgb[1], rgb[2]));
        if (black >= 1)
            return [0, 0, 0, 1];

        return [(1 - rgb[0] - black) / (1 - black), (1 - rgb[1] - black) / (1 - black), (1 - rgb[2] - black) / (1 - black), black];
    }

    private static double[] XyzToRgbMatrix()
    {
        var c = SrgbColorants;
        double[] m = [c[0][0], c[1][0], c[2][0], c[0][1], c[1][1], c[2][1], c[0][2], c[1][2], c[2][2]];
        var determinant = (m[0] * ((m[4] * m[8]) - (m[5] * m[7]))) - (m[1] * ((m[3] * m[8]) - (m[5] * m[6]))) + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        return
        [
            ((m[4] * m[8]) - (m[5] * m[7])) / determinant, ((m[2] * m[7]) - (m[1] * m[8])) / determinant, ((m[1] * m[5]) - (m[2] * m[4])) / determinant,
            ((m[5] * m[6]) - (m[3] * m[8])) / determinant, ((m[0] * m[8]) - (m[2] * m[6])) / determinant, ((m[2] * m[3]) - (m[0] * m[5])) / determinant,
            ((m[3] * m[7]) - (m[4] * m[6])) / determinant, ((m[1] * m[6]) - (m[0] * m[7])) / determinant, ((m[0] * m[4]) - (m[1] * m[3])) / determinant,
        ];
    }

    private static double[] HalfXyzToRgbMatrix() => [.. XyzToRgbMatrix().Select(static value => value / 2)];

    private static void AddMatrix(List<byte> data, double[]? matrix)
    {
        foreach (var value in matrix ?? [1, 0, 0, 0, 1, 0, 0, 0, 1])
        {
            AddInt32(data, ToS15Fixed16(value));
        }
    }

    private static void AddTables(List<byte> data, int channels, int entries, Func<int, double, double>? table, int bytesPerEntry)
    {
        for (var channel = 0; channel < channels; channel++)
        {
            for (var i = 0; i < entries; i++)
            {
                var input = (double)i / (entries - 1);
                AddNormalized(data, table is null ? input : table(channel, input), bytesPerEntry);
            }
        }
    }

    private static void AddClut(List<byte> data, int[] gridPoints, int outputs, Func<double[], double[]> values, int bytesPerEntry)
    {
        var indexes = new int[gridPoints.Length];
        var inputs = new double[gridPoints.Length];
        while (true)
        {
            for (var i = 0; i < inputs.Length; i++)
            {
                inputs[i] = (double)indexes[i] / (gridPoints[i] - 1);
            }

            var output = values(inputs);
            if (output.Length != outputs)
                throw new InvalidOperationException("The color lookup table function must return one value per output channel.");

            foreach (var value in output)
            {
                AddNormalized(data, value, bytesPerEntry);
            }

            // The last input varies most rapidly
            var dimension = indexes.Length - 1;
            while (dimension >= 0 && ++indexes[dimension] == gridPoints[dimension])
            {
                indexes[dimension--] = 0;
            }

            if (dimension < 0)
                break;
        }
    }

    private static void AddNormalized(List<byte> data, double value, int bytesPerEntry)
    {
        var maximum = bytesPerEntry == 1 ? 255 : 65535;
        var stored = (int)Math.Round(Math.Clamp(value, 0, 1) * maximum, MidpointRounding.AwayFromZero);
        if (bytesPerEntry == 1)
        {
            data.Add((byte)stored);
        }
        else
        {
            AddUInt16(data, stored);
        }
    }

    private static void AddUInt16(List<byte> data, int value)
    {
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }

    private static void AddInt32(List<byte> data, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        data.AddRange(bytes);
    }

    /// <summary>A matrix-based RGB display profile with the same curve for the three channels.</summary>
    public static IccProfile Rgb(double[][] colorants, byte[] curve) => Rgb(colorants, curve, curve, curve);

    /// <summary>A matrix-based RGB display profile.</summary>
    public static IccProfile Rgb(double[][] colorants, byte[] red, byte[] green, byte[] blue)
        => Build(
            "RGB ",
            "XYZ ",
            ("rXYZ", Xyz(colorants[0][0], colorants[0][1], colorants[0][2])),
            ("gXYZ", Xyz(colorants[1][0], colorants[1][1], colorants[1][2])),
            ("bXYZ", Xyz(colorants[2][0], colorants[2][1], colorants[2][2])),
            ("rTRC", red),
            ("gTRC", green),
            ("bTRC", blue));

    /// <summary>A monochrome display profile.</summary>
    public static IccProfile Gray(byte[] curve, string connectionSpace = "XYZ ") => Build("GRAY", connectionSpace, ("kTRC", curve));

    /// <summary>A version 4 display profile with the given tags.</summary>
    public static IccProfile Build(string colorSpace, string connectionSpace, params (string Signature, byte[] Data)[] tags)
        => new(new MetadataBlob(BuildBytes(colorSpace, connectionSpace, tags)));

    /// <summary>The bytes of a profile: a version 4 display profile unless the header is changed by <paramref name="configureHeader"/>.</summary>
    public static byte[] BuildBytes(string colorSpace, string connectionSpace, (string Signature, byte[] Data)[] tags, Action<byte[]>? configureHeader = null)
    {
        var tableSize = 132 + (12 * tags.Length);
        var body = new List<byte>(new byte[tableSize]);
        var entries = new (int Offset, int Length)[tags.Length];
        for (var i = 0; i < tags.Length; i++)
        {
            // Identical tag data is stored once, as real profiles do for their three tone curves
            var shared = Array.FindIndex(tags, tag => ReferenceEquals(tag.Data, tags[i].Data));
            if (shared < i)
            {
                entries[i] = entries[shared];
                continue;
            }

            while (body.Count % 4 != 0)
            {
                body.Add(0);
            }

            entries[i] = (body.Count, tags[i].Data.Length);
            body.AddRange(tags[i].Data);
        }

        while (body.Count % 4 != 0)
        {
            body.Add(0);
        }

        var data = body.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        data[8] = 4;
        data[9] = 0x40;
        "mntr"u8.CopyTo(data.AsSpan(12));
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
        Encoding.ASCII.GetBytes(connectionSpace).CopyTo(data, 20);
        "acsp"u8.CopyTo(data.AsSpan(36));

        // The PCS illuminant: D50
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(68), 0x0000F6D6);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(72), 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(76), 0x0000D32D);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(128), (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = data.AsSpan(132 + (12 * i), 12);
            Encoding.ASCII.GetBytes(tags[i].Signature).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)entries[i].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)entries[i].Length);
        }

        configureHeader?.Invoke(data);
        return data;
    }

    public static int ToS15Fixed16(double value) => (int)Math.Round(value * 65536, MidpointRounding.AwayFromZero);
}
