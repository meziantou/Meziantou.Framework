namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The colors derived from the colors of the sections of a mindmap.</summary>
internal static class MindmapColors
{
    /// <summary>Returns black or white, whichever reads better on a color.</summary>
    public static SceneColor Contrast(SceneColor color) => Luminance(color) > 0.5 ? SceneColor.Black : SceneColor.White;

    public static SceneColor Darken(SceneColor color) => Mix(color, SceneColor.Black, 0.3);

    public static SceneColor Lighten(SceneColor color) => Mix(color, SceneColor.White, 0.3);

    private static double Luminance(SceneColor color) => ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;

    private static SceneColor Mix(SceneColor color, SceneColor other, double amount) => new(
        (byte)Math.Round(color.R + ((other.R - color.R) * amount), MidpointRounding.AwayFromZero),
        (byte)Math.Round(color.G + ((other.G - color.G) * amount), MidpointRounding.AwayFromZero),
        (byte)Math.Round(color.B + ((other.B - color.B) * amount), MidpointRounding.AwayFromZero),
        color.A);
}
