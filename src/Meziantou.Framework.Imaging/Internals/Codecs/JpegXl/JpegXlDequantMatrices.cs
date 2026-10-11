namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The dequantization matrices of a VarDCT frame (ISO/IEC 18181-1, section I.2.4): for each of the 17 quantization tables
/// and each channel, the factor of every coefficient. A table is either the default one of the standard or described in
/// <c>HfGlobal</c>, by the parameters of a function of the frequency or by its values.
/// </summary>
/// <remarks>
/// <para>
/// The coefficients of a transform are laid out with the smaller side of the transform as rows; a table has that layout.
/// The matrices are computed when a transform that uses them is met: a frame seldom uses more than a few tables, and the
/// largest one holds 3 x 65536 values.
/// </para>
/// <para>
/// The default parameters were taken from libjxl 0.12.0 (<c>quant_weights.cc</c>): the standard is not publicly available.
/// </para>
/// </remarks>
internal sealed class JpegXlDequantMatrices : IDisposable
{
    private const int ModeLibrary = 0;
    private const int ModeIdentity = 1;
    private const int ModeDct2 = 2;
    private const int ModeDct4 = 3;
    private const int ModeDct4x8 = 4;
    private const int ModeAfv = 5;
    private const int ModeDct = 6;
    private const int ModeRaw = 7;

    private const int MaxBandCount = 17;
    private const float AlmostZero = 1e-8f;

    private static readonly Encoding[] Library = CreateLibrary();

    private readonly Encoding[] _encodings = new Encoding[JpegXlVarDctTables.QuantTableCount];
    private readonly Av1PooledArray<float>?[] _matrices = new Av1PooledArray<float>?[JpegXlVarDctTables.QuantTableCount];
    private readonly AllocationScope _scope;

    private JpegXlDequantMatrices(AllocationScope scope)
    {
        _scope = scope;
        Library.CopyTo(_encodings, 0);
    }

    /// <summary>Reads the samples of a table that is stored by its values, as a Modular image of three channels.</summary>
    /// <param name="tableIndex">The index of the table, which selects the Modular sub-bitstream.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <returns>The image; the caller disposes it.</returns>
    public delegate JpegXlModularImage RawTableReader(int tableIndex, int width, int height);

    /// <summary>Creates the default matrices.</summary>
    public static JpegXlDequantMatrices CreateDefault(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new JpegXlDequantMatrices(scope);
    }

    /// <summary>Reads the description of the matrices from <c>HfGlobal</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="readRawTable">Reads the Modular image of a table stored by its values.</param>
    /// <param name="scope">The scope of the matrices.</param>
    public static JpegXlDequantMatrices Read(JpegXlBitReader reader, RawTableReader readRawTable, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(readRawTable);
        ArgumentNullException.ThrowIfNull(scope);
        var result = new JpegXlDequantMatrices(scope);
        if (reader.ReadBool())
            return result;

        for (var i = 0; i < JpegXlVarDctTables.QuantTableCount; i++)
        {
            result._encodings[i] = ReadEncoding(reader, i, readRawTable);
        }

        return result;
    }

    /// <summary>
    /// Gets the matrix of a quantization table: for each channel (X, Y, B), one after the other, the factor of each
    /// coefficient, in rows of the larger side of the transform.
    /// </summary>
    public ReadOnlySpan<float> GetMatrix(int quantTable)
    {
        var matrix = _matrices[quantTable];
        if (matrix is null)
        {
            var rows = JpegXlVarDctTables.QuantTableRows[quantTable] * 8;
            var columns = JpegXlVarDctTables.QuantTableColumns[quantTable] * 8;
            Av1PooledArray<float>? owned = null;
            try
            {
                owned = Av1PooledArray<float>.Rent(_scope, 3L * rows * columns, clear: false);
                Compute(_encodings[quantTable], rows, columns, owned.Span);
                _matrices[quantTable] = matrix = owned;
                owned = null;
            }
            finally
            {
                owned?.Dispose();
            }
        }

        return matrix.Span;
    }

    public void Dispose()
    {
        for (var i = 0; i < _matrices.Length; i++)
        {
            _matrices[i]?.Dispose();
            _matrices[i] = null;
        }
    }

    private static Encoding ReadEncoding(JpegXlBitReader reader, int tableIndex, RawTableReader readRawTable)
    {
        var isSingleBlock = JpegXlVarDctTables.QuantTableRows[tableIndex] == 1 && JpegXlVarDctTables.QuantTableColumns[tableIndex] == 1;
        var mode = (int)reader.ReadBits(3);
        if (mode is ModeIdentity or ModeDct2 or ModeDct4 or ModeDct4x8 or ModeAfv && !isSingleBlock)
            throw JpegXlFormat.Invalid("a quantization table of more than one block is described as a table of one block.");

        switch (mode)
        {
            case ModeLibrary:
                // The index of a predefined table takes no bit: the standard defines a single one
                return Library[tableIndex];

            case ModeIdentity:
                return new Encoding(mode) { Weights = ReadWeights(reader, 3, scaledCount: 3, rejectZero: true) };

            case ModeDct2:
                return new Encoding(mode) { Weights = ReadWeights(reader, 6, scaledCount: 6, rejectZero: true) };

            case ModeDct4:
                return new Encoding(mode) { Weights = ReadWeights(reader, 2, scaledCount: 0, rejectZero: true), Bands = ReadBands(reader) };

            case ModeDct4x8:
                return new Encoding(mode) { Weights = ReadWeights(reader, 1, scaledCount: 0, rejectZero: true), Bands = ReadBands(reader) };

            case ModeAfv:
            {
                var weights = ReadWeights(reader, 9, scaledCount: 6, rejectZero: false);
                var bands = ReadBands(reader);
                return new Encoding(mode) { Weights = weights, Bands = bands, SecondBands = ReadBands(reader) };
            }

            case ModeDct:
                return new Encoding(mode) { Bands = ReadBands(reader) };

            default:
            {
                var denominator = reader.ReadF16();
                if (!(denominator >= AlmostZero) || !float.IsFinite(denominator))
                    throw JpegXlFormat.Invalid("the denominator of a quantization table is not valid.");

                // The image has the smaller side of the table as its width; its samples are the values of the table, in the
                // order of the samples whatever the shape of the table
                var rows = JpegXlVarDctTables.QuantTableRows[tableIndex] * 8;
                var columns = JpegXlVarDctTables.QuantTableColumns[tableIndex] * 8;
                using var image = readRawTable(tableIndex, rows, columns);
                if (image.Channels.Count != 3)
                    throw JpegXlFormat.Invalid("a quantization table does not have three channels.");

                var values = new int[3][];
                for (var c = 0; c < 3; c++)
                {
                    if (image.Channels[c].Width != rows || image.Channels[c].Height != columns)
                        throw JpegXlFormat.Invalid("a quantization table does not have the size of its transform.");

                    values[c] = image.Channels[c].Samples.ToArray();
                }

                return new Encoding(mode) { RawDenominator = denominator, RawValues = values };
            }
        }
    }

    /// <summary>Reads, for each channel, a number of parameters; the first ones are stored divided by 64.</summary>
    private static float[][] ReadWeights(JpegXlBitReader reader, int count, int scaledCount, bool rejectZero)
    {
        var result = new float[3][];
        for (var c = 0; c < 3; c++)
        {
            result[c] = new float[count];
            for (var i = 0; i < count; i++)
            {
                var value = reader.ReadF16();
                if (rejectZero && MathF.Abs(value) < AlmostZero)
                    throw JpegXlFormat.Invalid("a parameter of a quantization table is zero.");

                result[c][i] = i < scaledCount ? value * 64 : value;
            }
        }

        return result;
    }

    /// <summary>
    /// Reads, for each channel, the bands of a function of the frequency: the value of the first band, stored divided by
    /// 64, then the ratio of each band to the previous one.
    /// </summary>
    private static float[][] ReadBands(JpegXlBitReader reader)
    {
        var count = (int)reader.ReadBits(4) + 1;
        var result = new float[3][];
        for (var c = 0; c < 3; c++)
        {
            result[c] = new float[count];
            for (var i = 0; i < count; i++)
            {
                result[c][i] = reader.ReadF16();
            }

            if (result[c][0] < AlmostZero)
                throw JpegXlFormat.Invalid("the first band of a quantization table is too small.");

            result[c][0] *= 64;
        }

        return result;
    }

    /// <summary>Computes the weights of a table, then their inverses, which are the factors of the coefficients.</summary>
    private static void Compute(Encoding encoding, int rows, int columns, Span<float> matrix)
    {
        var size = rows * columns;
        for (var c = 0; c < 3; c++)
        {
            var weights = matrix.Slice(c * size, size);
            switch (encoding.Mode)
            {
                case ModeIdentity:
                {
                    var values = encoding.Weights![c];
                    weights.Fill(values[0]);
                    weights[1] = values[1];
                    weights[8] = values[1];
                    weights[9] = values[2];
                    break;
                }

                case ModeDct2:
                {
                    // The weight of a coefficient only depends on the level of the nested transforms that produces it
                    var values = encoding.Weights![c];
                    weights[0] = 1;
                    weights[1] = values[0];
                    weights[8] = values[0];
                    weights[9] = values[1];
                    for (var y = 0; y < 8; y++)
                    {
                        for (var x = 0; x < 8; x++)
                        {
                            var level = Math.Max(y, x) switch
                            {
                                < 2 => -1,
                                < 4 => 0,
                                _ => 1,
                            };

                            if (level >= 0)
                            {
                                var half = 2 << level;
                                weights[(y * 8) + x] = values[2 + (level * 2) + (y >= half && x >= half ? 1 : 0)];
                            }
                        }
                    }

                    break;
                }

                case ModeDct4:
                {
                    Span<float> small = stackalloc float[16];
                    ComputeBandWeights(encoding.Bands![c], 4, 4, small);
                    for (var y = 0; y < 8; y++)
                    {
                        for (var x = 0; x < 8; x++)
                        {
                            weights[(y * 8) + x] = small[((y / 2) * 4) + (x / 2)];
                        }
                    }

                    weights[1] /= encoding.Weights![c][0];
                    weights[8] /= encoding.Weights[c][0];
                    weights[9] /= encoding.Weights[c][1];
                    break;
                }

                case ModeDct4x8:
                {
                    Span<float> half = stackalloc float[32];
                    ComputeBandWeights(encoding.Bands![c], 4, 8, half);
                    for (var y = 0; y < 8; y++)
                    {
                        half.Slice((y / 2) * 8, 8).CopyTo(weights.Slice(y * 8, 8));
                    }

                    weights[8] /= encoding.Weights![c][0];
                    break;
                }

                case ModeAfv:
                    ComputeAfvWeights(encoding, c, weights);
                    break;

                case ModeDct:
                    ComputeBandWeights(encoding.Bands![c], rows, columns, weights);
                    break;

                case ModeRaw:
                {
                    var values = encoding.RawValues![c];
                    for (var i = 0; i < size; i++)
                    {
                        weights[i] = 1f / (encoding.RawDenominator * values[i]);
                    }

                    break;
                }

                default:
                    throw new InvalidOperationException("A quantization table has no description.");
            }

            for (var i = 0; i < size; i++)
            {
                var weight = weights[i];
                if (!(weight >= AlmostZero) || !(weight < 1f / AlmostZero))
                    throw JpegXlFormat.Invalid("a quantization table has a weight that is not valid.");

                weights[i] = 1f / weight;
            }
        }
    }

    /// <summary>
    /// Computes weights that only depend on the distance of a coefficient to the lowest frequency: the bands are the
    /// values at regular distances, and the weights between two bands are interpolated geometrically.
    /// </summary>
    private static void ComputeBandWeights(ReadOnlySpan<float> parameters, int rows, int columns, Span<float> weights)
    {
        Span<float> bands = stackalloc float[MaxBandCount];
        bands = bands[..parameters.Length];
        ExpandBands(parameters, bands);

        var scale = (bands.Length - 1) / (MathF.Sqrt(2) + 1e-6f);
        var columnStep = scale / (columns - 1);
        var rowStep = scale / (rows - 1);
        for (var y = 0; y < rows; y++)
        {
            var dy = y * rowStep;
            for (var x = 0; x < columns; x++)
            {
                var dx = x * columnStep;
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                weights[(y * columns) + x] = bands.Length == 1 ? bands[0] : Interpolate(distance, bands);
            }
        }
    }

    private static void ExpandBands(ReadOnlySpan<float> parameters, Span<float> bands)
    {
        bands[0] = parameters[0];
        if (bands[0] < AlmostZero)
            throw JpegXlFormat.Invalid("a band of a quantization table is too small.");

        for (var i = 1; i < parameters.Length; i++)
        {
            // A positive ratio multiplies by 1 + ratio; a negative one divides by 1 - ratio
            var ratio = parameters[i];
            bands[i] = bands[i - 1] * (ratio > 0 ? 1 + ratio : 1 / (1 - ratio));
            if (!(bands[i] >= AlmostZero))
                throw JpegXlFormat.Invalid("a band of a quantization table is too small.");
        }
    }

    private static float Interpolate(float position, ReadOnlySpan<float> bands)
    {
        var index = Math.Min((int)position, bands.Length - 2);
        var a = bands[index];
        var b = bands[index + 1];
        return a * MathF.Pow(b / a, position - index);
    }

    private static void ComputeAfvWeights(Encoding encoding, int channel, Span<float> weights)
    {
        // The frequencies of the coefficients of the AFV transform of 4x4 samples; the first ones have their own weights
        ReadOnlySpan<float> frequencies =
        [
            0, 0, 0.8517778890324296f, 5.37778436506804f,
            0, 0, 4.734747904497923f, 5.449245381693219f,
            1.6598270267479331f, 4, 7.275749096817861f, 10.423227632456525f,
            2.662932286148962f, 7.630657783650829f, 8.962388608184032f, 12.97166202570235f,
        ];
        const float Lowest = 0.8517778890324296f;
        const float Range = 12.97166202570235f - Lowest + 1e-6f;

        var values = encoding.Weights![channel];
        Span<float> wide = stackalloc float[32];
        Span<float> small = stackalloc float[16];
        ComputeBandWeights(encoding.Bands![channel], 4, 8, wide);
        ComputeBandWeights(encoding.SecondBands![channel], 4, 4, small);

        Span<float> bands = stackalloc float[4];
        ExpandBands(values.AsSpan(5, 4), bands);

        weights[0] = 1;
        weights[8] = values[0];
        weights[1] = values[1];
        weights[16] = values[2];
        weights[2] = values[3];
        weights[18] = values[4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                if (x < 2 && y < 2)
                    continue;

                var position = (frequencies[(y * 4) + x] - Lowest) * 3 / Range;
                weights[(2 * y * 8) + (2 * x)] = Interpolate(position, bands);
            }
        }

        // The coefficients of the DCT of 4x8 samples are in the odd rows, those of the DCT of 4x4 samples in the odd columns
        // of the even rows
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                weights[(((2 * y) + 1) * 8) + x] = wide[(y * 8) + x];
            }

            for (var x = 0; x < 4; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                weights[(2 * y * 8) + (2 * x) + 1] = small[(y * 4) + x];
            }
        }
    }

    private static Encoding[] CreateLibrary()
    {
        float[][] dct4x8Bands =
        [
            [2198.050556016380522f, -0.96269623020744692f, -0.76194253026666783f, -0.6551140670773547f],
            [764.3655248643528689f, -0.92630200888366945f, -0.9675229603596517f, -0.27845290869168118f],
            [527.107573587542228f, -1.4594385811273854f, -1.450082094097871593f, -1.5843722511996204f],
        ];
        float[][] dct4Bands = [[2200, 0, 0, 0], [392, 0, 0, 0], [112, -0.25f, -0.25f, -0.5f]];

        return
        [
            Dct([3150, 0, -0.4f, -0.4f, -0.4f, -2], [560, 0, -0.3f, -0.3f, -0.3f, -0.3f], [512, -2, -1, 0, -1, -2]),
            new Encoding(ModeIdentity) { Weights = [[280, 3160, 3160], [60, 864, 864], [18, 200, 200]] },
            new Encoding(ModeDct2) { Weights = [[3840, 2560, 1280, 640, 480, 300], [960, 640, 320, 180, 140, 120], [640, 320, 128, 64, 32, 16]] },
            new Encoding(ModeDct4) { Weights = [[1, 1], [1, 1], [1, 1]], Bands = dct4Bands },
            Dct(
                [8996.8725711814115328f, -1.3000777393353804f, -0.49424529824571225f, -0.439093774457103443f, -0.6350101832695744f, -0.90177264050827612f, -1.6162099239887414f],
                [3191.48366296844234752f, -0.67424582104194355f, -0.80745813428471001f, -0.44925837484843441f, -0.35865440981033403f, -0.31322389111877305f, -0.37615025315725483f],
                [1157.50408145487200256f, -2.0531423165804414f, -1.4f, -0.50687130033378396f, -0.42708730624733904f, -1.4856834539296244f, -4.9209142884401604f]),
            Dct(
                [15718.40830982518931456f, -1.025f, -0.98f, -0.9012f, -0.4f, -0.48819395464f, -0.421064f, -0.27f],
                [7305.7636810695983104f, -0.8041958212306401f, -0.7633036457487539f, -0.55660379990111464f, -0.49785304658857626f, -0.43699592683512467f, -0.40180866526242109f, -0.27321683125358037f],
                [3803.53173721215041536f, -3.060733579805728f, -2.0413270132490346f, -2.0235650159727417f, -0.5495389509954993f, -0.4f, -0.4f, -0.3f]),
            Dct(
                [7240.7734393502f, -0.7f, -0.7f, -0.2f, -0.2f, -0.2f, -0.5f],
                [1448.15468787004f, -0.5f, -0.5f, -0.5f, -0.2f, -0.2f, -0.2f],
                [506.854140754517f, -1.4f, -0.2f, -0.5f, -0.5f, -1.5f, -3.6f]),
            Dct(
                [16283.2494710648897f, -1.7812845336559429f, -1.6309059012653515f, -1.0382179034313539f, -0.85f, -0.7f, -0.9f, -1.2360638576849587f],
                [5089.15750884921511936f, -0.320049391452786891f, -0.35362849922161446f, -0.30340000000000003f, -0.61f, -0.5f, -0.5f, -0.6f],
                [3397.77603275308720128f, -0.321327362693153371f, -0.34507619223117997f, -0.70340000000000003f, -0.9f, -1.0f, -1.0f, -1.1754605576265209f]),
            Dct(
                [13844.97076442300573f, -0.97113799999999995f, -0.658f, -0.42026f, -0.22712f, -0.2206f, -0.226f, -0.6f],
                [4798.964084220744293f, -0.61125308982767057f, -0.83770786552491361f, -0.79014862079498627f, -0.2692727459704829f, -0.38272769465388551f, -0.22924222653091453f, -0.20719098826199578f],
                [1807.236946760964614f, -1.2f, -1.2f, -0.7f, -0.7f, -0.7f, -0.4f, -0.5f]),
            new Encoding(ModeDct4x8) { Weights = [[1], [1], [1]], Bands = dct4x8Bands },
            new Encoding(ModeAfv)
            {
                Weights = [[3072, 3072, 256, 256, 256, 414, 0, 0, 0], [1024, 1024, 50, 50, 50, 58, 0, 0, 0], [384, 384, 12, 12, 12, 22, -0.25f, -0.25f, -0.25f]],
                Bands = dct4x8Bands,
                SecondBands = dct4Bands,
            },
            LargeSquare(0.9f),
            LargeRectangle(0.65f),
            LargeSquare(1.8f),
            LargeRectangle(1.3f),
            LargeSquare(3.6f),
            LargeRectangle(2.6f),
        ];

        static Encoding Dct(float[] x, float[] y, float[] b) => new(ModeDct) { Bands = [x, y, b] };

        // The transforms of 64 samples and more share the ratios of their bands
        static Encoding LargeSquare(float scale) => Large(scale * 26629.073922049845f, scale * 9311.3238710010046f, scale * 4992.2486445538634f);

        static Encoding LargeRectangle(float scale) => Large(scale * 23629.073922049845f, scale * 8611.3238710010046f, scale * 4492.2486445538634f);

        static Encoding Large(float x, float y, float b) => Dct(
            [x, -1.025f, -0.78f, -0.65012f, -0.19041574084286472f, -0.20819395464f, -0.421064f, -0.32733845535848671f],
            [y, -0.3041958212306401f, -0.3633036457487539f, -0.35660379990111464f, -0.3443074455424403f, -0.33699592683512467f, -0.30180866526242109f, -0.27321683125358037f],
            [b, -1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f]);
    }

    /// <summary>The description of one quantization table.</summary>
    private sealed class Encoding
    {
        public Encoding(int mode) => Mode = mode;

        public int Mode { get; }

        /// <summary>Gets, for each channel, the weights or the multipliers of the mode.</summary>
        public float[][]? Weights { get; init; }

        /// <summary>Gets, for each channel, the bands of the function of the frequency.</summary>
        public float[][]? Bands { get; init; }

        /// <summary>Gets, for each channel, the bands of the DCT of 4x4 samples of the AFV transform.</summary>
        public float[][]? SecondBands { get; init; }

        public float RawDenominator { get; init; }

        /// <summary>Gets, for each channel, the values of a table stored as an image.</summary>
        public int[][]? RawValues { get; init; }
    }
}
