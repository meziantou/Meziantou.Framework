using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Encodes the pictures of a test with the HEVC encoder, and keeps what the tests compare: the byte stream, with the
/// parameter sets and the hash of every picture, the access units as a file stores them, the samples that were encoded
/// and the pictures that the encoder reconstructed.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
internal sealed class HevcEncoderTestSession
{
    private readonly List<ReadOnlyMemory<byte>> _nalUnits = [];

    public HevcEncoderTestSession(HevcEncoderSettings settings)
    {
        Settings = settings;
    }

    public HevcEncoderSettings Settings { get; }

    /// <summary>Gets the parameters of the sequence.</summary>
    public HevcSequenceParameters Parameters { get; private set; }

    /// <summary>Gets the NAL units of the parameter sets.</summary>
    public List<byte[]> ParameterSets { get; } = [];

    /// <summary>Gets the access unit of each picture: its NAL units, each preceded by its length.</summary>
    public List<byte[]> Payloads { get; } = [];

    /// <summary>Gets the planes of each picture that was encoded: the samples of the output picture.</summary>
    public List<ushort[][]> Sources { get; } = [];

    /// <summary>Gets the planes of each picture that the encoder reconstructed: the samples of the coded picture.</summary>
    public List<ushort[][]> Reconstructions { get; } = [];

    /// <summary>Gets whether each picture is a key frame.</summary>
    public List<bool> KeyFrames { get; } = [];

    /// <summary>Gets the number of calls that encoded each picture.</summary>
    public List<int> Steps { get; } = [];

    public int PlaneCount => Settings.ChromaFormatIdc is 0 ? 1 : 3;

    public int SubWidthC => Settings.ChromaFormatIdc is 1 or 2 ? 2 : 1;

    public int SubHeightC => Settings.ChromaFormatIdc is 1 ? 2 : 1;

    /// <summary>Gets the size of the pictures, in bytes: their access units without the parameter sets.</summary>
    public long PayloadLength => Payloads.Sum(payload => (long)payload.Length);

    public static HevcEncoderSettings CreateSettings(int width, int height, int bitDepth, string format, bool lossless, int quality, int effort, bool stillPicture = true, int keyFrameInterval = 0)
    {
        var chromaFormatIdc = format switch
        {
            "400" => 0,
            "420" => 1,
            "422" => 2,
            "444" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        return new HevcEncoderSettings(width, height, bitDepth, chromaFormatIdc, ColourPrimaries: 1, TransferCharacteristics: 13, MatrixCoeffs: format is "444" ? 0 : 6, FullRange: true, ChromaSampleLocType: 0, stillPicture, lossless, quality, effort, keyFrameInterval);
    }

    /// <summary>Encodes the pictures that <paramref name="createPlane"/> gives: the samples of a plane of a picture, from the index of the picture, the plane, its width and its height.</summary>
    public void Encode(AllocationScope scope, int pictureCount, Func<int, int, int, int, ushort[]> createPlane, CancellationToken cancellationToken)
    {
        using var encoder = new HevcEncoder(scope, Settings);
        Parameters = encoder.Parameters;
        ParameterSets.Add(encoder.VideoParameterSet.ToArray());
        ParameterSets.Add(encoder.SequenceParameterSet.ToArray());
        ParameterSets.Add(encoder.PictureParameterSet.ToArray());
        _nalUnits.AddRange(ParameterSets.Select(parameterSet => (ReadOnlyMemory<byte>)parameterSet));
        for (var index = 0; index < pictureCount; index++)
        {
            var source = new ushort[PlaneCount][];
            for (var plane = 0; plane < PlaneCount; plane++)
            {
                var buffer = encoder.GetSourcePlane(plane);
                source[plane] = createPlane(index, plane, buffer.Width, buffer.Height);
                for (var y = 0; y < buffer.Height; y++)
                {
                    source[plane].AsSpan(y * buffer.Width, buffer.Width).CopyTo(buffer.GetRow(y));
                }
            }

            Sources.Add(source);
            encoder.BeginFrame();
            var steps = 1;
            while (!encoder.Encode(cancellationToken))
            {
                steps++;
            }

            Steps.Add(steps);
            KeyFrames.Add(encoder.IsKeyFrame);
            var payload = encoder.Payload.ToArray();
            Payloads.Add(payload);

            // The picture of the encoder, and its hash in a suffix SEI message, which the encoder does not write
            var picture = encoder.Reconstruction;
            var planes = new ushort[PlaneCount][];
            var md5 = new byte[PlaneCount * 16];
            for (var plane = 0; plane < PlaneCount; plane++)
            {
                var reconstructed = picture.GetPlane(plane);
                planes[plane] = new ushort[reconstructed.Width * reconstructed.Height];
                for (var y = 0; y < reconstructed.Height; y++)
                {
                    reconstructed.GetRow(y).CopyTo(planes[plane].AsSpan(y * reconstructed.Width));
                }

                ReferenceHevcPictureHash.ComputeMd5(planes[plane], reconstructed.Width, reconstructed.Height, reconstructed.Width, Settings.BitDepth).CopyTo(md5, plane * 16);
            }

            Reconstructions.Add(planes);
            _nalUnits.AddRange(ReferenceHevcStream.SplitLengthPrefixed(payload));
            using var rbsp = new PooledPayloadWriter(scope);
            using var nalUnit = new PooledPayloadWriter(scope);
            HevcHeaderWriter.WriteDecodedPictureHash(rbsp, new HevcDecodedPictureHash(HevcDecodedPictureHash.Md5, PlaneCount, md5, new uint[PlaneCount]));
            HevcNalWriter.WriteNalUnit(nalUnit, HevcConstants.NalSuffixSei, 0, rbsp.WrittenSpan);
            _nalUnits.Add(nalUnit.WrittenSpan.ToArray());
        }
    }

    /// <summary>Gets the byte stream: the parameter sets, then the slice segment and the hash of each picture.</summary>
    public byte[] ToByteStream() => ReferenceHevcStream.ToAnnexB(_nalUnits);

    /// <summary>
    /// Gets the peak signal to noise ratio of the pictures that the encoder reconstructed, in decibels, over the samples of
    /// the output pictures.
    /// </summary>
    public double GetPsnr()
    {
        var error = (double)GetSquaredError(out var count);
        if (error is 0)
            return double.PositiveInfinity;

        var max = (1 << Settings.BitDepth) - 1;
        return 10 * Math.Log10(max * (double)max * count / error);
    }

    /// <summary>
    /// Gets the sum of the squared differences between the samples that were encoded and the ones that the encoder
    /// reconstructed, over the samples of the output pictures.
    /// </summary>
    public long GetSquaredError(out long count)
    {
        var error = 0L;
        count = 0L;
        for (var index = 0; index < Sources.Count; index++)
        {
            for (var plane = 0; plane < PlaneCount; plane++)
            {
                var width = plane is 0 ? Settings.Width : (Settings.Width + SubWidthC - 1) / SubWidthC;
                var height = plane is 0 ? Settings.Height : (Settings.Height + SubHeightC - 1) / SubHeightC;
                var stride = (plane is 0 ? Parameters.PicWidthInLumaSamples : Parameters.PicWidthInLumaSamples / SubWidthC);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        long difference = Sources[index][plane][(y * width) + x] - Reconstructions[index][plane][(y * stride) + x];
                        error += difference * difference;
                    }
                }

                count += (long)width * height;
            }
        }

        return error;
    }

    /// <summary>
    /// The plane of a picture: one value ("flat"), a ramp ("gradient"), random samples ("noise"), rectangles of a few
    /// values ("blocks"), glyphs of two values on a third one ("text"), or smooth shapes with a little noise ("photo").
    /// </summary>
    public static ushort[] CreateSamples(string content, int width, int height, int bitDepth, int seed)
    {
        var random = new Random(seed);
        var max = (1 << bitDepth) - 1;
        var samples = new ushort[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = content switch
                {
                    "flat" => max / 3,
                    "gradient" => ((x * 3) + (y * 5)) * max / Math.Max(1, (width * 3) + (height * 5)),
                    "noise" => random.Next(max + 1),
                    "blocks" => (((x / 7) + (y / 5) + seed) % 4) * max / 3,
                    "edges" => ((x + (2 * y)) / 11 % 2 is 0) ? max / 8 : max * 7 / 8,
                    "text" => (((x * 7) ^ (y * 13) ^ seed) % 11 < 3 && (x % 6) < 4 && (y % 9) < 6) ? max / 10 : max * 9 / 10,
                    "mixed" => (((x * 7) ^ (y * 13)) % 11 < 3 && (x % 6) < 4 && (y % 9) < 6 && y < height / 2) ? max / 10 : (int)((max / 2.0) + (max / 4.0 * Math.Sin((x + seed) / 9.0)) + (max / 5.0 * Math.Cos(y / 6.0)) + random.Next(-max / 64, (max / 64) + 1)),
                    _ => (int)((max / 2.0) + (max / 4.0 * Math.Sin((x + seed) / 9.0)) + (max / 5.0 * Math.Cos(y / 6.0)) + random.Next(-max / 64, (max / 64) + 1) + ((x / 23) % 2 * max / 6.0)),
                };

                samples[(y * width) + x] = (ushort)Math.Clamp(value, 0, max);
            }
        }

        return samples;
    }

    /// <summary>
    /// The plane of a picture of a sequence: a scene that does not change ("static"), that moves by whole samples ("pan")
    /// or by a part of a sample ("drift"), that a square moves across ("object"), that another scene replaces ("cut"), or
    /// that is noise.
    /// </summary>
    public static ushort[] CreateFrame(string motion, int index, int width, int height, int subWidth, int subHeight, int bitDepth, int seed)
    {
        const int Margin = 64;
        var max = (1 << bitDepth) - 1;
        if (motion is "noise")
            return CreateSamples("noise", width, height, bitDepth, seed + index);

        if (motion is "cut" && index >= 2)
            return CreateSamples("blocks", width, height, bitDepth, seed);

        // The motion of the luma plane is a multiple of the subsampling, so that the chroma planes move by whole samples
        var sceneWidth = width + (2 * Margin);
        var scene = CreateSamples(motion is "drift" ? "mixed" : "photo", sceneWidth, height + (2 * Margin), bitDepth, seed);
        var offsetX = motion is "pan" ? 6 * index / subWidth : 0;
        var offsetY = motion is "pan" ? -4 * index / subHeight : 0;
        var samples = new ushort[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = ((y + Margin + offsetY) * sceneWidth) + x + Margin + offsetX;
                int value = scene[position];
                if (motion is "drift")
                {
                    // A quarter of a sample to the right for each picture
                    var phase = index & 3;
                    value = ((scene[position - (index >> 2)] * (4 - phase)) + (scene[position - (index >> 2) - 1] * phase) + 2) >> 2;
                }

                samples[(y * width) + x] = (ushort)value;
            }
        }

        if (motion is "object")
        {
            var size = Math.Max(2, Math.Min(width, height) / 3);
            var left = Math.Min(width - 1, (10 * index / subWidth) % width);
            var top = Math.Min(height - 1, 4 * index / subHeight);
            for (var y = top; y < Math.Min(height, top + size); y++)
            {
                samples.AsSpan((y * width) + left, Math.Min(size, width - left)).Fill((ushort)(max * 3 / 4));
            }
        }

        return samples;
    }
}
