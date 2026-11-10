namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>A small pseudo-random generator (Mulberry32), so the random restarts of the layout give the same result everywhere.</summary>
internal sealed class VennRandom(uint seed)
{
    private uint _state = seed;

    /// <summary>Returns a number in [0, 1).</summary>
    public double NextDouble()
    {
        unchecked
        {
            _state += 0x6D2B79F5;
            var t = _state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + ((t ^ (t >> 7)) * (t | 61));
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
