namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of a coded stream of a file of the HEIF family (the color images, or the alpha planes), as
/// <see cref="HeifEncoderSession"/> drives it: the session writes the samples of a picture to the source planes, then the
/// codec encodes it a bounded part at a time. <see cref="HeifCodecBinding.CreatePictureEncoder"/> creates it.
/// </summary>
internal abstract class HeifPictureEncoder : IDisposable
{
    /// <summary>Gets the description of the stream for the container; it is the same for every picture.</summary>
    public abstract HeifStreamDescription Description { get; }

    /// <summary>
    /// Gets the last encoded picture as the data of an item or of a sample, in the format that the binding of the codec to
    /// the container defines; valid until the next picture starts.
    /// </summary>
    public abstract ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Gets whether the last encoded picture is a key frame: a picture that a decoder can start from, which is a sync sample
    /// of an image sequence.
    /// </summary>
    public abstract bool IsKeyFrame { get; }

    /// <summary>
    /// Gets the plane <paramref name="plane"/> of the picture to encode (0 for Y, 1 for U, 2 for V): the caller writes the
    /// <see cref="Av1Plane.Width"/> x <see cref="Av1Plane.Height"/> samples of the picture before <see cref="BeginFrame"/>.
    /// </summary>
    public abstract Av1Plane GetSourcePlane(int plane);

    /// <summary>Starts the encoding of the picture whose samples are in the source planes.</summary>
    public abstract void BeginFrame();

    /// <summary>
    /// Forgets the pictures that were encoded, so that the next one starts a new stream: another still picture of the same
    /// size and format (the tiles of a grid are coded one after the other by one encoder). The payload of the last
    /// picture is no longer valid.
    /// </summary>
    public abstract void Reset();

    /// <summary>Encodes a bounded part of the picture.</summary>
    /// <returns><see langword="true"/> when the picture is complete.</returns>
    /// <exception cref="ImageResourceLimitException">A buffer exceeds the live-allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
    public abstract bool Encode(CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}
