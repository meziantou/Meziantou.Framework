namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>A small deterministic generator (SplitMix64): the same seed produces the same mutations on every runtime and platform.</summary>
internal sealed class FuzzRandom(ulong seed)
{
    private ulong _state = seed;

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

    public bool Chance(int percent) => Next(100) < percent;

    public byte NextByte() => (byte)NextUInt64();

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
}
