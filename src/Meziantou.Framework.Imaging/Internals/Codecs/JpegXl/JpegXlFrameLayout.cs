namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Where a frame written by the encoder goes and how it is combined (ISO/IEC 18181-1, section C.2): its rectangle on the
/// image, its blend mode and the reference slot it is blended with, the slot it is saved to, and its name.
/// </summary>
internal sealed class JpegXlFrameLayout
{
    /// <summary>The largest length of a name in bytes: the largest value of its <c>U32</c> field.</summary>
    public const int MaxNameLength = 48 + 1023;

    /// <summary>Gets the layout of a frame that covers the image and replaces it.</summary>
    public static JpegXlFrameLayout Full { get; } = new();

    /// <summary>Gets a value indicating whether the frame has its own size and position (<c>have_crop</c>).</summary>
    public bool HasCrop { get; init; }

    /// <summary>Gets the column of the image where the frame starts; may be negative.</summary>
    public int X0 { get; init; }

    /// <summary>Gets the row of the image where the frame starts; may be negative.</summary>
    public int Y0 { get; init; }

    /// <summary>Gets the width of the frame, when it has its own size.</summary>
    public int Width { get; init; }

    /// <summary>Gets the height of the frame, when it has its own size.</summary>
    public int Height { get; init; }

    /// <summary>Gets the blend mode of every channel, one of the modes of <see cref="JpegXlBlendingInfo"/>.</summary>
    public int BlendMode { get; init; } = JpegXlBlendingInfo.ModeReplace;

    /// <summary>Gets the reference slot the frame is blended with.</summary>
    public int Source { get; init; }

    /// <summary>Gets the reference slot the frame is saved to, once blended, when it is not the last one.</summary>
    public int SaveAsReference { get; init; }

    /// <summary>Gets the name of the frame, in UTF-8.</summary>
    public byte[] Name { get; init; } = [];

    /// <summary>Gets the size of the frame for an image of a size.</summary>
    public Size GetSize(Size image) => HasCrop ? new Size(Width, Height) : image;

    /// <summary>Determines whether the frame does not cover the image: the source slot is then written.</summary>
    public bool IsPartial(Size image) => HasCrop && (X0 > 0 || Y0 > 0 || Width + (long)X0 < image.Width || Height + (long)Y0 < image.Height);
}
