namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The colors of a Git graph: those of the themes of Mermaid for its themes, derived from the palette of the theme
/// otherwise, then replaced by the colors of the diagram.
/// </summary>
internal sealed class GitGraphColors
{
    private static readonly string[][] DefaultColors =
    [
        ["#0000ec", "#dede00", "#9dec00", "#0076ec", "#00ecec", "#00ec76", "#ec00ec", "#ec0000"],
        ["#131300", "#0000a0", "#310093", "#934900", "#930000", "#930049", "#009300", "#009393"],
        ["#ffffff", "#000000", "#000000", "#ffffff", "#000000", "#000000", "#000000", "#000000"],
        ["#000021", "#ffffde", "#131300", "#ececff", "#c7c7f1"],
    ];

    private static readonly string[][] DarkColors =
    [
        ["#797d7d", "#a12273", "#6a8993", "#9b5c35", "#cb1313", "#65007b", "#cc7801", "#31a50e"],
        ["#868282", "#5edd8c", "#95766c", "#64a3ca", "#34ecec", "#9aff84", "#3387fe", "#ce5af1"],
        ["#2c2c2c", "#d3d3d3", "#d3d3d3", "#2c2c2c", "#d3d3d3", "#d3d3d3", "#d3d3d3", "#d3d3d3"],
        ["#b8b6b6", "#474949", "#e0dfdf", "#1f2020", "#cccccc"],
    ];

    private static readonly string[][] ForestColors =
    [
        ["#9bc834", "#7aff32", "#b0d45b", "#c8ab34", "#c86134", "#c83452", "#34c861", "#349bc8"],
        ["#6437cb", "#8500cd", "#4f2ba4", "#3754cb", "#379ecb", "#37cbad", "#cb379e", "#cb6437"],
        ["#ffffff", "#000000", "#000000", "#ffffff", "#000000", "#000000", "#000000", "#000000"],
        ["#32004d", "#cdffb2", "#321b67", "#cde498", "#abb594"],
    ];

    private static readonly string[][] NeutralColors =
    [
        ["#b4b4b4", "#555555", "#bbbbbb", "#777777", "#999999", "#dddddd", "#ffffff", "#dddddd"],
        ["#4b4b4b", "#aaaaaa", "#444444", "#888888", "#666666", "#222222", "#000000", "#222222"],
        ["#333333", "#ffffff", "#333333", "#ffffff", "#333333", "#333333", "#333333", "#333333"],
        ["#030303", "#fcfcfc", "#111111", "#eeeeee", "#d4d4d4"],
    ];

    private static readonly string[][] BaseColors =
    [
        ["#ffcb5d", "#cb5dff", "#77a3ff", "#ff7a5d", "#ff5d92", "#ff5de3", "#92ff5d", "#5dffcb"],
        ["#0034a2", "#34a200", "#885c00", "#0085a2", "#00a26d", "#00a21c", "#6d00a2", "#a20034"],
        ["#333333", "#333333", "#333333", "#333333", "#333333", "#333333", "#333333", "#333333"],
        ["#0b2200", "#f4ddff", "#333333", "#fff4dd", "#eeddbb"],
    ];

    private readonly SceneColor[] _branches = new SceneColor[8];
    private readonly SceneColor[] _highlights = new SceneColor[8];
    private readonly SceneColor[] _labels = new SceneColor[8];

    public GitGraphColors(GitGraphDiagram diagram, DiagramTheme theme)
    {
        var colors = theme switch
        {
            _ when ReferenceEquals(theme, DiagramTheme.Default) => DefaultColors,
            _ when ReferenceEquals(theme, DiagramTheme.Dark) => DarkColors,
            _ when ReferenceEquals(theme, DiagramTheme.Forest) => ForestColors,
            _ when ReferenceEquals(theme, DiagramTheme.Neutral) => NeutralColors,
            _ when ReferenceEquals(theme, DiagramTheme.Base) => BaseColors,
            _ => null,
        };
        for (var i = 0; i < 8; i++)
        {
            if (colors is not null)
            {
                _branches[i] = SceneColor.Parse(colors[0][i]);
                _highlights[i] = SceneColor.Parse(colors[1][i]);
                _labels[i] = SceneColor.Parse(colors[2][i]);
            }
            else
            {
                // A theme of its own: the colors of its palette, their inverse for the highlights, and a readable text
                var color = theme.Palette.Count > 0 ? theme.GetPaletteColor(i) : theme.PrimaryBorderColor;
                _branches[i] = color;
                _highlights[i] = new SceneColor((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B));
                _labels[i] = IsLight(color) ? SceneColor.Black : SceneColor.White;
            }

            _branches[i] = diagram.BranchColors.TryGetValue(i, out var branch) ? branch : _branches[i];
            _highlights[i] = diagram.HighlightColors.TryGetValue(i, out var highlight) ? highlight : _highlights[i];
            _labels[i] = diagram.BranchLabelColors.TryGetValue(i, out var label) ? label : _labels[i];
        }

        CommitLabel = diagram.CommitLabelColor ?? (colors is null ? theme.PrimaryTextColor : SceneColor.Parse(colors[3][0]));
        CommitLabelBackground = diagram.CommitLabelBackground ?? (colors is null ? theme.SecondaryColor : SceneColor.Parse(colors[3][1]));
        TagLabel = diagram.TagLabelColor ?? (colors is null ? theme.PrimaryTextColor : SceneColor.Parse(colors[3][2]));
        TagLabelBackground = diagram.TagLabelBackground ?? (colors is null ? theme.PrimaryColor : SceneColor.Parse(colors[3][3]));
        TagLabelBorder = diagram.TagLabelBorder ?? (colors is null ? theme.PrimaryBorderColor : SceneColor.Parse(colors[3][4]));
    }

    public SceneColor CommitLabel { get; }

    public SceneColor CommitLabelBackground { get; }

    public SceneColor TagLabel { get; }

    public SceneColor TagLabelBackground { get; }

    public SceneColor TagLabelBorder { get; }

    // The colors repeat every 8 branches, as the 8 colors of the themes of Mermaid
    public SceneColor Branch(int index) => _branches[((index % 8) + 8) % 8];

    public SceneColor Highlight(int index) => _highlights[((index % 8) + 8) % 8];

    public SceneColor BranchLabel(int index) => _labels[((index % 8) + 8) % 8];

    internal static bool IsLight(SceneColor color) => (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B) > 140;
}
