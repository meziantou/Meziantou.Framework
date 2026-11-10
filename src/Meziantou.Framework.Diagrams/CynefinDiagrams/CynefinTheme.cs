namespace Meziantou.Framework.Diagrams;

/// <summary>
/// The colors and sizes of a <see cref="CynefinDiagram"/> that replace the ones of the theme (the <c>cynefin</c> theme
/// variables of Mermaid). A <see langword="null"/> value keeps the one of the theme.
/// </summary>
public sealed class CynefinTheme
{
    private double? _boundaryWidth;
    private double? _cliffWidth;
    private double? _arrowWidth;
    private double? _domainFontSize;
    private double? _itemFontSize;

    /// <summary>Gets or sets the background of the complex domain (Mermaid <c>complexBg</c>).</summary>
    public SceneColor? ComplexBackground { get; set; }

    /// <summary>Gets or sets the background of the complicated domain (Mermaid <c>complicatedBg</c>).</summary>
    public SceneColor? ComplicatedBackground { get; set; }

    /// <summary>Gets or sets the background of the clear domain (Mermaid <c>clearBg</c>).</summary>
    public SceneColor? ClearBackground { get; set; }

    /// <summary>Gets or sets the background of the chaotic domain (Mermaid <c>chaoticBg</c>).</summary>
    public SceneColor? ChaoticBackground { get; set; }

    /// <summary>Gets or sets the background of the confusion domain, in the center (Mermaid <c>confusionBg</c>).</summary>
    public SceneColor? ConfusionBackground { get; set; }

    /// <summary>Gets or sets the color of the wavy boundaries between the domains (Mermaid <c>boundaryColor</c>).</summary>
    public SceneColor? BoundaryColor { get; set; }

    /// <summary>Gets or sets the width of the boundaries between the domains (Mermaid <c>boundaryWidth</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 100.</exception>
    public double? BoundaryWidth { get => _boundaryWidth; set => _boundaryWidth = Check(value, 100); }

    /// <summary>Gets or sets the color of the cliff between the clear and the chaotic domains (Mermaid <c>cliffColor</c>).</summary>
    public SceneColor? CliffColor { get; set; }

    /// <summary>Gets or sets the width of the cliff between the clear and the chaotic domains (Mermaid <c>cliffWidth</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 100.</exception>
    public double? CliffWidth { get => _cliffWidth; set => _cliffWidth = Check(value, 100); }

    /// <summary>Gets or sets the color of the arrows of the transitions (Mermaid <c>arrowColor</c>).</summary>
    public SceneColor? ArrowColor { get; set; }

    /// <summary>Gets or sets the width of the arrows of the transitions (Mermaid <c>arrowWidth</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 100.</exception>
    public double? ArrowWidth { get => _arrowWidth; set => _arrowWidth = Check(value, 100); }

    /// <summary>Gets or sets the color of the names of the domains (Mermaid <c>labelColor</c>).</summary>
    public SceneColor? LabelColor { get; set; }

    /// <summary>Gets or sets the color of the items, of the descriptions of the domains and of the texts of the transitions (Mermaid <c>textColor</c>).</summary>
    public SceneColor? TextColor { get; set; }

    /// <summary>Gets or sets the size of the names of the domains (Mermaid <c>domainFontSize</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 1000.</exception>
    public double? DomainFontSize { get => _domainFontSize; set => _domainFontSize = CheckFontSize(value); }

    /// <summary>Gets or sets the size of the items; the descriptions and the texts of the transitions are one pixel smaller (Mermaid <c>itemFontSize</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 2 and 1000.</exception>
    public double? ItemFontSize { get => _itemFontSize; set => _itemFontSize = CheckFontSize(value, minimum: 2); }

    private static double? Check(double? value, double maximum)
    {
        if (value is { } number && !(number >= 0 && number <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value is negative, too large or not a number.");

        return value;
    }

    private static double? CheckFontSize(double? value, double minimum = 1)
    {
        if (value is { } number && !(number >= minimum && number <= 1000))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The font size is too small, too large or not a number.");

        return value;
    }
}
