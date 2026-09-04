namespace Meziantou.Framework;

/// <summary>
/// Represents a background/foreground color pair used to render an avatar.
/// </summary>
/// <remarks>
/// This is a struct, so <see langword="default"/> bypasses the constructor and leaves both colors null.
/// <see cref="AvatarGenerator.CreateSvg(string, AvatarOptions)"/> rejects such an entry in the palette.
/// </remarks>
public readonly record struct AvatarColorPair
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarColorPair"/> struct.
    /// </summary>
    /// <param name="backgroundColor">The background color.</param>
    /// <param name="foregroundColor">The foreground color.</param>
    public AvatarColorPair(string backgroundColor, string foregroundColor)
    {
        BackgroundColor = string.IsNullOrWhiteSpace(backgroundColor) ? throw new ArgumentException("The background color cannot be null or whitespace.", nameof(backgroundColor)) : backgroundColor;
        ForegroundColor = string.IsNullOrWhiteSpace(foregroundColor) ? throw new ArgumentException("The foreground color cannot be null or whitespace.", nameof(foregroundColor)) : foregroundColor;
    }

    /// <summary>
    /// Gets the background color.
    /// </summary>
    public string BackgroundColor { get; }

    /// <summary>
    /// Gets the foreground color.
    /// </summary>
    public string ForegroundColor { get; }
}
