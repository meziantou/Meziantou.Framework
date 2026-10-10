using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>
/// The exact output samples of a reference filter (<see cref="ReferenceResampler"/>, <see cref="Convolution.ReferenceConvolver"/>),
/// and their comparison with actual pixels.
/// </summary>
public class ReferenceFilterResult
{
    /// <summary>
    /// The tie bias of the rounding contract: a sample is <c>floor(v + 1/2 + 2^-20)</c>, so that exact ties round upward
    /// even when the double-precision computation of the library lands just below them.
    /// </summary>
    public const decimal TieBias = 1m / 1048576;

    /// <summary>
    /// The distance to the (biased) rounding boundary, in sample units, under which either neighbor is accepted. The
    /// library accumulates in double precision (relative error below 1e-13 for these sizes, that is below 1e-8 sample units
    /// even for premultiplied 16-bit sums); a value this close to the boundary may legitimately round either way, while a
    /// wrong kernel, geometry, alpha handling or precision changes samples by orders of magnitude more. Exact ties are
    /// <see cref="TieBias"/> away from the boundary, so they must always round upward.
    /// </summary>
    public const decimal TieWindow = 0.0000001m;

    private readonly decimal[] _values;

    private readonly string _description;
    private readonly string _artifactDirectory;

    internal ReferenceFilterResult(string description, int width, int height, RawPixelLayout layout, decimal[] values, bool zeroAlphaIsTransparentBlack, string artifactDirectory)
    {
        _description = description;
        _artifactDirectory = artifactDirectory;
        ZeroAlphaIsTransparentBlack = zeroAlphaIsTransparentBlack;
        Width = width;
        Height = height;
        Layout = layout;
        _values = values;
    }

    /// <summary>Gets the output width.</summary>
    public int Width { get; }

    /// <summary>Gets the output height.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether a pixel whose alpha rounds to 0 is transparent black. It is the case when alpha is
    /// filtered with the colors (premultiplied); it is not when the pixels or their alpha are kept as they are (hidden
    /// colors of transparent pixels included).
    /// </summary>
    public bool ZeroAlphaIsTransparentBlack { get; }

    /// <summary>Gets the layout (the source layout).</summary>
    public RawPixelLayout Layout { get; }

    /// <summary>Gets the exact (unrounded) value of a sample, clamped to the sample range (encoded scale for colors).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">The channel.</param>
    /// <returns>The exact value.</returns>
    public decimal GetExactValue(int x, int y, int channel) => _values[(((y * Width) + x) * Layout.ChannelCount) + channel];

    /// <summary>Gets the expected pixels: every exact value rounded to nearest with ties upward (see <see cref="TieBias"/>), zero-alpha pixels transparent black.</summary>
    /// <returns>The rounded reference.</returns>
    public RawPixelBuffer ToRoundedBuffer()
    {
        var builder = new RawPixelBufferBuilder(Width, Height, Layout);
        var channels = Layout.ChannelCount;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var transparent = ZeroAlphaIsTransparentBlack && Layout.HasAlpha && RoundHalfUp(GetExactValue(x, y, Layout.AlphaChannel)) == 0;
                for (var c = 0; c < channels; c++)
                {
                    builder.SetSample(x, y, c, transparent ? 0 : RoundHalfUp(GetExactValue(x, y, c)));
                }
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Compares actual pixels with the exact values: each sample must equal its correctly rounded value, or either
    /// neighbor when the exact value is within <see cref="TieWindow"/> of the (biased) rounding boundary (exact ties must round
    /// upward); a pixel whose alpha is 0 must
    /// be transparent black. Alpha is compared with the same rule (it is filtered too).
    /// </summary>
    /// <param name="actual">The actual pixels.</param>
    /// <param name="context">The context used in messages.</param>
    /// <param name="writePreviews">Whether diagnostic previews are written on failure (default: <see cref="TestArtifacts.PreviewsEnabled"/>).</param>
    /// <returns>The result.</returns>
    public ResampleComparisonResult Compare(RawPixelBuffer actual, string context, bool? writePreviews = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var description = $"{context} [{_description}]";
        if (actual.Layout != Layout || actual.Width != Width || actual.Height != Height)
            return new ResampleComparisonResult(description, string.Create(CultureInfo.InvariantCulture, $"Structure mismatch: expected {Width}x{Height} {Layout}, actual {actual.Width}x{actual.Height} {actual.Layout}."), 0, 0, 0, null, []);

        var channels = Layout.ChannelCount;
        var mismatches = new List<ResampleMismatch>();
        long ties = 0, exact = 0, violations = 0;
        ResampleMismatch? largest = null;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var actualAlpha = Layout.HasAlpha ? actual.GetSample(x, y, Layout.AlphaChannel) : -1;
                for (var c = 0; c < channels; c++)
                {
                    var value = GetExactValue(x, y, c);
                    var sample = actual.GetSample(x, y, c);
                    var (low, high) = Accepted(value);
                    if (low != high)
                    {
                        ties++;
                    }

                    // An actual alpha of 0 (when accepted) makes the expected color 0
                    if (ZeroAlphaIsTransparentBlack && Layout.HasAlpha && c != Layout.AlphaChannel && actualAlpha == 0 && Accepted(GetExactValue(x, y, Layout.AlphaChannel)).Low == 0)
                    {
                        (low, high) = (0, 0);
                    }

                    var mismatch = new ResampleMismatch(x, y, c, Layout.GetChannelName(c), value, low, high, sample);
                    if (largest is null || mismatch.Deviation > largest.Deviation)
                    {
                        largest = mismatch;
                    }

                    if (sample >= low && sample <= high)
                    {
                        if (sample == RoundHalfUp(value))
                        {
                            exact++;
                        }

                        continue;
                    }

                    violations++;
                    if (mismatches.Count < 32)
                    {
                        mismatches.Add(mismatch);
                    }
                }
            }
        }

        var result = new ResampleComparisonResult(description, StructuralError: null, (long)Width * Height * channels, exact, ties, largest, mismatches) { ViolationCount = violations };
        if (!result.IsMatch && (writePreviews ?? TestArtifacts.PreviewsEnabled))
        {
            var expected = ToRoundedBuffer();
            var files = DiffPreviewWriter.Write(TestArtifacts.GetDirectory(_artifactDirectory), context, expected, actual, PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, description));
            result = result with { PreviewFiles = files };
        }

        return result;
    }

    private (int Low, int High) Accepted(decimal value)
    {
        var biased = value + TieBias;
        var floor = decimal.Floor(biased);
        int Clamp(decimal v) => (int)Math.Clamp(v, 0, Layout.MaxSampleValue);
        return Math.Abs(biased - floor - 0.5m) <= TieWindow ? (Clamp(floor), Clamp(floor + 1)) : (RoundHalfUp(value), RoundHalfUp(value));
    }

    private int RoundHalfUp(decimal value) => (int)Math.Clamp(decimal.Floor(value + 0.5m + TieBias), 0, Layout.MaxSampleValue);
}
