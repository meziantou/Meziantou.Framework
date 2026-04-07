using Meziantou.Framework.Internal;
using Meziantou.Framework.Internal.MicroQR;
using Meziantou.Framework.Internal.RMQR;

namespace Meziantou.Framework;

/// <summary>
/// Represents a generated QR code as a boolean matrix of modules.
/// </summary>
public sealed class QRCode
{
    private readonly bool[,] _modules;

    internal QRCode(bool[,] modules, int version, QRCodeType type)
    {
        _modules = modules;
        Version = version;
        Type = type;
        Height = modules.GetLength(0);
        Width = modules.GetLength(1);
    }

    /// <summary>Gets the type of QR code.</summary>
    public QRCodeType Type { get; }

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
    public static QRCode Create(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        var mode = DataAnalyzer.DetermineMode(data);
        var version = DataAnalyzer.DetermineVersion(data, errorCorrectionLevel, mode);
        var dataCodewords = DataEncoder.Encode(data, version, errorCorrectionLevel, mode);
        var allCodewords = ErrorCorrectionEncoder.AddErrorCorrection(dataCodewords, version, errorCorrectionLevel);

        return BuildStandardMatrix(allCodewords, version, errorCorrectionLevel);
    }

    /// <summary>
    /// Creates a standard QR code from the specified binary data.
    /// </summary>
    /// <param name="data">The binary data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    public static QRCode Create(ReadOnlySpan<byte> data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        var version = DataAnalyzer.DetermineVersion(data.Length, errorCorrectionLevel, EncodingMode.Byte);
        var dataCodewords = DataEncoder.Encode(data, version, errorCorrectionLevel);
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
    public static QRCode CreateMicroQR(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.L)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
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
    public static QRCode CreateRMQR(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return RMQREncoder.Encode(data, errorCorrectionLevel);
    }

    private static QRCode BuildStandardMatrix(byte[] allCodewords, int version, ErrorCorrectionLevel errorCorrectionLevel)
    {
        var bestMask = MaskEvaluator.FindBestMask(allCodewords, version, errorCorrectionLevel);
        var builder = new MatrixBuilder(version);
        builder.Build(allCodewords, errorCorrectionLevel, bestMask);

        return new QRCode(builder.Modules, version, QRCodeType.Standard);
    }
}
