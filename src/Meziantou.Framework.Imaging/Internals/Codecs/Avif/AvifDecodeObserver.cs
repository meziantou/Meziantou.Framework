namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Receives the image of an AVIF file, or the images of its sequence, from <see cref="AvifStructureParser"/> in a decode walk:
/// the walker owns the container rules, the observer turns the data of the coded items and of the samples into pixels.
/// </summary>
internal abstract class AvifDecodeObserver : IImageDecodeObserver
{
    /// <summary>The image is resolved: its size, its pixel format and its box-borne metadata are known.</summary>
    public abstract void OnHeaderComplete(AvifStructureParser structure);

    /// <summary>The data of every item of the image are captured (<see cref="AvifStructureParser.GetItemBuffer"/>).</summary>
    public abstract void OnImageData(AvifStructureParser structure);

    /// <summary>The samples of the next image of an image sequence are captured.</summary>
    /// <param name="structure">The walker.</param>
    /// <param name="color">The temporal unit of the color image.</param>
    /// <param name="alpha">The temporal unit of the alpha plane, or <see langword="null"/> for an opaque sequence.</param>
    /// <param name="duration">The duration of the image.</param>
    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> once the frame limit is reached.</returns>
    public abstract bool OnSequenceFrame(AvifStructureParser structure, WebPPayloadBuffer color, WebPPayloadBuffer? alpha, FrameDuration duration);

    public abstract Image GetResult();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
