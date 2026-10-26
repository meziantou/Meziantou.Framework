using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The normalization options of a <see cref="PeriodicWave"/> (https://webaudio.github.io/web-audio-api/#PeriodicWaveConstraints).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#PeriodicWaveConstraints"/>
[IdlInterface("PeriodicWaveConstraints", "https://webaudio.github.io/web-audio-api/#PeriodicWaveConstraints")]
public class PeriodicWaveConstraints
{
    /// <summary>Gets a value indicating whether the waveform is not normalized (by default, its maximum is 1).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-periodicwaveconstraints-disablenormalization"/>
    [IdlMember("disableNormalization", "https://webaudio.github.io/web-audio-api/#dom-periodicwaveconstraints-disablenormalization")]
    public bool DisableNormalization { get; init; }
}

/// <summary>The options of a <see cref="PeriodicWave"/> (https://webaudio.github.io/web-audio-api/#PeriodicWaveOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#PeriodicWaveOptions"/>
[IdlInterface("PeriodicWaveOptions", "https://webaudio.github.io/web-audio-api/#PeriodicWaveOptions")]
public sealed class PeriodicWaveOptions : PeriodicWaveConstraints
{
    /// <summary>Gets the cosine terms of the Fourier series (the first one is the DC offset, which is ignored).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-periodicwaveoptions-real"/>
    [IdlMember("real", "https://webaudio.github.io/web-audio-api/#dom-periodicwaveoptions-real")]
    public IReadOnlyList<float>? Real { get; init; }

    /// <summary>Gets the sine terms of the Fourier series (the first one is ignored).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-periodicwaveoptions-imag"/>
    [IdlMember("imag", "https://webaudio.github.io/web-audio-api/#dom-periodicwaveoptions-imag")]
    public IReadOnlyList<float>? Imag { get; init; }
}

/// <summary>
/// An arbitrary periodic waveform for an <see cref="OscillatorNode"/> (https://webaudio.github.io/web-audio-api/#PeriodicWave),
/// defined by the coefficients of its Fourier series.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#PeriodicWave"/>
[IdlInterface("PeriodicWave", "https://webaudio.github.io/web-audio-api/#PeriodicWave")]
public sealed class PeriodicWave
{
    /// <summary>The number of points of the wavetables, and of the computation of the normalization (twice the maximum number of partials).</summary>
    internal const int TableSize = 4096;

    /// <summary>The number of partials up to which the waveform is computed directly (sums of sines and cosines) rather than from a wavetable.</summary>
    private const int MaxDirectPartials = 64;

    private readonly Dictionary<int, float[]> _tables = [];

    /// <summary>Creates a periodic wave.</summary>
    /// <exception cref="DomException">The coefficient arrays have different lengths, or a length less than 2 (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-periodicwave-periodicwave"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-periodicwave-periodicwave")]
    public PeriodicWave(BaseAudioContext context, PeriodicWaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        var real = options?.Real;
        var imag = options?.Imag;
        int length;
        if (real is not null && imag is not null)
        {
            if (real.Count != imag.Count)
                throw new DomException($"The real ({real.Count}) and imaginary ({imag.Count}) coefficients have different lengths", "IndexSizeError");

            length = real.Count;
        }
        else
        {
            length = real?.Count ?? imag?.Count ?? 2;
        }

        if (length < 2)
            throw new DomException($"The coefficients must have at least 2 elements, not {length}", "IndexSizeError");

        Real = new double[length];
        Imag = new double[length];
        if (real is null && imag is null)
        {
            Imag[1] = 1;
        }
        else
        {
            for (var i = 1; i < length; i++)
            {
                Real[i] = real is null ? 0 : real[i];
                Imag[i] = imag is null ? 0 : imag[i];
            }
        }

        Normalize = !(options?.DisableNormalization ?? false);
        NormalizationScale = Normalize ? ComputeNormalizationScale() : 1;
    }

    private PeriodicWave(BaseAudioContext context, double[] real, double[] imag)
    {
        Context = context;
        Real = real;
        Imag = imag;
        Normalize = true;
        NormalizationScale = ComputeNormalizationScale();
    }

    internal BaseAudioContext Context { get; }

    /// <summary>The cosine coefficients ([[real]]; the DC offset is 0).</summary>
    internal double[] Real { get; }

    /// <summary>The sine coefficients ([[imag]]).</summary>
    internal double[] Imag { get; }

    /// <summary>[[normalize]].</summary>
    internal bool Normalize { get; }

    /// <summary>The factor applied to the waveform so that its maximum is 1 (https://webaudio.github.io/web-audio-api/#waveform-normalization), or 1.</summary>
    internal double NormalizationScale { get; }

    /// <summary>The number of partials of the waveform (the maximum harmonic), limited to the size of the wavetables.</summary>
    internal int PartialCount => Math.Min(Real.Length - 1, TableSize / 2);

    /// <summary>Creates the waveform of a built-in oscillator type (https://webaudio.github.io/web-audio-api/#oscillator-coefficients).</summary>
    internal static PeriodicWave CreateBuiltIn(BaseAudioContext context, OscillatorType type)
    {
        var length = type == OscillatorType.Sine ? 2 : (TableSize / 2) + 1;
        var real = new double[length];
        var imag = new double[length];
        for (var n = 1; n < length; n++)
        {
            imag[n] = type switch
            {
                OscillatorType.Sine => n == 1 ? 1 : 0,
                OscillatorType.Square => 2 / (n * Math.PI) * (1 - ((n & 1) == 0 ? 1 : -1)),
                OscillatorType.Sawtooth => ((n & 1) == 1 ? 1 : -1) * 2 / (n * Math.PI),
                OscillatorType.Triangle => 8 * Math.Sin(n * Math.PI / 2) / (Math.PI * Math.PI * n * n),
                _ => 0,
            };
        }

        return new PeriodicWave(context, real, imag);
    }

    /// <summary>The normalization factor: the inverse of the maximum of the waveform sampled at <see cref="TableSize"/> points.</summary>
    private double ComputeNormalizationScale()
    {
        var samples = ComputeTable(PartialCount);
        var max = 0.0;
        foreach (var sample in samples)
        {
            max = Math.Max(max, Math.Abs(sample));
        }

        return max == 0 ? 1 : 1 / max;
    }

    /// <summary>The waveform (not normalized) with the partials up to a harmonic, sampled at <see cref="TableSize"/> points (an inverse FFT).</summary>
    private double[] ComputeTable(int partials)
    {
        var real = new double[TableSize];
        var imag = new double[TableSize];
        for (var k = 1; k <= partials; k++)
        {
            // x(n) = sum a[k] cos(2πkn/N) + b[k] sin(2πkn/N) = Re(sum (a[k] - i b[k]) e^(2πikn/N))
            real[k] = Real[k];
            imag[k] = -Imag[k];
        }

        Fft.Get(TableSize).Transform(real, imag, inverse: true);
        return real;
    }

    /// <summary>
    /// Computes a sample of the normalized waveform at a phase (in cycles), band-limited to a number of partials: the
    /// partials are summed directly when there are few of them, else the sample is interpolated from a wavetable.
    /// </summary>
    internal float GetSample(double phase, int partials)
    {
        if (partials <= 0)
            return 0;

        if (partials > PartialCount)
        {
            partials = PartialCount;
        }

        if (partials <= MaxDirectPartials)
            return (float)(SumPartials(phase, partials) * NormalizationScale);

        var table = GetTable(partials);
        var position = (phase - Math.Floor(phase)) * TableSize;
        var index = (int)position;
        var fraction = position - index;
        var s0 = table[index & (TableSize - 1)];
        var s1 = table[(index + 1) & (TableSize - 1)];
        return (float)(s0 + ((s1 - s0) * fraction));
    }

    private double SumPartials(double phase, int partials)
    {
        // The sines and cosines of the harmonics are computed by rotation from the fundamental
        var angle = 2 * Math.PI * (phase - Math.Floor(phase));
        var c1 = Math.Cos(angle);
        var s1 = Math.Sin(angle);
        var c = c1;
        var s = s1;
        var sum = 0.0;
        for (var k = 1; k <= partials; k++)
        {
            sum += (Real[k] * c) + (Imag[k] * s);
            var next = (c * c1) - (s * s1);
            s = (s * c1) + (c * s1);
            c = next;
        }

        return sum;
    }

    /// <summary>The normalized wavetable of a number of partials, rounded down to a third of an octave (the tables are cached).</summary>
    private float[] GetTable(int partials)
    {
        var bucket = (int)Math.Floor(Math.Pow(2, Math.Floor(Math.Log2(partials) * 3) / 3));
        bucket = Math.Clamp(bucket, 1, PartialCount);
        if (!_tables.TryGetValue(bucket, out var table))
        {
            var samples = ComputeTable(bucket);
            table = new float[TableSize];
            for (var i = 0; i < TableSize; i++)
            {
                table[i] = (float)(samples[i] * NormalizationScale);
            }

            _tables[bucket] = table;
        }

        return table;
    }
}
