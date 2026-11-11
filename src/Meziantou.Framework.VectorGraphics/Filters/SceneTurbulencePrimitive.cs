namespace Meziantou.Framework.VectorGraphics;

/// <summary>A primitive that fills its subregion with Perlin noise: <c>feTurbulence</c> of SVG.</summary>
/// <remarks>The noise is the one of the reference code of "Filter Effects Module Level 1", from the seed.</remarks>
public sealed class SceneTurbulencePrimitive : SceneFilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="SceneTurbulencePrimitive"/> class.</summary>
    /// <param name="type">The noise.</param>
    /// <param name="baseFrequencyX">The frequency of the first octave along x, in cycles per unit: <c>baseFrequency</c>.</param>
    /// <param name="baseFrequencyY">The frequency of the first octave along y.</param>
    /// <param name="octaves">The number of octaves: <c>numOctaves</c>.</param>
    /// <param name="seed">The seed of the random numbers: <c>seed</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not defined, a frequency is negative, or <paramref name="octaves"/> is negative.</exception>
    /// <exception cref="ArgumentException">A frequency is not a finite number.</exception>
    public SceneTurbulencePrimitive(SceneTurbulenceType type, double baseFrequencyX, double baseFrequencyY, int octaves, int seed)
    {
        Type = FilterValidation.Defined(type, SceneTurbulenceType.FractalNoise, nameof(type));
        BaseFrequencyX = FilterValidation.NonNegative(baseFrequencyX, nameof(baseFrequencyX));
        BaseFrequencyY = FilterValidation.NonNegative(baseFrequencyY, nameof(baseFrequencyY));
        ArgumentOutOfRangeException.ThrowIfNegative(octaves);
        Octaves = octaves;
        Seed = seed;
    }

    /// <summary>Gets the noise.</summary>
    public SceneTurbulenceType Type { get; }

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

    internal override IEnumerable<SceneFilterInput> GetInputs() => [];
}
