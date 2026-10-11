using System.Numerics;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>
/// The MT19937 generator seeded and consumed exactly like CPython's <c>random.Random(int)</c> (init_by_array of the 32-bit
/// words of the seed; randrange/randint by rejection sampling of getrandbits), so seeded patterns stay byte-identical.
/// </summary>
internal sealed class PyRandom
{
    private const int N = 624;
    private const int M = 397;
    private readonly uint[] _mt = new uint[N];
    private int _index;

    public PyRandom(long seed)
    {
        var n = BigInteger.Abs(seed);
        var key = new List<uint>();
        do
        {
            key.Add((uint)(n & uint.MaxValue));
            n >>= 32;
        }
        while (!n.IsZero);
        InitByArray(key);
    }

    public int RandInt(int a, int b) => RandRange(a, b + 1);

    public int RandRange(int stop)
    {
        if (stop <= 0)
            throw new ArgumentOutOfRangeException(nameof(stop), "empty range for randrange()");
        return (int)RandBelow(stop);
    }

    public int RandRange(int start, int stop, int step = 1)
    {
        var width = (long)stop - start;
        if (step == 1)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(stop), "empty range for randrange()");
            return (int)(start + RandBelow(width));
        }

        if (step == 0)
            throw new ArgumentOutOfRangeException(nameof(step), "zero step for randrange()");
        var n = step > 0 ? Py.FloorDiv(width + step - 1, step) : Py.FloorDiv(width + step + 1, step);
        if (n <= 0)
            throw new ArgumentOutOfRangeException(nameof(stop), "empty range for randrange()");
        return (int)(start + step * RandBelow(n));
    }

    /// <summary>random(): 53-bit precision float in [0, 1).</summary>
    public double Random()
    {
        var a = GenRandUInt32() >> 5;
        var b = GenRandUInt32() >> 6;
        return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
    }

    public T Choice<T>(IReadOnlyList<T> items) => items[(int)RandBelow(items.Count)];

    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = (int)RandBelow(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    public BigInteger GetRandBits(int k)
    {
        if (k <= 32)
            return GenRandUInt32() >> (32 - k);

        var result = BigInteger.Zero;
        var shift = 0;
        while (k > 0)
        {
            var r = GenRandUInt32();
            if (k < 32)
                r >>= 32 - k;
            result |= (BigInteger)r << shift;
            shift += 32;
            k -= 32;
        }

        return result;
    }

    private long RandBelow(long n)
    {
        var k = Py.BitLength(n);
        var r = (long)GetRandBits(k);
        while (r >= n)
            r = (long)GetRandBits(k);
        return r;
    }

    private void InitGenRand(uint s)
    {
        _mt[0] = s;
        for (var i = 1; i < N; i++)
            _mt[i] = unchecked(1812433253u * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i);
        _index = N;
    }

    private void InitByArray(List<uint> key)
    {
        InitGenRand(19650218u);
        int i = 1, j = 0;
        for (var k = Math.Max(N, key.Count); k > 0; k--)
        {
            _mt[i] = unchecked((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j);
            i++;
            j++;
            if (i >= N)
            {
                _mt[0] = _mt[N - 1];
                i = 1;
            }

            if (j >= key.Count)
                j = 0;
        }

        for (var k = N - 1; k > 0; k--)
        {
            _mt[i] = unchecked((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941u)) - (uint)i);
            i++;
            if (i >= N)
            {
                _mt[0] = _mt[N - 1];
                i = 1;
            }
        }

        _mt[0] = 0x80000000u;
    }

    private uint GenRandUInt32()
    {
        if (_index >= N)
        {
            int kk;
            uint y;
            for (kk = 0; kk < N - M; kk++)
            {
                y = (_mt[kk] & 0x80000000u) | (_mt[kk + 1] & 0x7fffffffu);
                _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0);
            }

            for (; kk < N - 1; kk++)
            {
                y = (_mt[kk] & 0x80000000u) | (_mt[kk + 1] & 0x7fffffffu);
                _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0);
            }

            y = (_mt[N - 1] & 0x80000000u) | (_mt[0] & 0x7fffffffu);
            _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0);
            _index = 0;
        }

        var value = _mt[_index++];
        value ^= value >> 11;
        value ^= (value << 7) & 0x9d2c5680u;
        value ^= (value << 15) & 0xefc60000u;
        value ^= value >> 18;
        return value;
    }
}
