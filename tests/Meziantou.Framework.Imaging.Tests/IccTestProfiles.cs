using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Synthetic ICC profiles for the color conversion tests. The bytes are built by <see cref="IccProfileBuilder"/>, a file
/// of the corpus generator with no dependency on the library (the generator writes some of the same profiles to the
/// corpus); this class only wraps them in <see cref="IccProfile"/>.
/// </summary>
internal static class IccTestProfiles
{
    /// <inheritdoc cref="IccProfileBuilder.SrgbColorants"/>
    public static double[][] SrgbColorants => IccProfileBuilder.SrgbColorants;

    /// <inheritdoc cref="IccProfileBuilder.DisplayP3Colorants"/>
    public static double[][] DisplayP3Colorants => IccProfileBuilder.DisplayP3Colorants;

    /// <inheritdoc cref="IccProfileBuilder.Curve"/>
    public static byte[] Curve(params ushort[] entries) => IccProfileBuilder.Curve(entries);

    /// <inheritdoc cref="IccProfileBuilder.Gamma"/>
    public static byte[] Gamma(ushort gamma256) => IccProfileBuilder.Gamma(gamma256);

    /// <inheritdoc cref="IccProfileBuilder.Parametric"/>
    public static byte[] Parametric(ushort function, params double[] parameters) => IccProfileBuilder.Parametric(function, parameters);

    /// <inheritdoc cref="IccProfileBuilder.SrgbCurve"/>
    public static byte[] SrgbCurve() => IccProfileBuilder.SrgbCurve();

    /// <inheritdoc cref="IccProfileBuilder.Xyz"/>
    public static byte[] Xyz(double x, double y, double z) => IccProfileBuilder.Xyz(x, y, z);

    /// <inheritdoc cref="IccProfileBuilder.Lut8"/>
    public static byte[] Lut8(int inputs, int outputs, int gridPoints, Func<double[], double[]> clut, Func<int, double, double>? inputTable = null, Func<int, double, double>? outputTable = null, double[]? matrix = null)
        => IccProfileBuilder.Lut8(inputs, outputs, gridPoints, clut, inputTable, outputTable, matrix);

    /// <inheritdoc cref="IccProfileBuilder.Lut16"/>
    public static byte[] Lut16(int inputs, int outputs, int gridPoints, Func<double[], double[]> clut, int inputEntries = 2, int outputEntries = 2, Func<int, double, double>? inputTable = null, Func<int, double, double>? outputTable = null, double[]? matrix = null)
        => IccProfileBuilder.Lut16(inputs, outputs, gridPoints, clut, inputEntries, outputEntries, inputTable, outputTable, matrix);

    /// <inheritdoc cref="IccProfileBuilder.LutAToB"/>
    public static byte[] LutAToB(string type, int inputs, int outputs, byte[][]? curvesA = null, (int[] GridPoints, int BytesPerEntry, Func<double[], double[]> Values)? clut = null, byte[][]? curvesM = null, double[]? matrix = null, byte[][]? curvesB = null)
        => IccProfileBuilder.LutAToB(type, inputs, outputs, curvesA, clut, curvesM, matrix, curvesB);

    /// <inheritdoc cref="IccProfileBuilder.BuildBytes"/>
    public static byte[] BuildBytes(string colorSpace, string connectionSpace, (string Signature, byte[] Data)[] tags, Action<byte[]>? configureHeader = null)
        => IccProfileBuilder.BuildBytes(colorSpace, connectionSpace, tags, configureHeader);

    /// <summary>A version 4 display profile with the given tags.</summary>
    public static IccProfile Build(string colorSpace, string connectionSpace, params (string Signature, byte[] Data)[] tags)
        => Wrap(IccProfileBuilder.BuildBytes(colorSpace, connectionSpace, tags));

    /// <summary>A lookup-table profile: tags for one or several rendering intents and directions.</summary>
    public static IccProfile Lut(string colorSpace, string connectionSpace, params (string Signature, byte[] Data)[] tags)
        => Build(colorSpace, connectionSpace, tags);

    /// <summary>A matrix-based RGB display profile with the same curve for the three channels.</summary>
    public static IccProfile Rgb(double[][] colorants, byte[] curve) => Wrap(IccProfileBuilder.Rgb(colorants, curve));

    /// <summary>A matrix-based RGB display profile.</summary>
    public static IccProfile Rgb(double[][] colorants, byte[] red, byte[] green, byte[] blue) => Wrap(IccProfileBuilder.Rgb(colorants, red, green, blue));

    /// <summary>A monochrome display profile.</summary>
    public static IccProfile Gray(byte[] curve, string connectionSpace = "XYZ ") => Wrap(IccProfileBuilder.Gray(curve, connectionSpace));

    /// <inheritdoc cref="IccProfileBuilder.RgbLabLut8"/>
    public static IccProfile RgbLabLut8() => Wrap(IccProfileBuilder.RgbLabLut8());

    /// <inheritdoc cref="IccProfileBuilder.RgbXyzLut16"/>
    public static IccProfile RgbXyzLut16() => Wrap(IccProfileBuilder.RgbXyzLut16());

    /// <inheritdoc cref="IccProfileBuilder.CmykLabLut16"/>
    public static IccProfile CmykLabLut16(bool withColorimetricTables = true, bool withSaturationTables = true)
        => Wrap(IccProfileBuilder.CmykLabLut16(withColorimetricTables, withSaturationTables));

    /// <inheritdoc cref="IccProfileBuilder.GrayPaper"/>
    public static IccProfile GrayPaper() => Wrap(IccProfileBuilder.GrayPaper());

    /// <inheritdoc cref="IccProfileBuilder.RgbScanner"/>
    public static IccProfile RgbScanner() => Wrap(IccProfileBuilder.RgbScanner());

    /// <inheritdoc cref="IccProfileBuilder.GrayPrinterLutAToB"/>
    public static IccProfile GrayPrinterLutAToB() => Wrap(IccProfileBuilder.GrayPrinterLutAToB());

    /// <inheritdoc cref="IccProfileBuilder.GrayPrinterLut8"/>
    public static IccProfile GrayPrinterLut8() => Wrap(IccProfileBuilder.GrayPrinterLut8());

    /// <inheritdoc cref="IccProfileBuilder.GrayLabLut8"/>
    public static IccProfile GrayLabLut8() => Wrap(IccProfileBuilder.GrayLabLut8());

    /// <inheritdoc cref="IccProfileBuilder.RgbLabLutAToB"/>
    public static IccProfile RgbLabLutAToB() => Wrap(IccProfileBuilder.RgbLabLutAToB());

    /// <inheritdoc cref="IccProfileBuilder.RgbXyzMatrixLutAToB"/>
    public static IccProfile RgbXyzMatrixLutAToB() => Wrap(IccProfileBuilder.RgbXyzMatrixLutAToB());

    /// <inheritdoc cref="IccProfileBuilder.CmykLabLutAToB"/>
    public static IccProfile CmykLabLutAToB() => Wrap(IccProfileBuilder.CmykLabLutAToB());

    private static IccProfile Wrap(byte[] data) => new(new MetadataBlob(data));
}
