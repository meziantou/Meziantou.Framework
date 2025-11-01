namespace Meziantou.AspNetCore.Components;

/// <summary>
/// Represents a highlighted portion of text in a log message.
/// </summary>
/// <param name="Index">The zero-based starting index of the highlighted text.</param>
/// <param name="Length">The length of the highlighted text.</param>
/// <param name="Priority">The priority of this highlight. Higher priority highlights take precedence when overlapping.</param>
public record LogHighlighterResult(int Index, int Length, int Priority)
{
    /// <summary>
    /// Gets or initializes the URL to link to when the highlighted text is clicked.
    /// </summary>
    public string? Link { get; init; }

    /// <summary>
    /// Gets or initializes the replacement text to display instead of the original text.
    /// </summary>
    public string? ReplacementText { get; init; }

    /// <summary>
    /// Gets or initializes the tooltip text to show when hovering over the highlighted text.
    /// </summary>
    public string? Title { get; init; }
}
