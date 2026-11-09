namespace Meziantou.Framework.VectorGraphics;

/// <summary>The options of <see cref="SvgSceneRenderer"/>.</summary>
public sealed class SvgRenderOptions
{
    internal const int DefaultPrecision = 2;

    private int _precision = DefaultPrecision;

    /// <summary>Gets or sets the number of decimals of the coordinates, from 0 to 15. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 15.</exception>
    public int Precision
    {
        get => _precision;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 15);
            _precision = value;
        }
    }

    /// <summary>
    /// Gets or sets the prefix of the identifiers of the gradients and clip paths. Give each drawing of an HTML page its
    /// own prefix, so the identifiers do not collide. The default is <c>scene</c>.
    /// </summary>
    public string IdPrefix { get; set; } = "scene";

    /// <summary>Gets or sets a value that indicates whether the <c>xmlns</c> attribute is written. Turn it off for SVG inlined in HTML. The default is <see langword="true"/>.</summary>
    public bool IncludeNamespace { get; set; } = true;
}
