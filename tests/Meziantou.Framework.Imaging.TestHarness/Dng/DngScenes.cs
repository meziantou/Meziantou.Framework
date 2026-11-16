namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>
/// The synthetic DNG files of the tests and of the corpus: smooth color fields seen through a camera (a mosaic of a color
/// filter array, or linear raw data), assembled by <see cref="DngFileBuilder"/> with one feature of the format each.
/// </summary>
public static class DngScenes
{
    /// <summary>A camera-like color matrix (XYZ to camera, D65).</summary>
    public static readonly double[] CameraMatrixD65 = [0.7, -0.2, -0.05, -0.4, 1.2, 0.2, -0.1, 0.25, 0.6];

    /// <summary>A camera-like color matrix (XYZ to camera, standard light A).</summary>
    public static readonly double[] CameraMatrixA = [0.9, -0.3, -0.05, -0.35, 1.1, 0.3, -0.05, 0.2, 0.45];

    /// <summary>Gets every scene, by name.</summary>
    public static IReadOnlyList<(string Name, string Description, DngFileBuilder Builder)> All { get; } = Create();

    /// <summary>Gets a scene by name.</summary>
    public static DngFileBuilder Get(string name) => All.Single(scene => scene.Name == name).Builder;

    /// <summary>
    /// Gets the stored samples of a smooth scene seen through a repeating pattern of colors: each position takes the value
    /// of its color (0 red, 1 green, 2 blue, 3 cyan, 4 magenta, 5 yellow), scaled from the black to the white level.
    /// </summary>
    public static double[] Mosaic(int width, int height, int rows, int columns, byte[] pattern, double black, double white, int seed = 0)
    {
        var samples = new double[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // The camera sees a white balanced by its usual neutral (0.5, 1, 0.7): the colors stay below the white level once balanced
                var color = pattern[((y % rows) * columns) + (x % columns)];
                var response = color switch { 0 => 0.5, 2 => 0.7, _ => 1 };
                samples[(y * width) + x] = Math.Round(black + (Field(x, y, width, height, color, seed) * response * (white - black)), MidpointRounding.AwayFromZero);
            }
        }

        return samples;
    }

    /// <summary>Gets the samples of linear raw data of a smooth scene, <paramref name="planes"/> per pixel.</summary>
    public static double[] Linear(int width, int height, int planes, double black, double white, bool round = true)
    {
        var samples = new double[width * height * planes];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var p = 0; p < planes; p++)
                {
                    var response = p switch { 0 => 0.5, 2 => 0.7, _ => 1 };
                    var value = black + (Field(x, y, width, height, (byte)p, 0) * (planes == 1 ? 1 : response) * (white - black));
                    samples[(((y * width) + x) * planes) + p] = round ? Math.Round(value, MidpointRounding.AwayFromZero) : value;
                }
            }
        }

        return samples;
    }

    /// <summary>The smooth field of a color at a position: between 0.1 and 0.85.</summary>
    public static double Field(int x, int y, int width, int height, byte color, int seed)
    {
        var u = (double)x / Math.Max(1, width - 1);
        var v = (double)y / Math.Max(1, height - 1);
        var value = color switch
        {
            0 => 0.15 + (0.55 * u) + (0.05 * v),
            1 => 0.3 + (0.35 * v) + (0.1 * u),
            2 => 0.2 + (0.25 * (u + v)),
            3 => 0.25 + (0.4 * u * v),
            4 => 0.4 + (0.3 * (1 - u)),
            _ => 0.1 + (0.5 * v * (1 - u)),
        };

        return Math.Clamp(value + (0.02 * seed), 0.1, 0.85);
    }

    private static List<(string, string, DngFileBuilder)> Create()
    {
        List<(string, string, DngFileBuilder)> scenes = [];
        byte[] rggb = [0, 1, 1, 2];
        void Add(string name, string description, DngFileBuilder builder) => scenes.Add((name, description, builder));

        Add("bayer-rggb", "A 48x32 RGGB mosaic of 16-bit samples, uncompressed, little-endian, one strip; a black level of 256 and a white level of 4000; ColorMatrix1 (D65) and AsShotNeutral.",
            new DngFileBuilder { Width = 48, Height = 32, Samples = Mosaic(48, 32, 2, 2, rggb, 256, 4000), BlackLevel = [256], WhiteLevel = [4000], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        foreach (var (name, pattern) in new[] { ("bayer-bggr", new byte[] { 2, 1, 1, 0 }), ("bayer-grbg", new byte[] { 1, 0, 2, 1 }), ("bayer-gbrg", new byte[] { 1, 2, 0, 1 }) })
        {
            Add(name, $"A 40x24 {name[6..].ToUpperInvariant()} mosaic of 16-bit samples, uncompressed.",
                new DngFileBuilder { Width = 40, Height = 24, Samples = Mosaic(40, 24, 2, 2, pattern, 0, 65535), CfaPattern = pattern, ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });
        }

        Add("big-endian-12-bit-packed", "A 36x20 RGGB mosaic of 12-bit samples packed most significant bit first, in a big-endian file, strips of 7 rows.",
            new DngFileBuilder { Width = 36, Height = 20, Samples = Mosaic(36, 20, 2, 2, rggb, 64, 4095), BitsPerSample = 12, IsBigEndian = true, RowsPerStrip = 7, BlackLevel = [64], WhiteLevel = [4095], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("packed-10-bit", "A 30x18 RGGB mosaic of 10-bit packed samples (rows end on a byte).",
            new DngFileBuilder { Width = 30, Height = 18, Samples = Mosaic(30, 18, 2, 2, rggb, 16, 1023), BitsPerSample = 10, BlackLevel = [16], WhiteLevel = [1023], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("packed-14-bit", "A 28x16 RGGB mosaic of 14-bit packed samples.",
            new DngFileBuilder { Width = 28, Height = 16, Samples = Mosaic(28, 16, 2, 2, rggb, 512, 16383), BitsPerSample = 14, BlackLevel = [512], WhiteLevel = [16383], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("8-bit", "A 32x16 RGGB mosaic of 8-bit samples.",
            new DngFileBuilder { Width = 32, Height = 16, Samples = Mosaic(32, 16, 2, 2, rggb, 4, 250), BitsPerSample = 8, BlackLevel = [4], WhiteLevel = [250], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("deflate-predictor", "A 48x32 RGGB mosaic of 16-bit samples compressed with Deflate after horizontal differencing (predictor 2), strips of 8 rows.",
            new DngFileBuilder { Width = 48, Height = 32, Samples = Mosaic(48, 32, 2, 2, rggb, 128, 16383), Compression = 8, Predictor = 2, RowsPerStrip = 8, BlackLevel = [128], WhiteLevel = [16383], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("deflate-predictor-x2", "A 48x32 RGGB mosaic of 16-bit samples, Deflate with the Horizontal Difference X2 predictor of DNG (34892), big-endian.",
            new DngFileBuilder { Width = 48, Height = 32, Samples = Mosaic(48, 32, 2, 2, rggb, 128, 16383), Compression = 8, Predictor = 34892, IsBigEndian = true, BlackLevel = [128], WhiteLevel = [16383], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("deflate-predictor-x4-8-bit", "A 40x16 RGGB mosaic of 8-bit samples, Deflate with the Horizontal Difference X4 predictor (34893).",
            new DngFileBuilder { Width = 40, Height = 16, Samples = Mosaic(40, 16, 2, 2, rggb, 0, 255), BitsPerSample = 8, Compression = 8, Predictor = 34893, ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("tiles", "A 40x40 RGGB mosaic of 16-bit samples in 16x16 tiles (the last ones padded), uncompressed.",
            new DngFileBuilder { Width = 40, Height = 40, Samples = Mosaic(40, 40, 2, 2, rggb, 0, 65535), Tile = (16, 16), ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("deflate-tiles", "A 40x36 RGGB mosaic of 16-bit samples in 32x32 Deflate tiles with horizontal differencing.",
            new DngFileBuilder { Width = 40, Height = 36, Samples = Mosaic(40, 36, 2, 2, rggb, 0, 65535), Tile = (32, 32), Compression = 8, Predictor = 2, ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        var withMasks = Mosaic(52, 36, 2, 2, rggb, 300, 4000);
        for (var y = 0; y < 36; y++)
        {
            for (var x = 0; x < 52; x++)
            {
                // Masked pixels around the active area (2, 4, 34, 50) read only the black level
                if (y < 2 || x < 4 || y >= 34 || x >= 50)
                {
                    withMasks[(y * 52) + x] = 300;
                }
            }
        }

        Add("active-area-crop", "A 52x36 mosaic whose active area is (2, 4, 34, 50) and whose default crop is 40x28 at (3, 2) of it: the masked pixels hold the black level.",
            new DngFileBuilder { Width = 52, Height = 36, Samples = withMasks, ActiveArea = [2, 4, 34, 50], DefaultCropOrigin = [3, 2], DefaultCropSize = [40, 28], BlackLevel = [300], WhiteLevel = [4000], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        // The stored values are the square roots of linear values: a linearization table maps them back
        var table = new ushort[4096];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (ushort)Math.Round(Math.Pow(i / 4095.0, 2) * 60000, MidpointRounding.AwayFromZero);
        }

        var curve = Mosaic(40, 24, 2, 2, rggb, 0, 1);
        Add("linearization-table", "A 40x24 mosaic of 12-bit values stored through a square-root curve, mapped back by a LinearizationTable of 4096 entries, white level 60000.",
            new DngFileBuilder { Width = 40, Height = 24, Samples = [.. curve.Select(value => Math.Round(Math.Sqrt(value) * 4095, MidpointRounding.AwayFromZero))], BitsPerSample = 16, LinearizationTable = table, WhiteLevel = [60000], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        var deltaH = Enumerable.Range(0, 40).Select(x => Math.Round(8 * Math.Sin(x / 5.0), 2, MidpointRounding.AwayFromZero)).ToArray();
        var deltaV = Enumerable.Range(0, 24).Select(y => Math.Round(6 * Math.Cos(y / 4.0), 2, MidpointRounding.AwayFromZero)).ToArray();
        var blacks = Mosaic(40, 24, 2, 2, rggb, 400, 8000);
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                blacks[(y * 40) + x] += ((y % 2) * 10) + ((x % 2) * 4) + Math.Round(deltaH[x] + deltaV[y], MidpointRounding.AwayFromZero);
            }
        }

        Add("black-level-pattern", "A 40x24 mosaic whose black level is a 2x2 repeating pattern (BlackLevelRepeatDim) plus BlackLevelDeltaH of each column and BlackLevelDeltaV of each row.",
            new DngFileBuilder { Width = 40, Height = 24, Samples = blacks, BlackLevelRepeatDim = [2, 2], BlackLevel = [400, 404, 410, 414], BlackLevelDeltaH = deltaH, BlackLevelDeltaV = deltaV, WhiteLevel = [8000], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("forward-matrices", "Two calibrations (standard light A and D65) with ForwardMatrix1/2, CameraCalibration1/2 and AnalogBalance, interpolated for the AsShotNeutral of about 4700 K.",
            new DngFileBuilder
            {
                Width = 40, Height = 24, Samples = Mosaic(40, 24, 2, 2, rggb, 0, 65535),
                CalibrationIlluminant1 = 17, CalibrationIlluminant2 = 21, ColorMatrix1 = CameraMatrixA, ColorMatrix2 = CameraMatrixD65,
                ForwardMatrix1 = [0.6, 0.25, 0.1142, 0.25, 0.7, 0.05, 0.02, 0.1, 0.7049], ForwardMatrix2 = [0.65, 0.2, 0.1142, 0.28, 0.67, 0.05, 0.03, 0.12, 0.6749],
                CameraCalibration1 = [1.02, 0, 0, 0, 1, 0, 0, 0, 0.98], CameraCalibration2 = [1.01, 0, 0, 0, 1, 0, 0, 0, 0.99], AnalogBalance = [1, 1, 1.05], AsShotNeutral = [0.55, 1, 0.62],
            });

        Add("color-matrices-white-xy", "Two calibrations (standard light A and D65) without forward matrices, white balanced by AsShotWhiteXY (0.36, 0.37): Bradford adaptation to D50.",
            new DngFileBuilder { Width = 40, Height = 24, Samples = Mosaic(40, 24, 2, 2, rggb, 0, 65535), CalibrationIlluminant1 = 17, CalibrationIlluminant2 = 21, ColorMatrix1 = CameraMatrixA, ColorMatrix2 = CameraMatrixD65, AsShotNeutral = null, AsShotWhiteXY = [0.36, 0.37] });

        Add("baseline-exposure", "BaselineExposure 1.5 and BaselineExposureOffset -0.5: the rendering is one stop brighter, its highlights above 1.",
            new DngFileBuilder { Width = 32, Height = 24, Samples = Mosaic(32, 24, 2, 2, rggb, 0, 65535), ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7], BaselineExposure = 1.5, BaselineExposureOffset = -0.5 });

        Add("linear-raw-rgb", "Linear raw data of three 16-bit samples per pixel (a demosaiced image), 40x24.",
            new DngFileBuilder { Width = 40, Height = 24, Photometric = 34892, SamplesPerPixel = 3, Samples = Linear(40, 24, 3, 0, 65535), ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("monochrome", "Linear raw data of one 16-bit sample per pixel without color matrix: a monochrome camera, rendered as gray.",
            new DngFileBuilder { Width = 36, Height = 20, Photometric = 34892, Samples = Linear(36, 20, 1, 100, 60000), BlackLevel = [100], WhiteLevel = [60000], ColorMatrix1 = null, AsShotNeutral = null, CalibrationIlluminant1 = null });

        Add("float-32", "Linear raw data of three 32-bit floating-point samples, Deflate with the floating-point predictor (3): values up to 0.85 of the white level 1.",
            new DngFileBuilder { Width = 32, Height = 20, Photometric = 34892, SamplesPerPixel = 3, BitsPerSample = 32, IsFloatingPoint = true, Samples = Linear(32, 20, 3, 0, 1, round: false), Compression = 8, Predictor = 3, DngVersion = [1, 4, 0, 0], ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        Add("float-16-cfa", "An RGGB mosaic of 16-bit floating-point samples (half precision), Deflate with the Floating Point X2 predictor of DNG (34894).",
            new DngFileBuilder { Width = 40, Height = 24, BitsPerSample = 16, IsFloatingPoint = true, Samples = [.. Mosaic(40, 24, 2, 2, rggb, 0, 1024).Select(value => value / 1024)], Compression = 8, Predictor = 34894, ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        byte[] cmyg = [3, 5, 4, 1];
        Add("cfa-cmyg", "A 2x2 pattern of cyan, magenta, yellow and green (CFAPlaneColor 3, 4, 5, 1): four camera planes, converted by the pseudo-inverse of a 4x3 ColorMatrix1.",
            new DngFileBuilder
            {
                Width = 40, Height = 24, Samples = Mosaic(40, 24, 2, 2, cmyg, 0, 65535), CfaPattern = cmyg, CfaPlaneColor = [3, 4, 5, 1],
                ColorMatrix1 = [-0.2, 0.8, 0.4, 0.6, -0.1, 0.5, 0.5, 0.6, -0.15, 0.1, 0.9, 0.05], AsShotNeutral = [0.9, 0.8, 1, 0.6],
            });

        byte[] sixBySix =
        [
            1, 2, 1, 1, 0, 1,
            0, 1, 0, 2, 1, 2,
            1, 2, 1, 1, 0, 1,
            1, 0, 1, 1, 2, 1,
            2, 1, 2, 0, 1, 0,
            1, 0, 1, 1, 2, 1,
        ];
        Add("cfa-6x6", "A 6x6 repeating pattern of red, green and blue (a pattern of the X-Trans kind): the colors missing around a pixel come from larger windows.",
            new DngFileBuilder { Width = 42, Height = 30, Samples = Mosaic(42, 30, 6, 6, sixBySix, 0, 65535), CfaRows = 6, CfaColumns = 6, CfaPattern = sixBySix, ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7] });

        var badPixels = Mosaic(40, 24, 2, 2, rggb, 0, 65535);
        badPixels[(5 * 40) + 7] = 65535;
        badPixels[(10 * 40) + 20] = 65535;
        badPixels[(15 * 40) + 9] = 0;
        foreach (var (x, y) in new[] { (30, 3), (31, 3), (30, 4), (31, 4) })
        {
            badPixels[(y * 40) + x] = 12;
        }

        Add("opcodes-list1", "OpcodeList1: FixBadPixelsConstant (the value 65535), FixBadPixelsList (a point and a 2x2 rectangle), then MapTable on the stored values of the right half.",
            new DngFileBuilder
            {
                Width = 40, Height = 24, Samples = badPixels, ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7],
                OpcodeList1 = new DngOpcodeWriter()
                    .FixBadPixelsConstant(65535, 0)
                    .FixBadPixelsList(0, [(15, 9)], [(3, 30, 5, 32)])
                    .MapTable(new DngOpcodeArea(0, 20, 24, 40), [.. Enumerable.Range(0, 65536).Select(value => (ushort)Math.Min(65535, (int)(value * 0.9)))])
                    .ToArray(),
            });

        Add("opcodes-list2", "OpcodeList2: a GainMap of 3x4 points over the red pixels (pitch 2), ScalePerRow and DeltaPerColumn on one plane, MapPolynomial of degree 2, and an optional WarpFisheye that is skipped.",
            new DngFileBuilder
            {
                Width = 40, Height = 24, Samples = Mosaic(40, 24, 2, 2, rggb, 0, 65535), ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7],
                OpcodeList2 = new DngOpcodeWriter()
                    .GainMap(new DngOpcodeArea(0, 0, 24, 40, RowPitch: 2, ColumnPitch: 2), 3, 4, 0.5, 1 / 3.0, 0, 0, 1, [1.2f, 1.1f, 1.05f, 1.2f, 1.1f, 1, 1, 1.1f, 1.25f, 1.15f, 1.1f, 1.3f])
                    .PerLine(12, new DngOpcodeArea(4, 0, 20, 40), [.. Enumerable.Range(0, 16).Select(i => 0.9f + (i * 0.01f))])
                    .PerLine(11, new DngOpcodeArea(0, 10, 24, 30, ColumnPitch: 2), [.. Enumerable.Range(0, 10).Select(i => i * 0.002f)])
                    .MapPolynomial(new DngOpcodeArea(0, 0, 24, 40), [0.01, 1.1, -0.15])
                    .Add(99, [1, 2, 3, 4], optional: true)
                    .ToArray(),
            });

        Add("opcodes-list3", "OpcodeList3 on the demosaiced image: FixVignetteRadial, a WarpRectilinear with one radial and tangential set, and ScalePerColumn on the blue plane.",
            new DngFileBuilder
            {
                Width = 40, Height = 28, Samples = Mosaic(40, 28, 2, 2, rggb, 0, 65535), ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7],
                OpcodeList3 = new DngOpcodeWriter()
                    .FixVignetteRadial([0.2, -0.05, 0, 0, 0], 0.5, 0.45)
                    .WarpRectilinear([[1.01, -0.02, 0.005, 0, 0.001, -0.002]], 0.52, 0.5)
                    .PerLine(13, new DngOpcodeArea(0, 0, 28, 40, Plane: 2), [.. Enumerable.Range(0, 40).Select(i => 1 - (i * 0.003f))])
                    .ToArray(),
            });

        Add("thumbnail-exif-xmp", "IFD 0 is a 16x12 RGB thumbnail (NewSubFileType 1) whose SubIFD is the raw image; IFD 0 also points to an EXIF IFD and holds an XMP packet, a serial number, a profile name and the orientation 6.",
            new DngFileBuilder
            {
                Width = 32, Height = 24, Samples = Mosaic(32, 24, 2, 2, rggb, 0, 65535), ColorMatrix1 = CameraMatrixD65, AsShotNeutral = [0.5, 1, 0.7],
                ThumbnailWidth = 16, ThumbnailHeight = 12, Orientation = 6, CameraSerialNumber = "SN 0042", ProfileName = "Embedded",
                ExifTags = [(36867, "2026:11:15 10:00:00"), (42036, "Synthetic lens")],
                Xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmp:CreatorTool=\"Meziantou\"/></rdf:RDF></x:xmpmeta>"u8.ToArray(),
            });

        return scenes;
    }
}
