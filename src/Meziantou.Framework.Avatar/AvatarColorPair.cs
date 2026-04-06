namespace Meziantou.Framework;

/// <summary>
/// Represents a foreground/background color pair used to render an avatar.
/// </summary>
/// <param name="backgroundColor">The background color.</param>
/// <param name="foregroundColor">The foreground color.</param>
public readonly record struct AvatarColorPair(string backgroundColor, string foregroundColor)
{
    /// <summary>
    /// Gets the background color.
    /// </summary>
    public string BackgroundColor { get; } = string.IsNullOrWhiteSpace(backgroundColor) ? throw new ArgumentException("The background color cannot be null or whitespace.", nameof(backgroundColor)) : backgroundColor;

    /// <summary>
    /// Gets the foreground color.
    /// </summary>
    public string ForegroundColor { get; } = string.IsNullOrWhiteSpace(foregroundColor) ? throw new ArgumentException("The foreground color cannot be null or whitespace.", nameof(foregroundColor)) : foregroundColor;
}
