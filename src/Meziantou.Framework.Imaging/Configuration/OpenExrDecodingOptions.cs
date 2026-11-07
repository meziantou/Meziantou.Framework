namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the OpenEXR decoder (<see cref="ImageConfiguration.OpenExrDecoding"/>): the channels it decodes and
/// the window the image covers.
/// </summary>
/// <remarks>
/// <para>
/// They apply to every operation that reads an OpenEXR file: identification, eager loads, sequential readers and
/// collections, which report and decode the same image. The defaults read only the files whose every channel and every
/// stored pixel is part of the image, and reject the others with an <see cref="UnsupportedImageFeatureException"/>.
/// </para>
/// <para>
/// A channel belongs to the layer named by its name up to its last dot: <c>diffuse.R</c> is the red channel of the layer
/// <c>diffuse</c>, and <c>R</c> the red channel of the default layer, whose name is empty. A layer is decoded from its
/// <c>R</c>, <c>G</c>, <c>B</c> and <c>A</c> channels, any of them (a missing color is 0, a missing alpha is 1), or from
/// its <c>Y</c> (luminance) and <c>A</c> channels.
/// </para>
/// </remarks>
public sealed class OpenExrDecodingOptions
{
    /// <summary>Gets the default options: every channel decoded, and the display window as the image.</summary>
    public static OpenExrDecodingOptions Default { get; } = new();

    /// <summary>
    /// Gets the layer that is decoded, or <see langword="null"/> (the default) to decode every channel of the file. Use
    /// <see cref="string.Empty"/> for the default layer (the channels <c>R</c>, <c>G</c>, <c>B</c>, <c>A</c> and <c>Y</c>
    /// without prefix).
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <see langword="null"/>, every channel must be one of <c>R</c>, <c>G</c>, <c>B</c>, <c>A</c> and <c>Y</c>: a
    /// file with other channels (depth, layers, other outputs of a renderer) is rejected, since they would be lost.
    /// </para>
    /// <para>
    /// With a layer, the channels of the layer are decoded and the other channels of the file are ignored: <c>Z</c>, the
    /// channels of the other layers, and the channels of the layer that are not colors. A layer that has no color channel
    /// (none of <c>R</c>, <c>G</c>, <c>B</c> and <c>Y</c>), that has both RGB channels and a luminance channel, that has
    /// chroma channels (<c>RY</c>, <c>BY</c>), or whose <c>Y</c> channel comes with an <c>X</c> channel (the components of
    /// a vector such as <c>N.X</c>, <c>N.Y</c>, <c>N.Z</c>, or CIE XYZ colors) is rejected with an
    /// <see cref="UnsupportedImageFeatureException"/>; the message of a missing layer lists the layers of the file that
    /// have colors: the decoder never guesses another layer.
    /// </para>
    /// </remarks>
    public string? Layer { get; init; }

    /// <summary>Gets the window the image covers. Defaults to <see cref="OpenExrWindowMode.DisplayWindow"/>.</summary>
    /// <remarks>
    /// With <see cref="OpenExrWindowMode.CropToDisplayWindow"/> and <see cref="OpenExrWindowMode.DataWindow"/>, the data
    /// window counts against the size limits too (<see cref="ImageResourceLimits.MaxWidth"/>,
    /// <see cref="ImageResourceLimits.MaxHeight"/> and <see cref="ImageResourceLimits.MaxFramePixels"/>), since every
    /// chunk of it is decoded.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="OpenExrWindowMode"/>.</exception>
    public OpenExrWindowMode WindowMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The OpenEXR window mode is not valid.");

            field = value;
        }
    }
}
