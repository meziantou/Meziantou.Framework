namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The channels of a Modular sub-bitstream (ISO/IEC 18181-1, section H.2): the meta-channels that transforms add come
/// first, then the channels of the image.
/// </summary>
internal sealed class JpegXlModularImage : IDisposable
{
    public JpegXlModularImage(int bitDepth)
    {
        BitDepth = bitDepth;
    }

    /// <summary>Gets the bit depth of the samples of the image, which scales the implicit colors of a palette.</summary>
    public int BitDepth { get; }

    /// <summary>Gets the channels.</summary>
    public List<JpegXlModularChannel> Channels { get; } = [];

    /// <summary>Gets or sets the number of leading channels that are meta-channels.</summary>
    public int MetaChannelCount { get; set; }

    /// <summary>Gets the transforms that were applied by the encoder, in the order of the bitstream; they are undone last to first.</summary>
    public List<JpegXlModularTransform> Transforms { get; } = [];

    /// <summary>Gets or sets the parameters of the weighted predictor of the sub-bitstream, which the palette transform may use.</summary>
    public JpegXlWeightedPredictorHeader WeightedPredictor { get; set; } = JpegXlWeightedPredictorHeader.Default;

    /// <summary>Adds a channel at the end of the list; the image owns it.</summary>
    public void AddChannel(int width, int height, int horizontalShift, int verticalShift) => Channels.Add(new JpegXlModularChannel(width, height, horizontalShift, verticalShift));

    /// <summary>Inserts a channel in the list; the image owns it.</summary>
    public void InsertChannel(int index, int width, int height, int horizontalShift, int verticalShift)
    {
        // The channel is added, then moved to its place: the list owns it from the start
        AddChannel(width, height, horizontalShift, verticalShift);
        for (var i = Channels.Count - 1; i > index; i--)
        {
            (Channels[i], Channels[i - 1]) = (Channels[i - 1], Channels[i]);
        }
    }

    public void Dispose()
    {
        foreach (var channel in Channels)
        {
            channel.Dispose();
        }

        Channels.Clear();
    }
}
