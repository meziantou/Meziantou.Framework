namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>A small deterministic generator (SplitMix64): the same seed produces the same mutations on every runtime and platform.</summary>
internal sealed class PdfFuzzRandom(ulong seed)
{
    private ulong _state = seed;

    /// <summary>Creates a generator for a seed and a name, such as the path of a fixture: the hash of the name is stable across processes.</summary>
    public static PdfFuzzRandom Create(ulong seed, string name)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in name)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return new PdfFuzzRandom(seed ^ hash);
    }

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Returns a value in [0, <paramref name="maxExclusive"/>).</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    /// <summary>Returns a value in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int minInclusive, int maxExclusive) => minInclusive + Next(maxExclusive - minInclusive);
}
