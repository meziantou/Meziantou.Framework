using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The frames a JPEG XL reference decoder wrote as a NumPy array (<c>.npy</c>, the format of the JPEG XL conformance
/// tests): little-endian 32-bit floating-point samples of shape (frames, height, width, channels), where 1.0 is the
/// largest value of an integer sample. The reader is independent of the library.
/// </summary>
public sealed partial class NpyFrames
{
    private readonly float[] _samples;

    private NpyFrames(int frameCount, int height, int width, int channelCount, float[] samples)
    {
        FrameCount = frameCount;
        Height = height;
        Width = width;
        ChannelCount = channelCount;
        _samples = samples;
    }

    /// <summary>Gets the number of frames.</summary>
    public int FrameCount { get; }

    /// <summary>Gets the height of every frame.</summary>
    public int Height { get; }

    /// <summary>Gets the width of every frame.</summary>
    public int Width { get; }

    /// <summary>Gets the number of channels: the color channels (1 or 3) followed by the extra channels the decoder returned.</summary>
    public int ChannelCount { get; }

    /// <summary>Gets one sample as the decoder wrote it.</summary>
    /// <param name="frame">The index of the frame.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The sample: 1.0 is the largest value of an integer sample.</returns>
    public float GetSample(int frame, int x, int y, int channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, ChannelCount);
        return _samples[(((((frame * Height) + y) * Width) + x) * ChannelCount) + channel];
    }

    /// <summary>
    /// Converts a frame to integer samples: each sample is multiplied by <paramref name="maxValue"/>, rounded to the nearest
    /// integer and clamped to the range of the samples.
    /// </summary>
    /// <param name="frame">The index of the frame.</param>
    /// <param name="maxValue">The largest value of a sample (255 for 8 bits, 65535 for 16 bits).</param>
    /// <param name="maxRoundingDistance">
    /// Receives the largest distance between a scaled sample and the integer it was rounded to (0 to 0.5), before clamping:
    /// close to 0 when the file stores integers of that depth exactly.
    /// </param>
    /// <returns>The samples, row by row, <see cref="ChannelCount"/> per pixel.</returns>
    public int[] ToIntegerSamples(int frame, int maxValue, out double maxRoundingDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxValue, 1);

        var length = Height * Width * ChannelCount;
        var result = new int[length];
        var source = _samples.AsSpan(frame * length, length);
        maxRoundingDistance = 0;
        for (var i = 0; i < length; i++)
        {
            var scaled = (double)source[i] * maxValue;
            var rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
            maxRoundingDistance = Math.Max(maxRoundingDistance, Math.Abs(scaled - rounded));
            result[i] = (int)Math.Clamp(rounded, 0, maxValue);
        }

        return result;
    }

    /// <summary>Parses a version 1.0 <c>.npy</c> file holding a four-dimensional array of little-endian 32-bit floats in C order.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The frames.</returns>
    /// <exception cref="InvalidDataException">The file is not such an array.</exception>
    public static NpyFrames Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10 || data[0] != 0x93 || !data[1..].StartsWith("NUMPY"u8) || data[6] != 1 || data[7] != 0)
            throw new InvalidDataException("The data is not a version 1.0 .npy file.");

        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (data.Length < 10 + headerLength)
            throw new InvalidDataException("The .npy header is truncated.");

        var header = Encoding.ASCII.GetString(data.Slice(10, headerLength));
        var match = HeaderRegex().Match(header);
        if (!match.Success)
            throw new InvalidDataException($"The .npy header is not a C-ordered array of '<f4' with four dimensions: {header.Trim()}");

        var frames = int.Parse(match.Groups["frames"].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
        var width = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
        var channels = int.Parse(match.Groups["channels"].Value, CultureInfo.InvariantCulture);
        var count = checked(frames * height * width * channels);
        var payload = data[(10 + headerLength)..];
        if (payload.Length != count * sizeof(float))
            throw new InvalidDataException($"The .npy file holds {payload.Length.ToString(CultureInfo.InvariantCulture)} bytes of samples; {(count * sizeof(float)).ToString(CultureInfo.InvariantCulture)} are expected for the shape ({frames.ToString(CultureInfo.InvariantCulture)}, {height.ToString(CultureInfo.InvariantCulture)}, {width.ToString(CultureInfo.InvariantCulture)}, {channels.ToString(CultureInfo.InvariantCulture)}).");

        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = BinaryPrimitives.ReadSingleLittleEndian(payload[(i * sizeof(float))..]);
        }

        return new NpyFrames(frames, height, width, channels, samples);
    }

    [GeneratedRegex(@"^\{'descr': '<f4', 'fortran_order': False, 'shape': \((?<frames>\d+), (?<height>\d+), (?<width>\d+), (?<channels>\d+)\), \}\s*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeaderRegex();
}
