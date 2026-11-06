using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>The space between the edges of a chart and its plot area.</summary>
/// <param name="Top">The space at the top.</param>
/// <param name="Right">The space on the right.</param>
/// <param name="Bottom">The space at the bottom.</param>
/// <param name="Left">The space on the left.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ChartMargin(double Top, double Right, double Bottom, double Left)
{
    /// <summary>Initializes a new instance of the <see cref="ChartMargin"/> struct with the same space on every side.</summary>
    /// <param name="all">The space on every side.</param>
    public ChartMargin(double all)
        : this(all, all, all, all)
    {
    }
}
