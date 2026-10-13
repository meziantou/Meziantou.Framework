namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded planes of an image of the HEIF family, at their output size: what a codec hands to
/// <see cref="HeifFrameWriter"/>, which converts them to pixels.
/// </summary>
internal abstract class HeifPlanarImage : IDisposable
{
    /// <summary>Gets the sample format and the color description of the bitstream of the image.</summary>
    public abstract HeifBitstreamFormat Format { get; }

    /// <summary>Gets the number of luma samples of a row.</summary>
    public abstract int Width { get; }

    /// <summary>Gets the number of luma rows.</summary>
    public abstract int Height { get; }

    public int PlaneCount => Format.PlaneCount;

    public abstract Av1Plane GetPlane(int plane);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}
