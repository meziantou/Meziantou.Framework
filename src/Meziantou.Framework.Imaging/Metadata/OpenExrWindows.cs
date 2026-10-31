namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The immutable windows of an OpenEXR file (its <c>dataWindow</c> and <c>displayWindow</c> attributes), in the pixel
/// coordinates of the file: the corners of each window, both inclusive.
/// </summary>
/// <remarks>
/// The display window is the area the image is meant to show, and the data window the pixels the file stores. The decoded
/// image covers one of them (<see cref="OpenExrDecodingOptions.WindowMode"/>): its pixel (0, 0) is
/// <see cref="DisplayWindowMin"/>, or <see cref="DataWindowMin"/> with <see cref="OpenExrWindowMode.DataWindow"/>.
/// </remarks>
public sealed class OpenExrWindows : IEquatable<OpenExrWindows>
{
    /// <summary>Initializes a new instance of the <see cref="OpenExrWindows"/> class.</summary>
    /// <param name="dataWindowMin">The top-left pixel of the data window.</param>
    /// <param name="dataWindowMax">The bottom-right pixel of the data window.</param>
    /// <param name="displayWindowMin">The top-left pixel of the display window.</param>
    /// <param name="displayWindowMax">The bottom-right pixel of the display window.</param>
    /// <exception cref="ArgumentException">A window is empty: its bottom-right pixel is above or to the left of its top-left pixel.</exception>
    public OpenExrWindows(Point dataWindowMin, Point dataWindowMax, Point displayWindowMin, Point displayWindowMax)
    {
        if (dataWindowMax.X < dataWindowMin.X || dataWindowMax.Y < dataWindowMin.Y)
            throw new ArgumentException("The data window is empty.", nameof(dataWindowMax));

        if (displayWindowMax.X < displayWindowMin.X || displayWindowMax.Y < displayWindowMin.Y)
            throw new ArgumentException("The display window is empty.", nameof(displayWindowMax));

        DataWindowMin = dataWindowMin;
        DataWindowMax = dataWindowMax;
        DisplayWindowMin = displayWindowMin;
        DisplayWindowMax = displayWindowMax;
    }

    /// <summary>Gets the top-left pixel of the data window.</summary>
    public Point DataWindowMin { get; }

    /// <summary>Gets the bottom-right pixel of the data window (inclusive).</summary>
    public Point DataWindowMax { get; }

    /// <summary>Gets the top-left pixel of the display window.</summary>
    public Point DisplayWindowMin { get; }

    /// <summary>Gets the bottom-right pixel of the display window (inclusive).</summary>
    public Point DisplayWindowMax { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] OpenExrWindows? other)
        => other is not null && DataWindowMin == other.DataWindowMin && DataWindowMax == other.DataWindowMax && DisplayWindowMin == other.DisplayWindowMin && DisplayWindowMax == other.DisplayWindowMax;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as OpenExrWindows);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(DataWindowMin, DataWindowMax, DisplayWindowMin, DisplayWindowMax);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"data ({DataWindowMin.X}, {DataWindowMin.Y}) - ({DataWindowMax.X}, {DataWindowMax.Y}), display ({DisplayWindowMin.X}, {DisplayWindowMin.Y}) - ({DisplayWindowMax.X}, {DisplayWindowMax.Y})");
}
