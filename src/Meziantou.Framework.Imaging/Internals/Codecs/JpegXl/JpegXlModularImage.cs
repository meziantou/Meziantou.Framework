namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The channels of a Modular sub-bitstream (ISO/IEC 18181-1, section H.2): the meta-channels that transforms add come
/// first, then the channels of the image.
/// </summary>
/// <remarks>
/// A squeeze keeps its channels and adds one residual per channel, which is empty when the channel has one sample in the
/// squeezed direction. The shifts of the channels of the image grow with each squeeze and are bounded, but those of a
/// meta-channel do not: a few bytes of squeeze steps can squeeze a 1x1 palette over and over, each step adding an empty
/// channel and moving the channels that follow it. The number of channels is bounded by <see cref="MaxChannelCount"/>,
/// and the number of channels that insertions move by <see cref="MaxChannelMoves"/>. The default squeeze of an image with
/// 259 channels (level 10 allows 256 extra channels) of 2^30 x 2^30 samples has about 15,000 channels and moves about
/// 420,000 of them.
/// </remarks>
internal sealed class JpegXlModularImage : IDisposable
{
    internal const int MaxChannelCount = 1 << 16;
    internal const long MaxChannelMoves = 1 << 22;

    private long _channelMoves;

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
    public void AddChannel(int width, int height, int horizontalShift, int verticalShift)
    {
        EnsureCanInsert(Channels.Count, 1);
        Channels.Add(new JpegXlModularChannel(width, height, horizontalShift, verticalShift));
    }

    /// <summary>Inserts a channel in the list; the image owns it.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The image would have too many channels, or its insertions would move too many channels.</exception>
    public void InsertChannel(int index, int width, int height, int horizontalShift, int verticalShift)
    {
        EnsureCanInsert(index, 1);

        // The channel is added, then moved to its place: the list owns it from the start
        Channels.Add(new JpegXlModularChannel(width, height, horizontalShift, verticalShift));
        var channel = Channels[^1];
        Channels.RemoveAt(Channels.Count - 1);
        Channels.Insert(index, channel);
    }

    /// <summary>Inserts channels that have no samples yet in the list, at once; the image owns them.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The image would have too many channels, or its insertions would move too many channels.</exception>
    public void InsertChannels(int index, JpegXlModularChannel[] channels)
    {
        EnsureCanInsert(index, channels.Length);
        Channels.InsertRange(index, channels);
    }

    public void Dispose()
    {
        foreach (var channel in Channels)
        {
            channel.Dispose();
        }

        Channels.Clear();
    }

    private void EnsureCanInsert(int index, int count)
    {
        _channelMoves += Channels.Count - index;
        if (Channels.Count + count > MaxChannelCount || _channelMoves > MaxChannelMoves)
            throw JpegXlFormat.Unsupported("The JPEG XL image has Modular transforms that add too many channels.", "Modular transforms that add too many channels");
    }
}
