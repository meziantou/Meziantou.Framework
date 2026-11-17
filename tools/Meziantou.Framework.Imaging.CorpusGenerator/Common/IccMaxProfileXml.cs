using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Synthetic version 5 (iccMAX) ICC profiles, written as the XML that iccFromXml of iccDEV (the reference implementation
/// of ICC.2:2023) turns into profiles: the corpus generator (IccCorpus.IccMax.cs) builds them with it. The element and
/// attribute names are those of iccDEV's XML format; the content follows ICC.2:2023 clause 11 (multiProcessElementsType)
/// and section 12.2.5 (namedColorStructure).
/// </summary>
/// <remarks>
/// The device models are those of <see cref="IccProfileBuilder"/>: the sRGB primaries adapted to D50, and a simple
/// subtractive CMYK model on paper. Numbers are written with 9 decimals, without exponent (iccFromXml does not read them).
/// </remarks>
internal static class IccMaxProfileXml
{
    /// <summary>The media white point of the absolute colorimetric profiles: a warm paper.</summary>
    public static readonly double[] PaperWhite = [0.9505, 0.98, 0.78];

    /// <summary>
    /// A display profile (RGB, CIEXYZ) whose AToB1 and BToA1 tags are multiProcessElementsType: the sRGB tone curves as
    /// segmented curves (formula types 0 and 3), and the matrix of the primaries and its inverse. It has no perceptual
    /// tag: the perceptual intent uses the colorimetric tags (ICC.2:2023 section 8.14.2).
    /// </summary>
    public static string RgbXyzMpe()
    {
        var inverse = Invert(SrgbMatrix());
        var tags = new StringBuilder();
        tags.Append(Mpe("A2B1", 3, 3,
            CurveSet(3, SrgbDecoding()),
            Matrix(3, 3, SrgbMatrix(), null)));
        tags.Append(Mpe("B2A1", 3, 3,
            Matrix(3, 3, inverse, null),
            CurveSet(3, SrgbEncoding())));
        return Profile("mntr", "RGB ", "XYZ ", "iccMAX RGB display with processing elements", tags.ToString());
    }

    /// <summary>
    /// A color space profile (RGB, CIELAB) whose tags are calculator elements: the sRGB decoding with a curve sub-element,
    /// the matrix of the primaries with a matrix sub-element and the CIELAB functions of the calculator ('tLab', 'tXYZ'),
    /// through stack, temporary channel, conditional and selection operations that leave the color unchanged; and a check
    /// of every arithmetic, comparison and stack operation, whose failures would change the lightness.
    /// </summary>
    public static string RgbLabCalculator()
    {
        // The matrix to CIEXYZ relative to the D50 white: tLab takes normalized values
        var white = new[] { 0.9642, 1.0, 0.8249 };
        var normalized = SrgbMatrix().Select((value, index) => value / white[index / 3]).ToArray();
        var inverse = Invert(normalized);
        var identityClut = Clut([2, 2, 2], 3, point => point);
        // 'solv' is left out: iccDEV 2.4.2.5 reports a failure for every system, even [2 0; 0 2] x = [2 2]
        var forward = Calculator(3, 3,
            [
                CurveSet(3, SrgbDecoding()),
                Matrix(3, 3, normalized, null),
                Tint(2, [1, 1, 1, 1]),
                identityClut,
            ],
            """
            in(0,3) tput(0,3)
            tget(0,3) curv(0)
            copy(3) pop(3)
            rotl(3,1) rotr(3,1)
            flip(3) flip(3)
            tsav(3,3)
            mtx(1)
            tLab
            ctop ptoc
            posd(3) 50 lt
            if { 0 } else { 0 }
            add
            tget(0) 3 mul rond
            sel case { 1 } case { 1 } case { 1 } case { 1 } dflt { 1 }
            mul
            tget(0) tint(2) mul(2)
            tget(0,3) clut(3) tget(0,3) sub(3) abs(3) sum(3) 0.0001 lt not
            tget(9) add tput(9)
            env(true) mul mul
            env(ndef) add add
            1
            7 3 mod 1 eq and(2)
            -7 3 mod -1 eq and(2)
            2 10 pow 1024 eq and(2)
            9 sqrt 3 eq and(2)
            27 cbrt 3 eq and(2)
            3 sq 9 eq and(2)
            2 cb 8 eq and(2)
            -2.5 abs 2.5 eq and(2)
            2.5 neg -2.5 eq and(2)
            2.5 rond 3 eq and(2)
            -2.5 rond -3 eq and(2)
            2.7 flor 2 eq and(2)
            2.2 ceil 3 eq and(2)
            -2.7 trnc -2 eq and(2)
            -3 sign -1 eq and(2)
            0 sign 0 eq and(2)
            0 exp 1 eq and(2)
            100 log 2 eq and(2)
            1 ln 0 eq and(2)
            0 sin 0 eq and(2)
            0 cos 1 eq and(2)
            0 tan 0 eq and(2)
            1 asin 1.5707 gt and(2)
            0 acos 1.5708 lt and(2)
            1 atan 0.7853 gt and(2)
            1 1 atn2 0.7854 lt and(2)
            2 3 lt 3 3 le 3 3 eq 3 3 ge 4 3 gt and(5) and(2)
            3 3 near 2 3 near not and(3)
            1 2 vmin 1 eq and(2)
            1 2 vmax 2 eq and(2)
            1 0 vand not and(2)
            1 0 vor and(2)
            1 1 vxor not and(2)
            0.2 not and(2)
            1 0 or(2) and(2)
            2 3 gama 8 eq and(2)
            2 3 sadd 5 eq and(2)
            2 3 ssub -1 eq and(2)
            2 3 smul 6 eq and(2)
            6 3 sdiv 2 eq and(2)
            1 2 3 sum(3) 6 eq and(2)
            2 3 4 prod(3) 24 eq and(2)
            5 3 4 min(3) 3 eq and(2)
            5 3 4 max(3) 5 eq and(2)
            3 4 ctop 53.13 gt flip(2) 5 eq and(3)
            1 rnum NaN rnum not +INF rnum not -INF rnum not and(5)
            NaN NaN eq and(2)
            1 2 3 4 tran(2,2) 1 3 2 4 sub(4) abs(4) sum(4) 0 eq and(2)
            1 2 3 copy(3,2) sum(9) 18 eq and(2)
            1 2 3 4 5 posd(4,2) sum(7) 19 eq and(2)
            1 2 3 4 5 rotl(5,2) 3 4 5 1 2 sub(5) abs(5) sum(5) 0 eq and(2)
            1 2 3 4 5 rotr(5,2) 4 5 1 2 3 sub(5) abs(5) sum(5) 0 eq and(2)
            1 2 3 flip(3) 3 2 1 sub(3) abs(3) sum(3) 0 eq and(2)
            1 2 3 pop(2) 1 eq and(2)
            0.5 0.25 0.75 tLab tXYZ 0.5 0.25 0.75 sub(3) abs(3) sum(3) 0.0001 lt and(2)
            noop
            not tget(9) add tput(9)
            tget(9) 10 mul add
            out(0,3)
            """);
        var backward = Calculator(3, 3,
            [
                Matrix(3, 3, inverse, null),
                CurveSet(3, SrgbEncoding()),
            ],
            """
            in(0,3) tXYZ mtx(0)
            0 0 0 vmax(3) 1 1 1 vmin(3)
            curv(1)
            out(0,3)
            """);
        var tags = Mpe("A2B1", 3, 3, forward) + Mpe("B2A1", 3, 3, backward);
        return Profile("spac", "RGB ", "Lab ", "iccMAX RGB color space with calculator elements", tags);
    }

    /// <summary>
    /// An output profile (CMYK, CIELAB) whose tags are color lookup table elements of float32Number entries, with single
    /// sampled curves and segmented curves with sampled segments, for the media-relative (AToB1, BToA1) and the
    /// ICC-absolute (AToB3, BToA3) colorimetric intents, and a media white point that is not the illuminant.
    /// </summary>
    public static string CmykLabClut()
    {
        var tags = new StringBuilder();
        foreach (var absolute in new[] { false, true })
        {
            var scale = absolute ? PaperWhite.Select((value, index) => value / new[] { 0.9642, 1.0, 0.8249 }[index]).ToArray() : [1, 1, 1];

            // Device to PCS: an identity curve stored as a single sampled curve, and a segmented curve with a sampled
            // segment, then the table of the model
            var toPcs = Clut([5, 5, 5, 5], 3, cmyk =>
            {
                var xyz = IccProfileBuilder.RgbToXyz(IccProfileBuilder.CmykToRgbOnPaper(cmyk));
                return IccProfileBuilder.XyzToLab([xyz[0] * scale[0], xyz[1] * scale[1], xyz[2] * scale[2]]);
            });

            // PCS to device: CIELAB normalized to [0, 1] by a matrix, then the table of the inverse model
            var fromPcs = Clut([9, 9, 9], 4, encoded =>
            {
                var lab = IccProfileBuilder.DecodeLab(encoded);
                var xyz = IccProfileBuilder.LabToXyz(lab);
                return IccProfileBuilder.RgbOnPaperToCmyk(IccProfileBuilder.XyzToRgb([xyz[0] / scale[0], xyz[1] / scale[1], xyz[2] / scale[2]]));
            });

            var linear = SingleSampledCurve(0, 1, [.. Enumerable.Range(0, 17).Select(i => i / 16.0)]);
            var sampled = SegmentedCurve(
                ("-infinity", "0", 0, [1, 0, 0, 0]),
                ("0", "1", -1, [.. Enumerable.Range(1, 8).Select(i => i / 8.0)]),
                ("1", "+infinity", 0, [1, 0, 1, 0]));
            tags.Append(Mpe(absolute ? "A2B3" : "A2B1", 4, 3, CurveSet(4, linear, sampled, linear, sampled), toPcs));
            tags.Append(Mpe(absolute ? "B2A3" : "B2A1", 3, 4, Matrix(3, 3, [0.01, 0, 0, 0, 1 / 255.0, 0, 0, 0, 1 / 255.0], [0, 128 / 255.0, 128 / 255.0]), fromPcs));
        }

        return Profile("prtr", "CMYK", "Lab ", "iccMAX CMYK output with color lookup tables", tags.ToString(), PaperWhite);
    }

    /// <summary>
    /// An output profile (gray, CIEXYZ) with ICC-absolute colorimetric tags only (AToB3, BToA3) and a media white point
    /// that is not the illuminant: the media-relative colorimetric intent derives its colors from them (ICC.2:2023 section
    /// 9.2.2). The tone curve is a segmented curve of the formula types 1, 2, 4, 5, 6 and 7 of ICC.2:2023 Table 111 (a
    /// gamma-like rise from 0 to 1), its inverse a single sampled curve.
    /// </summary>
    public static string GrayXyzAbsolute()
    {
        var segments = new StringBuilder("          <SegmentedCurve>\n");
        foreach (var (start, end, function, parameters) in GraySegments)
        {
            segments.Append(CultureInfo.InvariantCulture, $"            <FormulaSegment Start=\"{start}\" End=\"{end}\" FunctionType=\"{function}\">{Numbers(parameters)}</FormulaSegment>\n");
        }

        segments.Append("          </SegmentedCurve>\n");
        var inverse = SingleSampledCurve(0, 1, [.. Enumerable.Range(0, 257).Select(i => InvertGrayCurve(i / 256.0))]);
        var tags = Mpe("A2B3", 1, 3, CurveSet(1, segments.ToString()), Matrix(1, 3, PaperWhite, null))
            + Mpe("B2A3", 3, 1, Matrix(3, 1, [0, 1 / PaperWhite[1], 0], null), CurveSet(1, inverse));
        return Profile("prtr", "GRAY", "XYZ ", "iccMAX gray output with absolute colorimetric tags", tags, PaperWhite);
    }

    /// <summary>
    /// The segments of the tone curve of <see cref="GrayXyzAbsolute"/>: one formula of each type of ICC.2:2023 Table 111
    /// but 0 and 3, which the other profiles use, rising from 0 to 1 over [0, 1].
    /// </summary>
    private static readonly (string Start, string End, int Function, double[] Parameters)[] GraySegments =
    [
        ("-infinity", "0", 0, [1, 1, 0, 0]),                                     // x
        ("0", "0.2", 7, [1, 1, 0, 0.5, 1, 1]),                                   // 0.5 x / (1 + x)
        ("0.2", "0.4", 5, [1, 1, 0, 0.4, 1, 0.15]),                              // 0.15 exp(x - 0.4)
        ("0.4", "0.6", 1, [1, 1, 1, 0.6, 0.15]),                                 // log10(x + 0.6) + 0.15
        ("0.6", "0.8", 2, [0.229, 2, 2, -1.2, 0]),                               // 0.229 2^(2 x - 1.2)
        ("0.8", "0.95", 4, [1, 1, -1, 0.302 - Math.Log(1.8), 1]),                // ln(x + 1) + 0.302 - ln(1.8)
        ("0.95", "+infinity", 6, [1, 1, 11.36, 1, 0, 1, 12.36]),                 // 12.36 x - 11.36
    ];

    /// <summary>The tone curve of <see cref="GrayXyzAbsolute"/>, evaluated in double precision.</summary>
    public static double EvaluateGrayCurve(double x)
    {
        var index = 0;
        while (index < GraySegments.Length - 1 && !(x <= double.Parse(GraySegments[index].End, CultureInfo.InvariantCulture)))
        {
            index++;
        }

        var p = GraySegments[index].Parameters;
        return GraySegments[index].Function switch
        {
            0 => Math.Pow((p[1] * x) + p[2], p[0]) + p[3],
            1 => (p[1] * Math.Log10((p[2] * Math.Pow(x, p[0])) + p[3])) + p[4],
            2 => (p[0] * Math.Pow(p[1], (p[2] * x) + p[3])) + p[4],
            4 => (p[1] * Math.Log((p[4] * Math.Pow(x, p[0])) - p[2])) + p[3],
            5 => (p[5] * Math.Exp(((p[4] * Math.Pow(x, p[0])) - p[3]) / p[1])) + p[2],
            6 => p[5] * Math.Pow(Math.Max((p[6] * Math.Pow(x, p[1])) - p[2], 0) / (p[3] - (p[4] * Math.Pow(x, p[1]))), p[0]),
            _ => p[5] * Math.Pow((p[2] + (p[3] * Math.Pow(x, p[1]))) / (1 + (p[4] * Math.Pow(x, p[1]))), p[0]),
        };
    }

    private static double InvertGrayCurve(double target)
    {
        double low = 0, high = 1;
        for (var i = 0; i < 60; i++)
        {
            var middle = (low + high) / 2;
            if (EvaluateGrayCurve(middle) < target)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// A named color profile (version 5, CIELAB, CMYK device values): the colors of
    /// <see cref="IccProfileBuilder.NamedColorSamples"/> in a namedColorArray, some of them with tints.
    /// </summary>
    public static string NamedColors()
    {
        var colors = new StringBuilder();
        foreach (var (name, lab, cmyk) in IccProfileBuilder.NamedColorSamples)
        {
            colors.Append(CultureInfo.InvariantCulture, $"""
                    <tagStructType>
                      <namedColorStructure> <MemberTags>
                        <nmclNameNmClrMbr> <utf8Type> <TextData><![CDATA[{name}]]></TextData> </utf8Type> </nmclNameNmClrMbr>
                        <nmclTintMbr> <float32ArrayType> <Data>0.5 1</Data> </float32ArrayType> </nmclTintMbr>
                        <nmclPcsDataMbr> <float32ArrayType> <Data>{Numbers([(100 + lab[0]) / 2, lab[1] / 2, lab[2] / 2, .. lab])}</Data> </float32ArrayType> </nmclPcsDataMbr>
                        <nmclDeviceDataMbr> <float32ArrayType> <Data>{Numbers([.. cmyk.Select(value => value / 2), .. cmyk])}</Data> </float32ArrayType> </nmclDeviceDataMbr>
                      </MemberTags> </namedColorStructure>
                    </tagStructType>

                """);
        }

        var tags = $"""
                <namedColorTag> <tagArrayType>
                  <namedColorArray> <ArrayTags>
                    <tagStructType>
                      <tintZeroStructure> <MemberTags>
                        <tnt0PcsDataMbr> <float32ArrayType> <Data>100 0 0</Data> </float32ArrayType> </tnt0PcsDataMbr>
                        <tnt0DeviceDataMbr> <float32ArrayType> <Data>0 0 0 0</Data> </float32ArrayType> </tnt0DeviceDataMbr>
                      </MemberTags> </tintZeroStructure>
                    </tagStructType>
            {colors}      </ArrayTags> </namedColorArray>
                </tagArrayType> </namedColorTag>

            """;
        return Profile("nmcl", "CMYK", "Lab ", "iccMAX named colors", tags);
    }

    private static string Profile(string profileClass, string dataColorSpace, string connectionSpace, string description, string tags, double[]? mediaWhite = null)
    {
        var white = mediaWhite ?? [0.9642, 1, 0.8249];
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <IccProfile>
              <Header>
                <PreferredCMMType></PreferredCMMType>
                <ProfileVersion>5.00</ProfileVersion>
                <ProfileDeviceClass>{profileClass}</ProfileDeviceClass>
                <DataColourSpace>{dataColorSpace}</DataColourSpace>
                <PCS>{connectionSpace}</PCS>
                <CreationDateTime>2026-01-01T00:00:00</CreationDateTime>
                <ProfileFlags EmbeddedInFile="false" UseWithEmbeddedDataOnly="false"/>
                <DeviceAttributes ReflectiveOrTransparency="reflective" GlossyOrMatte="glossy" MediaPolarity="positive" MediaColour="colour"/>
                <RenderingIntent>Relative</RenderingIntent>
                <PCSIlluminant><XYZNumber X="0.96420288" Y="1.00000000" Z="0.82490540"/></PCSIlluminant>
                <ProfileCreator></ProfileCreator>
                <ProfileID>1</ProfileID>
              </Header>
              <Tags>
                <multiLocalizedUnicodeType><TagSignature>desc</TagSignature><LocalizedText LanguageCountry="enUS"><![CDATA[{description}]]></LocalizedText></multiLocalizedUnicodeType>
                <multiLocalizedUnicodeType><TagSignature>cprt</TagSignature><LocalizedText LanguageCountry="enUS"><![CDATA[CC0-1.0, Meziantou.Framework contributors]]></LocalizedText></multiLocalizedUnicodeType>
                <XYZType><TagSignature>wtpt</TagSignature><XYZNumber X="{Number(white[0])}" Y="{Number(white[1])}" Z="{Number(white[2])}"/></XYZType>
            {tags}  </Tags>
            </IccProfile>

            """;
    }

    private static string Mpe(string signature, int inputs, int outputs, params string[] elements)
        => $"""
                <multiProcessElementType><TagSignature>{signature}</TagSignature>
                  <MultiProcessElements InputChannels="{inputs}" OutputChannels="{outputs}">
            {string.Concat(elements)}      </MultiProcessElements>
                </multiProcessElementType>

            """;

    private static string CurveSet(int channels, params string[] curves)
    {
        var all = curves.Length == channels ? curves : [.. Enumerable.Repeat(curves[0], channels)];
        return $"""
                    <CurveSetElement InputChannels="{channels}" OutputChannels="{channels}">
            {string.Concat(all)}        </CurveSetElement>

            """;
    }

    /// <summary>A segmented curve: formula segments (a function type and its parameters) and sampled segments (function type -1, the samples).</summary>
    private static string SegmentedCurve(params (string Start, string End, int Function, double[] Values)[] segments)
    {
        var text = new StringBuilder("          <SegmentedCurve>\n");
        foreach (var (start, end, function, values) in segments)
        {
            text.Append(function < 0
                ? $"            <SampledSegment Start=\"{start}\" End=\"{end}\">{Numbers(values)}</SampledSegment>\n"
                : $"            <FormulaSegment Start=\"{start}\" End=\"{end}\" FunctionType=\"{function}\">{Numbers(values)}</FormulaSegment>\n");
        }

        return text.Append("          </SegmentedCurve>\n").ToString();
    }

    private static string SingleSampledCurve(double first, double last, double[] values)
        => $"          <SingleSampledCurve Start=\"-infinity\" End=\"+infinity\" FirstEntry=\"{Number(first)}\" LastEntry=\"{Number(last)}\" StorageType=\"0\" ExtensionType=\"0\">{Numbers(values)}</SingleSampledCurve>\n";

    private static string Matrix(int inputs, int outputs, double[] matrix, double[]? offsets)
        => $"""
                    <MatrixElement InputChannels="{inputs}" OutputChannels="{outputs}">
                      <MatrixData>{Numbers(matrix)}</MatrixData>
            {(offsets is null ? "" : $"          <ConstantData>{Numbers(offsets)}</ConstantData>\n")}        </MatrixElement>

            """;

    private static string Clut(int[] gridPoints, int outputs, Func<double[], double[]> values)
    {
        // The first input varies least rapidly (ICC.2:2023 section 11.2.3)
        var data = new StringBuilder();
        var indexes = new int[gridPoints.Length];
        while (true)
        {
            var point = indexes.Select((index, i) => (double)index / (gridPoints[i] - 1)).ToArray();
            data.Append("          ").Append(Numbers(values(point))).Append('\n');
            var dimension = gridPoints.Length - 1;
            while (dimension >= 0 && ++indexes[dimension] == gridPoints[dimension])
            {
                indexes[dimension--] = 0;
            }

            if (dimension < 0)
                break;
        }

        return $"""
                    <CLutElement InputChannels="{gridPoints.Length}" OutputChannels="{outputs}">
                      <GridPoints>{string.Join(' ', gridPoints)}</GridPoints>
                      <TableData>
            {data}          </TableData>
                    </CLutElement>

            """;
    }

    private static string Tint(int outputs, double[] values)
        => $"""
                    <TintArrayElement InputChannels="1" OutputChannels="{outputs}">
                      <float32NumberType> <Data>{Numbers(values)}</Data> </float32NumberType>
                    </TintArrayElement>

            """;

    private static string Calculator(int inputs, int outputs, string[] subElements, string function)
        => $$"""
                    <CalculatorElement InputChannels="{{inputs}}" OutputChannels="{{outputs}}">
                      <SubElements>
            {{string.Concat(subElements)}}          </SubElements>
                      <MainFunction>
            {
            {{function}}
            }
                      </MainFunction>
                    </CalculatorElement>

            """;

    /// <summary>The sRGB decoding function as a segmented curve: a linear segment, then formula type 0.</summary>
    private static string SrgbDecoding() => SegmentedCurve(("-infinity", "0.04045", 0, [1, 1 / 12.92, 0, 0]), ("0.04045", "+infinity", 0, [2.4, 1 / 1.055, 0.055 / 1.055, 0]));

    /// <summary>The sRGB encoding function as a segmented curve: a linear segment, then formula type 3.</summary>
    private static string SrgbEncoding() => SegmentedCurve(("-infinity", "0.0031308", 0, [1, 12.92, 0, 0]), ("0.0031308", "+infinity", 3, [1 / 2.4, 1.055, 1, 0, -0.055]));

    /// <summary>The matrix of the sRGB primaries adapted to D50, rows X, Y, Z.</summary>
    private static double[] SrgbMatrix()
    {
        var colorants = IccProfileBuilder.SrgbColorants;
        return [colorants[0][0], colorants[1][0], colorants[2][0], colorants[0][1], colorants[1][1], colorants[2][1], colorants[0][2], colorants[1][2], colorants[2][2]];
    }

    private static double[] Invert(double[] m)
    {
        var determinant = (m[0] * ((m[4] * m[8]) - (m[5] * m[7]))) - (m[1] * ((m[3] * m[8]) - (m[5] * m[6]))) + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        return
        [
            ((m[4] * m[8]) - (m[5] * m[7])) / determinant, ((m[2] * m[7]) - (m[1] * m[8])) / determinant, ((m[1] * m[5]) - (m[2] * m[4])) / determinant,
            ((m[5] * m[6]) - (m[3] * m[8])) / determinant, ((m[0] * m[8]) - (m[2] * m[6])) / determinant, ((m[2] * m[3]) - (m[0] * m[5])) / determinant,
            ((m[3] * m[7]) - (m[4] * m[6])) / determinant, ((m[1] * m[6]) - (m[0] * m[7])) / determinant, ((m[0] * m[4]) - (m[1] * m[3])) / determinant,
        ];
    }

    private static string Numbers(IEnumerable<double> values) => string.Join(' ', values.Select(Number));

    // iccFromXml does not read exponents: values smaller than 1e-9 are written as 0
    private static string Number(double value) => Math.Abs(value) < 1e-9 ? "0" : value.ToString("0.#########", CultureInfo.InvariantCulture);
}
