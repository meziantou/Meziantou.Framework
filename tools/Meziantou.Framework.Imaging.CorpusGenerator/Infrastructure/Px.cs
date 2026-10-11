using System.Collections;
using System.Globalization;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>A pixel: an immutable tuple of 1 to 4 samples with value equality and lexicographic ordering.</summary>
internal readonly struct Px : IEquatable<Px>, IComparable<Px>, IReadOnlyList<int>
{
    private readonly int _a;
    private readonly int _b;
    private readonly int _c;
    private readonly int _d;

    public Px(int a)
    {
        _a = a;
        Count = 1;
    }

    public Px(int a, int b)
    {
        (_a, _b) = (a, b);
        Count = 2;
    }

    public Px(int a, int b, int c)
    {
        (_a, _b, _c) = (a, b, c);
        Count = 3;
    }

    public Px(int a, int b, int c, int d)
    {
        (_a, _b, _c, _d) = (a, b, c, d);
        Count = 4;
    }

    public int Count { get; }

    public int this[int index] => (uint)index < (uint)Count
        ? index switch { 0 => _a, 1 => _b, 2 => _c, _ => _d }
        : throw new ArgumentOutOfRangeException(nameof(index));

    public static Px From(ReadOnlySpan<int> values) => values.Length switch
    {
        1 => new Px(values[0]),
        2 => new Px(values[0], values[1]),
        3 => new Px(values[0], values[1], values[2]),
        4 => new Px(values[0], values[1], values[2], values[3]),
        _ => throw new ArgumentOutOfRangeException(nameof(values), "A pixel has 1 to 4 samples"),
    };

    public static Px From(IEnumerable<int> values) => From([.. values]);

    public static Px From(ReadOnlySpan<byte> values)
    {
        Span<int> samples = stackalloc int[values.Length];
        for (var i = 0; i < values.Length; i++)
            samples[i] = values[i];
        return From(samples);
    }

    public static bool operator ==(Px left, Px right) => left.Equals(right);

    public static bool operator !=(Px left, Px right) => !left.Equals(right);

    public static bool operator <(Px left, Px right) => left.CompareTo(right) < 0;

    public static bool operator >(Px left, Px right) => left.CompareTo(right) > 0;

    public static bool operator <=(Px left, Px right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Px left, Px right) => left.CompareTo(right) >= 0;

    /// <summary>p[start:start + length] (clamped like a Python slice).</summary>
    public Px Slice(int start, int length)
    {
        var end = Math.Min(Count, start + length);
        return From(this.Skip(start).Take(end - start));
    }

    /// <summary>The tuple with one more sample (p + (value,)).</summary>
    public Px Append(int value) => From(this.Append<int>(value));

    /// <summary>Tuple concatenation (p + q).</summary>
    public Px Concat(Px other) => From(this.Concat<int>(other));

    /// <summary>The tuple with one sample replaced.</summary>
    public Px With(int index, int value) => From(this.Select((v, i) => i == index ? value : v));

    public Px Select(Func<int, int> selector) => From(Enumerable.Select(this, selector));

    public int[] ToArray() => [.. this];

    public bool Equals(Px other)
    {
        if (Count != other.Count)
            return false;
        for (var i = 0; i < Count; i++)
        {
            if (this[i] != other[i])
                return false;
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Px other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Count, _a, _b, _c, _d);

    public int CompareTo(Px other)
    {
        for (var i = 0; i < Math.Min(Count, other.Count); i++)
        {
            var comparison = this[i].CompareTo(other[i]);
            if (comparison != 0)
                return comparison;
        }

        return Count.CompareTo(other.Count);
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The Python tuple repr: "(1, 2, 3)", "(1,)".</summary>
    public override string ToString()
    {
        var values = this.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList();
        return values.Count == 1 ? "(" + values[0] + ",)" : "(" + string.Join(", ", values) + ")";
    }
}
