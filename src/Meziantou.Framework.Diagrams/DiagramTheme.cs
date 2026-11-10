namespace Meziantou.Framework.Diagrams;

/// <summary>
/// The colors and the font of a diagram: the variables of the themes of Mermaid. <see cref="Default"/>, <see cref="Dark"/>,
/// <see cref="Forest"/>, <see cref="Neutral"/> and <see cref="Base"/> are the themes of Mermaid 12; change a value with
/// <c>with</c>: <c>DiagramTheme.Default with { NodeBackground = "#e0f2fe" }</c>.
/// </summary>
public sealed record DiagramTheme
{
    private static readonly Lazy<DiagramTheme> DefaultTheme = new(CreateDefault);
    private static readonly Lazy<DiagramTheme> DarkTheme = new(CreateDark);
    private static readonly Lazy<DiagramTheme> ForestTheme = new(CreateForest);
    private static readonly Lazy<DiagramTheme> NeutralTheme = new(CreateNeutral);
    private static readonly Lazy<DiagramTheme> BaseTheme = new(CreateBase);

    /// <summary>Gets the default theme of Mermaid: lavender nodes and yellow clusters.</summary>
    public static DiagramTheme Default => DefaultTheme.Value;

    /// <summary>Gets the dark theme of Mermaid.</summary>
    public static DiagramTheme Dark => DarkTheme.Value;

    /// <summary>Gets the forest theme of Mermaid: shades of green.</summary>
    public static DiagramTheme Forest => ForestTheme.Value;

    /// <summary>Gets the neutral theme of Mermaid: shades of gray, for printing.</summary>
    public static DiagramTheme Neutral => NeutralTheme.Value;

    /// <summary>Gets the base theme of Mermaid, the starting point of custom themes.</summary>
    public static DiagramTheme Base => BaseTheme.Value;

    /// <summary>Gets the CSS <c>font-family</c> of the texts, or <see langword="null"/> for the family of the text measurer.</summary>
    /// <remarks>The texts are laid out with the text measurer: a family with other widths can overflow the shapes.</remarks>
    public string? FontFamily { get; init; }

    /// <summary>Gets the size of the texts, in pixels. The default is 16, the size of Mermaid.</summary>
    public double FontSize { get; init; } = 16;

    /// <summary>Gets the size of the title of the diagram, in pixels. The default is 18.</summary>
    public double TitleFontSize { get; init; } = 18;

    /// <summary>Gets the width of the outlines of the shapes. The default is 1.</summary>
    public double StrokeWidth { get; init; } = 1;

    /// <summary>Gets a value that indicates whether the theme has light texts on dark colors.</summary>
    public bool IsDark { get; init; }

    /// <summary>Gets the background of the diagram, or <see langword="null"/> for a transparent background (Mermaid <c>background</c>).</summary>
    public SceneColor? Background { get; init; }

    /// <summary>Gets the main color, the base of the others (Mermaid <c>primaryColor</c>).</summary>
    public SceneColor PrimaryColor { get; init; }

    /// <summary>Gets the color of the outlines drawn on <see cref="PrimaryColor"/> (Mermaid <c>primaryBorderColor</c>).</summary>
    public SceneColor PrimaryBorderColor { get; init; }

    /// <summary>Gets the color of the texts drawn on <see cref="PrimaryColor"/> (Mermaid <c>primaryTextColor</c>).</summary>
    public SceneColor PrimaryTextColor { get; init; }

    /// <summary>Gets the second color, for the elements that must stand out from the main ones (Mermaid <c>secondaryColor</c>).</summary>
    public SceneColor SecondaryColor { get; init; }

    /// <summary>Gets the color of the outlines drawn on <see cref="SecondaryColor"/> (Mermaid <c>secondaryBorderColor</c>).</summary>
    public SceneColor SecondaryBorderColor { get; init; }

    /// <summary>Gets the color of the texts drawn on <see cref="SecondaryColor"/> (Mermaid <c>secondaryTextColor</c>).</summary>
    public SceneColor SecondaryTextColor { get; init; }

    /// <summary>Gets the third color (Mermaid <c>tertiaryColor</c>).</summary>
    public SceneColor TertiaryColor { get; init; }

    /// <summary>Gets the color of the outlines drawn on <see cref="TertiaryColor"/> (Mermaid <c>tertiaryBorderColor</c>).</summary>
    public SceneColor TertiaryBorderColor { get; init; }

    /// <summary>Gets the color of the texts drawn on <see cref="TertiaryColor"/> (Mermaid <c>tertiaryTextColor</c>).</summary>
    public SceneColor TertiaryTextColor { get; init; }

    /// <summary>Gets the fill of the nodes (Mermaid <c>mainBkg</c>).</summary>
    public SceneColor NodeBackground { get; init; }

    /// <summary>Gets the outline of the nodes (Mermaid <c>nodeBorder</c>).</summary>
    public SceneColor NodeBorderColor { get; init; }

    /// <summary>Gets the color of the labels of the nodes (Mermaid <c>nodeTextColor</c>, or <c>textColor</c>).</summary>
    public SceneColor NodeTextColor { get; init; }

    /// <summary>Gets the color of the lines of the edges (Mermaid <c>lineColor</c>).</summary>
    public SceneColor LineColor { get; init; }

    /// <summary>Gets the color of the arrowheads (Mermaid <c>arrowheadColor</c>).</summary>
    public SceneColor ArrowheadColor { get; init; }

    /// <summary>Gets the color of the texts drawn on the background, such as the labels of the edges (Mermaid <c>textColor</c>).</summary>
    public SceneColor TextColor { get; init; }

    /// <summary>Gets the color of the titles of the diagram and of the clusters (Mermaid <c>titleColor</c>).</summary>
    public SceneColor TitleColor { get; init; }

    /// <summary>Gets the background of the labels of the edges (Mermaid <c>edgeLabelBackground</c>).</summary>
    public SceneColor EdgeLabelBackground { get; init; }

    /// <summary>Gets the fill of the clusters, such as the subgraphs of a flowchart (Mermaid <c>clusterBkg</c>).</summary>
    public SceneColor ClusterBackground { get; init; }

    /// <summary>Gets the outline of the clusters (Mermaid <c>clusterBorder</c>).</summary>
    public SceneColor ClusterBorderColor { get; init; }

    /// <summary>Gets the fill of the notes (Mermaid <c>noteBkgColor</c>).</summary>
    public SceneColor NoteBackground { get; init; }

    /// <summary>Gets the outline of the notes (Mermaid <c>noteBorderColor</c>).</summary>
    public SceneColor NoteBorderColor { get; init; }

    /// <summary>Gets the color of the texts of the notes (Mermaid <c>noteTextColor</c>).</summary>
    public SceneColor NoteTextColor { get; init; }

    /// <summary>Gets the fill of the odd rows of a table, such as the attributes of an entity (Mermaid <c>rowOdd</c>).</summary>
    public SceneColor RowOddBackground { get; init; }

    /// <summary>Gets the fill of the even rows of a table (Mermaid <c>rowEven</c>).</summary>
    public SceneColor RowEvenBackground { get; init; }

    /// <summary>Gets the fill of the start and end states (Mermaid <c>specialStateColor</c>).</summary>
    public SceneColor SpecialStateColor { get; init; }

    /// <summary>Gets the inner circle of the end states (Mermaid <c>innerEndBackground</c>).</summary>
    public SceneColor InnerEndBackground { get; init; }

    /// <summary>Gets the fill of the composite states (Mermaid <c>compositeBackground</c>).</summary>
    public SceneColor CompositeBackground { get; init; }

    /// <summary>Gets the fill of the title bar of the composite states (Mermaid <c>compositeTitleBackground</c>).</summary>
    public SceneColor CompositeTitleBackground { get; init; }

    /// <summary>Gets an alternate fill, such as the regions of a concurrent state (Mermaid <c>altBackground</c>).</summary>
    public SceneColor AlternateBackground { get; init; }

    /// <summary>Gets the colors of the categories, such as the sections of a mindmap or the groups of an architecture diagram (Mermaid <c>cScale0</c> to <c>cScale11</c>).</summary>
    public IReadOnlyList<SceneColor> Palette { get; init; } = [];

    /// <summary>Returns the color of a category: the colors of <see cref="Palette"/> repeat.</summary>
    /// <param name="index">The index of the category.</param>
    /// <returns>The color, or <see cref="PrimaryColor"/> when the palette is empty.</returns>
    public SceneColor GetPaletteColor(int index)
    {
        if (Palette.Count is 0)
            return PrimaryColor;

        var count = Palette.Count;
        return Palette[((index % count) + count) % count];
    }

    private static DiagramTheme CreateDefault() => new()
    {
        Background = SceneColor.Parse("#ffffff"),
        PrimaryColor = SceneColor.Parse("#ececff"),
        PrimaryBorderColor = SceneColor.Parse("#c7c7f1"),
        PrimaryTextColor = SceneColor.Parse("#131300"),
        SecondaryColor = SceneColor.Parse("#ffffde"),
        SecondaryBorderColor = SceneColor.Parse("#eeeebc"),
        SecondaryTextColor = SceneColor.Parse("#000021"),
        TertiaryColor = SceneColor.Parse("#f9ffec"),
        TertiaryBorderColor = SceneColor.Parse("#e3f1c7"),
        TertiaryTextColor = SceneColor.Parse("#060013"),
        NodeBackground = SceneColor.Parse("#ececff"),
        NodeBorderColor = SceneColor.Parse("#9370db"),
        NodeTextColor = SceneColor.Parse("#333333"),
        LineColor = SceneColor.Parse("#333333"),
        ArrowheadColor = SceneColor.Parse("#333333"),
        TextColor = SceneColor.Parse("#333333"),
        TitleColor = SceneColor.Parse("#333333"),
        EdgeLabelBackground = SceneColor.Parse("#e8e8e8cc"),
        ClusterBackground = SceneColor.Parse("#ffffde"),
        ClusterBorderColor = SceneColor.Parse("#aaaa33"),
        NoteBackground = SceneColor.Parse("#fff5ad"),
        NoteBorderColor = SceneColor.Parse("#aaaa33"),
        NoteTextColor = SceneColor.Parse("#000000"),
        RowOddBackground = SceneColor.Parse("#ffffff"),
        RowEvenBackground = SceneColor.Parse("#f1f1ff"),
        SpecialStateColor = SceneColor.Parse("#333333"),
        InnerEndBackground = SceneColor.Parse("#9370db"),
        CompositeBackground = SceneColor.Parse("#ffffff"),
        CompositeTitleBackground = SceneColor.Parse("#ececff"),
        AlternateBackground = SceneColor.Parse("#f0f0f0"),
        Palette = [SceneColor.Parse("#8686ff"), SceneColor.Parse("#ffff78"), SceneColor.Parse("#d7ff86"), SceneColor.Parse("#c286ff"), SceneColor.Parse("#ff86ff"), SceneColor.Parse("#ff86c2"), SceneColor.Parse("#ff8686"), SceneColor.Parse("#ffc286"), SceneColor.Parse("#c2ff86"), SceneColor.Parse("#86ffc2"), SceneColor.Parse("#86ffff"), SceneColor.Parse("#86c2ff")],
        IsDark = false,
    };

    private static DiagramTheme CreateDark() => new()
    {
        Background = SceneColor.Parse("#333333"),
        PrimaryColor = SceneColor.Parse("#1f2020"),
        PrimaryBorderColor = SceneColor.Parse("#cccccc"),
        PrimaryTextColor = SceneColor.Parse("#e0dfdf"),
        SecondaryColor = SceneColor.Parse("#474949"),
        SecondaryBorderColor = SceneColor.Parse("#2f2f2f"),
        SecondaryTextColor = SceneColor.Parse("#b8b6b6"),
        TertiaryColor = SceneColor.Parse("#201f1f"),
        TertiaryBorderColor = SceneColor.Parse("#060606"),
        TertiaryTextColor = SceneColor.Parse("#dfe0e0"),
        NodeBackground = SceneColor.Parse("#1f2020"),
        NodeBorderColor = SceneColor.Parse("#cccccc"),
        NodeTextColor = SceneColor.Parse("#cccccc"),
        LineColor = SceneColor.Parse("#d3d3d3"),
        ArrowheadColor = SceneColor.Parse("#d3d3d3"),
        TextColor = SceneColor.Parse("#cccccc"),
        TitleColor = SceneColor.Parse("#f9fffe"),
        EdgeLabelBackground = SceneColor.Parse("#585858"),
        ClusterBackground = SceneColor.Parse("#474949"),
        ClusterBorderColor = SceneColor.Parse("#ffffff40"),
        NoteBackground = SceneColor.Parse("#474949"),
        NoteBorderColor = SceneColor.Parse("#2f2f2f"),
        NoteTextColor = SceneColor.Parse("#b8b6b6"),
        RowOddBackground = SceneColor.Parse("#2c2d2d"),
        RowEvenBackground = SceneColor.Parse("#060606"),
        SpecialStateColor = SceneColor.Parse("#f4f4f4"),
        InnerEndBackground = SceneColor.Parse("#cccccc"),
        CompositeBackground = SceneColor.Parse("#333333"),
        CompositeTitleBackground = SceneColor.Parse("#1f2020"),
        AlternateBackground = SceneColor.Parse("#555555"),
        Palette = [SceneColor.Parse("#1f2020"), SceneColor.Parse("#0b0000"), SceneColor.Parse("#4d1037"), SceneColor.Parse("#3f5258"), SceneColor.Parse("#4f2f1b"), SceneColor.Parse("#6e0a0a"), SceneColor.Parse("#3b0048"), SceneColor.Parse("#995a01"), SceneColor.Parse("#154706"), SceneColor.Parse("#161722"), SceneColor.Parse("#00296f"), SceneColor.Parse("#01629c")],
        IsDark = true,
    };

    private static DiagramTheme CreateForest() => new()
    {
        Background = SceneColor.Parse("#ffffff"),
        PrimaryColor = SceneColor.Parse("#cde498"),
        PrimaryBorderColor = SceneColor.Parse("#abb594"),
        PrimaryTextColor = SceneColor.Parse("#321b67"),
        SecondaryColor = SceneColor.Parse("#cdffb2"),
        SecondaryBorderColor = SceneColor.Parse("#b4e599"),
        SecondaryTextColor = SceneColor.Parse("#32004d"),
        TertiaryColor = SceneColor.Parse("#e1efc0"),
        TertiaryBorderColor = SceneColor.Parse("#c3cab2"),
        TertiaryTextColor = SceneColor.Parse("#321b67"),
        NodeBackground = SceneColor.Parse("#cde498"),
        NodeBorderColor = SceneColor.Parse("#13540c"),
        NodeTextColor = SceneColor.Parse("#000000"),
        LineColor = SceneColor.Parse("#000000"),
        ArrowheadColor = SceneColor.Parse("#008000"),
        TextColor = SceneColor.Parse("#000000"),
        TitleColor = SceneColor.Parse("#333333"),
        EdgeLabelBackground = SceneColor.Parse("#e8e8e8"),
        ClusterBackground = SceneColor.Parse("#cdffb2"),
        ClusterBorderColor = SceneColor.Parse("#6eaa49"),
        NoteBackground = SceneColor.Parse("#fff5ad"),
        NoteBorderColor = SceneColor.Parse("#6eaa49"),
        NoteTextColor = SceneColor.Parse("#000000"),
        RowOddBackground = SceneColor.Parse("#ffffff"),
        RowEvenBackground = SceneColor.Parse("#f4f9e9"),
        SpecialStateColor = SceneColor.Parse("#000000"),
        InnerEndBackground = SceneColor.Parse("#abb594"),
        CompositeBackground = SceneColor.Parse("#ffffff"),
        CompositeTitleBackground = SceneColor.Parse("#cde498"),
        AlternateBackground = SceneColor.Parse("#f0f0f0"),
        Palette = [SceneColor.Parse("#b9d970"), SceneColor.Parse("#acff7f"), SceneColor.Parse("#cde498"), SceneColor.Parse("#84d970"), SceneColor.Parse("#70d990"), SceneColor.Parse("#70d9c5"), SceneColor.Parse("#70b9d9"), SceneColor.Parse("#7084d9"), SceneColor.Parse("#c570d9"), SceneColor.Parse("#d97084"), SceneColor.Parse("#d99070"), SceneColor.Parse("#d9c570")],
        IsDark = false,
    };

    private static DiagramTheme CreateNeutral() => new()
    {
        Background = SceneColor.Parse("#ffffff"),
        PrimaryColor = SceneColor.Parse("#eeeeee"),
        PrimaryBorderColor = SceneColor.Parse("#d4d4d4"),
        PrimaryTextColor = SceneColor.Parse("#111111"),
        SecondaryColor = SceneColor.Parse("#fcfcfc"),
        SecondaryBorderColor = SceneColor.Parse("#e3e3e3"),
        SecondaryTextColor = SceneColor.Parse("#030303"),
        TertiaryColor = SceneColor.Parse("#eeeeee"),
        TertiaryBorderColor = SceneColor.Parse("#d4d4d4"),
        TertiaryTextColor = SceneColor.Parse("#111111"),
        NodeBackground = SceneColor.Parse("#eeeeee"),
        NodeBorderColor = SceneColor.Parse("#999999"),
        NodeTextColor = SceneColor.Parse("#000000"),
        LineColor = SceneColor.Parse("#666666"),
        ArrowheadColor = SceneColor.Parse("#333333"),
        TextColor = SceneColor.Parse("#000000"),
        TitleColor = SceneColor.Parse("#333333"),
        EdgeLabelBackground = SceneColor.Parse("#ffffff"),
        ClusterBackground = SceneColor.Parse("#fcfcfc"),
        ClusterBorderColor = SceneColor.Parse("#707070"),
        NoteBackground = SceneColor.Parse("#666666"),
        NoteBorderColor = SceneColor.Parse("#999999"),
        NoteTextColor = SceneColor.Parse("#ffffff"),
        RowOddBackground = SceneColor.Parse("#ffffff"),
        RowEvenBackground = SceneColor.Parse("#f4f4f4"),
        SpecialStateColor = SceneColor.Parse("#222222"),
        InnerEndBackground = SceneColor.Parse("#d4d4d4"),
        CompositeBackground = SceneColor.Parse("#ffffff"),
        CompositeTitleBackground = SceneColor.Parse("#eeeeee"),
        AlternateBackground = SceneColor.Parse("#f4f4f4"),
        Palette = [SceneColor.Parse("#555555"), SceneColor.Parse("#f4f4f4"), SceneColor.Parse("#555555"), SceneColor.Parse("#bbbbbb"), SceneColor.Parse("#777777"), SceneColor.Parse("#999999"), SceneColor.Parse("#dddddd"), SceneColor.Parse("#ffffff"), SceneColor.Parse("#dddddd"), SceneColor.Parse("#bbbbbb"), SceneColor.Parse("#999999"), SceneColor.Parse("#777777")],
        IsDark = false,
    };

    private static DiagramTheme CreateBase() => new()
    {
        Background = SceneColor.Parse("#f4f4f4"),
        PrimaryColor = SceneColor.Parse("#fff4dd"),
        PrimaryBorderColor = SceneColor.Parse("#eeddbb"),
        PrimaryTextColor = SceneColor.Parse("#333333"),
        SecondaryColor = SceneColor.Parse("#f4ddff"),
        SecondaryBorderColor = SceneColor.Parse("#ddbbee"),
        SecondaryTextColor = SceneColor.Parse("#0b2200"),
        TertiaryColor = SceneColor.Parse("#f6f9ff"),
        TertiaryBorderColor = SceneColor.Parse("#cfdbf3"),
        TertiaryTextColor = SceneColor.Parse("#090600"),
        NodeBackground = SceneColor.Parse("#fff4dd"),
        NodeBorderColor = SceneColor.Parse("#eeddbb"),
        NodeTextColor = SceneColor.Parse("#333333"),
        LineColor = SceneColor.Parse("#0b0b0b"),
        ArrowheadColor = SceneColor.Parse("#0b0b0b"),
        TextColor = SceneColor.Parse("#333333"),
        TitleColor = SceneColor.Parse("#090600"),
        EdgeLabelBackground = SceneColor.Parse("#f4ddff"),
        ClusterBackground = SceneColor.Parse("#f6f9ff"),
        ClusterBorderColor = SceneColor.Parse("#cfdbf3"),
        NoteBackground = SceneColor.Parse("#fff5ad"),
        NoteBorderColor = SceneColor.Parse("#e4db95"),
        NoteTextColor = SceneColor.Parse("#333333"),
        RowOddBackground = SceneColor.Parse("#ffffff"),
        RowEvenBackground = SceneColor.Parse("#fffcf6"),
        SpecialStateColor = SceneColor.Parse("#0b0b0b"),
        InnerEndBackground = SceneColor.Parse("#eeddbb"),
        CompositeBackground = SceneColor.Parse("#f4f4f4"),
        CompositeTitleBackground = SceneColor.Parse("#fff4dd"),
        AlternateBackground = SceneColor.Parse("#f6f9ff"),
        Palette = [SceneColor.Parse("#ffcb5d"), SceneColor.Parse("#cb5dff"), SceneColor.Parse("#77a3ff"), SceneColor.Parse("#e3ff5d"), SceneColor.Parse("#92ff5d"), SceneColor.Parse("#5dff7a"), SceneColor.Parse("#5dffcb"), SceneColor.Parse("#5de3ff"), SceneColor.Parse("#9680ff"), SceneColor.Parse("#ff5de3"), SceneColor.Parse("#ff5d92"), SceneColor.Parse("#ff7a5d")],
        IsDark = false,
    };
}
