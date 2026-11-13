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

    internal QRCode(bool[,] modules, int version, QRCodeType type, ErrorCorrectionLevel errorCorrectionLevel, QRCodeStructuredAppend? structuredAppend = null)
    {
        _modules = modules;
        Version = version;
        Type = type;
        ErrorCorrectionLevel = errorCorrectionLevel;
        StructuredAppend = structuredAppend;
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
    /// Gets the place of the symbol in a structured append sequence, or <see langword="null"/> when the symbol holds all
    /// of its data.
    /// </summary>
    /// <seealso cref="CreateStructuredAppend(string, ErrorCorrectionLevel, QRCodeOptions)"/>
    public QRCodeStructuredAppend? StructuredAppend { get; }

    /// <summary>
    /// Gets whether the module at the specified position is dark.
    /// </summary>
    /// <param name="row">The row index (0-based).</param>
    /// <param name="column">The column index (0-based).</param>
    /// <returns><see langword="true"/> if the module is dark; otherwise, <see langword="false"/>.</returns>
    public bool this[int row, int column] => _modules[row, column];

    /// <summary>Gets the modules, without copying them: the callers must not modify them.</summary>
    internal bool[,] Modules => _modules;

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
    /// <exception cref="ArgumentException"><see cref="QRCodeOptions.EciAssignmentNumber"/> is not <see langword="null"/> or <see cref="QRCodeOptions.Utf8EciAssignmentNumber"/>, or <see cref="QRCodeOptions.MinimumVersion"/> is greater than <see cref="QRCodeOptions.MaximumVersion"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed version.</exception>
    public static QRCode Create(string data, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        ValidateErrorCorrectionLevel(errorCorrectionLevel);

        var (minVersion, maxVersion) = GetVersionRange(options);
        var eci = GetTextEciAssignmentNumber(options);

        // With an ECI designator, the encoding of the bytes is explicit: readers do not guess it, and Kanji mode is not used
        var (segments, version) = eci is null
            ? QRSegmentOptimizer.Optimize(data, errorCorrectionLevel, minVersion, maxVersion, headerBitCount: 0, allowSegments: DataAnalyzer.CanSegmentWithoutEci(data), allowKanji: true)
            : QRSegmentOptimizer.Optimize(data, errorCorrectionLevel, minVersion, maxVersion, headerBitCount: DataEncoder.GetEciBitCount(eci.Value), allowSegments: true, allowKanji: false);
        var dataSegments = QRSegmentOptimizer.ToSegments(data, segments);
        return BuildStandardMatrix(eci is null ? dataSegments : [QRCodeSegment.CreateEci(eci.Value), .. dataSegments], version, errorCorrectionLevel);
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
    /// <exception cref="ArgumentException"><see cref="QRCodeOptions.MinimumVersion"/> is greater than <see cref="QRCodeOptions.MaximumVersion"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed version.</exception>
    public static QRCode Create(ReadOnlySpan<byte> data, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        ValidateErrorCorrectionLevel(errorCorrectionLevel);

        return CreateStandard(GetSegments(data, options), errorCorrectionLevel, options);
    }

    /// <summary>
    /// Creates a standard QR code from segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element, or has no segment of data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest version.</exception>
    public static QRCode Create(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return Create(segments, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a standard QR code from segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <remarks>
    /// The segments are encoded as they are, in order, preceded by an ECI designator when <see cref="QRCodeOptions.EciAssignmentNumber"/> is set.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs" ref:region="QRCode_Segments" ref:dedent="true">
    /// // An ISO-8859-1 text followed by a long number: the text in byte mode, after an ECI designator that declares its
    /// // encoding (assignment number 3), and the digits in numeric mode
    /// QRCodeSegment[] segments =
    /// [
    ///     QRCodeSegment.CreateEci(3),
    ///     QRCodeSegment.CreateByte(Encoding.Latin1.GetBytes("Facture n\u00B0")),
    ///     QRCodeSegment.CreateNumeric("12345678901234567890123456789012345"),
    /// ];
    /// var qrCode = QRCode.Create(segments, ErrorCorrectionLevel.M);
    ///
    /// // The same bytes in a single byte segment need a larger symbol
    /// var bytes = Encoding.Latin1.GetBytes("Facture n\u00B012345678901234567890123456789012345");
    /// var singleSegment = QRCode.Create(bytes, ErrorCorrectionLevel.M, new QRCodeOptions { EciAssignmentNumber = 3 });
    /// Assert.Equal((3, 4), (qrCode.Version, singleSegment.Version));
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element, or has no segment of data, or <see cref="QRCodeOptions.MinimumVersion"/> is greater than <see cref="QRCodeOptions.MaximumVersion"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed version.</exception>
    public static QRCode Create(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        var list = GetSegments(segments, options?.EciAssignmentNumber, nameof(segments));
        ValidateErrorCorrectionLevel(errorCorrectionLevel);
        return CreateStandard(list, errorCorrectionLevel, options);
    }

    /// <summary>
    /// Creates a structured append sequence: standard QR codes that each hold a part of a text, for data too large for one
    /// symbol or to print several smaller symbols.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>The symbols of the sequence, in order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in 16 symbols of the largest version.</exception>
    public static IReadOnlyList<QRCode> CreateStructuredAppend(string data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return CreateStructuredAppend(data, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a structured append sequence: standard QR codes that each hold a part of a text, for data too large for one
    /// symbol or to print several smaller symbols.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="options">
    /// The options, or <see langword="null"/> for the defaults. <see cref="QRCodeOptions.MaximumVersion"/> sets the largest
    /// symbols, and <see cref="QRCodeOptions.MinimumVersion"/> the smallest ones.
    /// </param>
    /// <returns>The symbols of the sequence, in order.</returns>
    /// <remarks>
    /// <para>
    /// The sequence has the fewest symbols that a version from <see cref="QRCodeOptions.MinimumVersion"/> to
    /// <see cref="QRCodeOptions.MaximumVersion"/> gives, up to 16, then every symbol has the smallest version that gives as
    /// few symbols. Each symbol is split into segments of different
    /// modes like a symbol of <see cref="Create(string, ErrorCorrectionLevel, QRCodeOptions)"/>, never in the middle of a
    /// surrogate pair, and repeats the ECI designator of <see cref="QRCodeOptions.EciAssignmentNumber"/>.
    /// </para>
    /// <para>
    /// Data that fits in one symbol gives a sequence of one symbol. Readers that support structured append put the data
    /// back together; other readers read each symbol alone.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs" ref:region="QRCode_StructuredAppend" ref:dedent="true">
    /// var data = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 30));
    ///
    /// // Symbols of version 10 at most (57 × 57 modules), instead of one symbol of version 30 (137 × 137 modules)
    /// var symbols = QRCode.CreateStructuredAppend(data, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 10 });
    /// Assert.Equal(7, symbols.Count);
    /// Assert.Equal((10, 30), (symbols[0].Version, QRCode.Create(data, ErrorCorrectionLevel.M).Version));
    ///
    /// // Each symbol knows its position in the sequence
    /// Assert.Equal((0, 7), (symbols[0].StructuredAppend!.Index, symbols[0].StructuredAppend!.Count));
    /// Assert.Equal((6, 7), (symbols[6].StructuredAppend!.Index, symbols[6].StructuredAppend!.Count));
    ///
    /// // Print the symbols in order, side by side
    /// var images = symbols.Select(symbol =&gt; symbol.ToPng(new QRCodePngOptions { ModuleSize = 4 })).ToList();
    /// </code>
    /// </example>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="ArgumentException"><see cref="QRCodeOptions.EciAssignmentNumber"/> is not <see langword="null"/> or <see cref="QRCodeOptions.Utf8EciAssignmentNumber"/>, or <see cref="QRCodeOptions.MinimumVersion"/> is greater than <see cref="QRCodeOptions.MaximumVersion"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in 16 symbols of the largest allowed version.</exception>
    public static IReadOnlyList<QRCode> CreateStructuredAppend(string data, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        ValidateErrorCorrectionLevel(errorCorrectionLevel);
        var (minVersion, maxVersion) = GetVersionRange(options);
        var eci = GetTextEciAssignmentNumber(options);
        return QRStructuredAppendEncoder.Encode(QRStructuredAppendEncoder.Plan(data, errorCorrectionLevel, minVersion, maxVersion, eci), errorCorrectionLevel);
    }

    /// <summary>
    /// Creates a structured append sequence: standard QR codes that each hold a part of binary data.
    /// </summary>
    /// <param name="data">The binary data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>The symbols of the sequence, in order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in 16 symbols of the largest version.</exception>
    public static IReadOnlyList<QRCode> CreateStructuredAppend(ReadOnlySpan<byte> data, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return CreateStructuredAppend(data, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a structured append sequence: standard QR codes that each hold a part of binary data.
    /// </summary>
    /// <param name="data">The binary data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The symbols of the sequence, in order.</returns>
    /// <remarks>
    /// The bytes are encoded as they are, in byte mode, and the symbols are chosen as by
    /// <see cref="CreateStructuredAppend(string, ErrorCorrectionLevel, QRCodeOptions)"/>. A symbol can end in the middle
    /// of a multi-byte character.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="ArgumentException"><see cref="QRCodeOptions.MinimumVersion"/> is greater than <see cref="QRCodeOptions.MaximumVersion"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in 16 symbols of the largest allowed version.</exception>
    public static IReadOnlyList<QRCode> CreateStructuredAppend(ReadOnlySpan<byte> data, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        var segments = GetSegments(data, options);
        ValidateErrorCorrectionLevel(errorCorrectionLevel);
        var (minVersion, maxVersion) = GetVersionRange(options);
        return QRStructuredAppendEncoder.Encode(QRStructuredAppendEncoder.Plan(segments, errorCorrectionLevel, minVersion, maxVersion), errorCorrectionLevel);
    }

    /// <summary>
    /// Creates a structured append sequence: standard QR codes that each hold a part of segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <returns>The symbols of the sequence, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element, or has no segment of data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in 16 symbols of the largest version.</exception>
    public static IReadOnlyList<QRCode> CreateStructuredAppend(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return CreateStructuredAppend(segments, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a structured append sequence: standard QR codes that each hold a part of segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The symbols of the sequence, in order.</returns>
    /// <remarks>
    /// A segment can be split across two symbols, between two characters (or two bytes in byte mode). Each symbol repeats
    /// the ECI designator in force at its start. The symbols are chosen as by
    /// <see cref="CreateStructuredAppend(string, ErrorCorrectionLevel, QRCodeOptions)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element, or has no segment of data, or <see cref="QRCodeOptions.MinimumVersion"/> is greater than <see cref="QRCodeOptions.MaximumVersion"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not a defined <see cref="Barcodes.ErrorCorrectionLevel"/> value.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in 16 symbols of the largest allowed version.</exception>
    public static IReadOnlyList<QRCode> CreateStructuredAppend(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        var list = GetSegments(segments, options?.EciAssignmentNumber, nameof(segments));
        ValidateErrorCorrectionLevel(errorCorrectionLevel);
        var (minVersion, maxVersion) = GetVersionRange(options);
        return QRStructuredAppendEncoder.Encode(QRStructuredAppendEncoder.Plan(list, errorCorrectionLevel, minVersion, maxVersion), errorCorrectionLevel);
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
        return CreateMicroQR(data, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a Micro QR code from the specified text data.
    /// Micro QR codes are smaller (versions M1-M4, 11x11 to 17x17 modules) and have a single finder pattern.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level. M1 only supports detection; M2/M3 support L/M; M4 supports L/M/Q.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.L"/>, <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.Q"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="MicroQRCodeOptions.MinimumVersion"/> is greater than <see cref="MicroQRCodeOptions.MaximumVersion"/>, or no
    /// version of this range supports <paramref name="errorCorrectionLevel"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed version, or has characters these versions cannot encode.</exception>
    public static QRCode CreateMicroQR(string data, ErrorCorrectionLevel errorCorrectionLevel, MicroQRCodeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        var (minVersion, maxVersion) = GetMicroQRVersionRange(errorCorrectionLevel, options);
        return MicroQREncoder.Encode(data, errorCorrectionLevel, minVersion, maxVersion);
    }

    /// <summary>
    /// Creates a Micro QR code from segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level. M1 only supports detection; M2/M3 support L/M; M4 supports L/M/Q.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element or an ECI designator, or has no segment of data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.L"/>, <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.Q"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest version, or has a mode these versions cannot encode.</exception>
    public static QRCode CreateMicroQR(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.L)
    {
        return CreateMicroQR(segments, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a Micro QR code from segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level. M1 only supports detection; M2/M3 support L/M; M4 supports L/M/Q.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <remarks>
    /// M1 holds a single numeric segment, M2 numeric and alphanumeric segments, M3 and M4 every mode. Micro QR codes do not
    /// have ECI designators.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="segments"/> has a <see langword="null"/> element or an ECI designator, or has no segment of data; or
    /// <see cref="MicroQRCodeOptions.MinimumVersion"/> is greater than <see cref="MicroQRCodeOptions.MaximumVersion"/>, or no
    /// version of this range supports <paramref name="errorCorrectionLevel"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.L"/>, <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.Q"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed version, or has a mode these versions cannot encode.</exception>
    public static QRCode CreateMicroQR(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel, MicroQRCodeOptions? options)
    {
        var list = GetMicroSegments(segments, "Micro QR", nameof(segments));
        var (minVersion, maxVersion) = GetMicroQRVersionRange(errorCorrectionLevel, options);
        return MicroQREncoder.Encode(list, errorCorrectionLevel, minVersion, maxVersion);
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
        return CreateRMQR(data, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a Rectangular Micro QR code (rMQR) from the specified text data.
    /// rMQR codes are rectangular and optimized for narrow spaces.
    /// </summary>
    /// <param name="data">The text data to encode.</param>
    /// <param name="errorCorrectionLevel">The error correction level. Only M and H are supported.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.H"/>.</exception>
    /// <exception cref="ArgumentException">No rMQR size satisfies the size constraints of <paramref name="options"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static QRCode CreateRMQR(string data, ErrorCorrectionLevel errorCorrectionLevel, RMQRCodeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        return RMQREncoder.Encode(data, errorCorrectionLevel, GetRMQRSizeConstraints(errorCorrectionLevel, options));
    }

    /// <summary>
    /// Creates a Rectangular Micro QR code (rMQR) from segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level. Only M and H are supported.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element or an ECI designator, or has no segment of data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.H"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest size.</exception>
    public static QRCode CreateRMQR(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M)
    {
        return CreateRMQR(segments, errorCorrectionLevel, options: null);
    }

    /// <summary>
    /// Creates a Rectangular Micro QR code (rMQR) from segments of data that you choose.
    /// </summary>
    /// <param name="segments">The segments, in order.</param>
    /// <param name="errorCorrectionLevel">The error correction level. Only M and H are supported.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A new <see cref="QRCode"/> instance.</returns>
    /// <remarks>rMQR codes do not have ECI designators.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> has a <see langword="null"/> element or an ECI designator, or has no segment of data; or no rMQR size satisfies the size constraints of <paramref name="options"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="errorCorrectionLevel"/> is not <see cref="ErrorCorrectionLevel.M"/> or <see cref="ErrorCorrectionLevel.H"/>.</exception>
    /// <exception cref="InvalidOperationException">The data does not fit in the largest allowed size.</exception>
    public static QRCode CreateRMQR(IEnumerable<QRCodeSegment> segments, ErrorCorrectionLevel errorCorrectionLevel, RMQRCodeOptions? options)
    {
        var list = GetMicroSegments(segments, "rMQR", nameof(segments));
        return RMQREncoder.Encode(list, errorCorrectionLevel, GetRMQRSizeConstraints(errorCorrectionLevel, options));
    }

    private static (int MinVersion, int MaxVersion) GetMicroQRVersionRange(ErrorCorrectionLevel errorCorrectionLevel, MicroQRCodeOptions? options)
    {
        if (errorCorrectionLevel is not (ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCorrectionLevel), errorCorrectionLevel, "Micro QR only supports EC levels L, M and Q.");
        }

        var minVersion = options?.MinimumVersion ?? 1;
        var maxVersion = options?.MaximumVersion ?? 4;
        if (minVersion > maxVersion)
        {
            throw new ArgumentException($"The minimum version (M{minVersion}) is greater than the maximum version (M{maxVersion}).", nameof(options));
        }

        if (!MicroQRVersion.IsECLevelSupported(minVersion, maxVersion, errorCorrectionLevel))
        {
            throw new ArgumentException($"No Micro QR version from M{minVersion} to M{maxVersion} supports the error correction level {errorCorrectionLevel}: M1 supports L (detection only), M2 and M3 support L and M, M4 supports L, M and Q.", nameof(options));
        }

        return (minVersion, maxVersion);
    }

    private static RMQRSizeConstraints GetRMQRSizeConstraints(ErrorCorrectionLevel errorCorrectionLevel, RMQRCodeOptions? options)
    {
        if (errorCorrectionLevel is not (ErrorCorrectionLevel.M or ErrorCorrectionLevel.H))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCorrectionLevel), errorCorrectionLevel, "rMQR only supports EC levels M and H.");
        }

        var constraints = new RMQRSizeConstraints(options?.Height, options?.Width, options?.MaximumHeight, options?.MaximumWidth);
        if (!RMQRVersion.HasVersion(constraints))
        {
            throw new ArgumentException($"No rMQR size satisfies the constraints ({constraints}).", nameof(options));
        }

        return constraints;
    }

    private static int? GetTextEciAssignmentNumber(QRCodeOptions? options)
    {
        var eci = options?.EciAssignmentNumber;
        if (eci is not (null or QRCodeOptions.Utf8EciAssignmentNumber))
        {
            throw new ArgumentException($"A text is encoded as UTF-8, so its ECI assignment number must be {QRCodeOptions.Utf8EciAssignmentNumber}. Encode the text yourself and pass its bytes to declare another assignment number.", nameof(options));
        }

        return eci;
    }

    private static QRCodeSegment[] GetSegments(ReadOnlySpan<byte> data, QRCodeOptions? options)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("The data cannot be empty.", nameof(data));
        }

        var segment = QRCodeSegment.CreateByte(data);
        return options?.EciAssignmentNumber is { } eci ? [QRCodeSegment.CreateEci(eci), segment] : [segment];
    }

    private static QRCodeSegment[] GetSegments(IEnumerable<QRCodeSegment> segments, int? eciAssignmentNumber, string paramName)
    {
        ArgumentNullException.ThrowIfNull(segments, paramName);

        var result = new List<QRCodeSegment>();
        if (eciAssignmentNumber is { } eci)
        {
            result.Add(QRCodeSegment.CreateEci(eci));
        }

        var hasData = false;
        foreach (var segment in segments)
        {
            if (segment is null)
                throw new ArgumentException("The segments cannot contain null.", paramName);

            hasData |= segment.EncodingMode is not EncodingMode.Eci;
            result.Add(segment);
        }

        if (!hasData)
            throw new ArgumentException("The segments must contain a segment of data, not only ECI designators.", paramName);

        return [.. result];
    }

    private static QRCodeSegment[] GetMicroSegments(IEnumerable<QRCodeSegment> segments, string symbolName, string paramName)
    {
        var result = GetSegments(segments, eciAssignmentNumber: null, paramName);
        if (result.Any(segment => segment.EncodingMode is EncodingMode.Eci))
            throw new ArgumentException($"{symbolName} codes do not support ECI designators.", paramName);

        return result;
    }

    private static QRCode CreateStandard(QRCodeSegment[] segments, ErrorCorrectionLevel errorCorrectionLevel, QRCodeOptions? options)
    {
        var (minVersion, maxVersion) = GetVersionRange(options);
        for (var version = minVersion; version <= maxVersion; version++)
        {
            var bitCount = QRSymbolFormat.GetBitCount(segments, QRCodeType.Standard, version);
            if (bitCount >= 0 && bitCount <= QRCodeVersion.GetDataCodewords(version, errorCorrectionLevel) * 8)
                return BuildStandardMatrix(segments, version, errorCorrectionLevel);
        }

        throw DataAnalyzer.CreateDataTooLongException(minVersion, maxVersion);
    }

    private static (int MinVersion, int MaxVersion) GetVersionRange(QRCodeOptions? options)
    {
        var minVersion = options?.MinimumVersion ?? 1;
        var maxVersion = options?.MaximumVersion ?? 40;
        if (minVersion > maxVersion)
        {
            throw new ArgumentException($"The minimum version ({minVersion}) is greater than the maximum version ({maxVersion}).", nameof(options));
        }

        return (minVersion, maxVersion);
    }

    private static void ValidateErrorCorrectionLevel(ErrorCorrectionLevel errorCorrectionLevel)
    {
        if (errorCorrectionLevel is not (ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q or ErrorCorrectionLevel.H))
        {
            throw new ArgumentOutOfRangeException(nameof(errorCorrectionLevel), errorCorrectionLevel, "The error correction level must be L, M, Q or H.");
        }
    }

    private static QRCode BuildStandardMatrix(QRCodeSegment[] segments, int version, ErrorCorrectionLevel errorCorrectionLevel)
    {
        var dataCodewords = DataEncoder.Encode(segments, version, errorCorrectionLevel, structuredAppend: null);
        var allCodewords = ErrorCorrectionEncoder.AddErrorCorrection(dataCodewords, version, errorCorrectionLevel);
        var modules = MaskEvaluator.BuildMatrix(allCodewords, version, errorCorrectionLevel);
        return new QRCode(modules, version, QRCodeType.Standard, errorCorrectionLevel);
    }
}
