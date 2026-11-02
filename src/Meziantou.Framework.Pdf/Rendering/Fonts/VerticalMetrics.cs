using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// The vertical metrics of the CIDs of a CIDFont (ISO 32000-2, 9.7.4.3): the vertical displacement w1y and the position
/// vector (vx, vy) of each CID, from <c>/W2</c>, or <c>/DW2</c> and half the horizontal width; in text space units.
/// </summary>
internal sealed class VerticalMetrics
{
    // The CIDs the /W2 array may assign, as a budget: repeated entries would otherwise cost the square of its size
    private const int MaxEntries = 65536;

    private readonly Dictionary<int, (double W1, double Vx, double Vy)> _metrics = [];
    private readonly List<(int First, int Last, double W1, double Vx, double Vy)> _ranges = [];
    private readonly double _defaultVy = 0.88;
    private readonly double _defaultW1 = -1;

    public VerticalMetrics(PdfDictionary cidFont)
    {
        // DW2: the vertical component of the position vector and the vertical displacement, 880 and -1000 by default
        if (cidFont.Get<PdfArray>(PdfName.Create("DW2")) is { Count: 2 } defaults && IsFinite(defaults.GetNumber(0)) && IsFinite(defaults.GetNumber(1)))
        {
            _defaultVy = defaults.GetNumber(0) / 1000;
            _defaultW1 = defaults.GetNumber(1) / 1000;
        }

        if (cidFont.Get<PdfArray>(PdfName.Create("W2")) is { } array)
        {
            Read(array);
        }
    }

    /// <summary>Gets the vertical displacement and the position vector of a CID; the position vector is half the horizontal width <paramref name="width"/> when the CID has no metrics.</summary>
    public (double W1, double Vx, double Vy) Get(int cid, double width)
    {
        if (_metrics.TryGetValue(cid, out var metrics))
            return metrics;

        // The last range that has the CID wins, as a later entry of the array would; the result is kept for the next glyphs
        for (var i = _ranges.Count - 1; i >= 0; i--)
        {
            var range = _ranges[i];
            if (cid >= range.First && cid <= range.Last)
            {
                metrics = (range.W1, range.Vx, range.Vy);
                if (_metrics.Count < 2 * MaxEntries)
                {
                    _metrics[cid] = metrics;
                }

                return metrics;
            }
        }

        return (_defaultW1, width / 2, _defaultVy);
    }

    /// <summary>Reads the /W2 array: <c>c [w1y v1x v1y w1y v1x v1y ...]</c> or <c>cFirst cLast w1y v1x v1y</c>.</summary>
    private void Read(PdfArray array)
    {
        var budget = MaxEntries;
        var i = 0;
        while (i < array.Count && budget > 0)
        {
            if (array.GetResolved(i) is not PdfNumber first)
                return;

            if (i + 1 < array.Count && array.GetResolved(i + 1) is PdfArray values)
            {
                var cid = (int)first.DoubleValue;
                for (var j = 0; j + 2 < values.Count && budget > 0; j += 3, budget--)
                {
                    if (TryRead(values, j, out var metrics))
                    {
                        _metrics[cid] = metrics;
                    }

                    cid++;
                }

                i += 2;
            }
            else if (i + 4 < array.Count && array.GetResolved(i + 1) is PdfNumber last)
            {
                if (TryRead(array, i + 2, out var metrics) && last.DoubleValue >= first.DoubleValue)
                {
                    _ranges.Add(((int)first.DoubleValue, (int)Math.Min(int.MaxValue, last.DoubleValue), metrics.W1, metrics.Vx, metrics.Vy));
                    budget--;
                }

                i += 5;
            }
            else
            {
                return;
            }
        }
    }

    private static bool TryRead(PdfArray array, int index, out (double W1, double Vx, double Vy) metrics)
    {
        var w1 = array.GetNumber(index);
        var vx = array.GetNumber(index + 1);
        var vy = array.GetNumber(index + 2);
        metrics = (w1 / 1000, vx / 1000, vy / 1000);
        return IsFinite(w1) && IsFinite(vx) && IsFinite(vy);
    }

    private static bool IsFinite(double value) => double.IsFinite(value);
}
