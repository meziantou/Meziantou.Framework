using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Barcodes.Internal.MicroQR;
using Meziantou.Framework.Barcodes.Internal.RMQR;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a generated QR code as a boolean matrix of modules.
/// </summary>
public sealed class QRCode
{
    private readonly bool[,] _modules;

    internal QRCode(bool[,] modules, int version, QRCodeType type, ErrorCorrectionLevel errorCorrectionLevel)
    {
        _modules = modules;
        Version = version;
        Type = type;
        ErrorCorrectionLevel = errorCorrectionLevel;
        Height = modules.GetLength(0);
        Width = modules.GetLength(1);
    }

    /// <summary>Gets the type of QR code.</summary>
    public QRCodeType Type { get; }

    /// <summary>Gets the error correction level the symbol was built with.</summary>
    /// <remarks>
    /// This can differ from the requested level. Micro QR version M1 carries error detection only,
    /// and is reported as <see cref="ErrorCorrectionLevel.L"/> because the enumeration has no
    /// weaker member.
    /// </remarks>
    public ErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>Gets the QR code version.</summary>
    public int Version { get; }

    /// <summary>Gets the width (number of columns).</summary>
    public int Width { get; }

    /// <summary>Gets the height (number of rows).</summary>
    public int Height { get; }

    /// <summary>Gets the side length for square QR codes (same as <see cref="Width"/>).</summary>
    public int Size => Width;

    /// <summary>
    /// Gets whether the module at the specified position is dark.
    /// </summary>
    /// <param name="row">The row index (0-based).</param>
    /// <param name="column">The column index (0-based).</param>
    /// <returns><see langword="true"/> if the module is dark; otherwise, <see langword="false"/>.</returns>
    public bool this[int row, int column] => _modules[row, column];

    /// <summary>
    /// Creates a standard QR code from the specified text data.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    public static QRCode Create(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return Create(data, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a standard QR code from the specified text data.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <remarks>
    /// The text is encoded as UTF-8, without an ECI designator unless <see cref="QRCodeOptions.EciAssignmentNumber"/> is
    /// <see cref="QRCodeOptions.Utf8EciAssignmentNumber"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="ArgumentException"><see cref="QRCodeOptions.EciAssignmentNumber"/> is not <see langword="null"/> or <see cref="QRCodeOptions.Utf8EciAssignmentNumber"/>.</exception>
    public static QRCode Create(string data, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        ValidateErrorCorrectionLevel(errorCorrectionLevel);

        var eci = options?.EciAssignmentNumber;
        if (eci is not (null or QRCodeOptions.Utf8EciAssignmentNumber))
        {
            throw new ArgumentException($"A text is encoded as UTF-8, so its ECI assignment number must be {QRCodeOptions.Utf8EciAssignmentNumber}. Encode the text yourself and pass its bytes to declare another assignment number.", nameof(options));
        }

        // With an ECI designator, the encoding of the bytes is explicit: readers do not guess it, and Kanji mode is not used
        var (segments, version) = eci is null
            ? QRSegmentOptimizer.Optimize(data, errorCorrectionLevel, minVersion: 1, maxVersion: 40, headerBitCount: 0, allowSegments: DataAnalyzer.CanSegmentWithoutEci(data), allowKanji: true)
            : QRSegmentOptimizer.Optimize(data, errorCorrectionLevel, minVersion: 1, maxVersion: 40, headerBitCount: DataEncoder.GetEciBitCount(eci.Value), allowSegments: true, allowKanji: false);
        var dataCodewords = DataEncoder.Encode(data, segments, version, errorCorrectionLevel, eci);
        var allCodewords = ErrorCorrectionEncoder.AddErrorCorrection(dataCodewords, version, errorCorrectionLevel);

        return BuildStandardMatrix(allCodewords, version, errorCorrectionLevel);
    }

    /// <summary>
    /// Creates a standard QR code from the specified binary data.
    /// </summary>
    /// <param name="data">The binary data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    public static QRCode Create(ReadOnlySpan<byte> data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return Create(data, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a standard QR code from the specified binary data.
    /// </summary>
    /// <param name="data">The binary data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <remarks>
    /// The bytes are encoded as they are, in byte mode, preceded by an ECI designator when <see cref="QRCodeOptions.EciAssignmentNumber"/> is set.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    public static QRCode Create(ReadOnlySpan<byte> data, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        ValidateErrorCorrectionLevel(errorCorrectionLevel);

        var eci = options?.EciAssignmentNumber;
        var headerBitCount = eci is null ? 0 : DataEncoder.GetEciBitCount(eci.Value);
        var version = DataAnalyzer.DetermineVersion(data.Length, errorCorrectionLevel, minVersion: 1, maxVersion: 40, headerBitCount);
        var dataCodewords = DataEncoder.Encode(data, version, errorCorrectionLevel, eci);
        var allCodewords = ErrorCorrectionEncoder.AddErrorCorrection(dataCodewords, version, errorCorrectionLevel);

        return BuildStandardMatrix(allCodewords, version, errorCorrectionLevel);
    }

    /// <summary>
    /// Creates a Micro QR code from the specified text data.
    /// Micro QR codes are smaller (versions M1-M4, 11x11 to 17x17 modules) and have a single finder pattern.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level. M1 only supports detection; M2/M3 support L/M; M4 supports L/M/Q.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.L"/>, <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.Q"/>.</exception>
    public static QRCode CreateMicroQR(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.L)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        if (errorCorrectionLevel is not (ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCorrectionLevel), errorCorrectionLevel, "Micro QR only supports EC levels L, M and Q.");
        }

        return MicroQREncoder.Encode(data, errorCorrectionLevel);
    }

    /// <summary>
    /// Creates a Rectangular Micro QR code (rMQR) from the specified text data.
    /// rMQR codes are rectangular and optimized for narrow spaces.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level. Only M and H are supported.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.H"/>.</exception>
    public static QRCode CreateRMQR(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        if (errorCorrectionLevel is not (ErrorCorrectionLevel.M or ErrorCorrectionLevel.H))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCorrectionLevel), errorCorrectionLevel, "rMQR only supports EC levels M and H.");
        }

        return RMQREncoder.Encode(data, errorCorrectionLevel);
    }

    private static void ValidateErrorCorrectionLevel(ErrorCorrectionLevel errorCorrectionLevel)
    {
        if (errorCorrectionLevel is not (ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q or ErrorCorrectionLevel.H))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCorrectionLevel), errorCorrectionLevel, "The error correction level must be L, M, Q or H.");
        }
    }

    private static QRCode BuildStandardMatrix(byte[] allCodewords, int version, ErrorCorrectionLevel errorCorrectionLevel)
    {
        var modules = MaskEvaluator.BuildMatrix(allCodewords, version, errorCorrectionLevel);
        return new QRCode(modules, version, QRCodeType.Standard, errorCorrectionLevel);
    }
}
