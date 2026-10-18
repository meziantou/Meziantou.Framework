using System.Drawing;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The color palette of a forced colors mode (https://drafts.csswg.org/css-color-adjust-1/#forced): the values of the CSS system
/// colors (https://drafts.csswg.org/css-color-4/#css-system-colors), which the user agent enforces on the pages (see
/// <see cref="ViewportOptions.ForcedColors"/>) and exposes to them through the system color keywords. The colors are opaque:
/// their alpha is ignored.
/// </summary>
public sealed record ForcedColorsTheme
{
    /// <summary>Gets the default light palette: the system colors of the light color scheme (black text on a white canvas).</summary>
    public static ForcedColorsTheme Light { get; } = new();

    /// <summary>Gets the default dark palette: the system colors of the dark color scheme (white text on a dark canvas).</summary>
    public static ForcedColorsTheme Dark { get; } = new()
    {
        AccentColor = Color.FromArgb(153, 200, 255),
        AccentColorText = Color.FromArgb(0, 0, 0),
        ActiveText = Color.FromArgb(255, 158, 158),
        ButtonBorder = Color.FromArgb(107, 107, 107),
        ButtonFace = Color.FromArgb(107, 107, 107),
        ButtonText = Color.FromArgb(255, 255, 255),
        Canvas = Color.FromArgb(18, 18, 18),
        CanvasText = Color.FromArgb(255, 255, 255),
        Field = Color.FromArgb(59, 59, 59),
        FieldText = Color.FromArgb(255, 255, 255),
        GrayText = Color.FromArgb(128, 128, 128),
        Highlight = Color.FromArgb(99, 99, 99),
        HighlightText = Color.FromArgb(255, 255, 255),
        LinkText = Color.FromArgb(158, 158, 255),
        Mark = Color.FromArgb(102, 92, 0),
        MarkText = Color.FromArgb(255, 255, 255),
        SelectedItem = Color.FromArgb(153, 200, 255),
        SelectedItemText = Color.FromArgb(59, 59, 59),
        VisitedText = Color.FromArgb(208, 173, 240),
    };

    /// <summary>Gets or sets the <c>AccentColor</c> system color: the background of the accented user interface controls.</summary>
    public Color AccentColor { get; init; } = Color.FromArgb(0, 117, 255);

    /// <summary>Gets or sets the <c>AccentColorText</c> system color: the text of the accented user interface controls.</summary>
    public Color AccentColorText { get; init; } = Color.FromArgb(255, 255, 255);

    /// <summary>Gets or sets the <c>ActiveText</c> system color: the text of the active links.</summary>
    public Color ActiveText { get; init; } = Color.FromArgb(255, 0, 0);

    /// <summary>Gets or sets the <c>ButtonBorder</c> system color: the base border color of the controls.</summary>
    public Color ButtonBorder { get; init; } = Color.FromArgb(118, 118, 118);

    /// <summary>Gets or sets the <c>ButtonFace</c> system color: the background of the controls.</summary>
    public Color ButtonFace { get; init; } = Color.FromArgb(239, 239, 239);

    /// <summary>Gets or sets the <c>ButtonText</c> system color: the text of the controls.</summary>
    public Color ButtonText { get; init; } = Color.FromArgb(0, 0, 0);

    /// <summary>Gets or sets the <c>Canvas</c> system color: the background of the application content or of the documents.</summary>
    public Color Canvas { get; init; } = Color.FromArgb(255, 255, 255);

    /// <summary>Gets or sets the <c>CanvasText</c> system color: the text of the application content or of the documents.</summary>
    public Color CanvasText { get; init; } = Color.FromArgb(0, 0, 0);

    /// <summary>Gets or sets the <c>Field</c> system color: the background of the input fields.</summary>
    public Color Field { get; init; } = Color.FromArgb(255, 255, 255);

    /// <summary>Gets or sets the <c>FieldText</c> system color: the text of the input fields.</summary>
    public Color FieldText { get; init; } = Color.FromArgb(0, 0, 0);

    /// <summary>Gets or sets the <c>GrayText</c> system color: the disabled text.</summary>
    public Color GrayText { get; init; } = Color.FromArgb(128, 128, 128);

    /// <summary>Gets or sets the <c>Highlight</c> system color: the background of the selected text.</summary>
    public Color Highlight { get; init; } = Color.FromArgb(181, 213, 255);

    /// <summary>Gets or sets the <c>HighlightText</c> system color: the selected text.</summary>
    public Color HighlightText { get; init; } = Color.FromArgb(0, 0, 0);

    /// <summary>Gets or sets the <c>LinkText</c> system color: the text of the non-active, non-visited links.</summary>
    public Color LinkText { get; init; } = Color.FromArgb(0, 0, 238);

    /// <summary>Gets or sets the <c>Mark</c> system color: the background of the text that has been specially marked.</summary>
    public Color Mark { get; init; } = Color.FromArgb(255, 255, 0);

    /// <summary>Gets or sets the <c>MarkText</c> system color: the text that has been specially marked.</summary>
    public Color MarkText { get; init; } = Color.FromArgb(0, 0, 0);

    /// <summary>Gets or sets the <c>SelectedItem</c> system color: the background of the selected items.</summary>
    public Color SelectedItem { get; init; } = Color.FromArgb(0, 117, 255);

    /// <summary>Gets or sets the <c>SelectedItemText</c> system color: the text of the selected items.</summary>
    public Color SelectedItemText { get; init; } = Color.FromArgb(255, 255, 255);

    /// <summary>Gets or sets the <c>VisitedText</c> system color: the text of the visited links.</summary>
    public Color VisitedText { get; init; } = Color.FromArgb(85, 26, 139);
}
