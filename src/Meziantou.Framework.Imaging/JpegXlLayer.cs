using System.Text;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A layer of a <see cref="JpegXlLayeredImage"/>: an image of any size, at a position on the canvas, combined with the
/// layers below it with a blend mode, and an optional name.
/// </summary>
/// <remarks>
/// The image of the layer belongs to the <see cref="JpegXlLayeredImage"/>, which disposes it: its pixels may be edited in
/// place. A layer may extend beyond the canvas, where it is cropped when the image is shown.
/// </remarks>
public sealed class JpegXlLayer
{
    /// <summary>The smallest offset of a layer, on each axis.</summary>
    public const int MinOffset = -(1 << 29);

    /// <summary>The largest offset of a layer, on each axis.</summary>
    public const int MaxOffset = (1 << 29) - 1;

    /// <summary>The largest length of a name, in UTF-8 bytes.</summary>
    public const int MaxNameLength = JpegXlFrameLayout.MaxNameLength;

    internal JpegXlLayer(Image image, Point offset, JpegXlBlendMode blendMode, string? name)
    {
        Image = image;
        Offset = offset;
        BlendMode = blendMode;
        Name = name;
    }

    /// <summary>Gets the pixels of the layer, owned by the layered image.</summary>
    public Image Image { get; }

    /// <summary>Gets or sets the position of the top-left corner of the layer on the canvas; it may be negative.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is below <see cref="MinOffset"/> or above <see cref="MaxOffset"/>.</exception>
    public Point Offset
    {
        get;
        set
        {
            ValidateOffset(value);
            field = value;
        }
    }

    /// <summary>Gets or sets how the layer is combined with the layers below it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="JpegXlBlendMode"/>.</exception>
    public JpegXlBlendMode BlendMode
    {
        get;
        set
        {
            ValidateBlendMode(value);
            field = value;
        }
    }

    /// <summary>Gets or sets the name of the layer, or <see langword="null"/> for a layer without name.</summary>
    /// <remarks>An empty name is stored as no name.</remarks>
    /// <exception cref="ArgumentException">The name has more than <see cref="MaxNameLength"/> bytes in UTF-8, or is not valid UTF-16 text.</exception>
    public string? Name
    {
        get;
        set
        {
            ValidateName(value);
            field = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    internal static void ValidateOffset(Point value, string paramName = "value")
    {
        if (value.X is < MinOffset or > MaxOffset || value.Y is < MinOffset or > MaxOffset)
            throw new ArgumentOutOfRangeException(paramName, value, $"The coordinates of the offset of a layer are between {MinOffset} and {MaxOffset}.");
    }

    internal static void ValidateBlendMode(JpegXlBlendMode value, string paramName = "value")
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(paramName, value, "The blend mode is not valid.");
    }

    internal static void ValidateName(string? value, string paramName = "value")
    {
        if (string.IsNullOrEmpty(value))
            return;

        int length;
        try
        {
            length = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The name of a layer is not valid UTF-16 text.", paramName, exception);
        }

        if (length > MaxNameLength)
            throw new ArgumentException($"The name of a layer has at most {MaxNameLength} bytes in UTF-8.", paramName);
    }

    internal static UTF8Encoding StrictUtf8 { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
