namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Receives the image of a file of the HEIF family, or the images of its sequence, from <see cref="HeifStructureParser"/> in a decode walk:
/// the walker owns the container rules, the observer turns the data of the coded items and of the samples into pixels.
/// </summary>
internal abstract class HeifDecodeObserver : IImageDecodeObserver
{
    /// <summary>The image is resolved: its size, its pixel format and its box-borne metadata are known.</summary>
    public abstract void OnHeaderComplete(HeifStructureParser structure);

    /// <summary>The data of every item of the image are captured (<see cref="HeifStructureParser.GetItemBuffer"/>).</summary>
    public abstract void OnImageData(HeifStructureParser structure);

    /// <summary>
    /// The next samples of an image sequence, in decoding order, are captured. The buffers stay valid until the image that
    /// <see cref="TryWriteSequenceFrame"/> writes next is written.
    /// </summary>
    /// <param name="structure">The walker.</param>
    /// <param name="color">The sample of the color images.</param>
    /// <param name="alpha">The sample of the alpha planes, or <see langword="null"/> for an opaque sequence.</param>
    public abstract void OnSequenceSample(HeifStructureParser structure, WebPPayloadBuffer color, WebPPayloadBuffer? alpha);

    /// <summary>Every sample of the image sequence was given to <see cref="OnSequenceSample"/>: no other one follows.</summary>
    public virtual void OnSequenceEnd(HeifStructureParser structure)
    {
    }

    /// <summary>
    /// Writes the next image of an image sequence, in presentation order, when the samples received so far give it. A codec
    /// that reorders its images needs the samples of later images before it outputs an image.
    /// </summary>
    /// <param name="structure">The walker.</param>
    /// <param name="duration">The duration of the image.</param>
    /// <param name="proceed"><see langword="true"/> to continue the walk; <see langword="false"/> once the frame limit is reached.</param>
    /// <returns><see langword="true"/> when the image is written; <see langword="false"/> when it needs another sample.</returns>
    public abstract bool TryWriteSequenceFrame(HeifStructureParser structure, FrameDuration duration, out bool proceed);

    /// <summary>
    /// Decodes the next image of an image sequence, in presentation order, without writing it: an image that the edit list
    /// of the sequence leaves out, which the images that follow may refer to. Only asked of a format that reads such edit
    /// lists (<see cref="HeifContainerFeatures.EditLists"/>).
    /// </summary>
    /// <param name="structure">The walker.</param>
    /// <param name="proceed"><see langword="true"/> to continue the walk.</param>
    /// <returns><see langword="true"/> when the image is decoded; <see langword="false"/> when it needs another sample.</returns>
    public virtual bool TrySkipSequenceFrame(HeifStructureParser structure, out bool proceed)
        => throw new InvalidOperationException("The format does not read the edit lists that leave images out.");

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
