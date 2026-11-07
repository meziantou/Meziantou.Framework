namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The destination of the rows of a JPEG datastream that is not an image of its own but the compressed form of a part of
/// another image (a strip or a tile of a TIFF page). A <see cref="JpegDecoder"/> created with a receiver builds no image:
/// it hands over the samples row by row, and the container gives them their meaning.
/// </summary>
internal abstract class JpegSampleReceiver
{
    /// <summary>Validates the frame of the datastream against what the container declares, before anything is allocated.</summary>
    /// <param name="width">The width of the frame.</param>
    /// <param name="height">The height of the frame.</param>
    /// <param name="componentCount">The number of components of the frame: 1, 3 or 4.</param>
    /// <param name="precision">The number of bits of a sample: 8 or 12, or 2 to 16 for a lossless frame.</param>
    public abstract void OnFrame(int width, int height, int componentCount, int precision);

    /// <summary>Receives one row of a frame of 8-bit samples, from the top one down.</summary>
    /// <param name="y">The index of the row in the frame.</param>
    /// <param name="samples">
    /// The samples of the row, interleaved: one per pixel for one component; red, green and blue for three components
    /// (converted from YCbCr when the container says the components are YCbCr, the stored samples otherwise); the four
    /// stored samples for four components. The span is only valid during the call.
    /// </param>
    public abstract void OnRow(int y, ReadOnlySpan<byte> samples);

    /// <summary>Receives one row of a frame whose samples do not have 8 bits, from the top one down.</summary>
    /// <param name="y">The index of the row in the frame.</param>
    /// <param name="samples">
    /// The samples of the row, interleaved like the ones of <see cref="OnRow"/>, each scaled from the precision of the
    /// frame to 16 bits (<see cref="JpegDeepRowWriter"/>), in native byte order. The span is only valid during the call.
    /// </param>
    public abstract void OnDeepRow(int y, ReadOnlySpan<ushort> samples);
}
