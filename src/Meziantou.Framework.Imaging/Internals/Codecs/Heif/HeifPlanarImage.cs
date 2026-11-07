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

    /// <summary>Gets the row <paramref name="y"/> of a plane, whose rows are numbered from the top of the image.</summary>
    /// <param name="plane">The plane: 0 for luma or the alpha samples, 1 and 2 for chroma.</param>
    /// <param name="y">The row, in samples of the plane.</param>
    /// <returns>The samples of the row: at least the width of the plane.</returns>
    public virtual Span<ushort> GetRow(int plane, int y) => GetPlane(plane).GetRow(y);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}
