namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The samples of one decoded frame, before the color transform: the integer channels of its Modular data, and, for a
/// frame coded with VarDCT, the three color channels as floating-point samples.
/// </summary>
internal sealed class JpegXlDecodedFrame : IDisposable
{
    private readonly Av1PooledArray<float>?[] _planes = new Av1PooledArray<float>?[3];
    private readonly int[] _planeWidths = new int[3];
    private readonly int[] _planeHeights = new int[3];
    private readonly int[] _horizontalShifts = new int[3];
    private readonly int[] _verticalShifts = new int[3];

    public JpegXlDecodedFrame(JpegXlModularImage modular, int modularColorChannelCount)
    {
        ArgumentNullException.ThrowIfNull(modular);
        Modular = modular;
        ModularColorChannelCount = modularColorChannelCount;
    }

    /// <summary>Gets the Modular channels: the color channels of a Modular frame, then the extra channels.</summary>
    public JpegXlModularImage Modular { get; }

    /// <summary>Gets the number of color channels in <see cref="Modular"/>: none for a VarDCT frame.</summary>
    public int ModularColorChannelCount { get; }

    /// <summary>Gets the factors of the LF dequantization of the channels X, Y and B, which scale the samples of a Modular frame stored in XYB.</summary>
    public float[] LfQuant { get; init; } = [1f / 4096, 1f / 512, 1f / 256];

    /// <summary>Gets or sets the strength curve of the noise of the frame, or <see langword="null"/> when it has none.</summary>
    public float[]? NoiseStrengths { get; set; }

    /// <summary>Gets or sets the base factor of the prediction of X from Y of the frame.</summary>
    public float BaseCorrelationX { get; set; }

    /// <summary>Gets or sets the base factor of the prediction of B from Y of the frame.</summary>
    public float BaseCorrelationB { get; set; } = 1;

    /// <summary>Gets or sets the splines of the frame; the frame disposes them.</summary>
    public JpegXlSplines? Splines { get; set; }

    /// <summary>Gets or sets the patches of the frame; the frame disposes them.</summary>
    public JpegXlPatchDictionary? Patches { get; set; }

    /// <summary>Gets a value indicating whether the color channels are the floating-point samples of a VarDCT frame.</summary>
    public bool HasPlanes { get; private set; }

    /// <summary>Gets the samples of a color channel of a VarDCT frame (X, Y, B, or Cb, Y, Cr), in rows of <see cref="GetPlaneWidth"/> values.</summary>
    public Span<float> GetPlane(int channel) => _planes[channel]!.Span[..(_planeWidths[channel] * _planeHeights[channel])];

    /// <summary>Gets the width of the samples of a color channel: a multiple of 8 that covers the frame, divided by its subsampling.</summary>
    public int GetPlaneWidth(int channel) => _planeWidths[channel];

    /// <summary>Gets the height of the samples of a color channel.</summary>
    public int GetPlaneHeight(int channel) => _planeHeights[channel];

    /// <summary>Gets the number of bits by which a color channel is subsampled horizontally.</summary>
    public int GetHorizontalShift(int channel) => _horizontalShifts[channel];

    /// <summary>Gets the number of bits by which a color channel is subsampled vertically.</summary>
    public int GetVerticalShift(int channel) => _verticalShifts[channel];

    /// <summary>Gives the samples of a color channel to the caller, which disposes them.</summary>
    public Av1PooledArray<float> DetachPlane(int channel)
    {
        var plane = _planes[channel] ?? throw new InvalidOperationException("The channel has no samples.");
        _planes[channel] = null;
        return plane;
    }

    /// <summary>Takes the samples of a color channel; the frame disposes them.</summary>
    public void SetPlane(int channel, Av1PooledArray<float> samples, int width, int height, int horizontalShift, int verticalShift)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _planes[channel]?.Dispose();
        _planes[channel] = samples;
        HasPlanes = true;
        _planeWidths[channel] = width;
        _planeHeights[channel] = height;
        _horizontalShifts[channel] = horizontalShift;
        _verticalShifts[channel] = verticalShift;
    }

    public void Dispose()
    {
        Modular.Dispose();
        Patches?.Dispose();
        Patches = null;
        Splines?.Dispose();
        Splines = null;
        for (var i = 0; i < _planes.Length; i++)
        {
            _planes[i]?.Dispose();
            _planes[i] = null;
        }
    }
}
