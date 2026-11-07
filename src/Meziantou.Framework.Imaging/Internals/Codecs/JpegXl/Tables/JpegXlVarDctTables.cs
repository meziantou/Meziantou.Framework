namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the VarDCT mode (ISO/IEC 18181-1, annex I and section C.8): the 27 transform types, the quantization
/// table and the coefficient order each one uses, the contexts of the coefficients, and the basis of the AFV transform.
/// </summary>
/// <remarks>
/// The standard is not publicly available: the values were taken from libjxl 0.12.0 (<c>ac_strategy.h</c>,
/// <c>ac_context.h</c>, <c>quant_weights.h</c>, <c>dec_transforms-inl.h</c>).
/// </remarks>
internal static class JpegXlVarDctTables
{
    /// <summary>The number of transform types.</summary>
    public const int TransformCount = 27;

    /// <summary>The number of quantization tables: transforms of the same size, transposed or not, share one.</summary>
    public const int QuantTableCount = 17;

    /// <summary>The number of coefficient orders.</summary>
    public const int OrderCount = 13;

    /// <summary>The transform of 8x8 samples by a DCT.</summary>
    public const int TransformDct8 = 0;

    /// <summary>The transform that stores the samples of an 8x8 block themselves ("Hornuss").</summary>
    public const int TransformIdentity = 1;

    /// <summary>The transform of an 8x8 block by nested 2x2 transforms.</summary>
    public const int TransformDct2 = 2;

    /// <summary>The transform of an 8x8 block by four 4x4 DCTs.</summary>
    public const int TransformDct4 = 3;

    /// <summary>The transform of an 8x8 block by two DCTs of 4 rows and 8 columns.</summary>
    public const int TransformDct4x8 = 12;

    /// <summary>The transform of an 8x8 block by two DCTs of 8 rows and 4 columns.</summary>
    public const int TransformDct8x4 = 13;

    /// <summary>The first of the four AFV transforms, which differ by the corner of the 8x8 block that holds the AFV part.</summary>
    public const int TransformAfv0 = 14;

    /// <summary>The last of the four AFV transforms.</summary>
    public const int TransformAfv3 = 17;

    /// <summary>The number of contexts of the number of non-zero coefficients, for each block context.</summary>
    public const int NonZeroContextCount = 37;

    /// <summary>The number of contexts of a coefficient, for each block context.</summary>
    public const int CoefficientContextCount = 458;

    /// <summary>The number of contexts of one block context.</summary>
    public const int ContextsPerBlockContext = NonZeroContextCount + CoefficientContextCount;

    /// <summary>Gets the height of each transform, in blocks of 8 samples.</summary>
    public static ReadOnlySpan<byte> BlocksHigh => [1, 1, 1, 1, 2, 4, 2, 1, 4, 1, 4, 2, 1, 1, 1, 1, 1, 1, 8, 8, 4, 16, 16, 8, 32, 32, 16];

    /// <summary>Gets the width of each transform, in blocks of 8 samples.</summary>
    public static ReadOnlySpan<byte> BlocksWide => [1, 1, 1, 1, 2, 4, 1, 2, 1, 4, 2, 4, 1, 1, 1, 1, 1, 1, 8, 4, 8, 16, 8, 16, 32, 16, 32];

    /// <summary>Gets the quantization table of each transform.</summary>
    public static ReadOnlySpan<byte> QuantTable => [0, 1, 2, 3, 4, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 10, 10, 11, 12, 12, 13, 14, 14, 15, 16, 16];

    /// <summary>Gets the coefficient order of each transform.</summary>
    public static ReadOnlySpan<byte> Order => [0, 1, 1, 1, 2, 3, 4, 4, 5, 5, 6, 6, 1, 1, 1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12];

    /// <summary>Gets the number of rows of each quantization table, in blocks of 8 coefficients: the smaller side of the transform.</summary>
    public static ReadOnlySpan<byte> QuantTableRows => [1, 1, 1, 1, 2, 4, 1, 1, 2, 1, 1, 8, 4, 16, 8, 32, 16];

    /// <summary>Gets the number of columns of each quantization table, in blocks of 8 coefficients: the larger side of the transform.</summary>
    public static ReadOnlySpan<byte> QuantTableColumns => [1, 1, 1, 1, 2, 4, 2, 4, 4, 1, 1, 8, 8, 16, 16, 32, 32];

    /// <summary>Gets the number of rows of the coefficients of each order, in blocks of 8 coefficients.</summary>
    public static ReadOnlySpan<byte> OrderRows => [1, 1, 2, 4, 1, 1, 2, 8, 4, 16, 8, 32, 16];

    /// <summary>Gets the number of columns of the coefficients of each order, in blocks of 8 coefficients.</summary>
    public static ReadOnlySpan<byte> OrderColumns => [1, 1, 2, 4, 2, 4, 4, 8, 8, 16, 16, 32, 32];

    /// <summary>
    /// Gets the default block contexts: for each channel (Y, X, B) and each order, the cluster of the contexts of its
    /// coefficients. The transforms larger than 32x32 share their contexts.
    /// </summary>
    public static ReadOnlySpan<byte> DefaultBlockContextMap =>
    [
        0, 1, 2, 2, 3, 3, 4, 5, 6, 6, 6, 6, 6,
        7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
        7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
    ];

    /// <summary>Gets the part of the context of a coefficient that depends on its position in the order, divided by the number of blocks of the transform.</summary>
    public static ReadOnlySpan<byte> CoefficientFrequencyContext =>
    [
        0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
        23, 23, 23, 23, 24, 24, 24, 24, 25, 25, 25, 25, 26, 26, 26, 26,
        27, 27, 27, 27, 28, 28, 28, 28, 29, 29, 29, 29, 30, 30, 30, 30,
    ];

    /// <summary>Gets the part of the context of a coefficient that depends on the number of non-zero coefficients that remain, divided by the number of blocks of the transform.</summary>
    public static ReadOnlySpan<byte> CoefficientNonZeroContext =>
    [
        0, 0, 31, 62, 62, 93, 93, 93, 93, 123, 123, 123, 123,
        152, 152, 152, 152, 152, 152, 152, 152, 180, 180, 180, 180, 180,
        180, 180, 180, 180, 180, 180, 180, 206, 206, 206, 206, 206, 206,
        206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
        206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
    ];

    /// <summary>
    /// Gets the basis of the AFV transform of 4x4 samples: for each of the 16 coefficients, its contribution to each of the
    /// 16 samples, in raster order.
    /// </summary>
    public static ReadOnlySpan<float> AfvBasis =>
    [
        0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f,
        0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f,
        0.87690293f, 0.220651811f, -0.101400504f, -0.101400504f, 0.220651811f, -0.101400504f, -0.101400504f, -0.101400504f,
        -0.101400504f, -0.101400504f, -0.101400504f, -0.101400504f, -0.101400504f, -0.101400504f, -0.101400504f, -0.101400504f,
        0f, 0f, 0.406700758f, 0.444448166f, 0f, 0f, 0.195743994f, 0.292910014f,
        -0.406700758f, -0.195743994f, 0f, 0.113790745f, -0.444448166f, -0.292910014f, -0.113790745f, 0f,
        0f, 0f, -0.212557481f, 0.308549706f, 0f, 0.470670226f, -0.16212052f, 0f,
        -0.212557481f, -0.16212052f, -0.470670226f, -0.146429187f, 0.308549706f, 0f, -0.146429187f, 0.425114961f,
        0f, -0.707106781f, 0f, 0f, 0.707106781f, 0f, 0f, 0f,
        0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        -0.410537759f, 0.623548537f, -0.0643507166f, -0.0643507166f, 0.623548537f, -0.0643507166f, -0.0643507166f, -0.0643507166f,
        -0.0643507166f, -0.0643507166f, -0.0643507166f, -0.0643507166f, -0.0643507166f, -0.0643507166f, -0.0643507166f, -0.0643507166f,
        0f, 0f, -0.451755659f, 0.158545036f, 0f, -0.0403851516f, 0.00741822638f, 0.393510343f,
        -0.451755659f, 0.00741822638f, 0.110741658f, 0.0829816309f, 0.158545036f, 0.393510343f, 0.0829816309f, -0.451755659f,
        0f, 0f, -0.304684751f, 0.511261614f, 0f, 0f, -0.29048013f, -0.0657870155f,
        0.304684751f, 0.29048013f, 0f, -0.238897735f, -0.511261614f, 0.0657870155f, 0.238897735f, 0f,
        0f, 0f, 0.301792952f, 0.257923628f, 0f, 0.162723401f, 0.0952002265f, 0f,
        0.301792952f, 0.0952002265f, -0.162723401f, -0.353123854f, 0.257923628f, 0f, -0.353123854f, -0.603585903f,
        0f, 0f, 0.40824829f, 0f, 0f, 0f, 0f, -0.40824829f,
        -0.40824829f, 0f, 0f, -0.40824829f, 0f, 0.40824829f, 0.40824829f, 0f,
        0f, 0f, 0.174786698f, 0.0812611177f, 0f, 0f, -0.367539801f, -0.307882214f,
        -0.174786698f, 0.367539801f, 0f, 0.482668912f, -0.0812611177f, 0.307882214f, -0.482668912f, 0f,
        0f, 0f, -0.21105601f, 0.185671809f, 0f, 0f, 0.49215859f, -0.385250137f,
        0.21105601f, -0.49215859f, 0f, 0.174194127f, -0.185671809f, 0.385250137f, -0.174194127f, 0f,
        0f, 0f, -0.142660848f, -0.341644684f, 0f, 0.736749754f, 0.246271077f, -0.0857401904f,
        -0.142660848f, 0.246271077f, 0.148833992f, -0.0476868035f, -0.341644684f, -0.0857401904f, -0.0476868035f, -0.142660848f,
        0f, 0f, -0.138135404f, 0.330228255f, 0f, 0.08755115f, -0.0794670661f, -0.461337489f,
        -0.138135404f, -0.0794670661f, 0.497246471f, 0.125380594f, 0.330228255f, -0.461337489f, 0.125380594f, -0.138135404f,
        0f, 0f, -0.174376026f, 0.0702790691f, 0f, -0.292102664f, 0.362381733f, 0f,
        -0.174376026f, 0.362381733f, 0.292102664f, -0.432660802f, 0.0702790691f, 0f, -0.432660802f, 0.348752052f,
        0f, 0f, 0.113549873f, -0.074175046f, 0f, 0.19402893f, -0.435190497f, 0.219186848f,
        0.113549873f, -0.435190497f, 0.555044381f, -0.254682771f, -0.074175046f, 0.219186848f, -0.254682771f, 0.113549873f,
    ];
}
