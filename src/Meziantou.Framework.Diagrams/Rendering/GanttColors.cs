namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The colors of a Gantt chart: those of Mermaid for its themes, derived from the colors of the theme otherwise.</summary>
internal sealed record GanttColors
{
    public required SceneColor SectionBackground { get; init; }

    public required SceneColor AlternateSectionBackground { get; init; }

    public required SceneColor SectionBackground2 { get; init; }

    public required SceneColor ExcludeBackground { get; init; }

    public required SceneColor TaskBorder { get; init; }

    public required SceneColor TaskBackground { get; init; }

    public required SceneColor TaskText { get; init; }

    public required SceneColor TaskTextDark { get; init; }

    public required SceneColor TaskTextOutside { get; init; }

    public required SceneColor ActiveTaskBorder { get; init; }

    public required SceneColor ActiveTaskBackground { get; init; }

    public required SceneColor Grid { get; init; }

    public required SceneColor DoneTaskBackground { get; init; }

    public required SceneColor DoneTaskBorder { get; init; }

    public required SceneColor CriticalBorder { get; init; }

    public required SceneColor CriticalBackground { get; init; }

    public required SceneColor TodayLine { get; init; }

    private static readonly GanttColors DefaultColors = new()
    {
        SectionBackground = SceneColor.Parse("rgba(102, 102, 255, 0.49)"),
        AlternateSectionBackground = SceneColor.Parse("white"),
        SectionBackground2 = SceneColor.Parse("#fff400"),
        ExcludeBackground = SceneColor.Parse("#eeeeee"),
        TaskBorder = SceneColor.Parse("#534fbc"),
        TaskBackground = SceneColor.Parse("#8a90dd"),
        TaskText = SceneColor.Parse("white"),
        TaskTextDark = SceneColor.Parse("black"),
        TaskTextOutside = SceneColor.Parse("black"),
        ActiveTaskBorder = SceneColor.Parse("#534fbc"),
        ActiveTaskBackground = SceneColor.Parse("#bfc7ff"),
        Grid = SceneColor.Parse("lightgrey"),
        DoneTaskBackground = SceneColor.Parse("lightgrey"),
        DoneTaskBorder = SceneColor.Parse("grey"),
        CriticalBorder = SceneColor.Parse("#ff8888"),
        CriticalBackground = SceneColor.Parse("red"),
        TodayLine = SceneColor.Parse("red"),
    };

    private static readonly GanttColors DarkColors = new()
    {
        SectionBackground = SceneColor.Parse("hsl(52.9411764706, 28.813559322%, 58.431372549%)"),
        AlternateSectionBackground = SceneColor.Parse("#333"),
        SectionBackground2 = SceneColor.Parse("#EAE8D9"),
        ExcludeBackground = SceneColor.Parse("hsl(52.9411764706, 28.813559322%, 48.431372549%)"),
        TaskBorder = SceneColor.Parse("#ffffff"),
        TaskBackground = SceneColor.Parse("hsl(180, 1.5873015873%, 35.3529411765%)"),
        TaskText = SceneColor.Parse("hsl(28.5714285714, 17.3553719008%, 86.2745098039%)"),
        TaskTextDark = SceneColor.Parse("#2c2c2c"),
        TaskTextOutside = SceneColor.Parse("lightgrey"),
        ActiveTaskBorder = SceneColor.Parse("#ffffff"),
        ActiveTaskBackground = SceneColor.Parse("#81B1DB"),
        Grid = SceneColor.Parse("lightgrey"),
        DoneTaskBackground = SceneColor.Parse("lightgrey"),
        DoneTaskBorder = SceneColor.Parse("grey"),
        CriticalBorder = SceneColor.Parse("#E83737"),
        CriticalBackground = SceneColor.Parse("#E83737"),
        TodayLine = SceneColor.Parse("#DB5757"),
    };

    private static readonly GanttColors ForestColors = new()
    {
        SectionBackground = SceneColor.Parse("#6eaa49"),
        AlternateSectionBackground = SceneColor.Parse("white"),
        SectionBackground2 = SceneColor.Parse("#6eaa49"),
        ExcludeBackground = SceneColor.Parse("#eeeeee"),
        TaskBorder = SceneColor.Parse("#13540c"),
        TaskBackground = SceneColor.Parse("#487e3a"),
        TaskText = SceneColor.Parse("white"),
        TaskTextDark = SceneColor.Parse("black"),
        TaskTextOutside = SceneColor.Parse("black"),
        ActiveTaskBorder = SceneColor.Parse("#13540c"),
        ActiveTaskBackground = SceneColor.Parse("#cde498"),
        Grid = SceneColor.Parse("lightgrey"),
        DoneTaskBackground = SceneColor.Parse("lightgrey"),
        DoneTaskBorder = SceneColor.Parse("grey"),
        CriticalBorder = SceneColor.Parse("#ff8888"),
        CriticalBackground = SceneColor.Parse("red"),
        TodayLine = SceneColor.Parse("red"),
    };

    private static readonly GanttColors NeutralColors = new()
    {
        SectionBackground = SceneColor.Parse("hsl(0, 0%, 73.9215686275%)"),
        AlternateSectionBackground = SceneColor.Parse("white"),
        SectionBackground2 = SceneColor.Parse("hsl(0, 0%, 73.9215686275%)"),
        ExcludeBackground = SceneColor.Parse("#eeeeee"),
        TaskBorder = SceneColor.Parse("hsl(0, 0%, 33.9215686275%)"),
        TaskBackground = SceneColor.Parse("#707070"),
        TaskText = SceneColor.Parse("white"),
        TaskTextDark = SceneColor.Parse("#333"),
        TaskTextOutside = SceneColor.Parse("#333"),
        ActiveTaskBorder = SceneColor.Parse("hsl(0, 0%, 33.9215686275%)"),
        ActiveTaskBackground = SceneColor.Parse("#eee"),
        Grid = SceneColor.Parse("hsl(0, 0%, 90%)"),
        DoneTaskBackground = SceneColor.Parse("#bbb"),
        DoneTaskBorder = SceneColor.Parse("#666"),
        CriticalBorder = SceneColor.Parse("hsl(10.9090909091, 73.3333333333%, 40%)"),
        CriticalBackground = SceneColor.Parse("#d42"),
        TodayLine = SceneColor.Parse("#d42"),
    };

    private static readonly GanttColors BaseColors = new()
    {
        SectionBackground = SceneColor.Parse("hsl(220.5882352941, 100%, 98.3333333333%)"),
        AlternateSectionBackground = SceneColor.Parse("white"),
        SectionBackground2 = SceneColor.Parse("#fff4dd"),
        ExcludeBackground = SceneColor.Parse("#eeeeee"),
        TaskBorder = SceneColor.Parse("hsl(40.5882352941, 60%, 83.3333333333%)"),
        TaskBackground = SceneColor.Parse("#fff4dd"),
        TaskText = SceneColor.Parse("#333"),
        TaskTextDark = SceneColor.Parse("#333"),
        TaskTextOutside = SceneColor.Parse("#333"),
        ActiveTaskBorder = SceneColor.Parse("#fff4dd"),
        ActiveTaskBackground = SceneColor.Parse("hsl(40.5882352941, 100%, 100%)"),
        Grid = SceneColor.Parse("lightgrey"),
        DoneTaskBackground = SceneColor.Parse("lightgrey"),
        DoneTaskBorder = SceneColor.Parse("grey"),
        CriticalBorder = SceneColor.Parse("#ff8888"),
        CriticalBackground = SceneColor.Parse("red"),
        TodayLine = SceneColor.Parse("red"),
    };

    public static GanttColors For(DiagramTheme theme)
    {
        if (ReferenceEquals(theme, DiagramTheme.Default))
            return DefaultColors;

        if (ReferenceEquals(theme, DiagramTheme.Dark))
            return DarkColors;

        if (ReferenceEquals(theme, DiagramTheme.Forest))
            return ForestColors;

        if (ReferenceEquals(theme, DiagramTheme.Neutral))
            return NeutralColors;

        if (ReferenceEquals(theme, DiagramTheme.Base))
            return BaseColors;

        var background = theme.Background ?? (theme.IsDark ? SceneColor.Black : SceneColor.White);
        var light = theme.IsDark ? DarkColors : DefaultColors;
        return light with
        {
            SectionBackground = theme.PrimaryColor,
            AlternateSectionBackground = background,
            SectionBackground2 = theme.SecondaryColor,
            TaskBorder = theme.PrimaryBorderColor,
            TaskBackground = theme.GetPaletteColor(0),
            ActiveTaskBorder = theme.PrimaryBorderColor,
            ActiveTaskBackground = theme.PrimaryColor,
            TaskText = theme.PrimaryTextColor,
            TaskTextDark = theme.TextColor,
            TaskTextOutside = theme.TextColor,
        };
    }
}
