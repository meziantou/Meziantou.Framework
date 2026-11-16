using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The raw image of a DNG file before it is demosaiced: the samples it stores (the values of the sensor, a color filter
/// array or linear raw data), the parameters that map them to linear reference values (DNG specification 1.7.1, chapter
/// 5), and those values. <see cref="Image.Load(string, ImageDecodeOptions?)"/> renders the same samples to an image; this
/// type gives what precedes the demosaicing, which does not depend on the algorithm of a reader.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CopyStoredRow"/> gives the stored samples of the whole raw image after opcode list 1 (stage 1 of the DNG
/// processing model): integers, or the values of floating-point samples. <see cref="CopyLinearRow"/> gives the samples of
/// the active area mapped to linear reference values and clipped to [0, 1], after opcode list 2 (stage 2): 0 is no light
/// and 1 the white level.
/// </para>
/// <para>
/// Loading reads the whole input into memory (up to <see cref="ImageResourceLimits.MaxEncodedBytes"/>) and decodes the
/// samples once; they are held in memory charged to the limits of the configuration until the instance is disposed.
/// </para>
/// </remarks>
public sealed class DngRawImage : IDisposable
{
    private readonly DngRawDescriptor _raw;
    private DngPlanes? _stored;
    private DngPlanes? _linear;

    private DngRawImage(DngRawDescriptor raw, DngPlanes stored, DngPlanes linear)
    {
        _raw = raw;
        _stored = stored;
        _linear = linear;
        CfaPlaneColors = Array.AsReadOnly(raw.PlaneColors);
        BlackLevels = Array.AsReadOnly(raw.BlackLevels);
        WhiteLevels = Array.AsReadOnly(raw.WhiteLevels);
        BlackLevelDeltaH = raw.BlackLevelDeltaH is { } h ? Array.AsReadOnly(h) : null;
        BlackLevelDeltaV = raw.BlackLevelDeltaV is { } v ? Array.AsReadOnly(v) : null;
        LinearizationTable = raw.LinearizationTable is { } table ? Array.AsReadOnly(table) : null;
    }

    /// <summary>Gets the size of the stored raw image (<c>ImageWidth</c> and <c>ImageLength</c> of the raw IFD).</summary>
    public Size Size => _raw.Size;

    /// <summary>Gets the active area: the rectangle of the stored image that holds the image, outside its masked pixels (<c>ActiveArea</c>).</summary>
    public Rectangle ActiveArea => _raw.ActiveArea;

    /// <summary>Gets the default crop, relative to the top-left corner of the active area (<c>DefaultCropOrigin</c> and <c>DefaultCropSize</c>, rounded to whole pixels): the area of the rendered image.</summary>
    public Rectangle DefaultCrop => _raw.DefaultCrop;

    /// <summary>Gets the number of samples of a pixel: 1 for a color filter array, 1 to 4 for linear raw data.</summary>
    public int SamplesPerPixel => _raw.SamplesPerPixel;

    /// <summary>Gets the number of bits of a stored sample (8 to 16 for integers; 16, 24 or 32 for floating-point numbers).</summary>
    public int BitsPerSample => _raw.Page.BitsPerSample;

    /// <summary>Gets a value indicating whether the stored samples are floating-point numbers.</summary>
    public bool IsFloatingPoint => _raw.IsFloatingPoint;

    /// <summary>Gets a value indicating whether the samples are those of a color filter array (<c>CFA</c>), rather than linear raw data (<c>LinearRaw</c>).</summary>
    public bool IsColorFilterArray => _raw.IsCfa;

    /// <summary>Gets the number of rows of the repeating pattern of the color filter array (1 for linear raw data).</summary>
    public int CfaPatternRows => _raw.PatternRows;

    /// <summary>Gets the number of columns of the repeating pattern of the color filter array (1 for linear raw data).</summary>
    public int CfaPatternColumns => _raw.PatternColumns;

    /// <summary>
    /// Gets the CFA colors of the planes of the camera color space (<c>CFAPlaneColor</c>): 0 red, 1 green, 2 blue, 3 cyan,
    /// 4 magenta, 5 yellow, 6 white. Empty for linear raw data.
    /// </summary>
    public ReadOnlyCollection<byte> CfaPlaneColors { get; }

    /// <summary>Gets the linearization table (<c>LinearizationTable</c>), or <see langword="null"/>.</summary>
    public ReadOnlyCollection<ushort>? LinearizationTable { get; }

    /// <summary>Gets the number of rows of the repeating pattern of the black levels (<c>BlackLevelRepeatDim</c>).</summary>
    public int BlackLevelRepeatRows => _raw.BlackLevelRows;

    /// <summary>Gets the number of columns of the repeating pattern of the black levels (<c>BlackLevelRepeatDim</c>).</summary>
    public int BlackLevelRepeatColumns => _raw.BlackLevelColumns;

    /// <summary>
    /// Gets the black levels of the repeating pattern (<c>BlackLevel</c>), in row, column and sample order; the origin of the
    /// pattern is the top-left corner of the active area.
    /// </summary>
    public ReadOnlyCollection<double> BlackLevels { get; }

    /// <summary>Gets the black level added for each column of the active area (<c>BlackLevelDeltaH</c>), or <see langword="null"/>.</summary>
    public ReadOnlyCollection<double>? BlackLevelDeltaH { get; }

    /// <summary>Gets the black level added for each row of the active area (<c>BlackLevelDeltaV</c>), or <see langword="null"/>.</summary>
    public ReadOnlyCollection<double>? BlackLevelDeltaV { get; }

    /// <summary>Gets the white level of each sample (<c>WhiteLevel</c>).</summary>
    public ReadOnlyCollection<double> WhiteLevels { get; }

    /// <summary>Loads the raw image of a DNG file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The raw image, owned by the caller.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="UnknownImageFormatException">The file is not a DNG file.</exception>
    /// <exception cref="InvalidImageContentException">The file is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The raw image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static DngRawImage Load(string path, ImageConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        configuration ??= ImageConfiguration.Default;
        using var stream = ImageIO.OpenFile(path, asynchronous: false);
        return Load(stream, configuration);
    }

    /// <summary>Loads the raw image of a DNG file from a stream, read from its current position to its end.</summary>
    /// <param name="stream">The stream; it is not closed.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The raw image, owned by the caller.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The data is not a DNG file.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The raw image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static DngRawImage Load(Stream stream, ImageConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream is not readable.", nameof(stream));

        configuration ??= ImageConfiguration.Default;

        // The input buffer of Image.Load: rented from the allocation scope, bounded by MaxEncodedBytes
        var context = new ImageCodecContext(configuration, "DngRawImage", CancellationToken.None);
        using var input = new ImageInputBuffer(stream, ownsStream: false, context);
        if (ImageInputPump.Detect(ImageCodecRegistry.For(configuration), input, context).Format != ImageFormat.Tiff)
            throw CreateNotDngException();

        using var parser = new WholeInputParser<DngRawImage>(context, ImageFormat.Dng, source => Decode(source, context));
        return ImageInputPump.Run(input, parser, context);
    }

    /// <summary>Loads the raw image of DNG data.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The raw image, owned by the caller.</returns>
    /// <exception cref="UnknownImageFormatException">The data is not a DNG file.</exception>
    /// <exception cref="InvalidImageContentException">The data is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The raw image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static DngRawImage Load(ReadOnlySpan<byte> data, ImageConfiguration? configuration = null)
    {
        configuration ??= ImageConfiguration.Default;
        if (!TiffHeader.MatchesSignature(data) || Image.DetectFormat(data) != ImageFormat.Dng)
            throw CreateNotDngException();

        var context = new ImageCodecContext(configuration, "DngRawImage", CancellationToken.None);
        using var parser = new WholeInputParser<DngRawImage>(context, ImageFormat.Dng, source => Decode(source, context));
        return ImageInputPump.Run(data, parser, context);
    }

    /// <summary>Gets the CFA color of a pixel, from its coordinates relative to the top-left corner of the active area (the origin of the pattern).</summary>
    /// <param name="x">The column, relative to the active area.</param>
    /// <param name="y">The row, relative to the active area.</param>
    /// <returns>The CFA color (an entry of <see cref="CfaPlaneColors"/>).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="x"/> or <paramref name="y"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The samples are linear raw data, which has no color filter array.</exception>
    public byte GetCfaColor(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (!_raw.IsCfa)
            throw new InvalidOperationException("Linear raw data has no color filter array.");

        return _raw.PlaneColors[_raw.GetPlane(x, y)];
    }

    /// <summary>
    /// Copies a row of the stored samples of the whole raw image, after opcode list 1 (stage 1): the integer values the file
    /// stores, or the values of its floating-point samples, <see cref="SamplesPerPixel"/> per pixel.
    /// </summary>
    /// <param name="y">The row, from 0 to the height of <see cref="Size"/>.</param>
    /// <param name="destination">The destination, at least <c>Size.Width * SamplesPerPixel</c> values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void CopyStoredRow(int y, Span<float> destination)
    {
        var stored = _stored ?? throw new ObjectDisposedException(nameof(DngRawImage));
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, stored.Height);
        var row = stored.GetRow(y);
        if (destination.Length < row.Length)
            throw new ArgumentException("The destination is shorter than a row.", nameof(destination));

        for (var i = 0; i < row.Length; i++)
        {
            destination[i] = _raw.IsFloatingPoint ? row[i] : DngSampleReader.Denormalize(row[i]);
        }
    }

    /// <summary>
    /// Copies a row of the active area mapped to linear reference values, after opcode list 2 (stage 2): 0 is no light and 1
    /// the white level, <see cref="SamplesPerPixel"/> values per pixel, clipped to [0, 1].
    /// </summary>
    /// <param name="y">The row, relative to the active area.</param>
    /// <param name="destination">The destination, at least <c>ActiveArea.Width * SamplesPerPixel</c> values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void CopyLinearRow(int y, Span<float> destination)
    {
        var linear = _linear ?? throw new ObjectDisposedException(nameof(DngRawImage));
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, linear.Height);
        var row = linear.GetRow(y);
        if (destination.Length < row.Length)
            throw new ArgumentException("The destination is shorter than a row.", nameof(destination));

        row.CopyTo(destination);
    }

    /// <summary>Releases the samples.</summary>
    public void Dispose()
    {
        _stored?.Dispose();
        _stored = null;
        _linear?.Dispose();
        _linear = null;
        _raw.Dispose();
    }

    private static UnknownImageFormatException CreateNotDngException() => new("The data is not a DNG file: a TIFF file whose first image file directory has a DNGVersion tag.");

    private static DngRawImage Decode(RandomAccessSource source, ImageCodecContext context)
    {
        try
        {
            var header = TiffHeader.Read(source);
            var ifd0 = TiffDirectory.Read(source, header, header.FirstDirectoryOffset);
            if (!DngDocument.IsDng(ifd0))
                throw CreateNotDngException();

            var document = DngDocument.Read(source, header, ifd0, context);
            var raw = DngRawDescriptor.Resolve(document.RawImage, context);
            DngPlanes? stored = null;
            try
            {
                stored = DngPipeline.ReadStage1(source, raw, context);
                var linear = DngPipeline.Linearize(stored, raw, context);
                return new DngRawImage(raw, stored, linear);
            }
            catch
            {
                stored?.Dispose();
                raw.Dispose();
                throw;
            }
        }
        catch (InvalidImageContentException exception) when (exception.Format == ImageFormat.Tiff)
        {
            throw new InvalidImageContentException(exception.Message, ImageFormat.Dng, exception);
        }
    }
}
