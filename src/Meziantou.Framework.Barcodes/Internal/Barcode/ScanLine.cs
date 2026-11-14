namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// The runs of a scan line across linear barcodes: alternating light and dark widths, the first one light (possibly 0
/// wide), in any unit.
/// </summary>
internal sealed class ScanLine
{
    private readonly double[] _positions;

    public ScanLine(ReadOnlySpan<int> runs)
        : this(ToDouble(runs))
    {
    }

    private ScanLine(double[] runs)
    {
        Runs = runs;
        _positions = new double[Runs.Length + 1];
        for (var i = 0; i < Runs.Length; i++)
        {
            _positions[i + 1] = _positions[i] + Math.Max(0, Runs[i]);
        }
    }

    public double[] Runs { get; }

    public int Count => Runs.Length;

    public double TotalWidth => _positions[^1];

    /// <summary>Gets the position of the start of a run, or of the end of the line for <see cref="Count"/>.</summary>
    public double GetPosition(int run) => _positions[run];

    public double GetWidth(int start, int count) => _positions[start + count] - _positions[start];

    /// <summary>Gets whether the run is dark: the runs at odd indices.</summary>
    public static bool IsDark(int run) => (run & 1) is 1;

    /// <summary>
    /// Gets the line with the bars <paramref name="growth"/> narrower on each side and the spaces as much wider: the
    /// correction of the ink spread of a printed symbol, or of a threshold that is not in the middle of the gray levels.
    /// </summary>
    public ScanLine Compensate(double growth)
    {
        var runs = new double[Runs.Length];
        for (var i = 0; i < runs.Length; i++)
        {
            runs[i] = Math.Max(0.01, Runs[i] + (IsDark(i) ? -2 * growth : 2 * growth));
        }

        return new ScanLine(runs);
    }

    /// <summary>Gets the same line read from the other end: its runs reversed, starting with a light run.</summary>
    public ScanLine Reverse()
    {
        var reversed = new List<double>(Runs.Length + 1);
        if (IsDark(Runs.Length - 1))
        {
            reversed.Add(0);
        }

        for (var i = Runs.Length - 1; i >= 0; i--)
        {
            reversed.Add(Runs[i]);
        }

        return new ScanLine(reversed.ToArray());
    }

    private static double[] ToDouble(ReadOnlySpan<int> runs)
    {
        var result = new double[runs.Length];
        for (var i = 0; i < runs.Length; i++)
        {
            result[i] = runs[i];
        }

        return result;
    }
}
