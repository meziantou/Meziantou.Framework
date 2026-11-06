namespace Meziantou.Framework.Charts;

/// <summary>
/// Replaces the rows of each series (the <c>Series</c> channel, else the colors) with the points of a polynomial fitted
/// to them by least squares (<see cref="Regression"/>), across the range of their x values: a trend line when used with
/// a <see cref="LineMark{T}"/>. The tooltip of each line is its equation. A series with too few points is not drawn.
/// </summary>
public sealed class RegressionTransform : Transform
{
    private int _degree = 1;
    private int _samples = 50;

    /// <summary>Gets or sets the degree of the polynomial: 1 for a straight line (the default), 2 for a parabola. From 0 to 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 10.</exception>
    public int Degree
    {
        get => _degree;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 10);
            _degree = value;
        }
    }

    /// <summary>Gets or sets the number of points of each curve. A straight line has 2. The default is 50.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 2.</exception>
    public int Samples
    {
        get => _samples;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            _samples = value;
        }
    }

    internal override MarkData Apply(MarkData data)
    {
        if (data.X is not { IsContinuous: true } x || data.Y is not { IsContinuous: true } y)
            return data;

        var xs = new List<double>();
        var ys = new List<double>();
        var rows = new List<int>();
        var titles = new List<string?>();
        foreach (var group in MarkData.GroupBy(data.Count, data.SeriesKey))
        {
            var points = group.Select(row => (X: x.Numbers[row], Y: y.Numbers[row])).Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y)).ToArray();
            if (points.Select(point => point.X).Distinct().Count() <= Degree)
                continue;

            var fit = Regression.Fit(points, Degree);
            var minimum = points.Min(point => point.X);
            var maximum = points.Max(point => point.X);
            var count = Degree <= 1 ? 2 : Samples;
            for (var i = 0; i < count; i++)
            {
                var value = minimum + ((maximum - minimum) * i / (count - 1));
                xs.Add(value);
                ys.Add(fit.Evaluate(value));
                rows.Add(group[0]);
                titles.Add(fit.ToString());
            }
        }

        var result = data.Select(rows);
        result.X = ChannelValues.FromNumbers([.. xs], x.Kind);
        result.Y = ChannelValues.FromNumbers([.. ys]);
        result.Y1 = null;
        result.Y2 = null;
        result.Titles = [.. titles];
        return result;
    }
}
