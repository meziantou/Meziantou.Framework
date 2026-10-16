using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that fills its subregion with Perlin noise: <c>feTurbulence</c> of SVG.</summary>
/// <remarks>
/// <para>
/// The noise is the one of the reference code of "Filter Effects Module Level 1", transcribed: the same random numbers
/// from the seed, the same lattice, the same gradients, the same octaves, with a separate noise for the red, the green,
/// the blue and the alpha. A pixel takes the noise at the point of the drawing of its top left corner. The values are
/// straight colors, clamped to the range from 0 to 1, in the colors of the primitive (linear light by default).
/// </para>
/// <para>
/// With <see cref="StitchTiles"/>, the frequencies are adjusted so that the noise repeats with the size of the subregion
/// (its <see cref="FilterPrimitive.Subregion"/>, or the region of the filter), and the lattice wraps there. The noise only
/// involves additions, multiplications, divisions and square roots: it is the same on every platform. At most 32
/// octaves are computed: the others add less than 2^-32.
/// </para>
/// </remarks>
public sealed class TurbulencePrimitive : FilterPrimitive
{
    private const int LatticeSize = 0x100;
    private const int LatticeMask = 0xff;
    private const int PerlinOffset = 0x1000;
    private const int MaxOctaves = 32;
    private const long RandomModulus = 2147483647;
    private const long RandomMultiplier = 16807;
    private const long RandomQuotient = 127773;
    private const long RandomRemainder = 2836;

    private readonly int[] _lattice = new int[LatticeSize + LatticeSize + 2];
    private readonly double[] _gradients = new double[4 * (LatticeSize + LatticeSize + 2) * 2];

    /// <summary>Initializes a new instance of the <see cref="TurbulencePrimitive"/> class.</summary>
    /// <param name="type">The noise.</param>
    /// <param name="baseFrequencyX">The frequency of the first octave along x, in cycles per unit of the drawing: <c>baseFrequency</c>.</param>
    /// <param name="baseFrequencyY">The frequency of the first octave along y.</param>
    /// <param name="octaves">The number of octaves: <c>numOctaves</c>. Must be zero or positive.</param>
    /// <param name="seed">The seed of the random numbers: <c>seed</c>, truncated to an integer.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not defined, a frequency is negative or not a finite number, or <paramref name="octaves"/> is negative.</exception>
    public TurbulencePrimitive(FilterTurbulenceType type, double baseFrequencyX, double baseFrequencyY, int octaves, int seed)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), type, "The type is not valid.");

        if (!(baseFrequencyX >= 0 && double.IsFinite(baseFrequencyX)))
            throw new ArgumentOutOfRangeException(nameof(baseFrequencyX), baseFrequencyX, "The frequency must be a finite number, zero or positive.");

        if (!(baseFrequencyY >= 0 && double.IsFinite(baseFrequencyY)))
            throw new ArgumentOutOfRangeException(nameof(baseFrequencyY), baseFrequencyY, "The frequency must be a finite number, zero or positive.");

        ArgumentOutOfRangeException.ThrowIfNegative(octaves);
        Type = type;
        BaseFrequencyX = baseFrequencyX;
        BaseFrequencyY = baseFrequencyY;
        Octaves = octaves;
        Seed = seed;
        Initialize(seed);
    }

    /// <summary>Gets the noise.</summary>
    public FilterTurbulenceType Type { get; }

    /// <summary>Gets the frequency of the first octave along x.</summary>
    public double BaseFrequencyX { get; }

    /// <summary>Gets the frequency of the first octave along y.</summary>
    public double BaseFrequencyY { get; }

    /// <summary>Gets the number of octaves.</summary>
    public int Octaves { get; }

    /// <summary>Gets the seed of the random numbers.</summary>
    public int Seed { get; }

    /// <summary>Gets a value indicating whether the noise repeats with the size of the subregion: <c>stitchTiles="stitch"</c>.</summary>
    public bool StitchTiles { get; init; }

    internal override int InputCount => 0;

    internal override FilterInput GetInput(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var frequencyX = BaseFrequencyX;
        var frequencyY = BaseFrequencyY;
        var stitch = default(Stitch);
        var isStitching = false;
        if (StitchTiles)
        {
            // The frequencies whose number of cycles across the tile is whole, the nearer in ratio
            var tile = Subregion ?? context.RegionRectangle;
            if (frequencyX != 0)
            {
                var low = Math.Floor(tile.Width * frequencyX) / tile.Width;
                var high = Math.Ceiling(tile.Width * frequencyX) / tile.Width;
                frequencyX = frequencyX / low < high / frequencyX ? low : high;
            }

            if (frequencyY != 0)
            {
                var low = Math.Floor(tile.Height * frequencyY) / tile.Height;
                var high = Math.Ceiling(tile.Height * frequencyY) / tile.Height;
                frequencyY = frequencyY / low < high / frequencyY ? low : high;
            }

            isStitching = true;
            stitch.Width = (long)((tile.Width * frequencyX) + 0.5);
            stitch.WrapX = (long)((tile.X * frequencyX) + PerlinOffset + stitch.Width);
            stitch.Height = (long)((tile.Height * frequencyY) + 0.5);
            stitch.WrapY = (long)((tile.Y * frequencyY) + PerlinOffset + stitch.Height);
        }

        var octaves = Math.Min(Octaves, MaxOctaves);
        var isFractal = Type == FilterTurbulenceType.FractalNoise;
        var values = result.Values;
        Span<double> color = stackalloc double[4];
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            context.Processor.CancellationToken.ThrowIfCancellationRequested();
            for (var x = subregion.Left; x < subregion.Right; x++)
            {
                var (pointX, pointY) = context.Processor.ToDrawing(x, y);
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = GetTurbulence(channel, pointX * frequencyX, pointY * frequencyY, octaves, isFractal, isStitching, stitch);
                    var value = isFractal ? (sum + 1) / 2 : sum;

                    // Written so that a value that is not a number is zero
                    color[channel] = value > 0 ? (value < 1 ? value : 1) : 0;
                }

                var offset = result.IndexOf(x, y);
                values[offset] = (float)(color[0] * color[3]);
                values[offset + 1] = (float)(color[1] * color[3]);
                values[offset + 2] = (float)(color[2] * color[3]);
                values[offset + 3] = (float)color[3];
            }
        }
    }

    /// <summary>The random numbers of the reference code: a Park and Miller generator, by the method of Schrage.</summary>
    private static long NextRandom(long seed)
    {
        var result = (RandomMultiplier * (seed % RandomQuotient)) - (RandomRemainder * (seed / RandomQuotient));
        if (result <= 0)
        {
            result += RandomModulus;
        }

        return result;
    }

    private static double SCurve(double t) => t * t * (3 - (2 * t));

    private static double Lerp(double t, double a, double b) => a + (t * (b - a));

    /// <summary>Builds the lattice and the gradients of the four channels from the seed: <c>init</c> of the reference code.</summary>
    private void Initialize(long seed)
    {
        if (seed <= 0)
        {
            seed = -(seed % (RandomModulus - 1)) + 1;
        }

        if (seed > RandomModulus - 1)
        {
            seed = RandomModulus - 1;
        }

        int i;
        for (var k = 0; k < 4; k++)
        {
            for (i = 0; i < LatticeSize; i++)
            {
                _lattice[i] = i;
                for (var j = 0; j < 2; j++)
                {
                    seed = NextRandom(seed);
                    _gradients[GradientIndex(k, i, j)] = (double)((seed % (LatticeSize + LatticeSize)) - LatticeSize) / LatticeSize;
                }

                var x = _gradients[GradientIndex(k, i, 0)];
                var y = _gradients[GradientIndex(k, i, 1)];
                var length = Math.Sqrt((x * x) + (y * y));
                _gradients[GradientIndex(k, i, 0)] = x / length;
                _gradients[GradientIndex(k, i, 1)] = y / length;
            }
        }

        for (i = LatticeSize - 1; i > 0; i--)
        {
            var k = _lattice[i];
            seed = NextRandom(seed);
            var j = (int)(seed % LatticeSize);
            _lattice[i] = _lattice[j];
            _lattice[j] = k;
        }

        for (i = 0; i < LatticeSize + 2; i++)
        {
            _lattice[LatticeSize + i] = _lattice[i];
            for (var k = 0; k < 4; k++)
            {
                for (var j = 0; j < 2; j++)
                {
                    _gradients[GradientIndex(k, LatticeSize + i, j)] = _gradients[GradientIndex(k, i, j)];
                }
            }
        }
    }

    private static int GradientIndex(int channel, int index, int component) => (((channel * (LatticeSize + LatticeSize + 2)) + index) * 2) + component;

    /// <summary>The sum of the octaves at a point: <c>turbulence</c> of the reference code.</summary>
    private double GetTurbulence(int channel, double x, double y, int octaves, bool isFractal, bool isStitching, Stitch stitch)
    {
        double sum = 0;
        double ratio = 1;
        for (var octave = 0; octave < octaves; octave++)
        {
            var noise = GetNoise(channel, x, y, isStitching, stitch);
            sum += (isFractal ? noise : Math.Abs(noise)) / ratio;
            x *= 2;
            y *= 2;
            ratio *= 2;
            if (isStitching)
            {
                stitch.Width *= 2;
                stitch.WrapX = (2 * stitch.WrapX) - PerlinOffset;
                stitch.Height *= 2;
                stitch.WrapY = (2 * stitch.WrapY) - PerlinOffset;
            }
        }

        return sum;
    }

    /// <summary>The noise of an octave at a point: <c>noise2</c> of the reference code. The integer parts are truncated toward zero, as in C.</summary>
    private double GetNoise(int channel, double x, double y, bool isStitching, Stitch stitch)
    {
        var t = x + PerlinOffset;
        var whole = (long)t;
        var bx0 = whole & LatticeMask;
        var bx1 = (bx0 + 1) & LatticeMask;
        var rx0 = t - whole;
        var rx1 = rx0 - 1;
        t = y + PerlinOffset;
        whole = (long)t;
        var by0 = whole & LatticeMask;
        var by1 = (by0 + 1) & LatticeMask;
        var ry0 = t - whole;
        var ry1 = ry0 - 1;
        if (isStitching)
        {
            if (bx0 >= stitch.WrapX)
            {
                bx0 -= stitch.Width;
            }

            if (bx1 >= stitch.WrapX)
            {
                bx1 -= stitch.Width;
            }

            if (by0 >= stitch.WrapY)
            {
                by0 -= stitch.Height;
            }

            if (by1 >= stitch.WrapY)
            {
                by1 -= stitch.Height;
            }
        }

        bx0 &= LatticeMask;
        bx1 &= LatticeMask;
        by0 &= LatticeMask;
        by1 &= LatticeMask;
        var i = _lattice[bx0];
        var j = _lattice[bx1];
        var b00 = _lattice[i + by0];
        var b10 = _lattice[j + by0];
        var b01 = _lattice[i + by1];
        var b11 = _lattice[j + by1];
        var sx = SCurve(rx0);
        var sy = SCurve(ry0);
        var u = (rx0 * _gradients[GradientIndex(channel, b00, 0)]) + (ry0 * _gradients[GradientIndex(channel, b00, 1)]);
        var v = (rx1 * _gradients[GradientIndex(channel, b10, 0)]) + (ry0 * _gradients[GradientIndex(channel, b10, 1)]);
        var a = Lerp(sx, u, v);
        u = (rx0 * _gradients[GradientIndex(channel, b01, 0)]) + (ry1 * _gradients[GradientIndex(channel, b01, 1)]);
        v = (rx1 * _gradients[GradientIndex(channel, b11, 0)]) + (ry1 * _gradients[GradientIndex(channel, b11, 1)]);
        var b = Lerp(sx, u, v);
        return Lerp(sy, a, b);
    }

    /// <summary>The lattice wrapping of the stitching: <c>StitchInfo</c> of the reference code.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Stitch
    {
        public long Width;
        public long Height;
        public long WrapX;
        public long WrapY;
    }
}
