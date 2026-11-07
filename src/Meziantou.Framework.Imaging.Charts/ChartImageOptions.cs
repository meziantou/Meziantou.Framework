using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Charts;

/// <summary>The options of <see cref="ChartImageRenderer"/>.</summary>
public sealed class ChartImageOptions
{
    private double _scale = 1;

    /// <summary>Gets or sets the number of pixels per unit of the chart: 2 draws a chart of 640 by 400 as an image of 1280 by 800, for high-density screens. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Scale
    {
        get => _scale;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scale must be a positive finite number.");

            _scale = value;
        }
    }

    /// <summary>Gets or sets the font of the regular texts, or <see langword="null"/> for <see cref="ChartFonts.Sans"/>. The layout measures the texts with it.</summary>
    public Font? Font { get; set; }

    /// <summary>Gets or sets the font of the bold texts (the title), or <see langword="null"/> for <see cref="ChartFonts.SansBold"/> when <see cref="Font"/> is not set, and <see cref="Font"/> otherwise.</summary>
    public Font? BoldFont { get; set; }

    internal Font RegularFont => Font ?? ChartFonts.Sans;

    internal Font BoldOrRegularFont => BoldFont ?? (Font is null ? ChartFonts.SansBold : Font);
}
