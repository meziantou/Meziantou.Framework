using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The validated structure of an animated cursor, resolved without decoding a pixel by
/// <see cref="AniStructureReader"/>: the playback (steps, rates and the frame each step shows), the stored frames a step
/// references with their displayed representation, and the canvas and pixel format shared by the whole animation.
/// </summary>
internal sealed class AniStructure : IDisposable
{
    private readonly AniFrame[] _frames;
    private PooledBuffer? _rates;
    private PooledBuffer? _sequence;

    public AniStructure(AniFrame[] frames, int stepCount, uint displayRate, PooledBuffer? rates, PooledBuffer? sequence, ImageMetadata metadata)
    {
        _frames = frames;
        StepCount = stepCount;
        DisplayRate = displayRate;
        _rates = rates;
        _sequence = sequence;
        Metadata = metadata;
    }

    /// <summary>Gets the number of displayed steps, which is the number of frames of the decoded animation.</summary>
    public int StepCount { get; }

    /// <summary>Gets the display rate of every step that has no entry in a <c>rate</c> chunk, in jiffies.</summary>
    public uint DisplayRate { get; }

    /// <summary>Gets the title and the author of the <c>INFO</c> list.</summary>
    public ImageMetadata Metadata { get; }

    /// <summary>Gets or sets the size of every displayed frame: the size of the representation shown by the first step.</summary>
    public Size Canvas { get; set; }

    /// <summary>Gets or sets the pixel format that stores every displayed frame losslessly.</summary>
    public PixelFormat PixelFormat { get; set; }

    /// <summary>Gets or sets the color model of the encoded samples of the first step.</summary>
    public ImageColorModel ColorModel { get; set; }

    /// <summary>Gets or sets the largest precision of one encoded component among the displayed frames.</summary>
    public int BitsPerComponent { get; set; }

    /// <summary>Gets or sets a value indicating whether a displayed frame can have transparent pixels.</summary>
    public bool MayHaveTransparency { get; set; }

    /// <summary>Gets the stored frame shown at a step.</summary>
    /// <param name="step">The zero-based step.</param>
    public AniFrame GetFrame(int step)
        => _frames[_sequence is null ? step : (int)BinaryPrimitives.ReadUInt32LittleEndian(_sequence.Span[(step * AniFormat.TableEntryLength)..])];

    /// <summary>Gets the display rate of a step, in jiffies (sixtieths of a second).</summary>
    /// <param name="step">The zero-based step.</param>
    public uint GetRate(int step)
        => _rates is null ? DisplayRate : BinaryPrimitives.ReadUInt32LittleEndian(_rates.Span[(step * AniFormat.TableEntryLength)..]);

    public void Dispose()
    {
        _rates?.Dispose();
        _rates = null;
        _sequence?.Dispose();
        _sequence = null;
    }
}
