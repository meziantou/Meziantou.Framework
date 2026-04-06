namespace Meziantou.Framework;

/// <summary>
/// Provides options used to generate an avatar SVG.
/// </summary>
public class AvatarOptions
{
    internal static AvatarOptions Default { get; } = new AvatarOptions();

    /// <summary>
    /// The default avatar size (64x64).
    /// </summary>
    public const int DefaultSize = 64;

    /// <summary>
    /// Gets the list of colors used for rendering.
    /// </summary>
    public IList<AvatarColorPair> Palette { get; }

    /// <summary>
    /// Gets or sets the explicit bigram to render. If null, the bigram is extracted from the name.
    /// </summary>
    public string? Bigram { get; set; }

    /// <summary>
    /// Gets or sets the shape of the avatar.
    /// </summary>
    public AvatarShape Shape { get; set; }

    /// <summary>
    /// Gets or sets the size of the generated SVG in pixels.
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarOptions"/> class.
    /// </summary>
    public AvatarOptions()
    {
        Shape = AvatarShape.Round;
        Size = DefaultSize;
        Palette =
        [
            new AvatarColorPair("#1abc9c", "#080d14"),
            new AvatarColorPair("#3498db", "#080d14"),
            new AvatarColorPair("#9b59b6", "#ffffff"),
            new AvatarColorPair("#e67e22", "#080d14"),
            new AvatarColorPair("#e74c3c", "#080d14"),
            new AvatarColorPair("#2ecc71", "#080d14"),
            new AvatarColorPair("#34495e", "#ffffff"),
            new AvatarColorPair("#cfdade", "#153037"),
            new AvatarColorPair("#27ae60", "#080d14"),
            new AvatarColorPair("#2980b9", "#080d14"),
            new AvatarColorPair("#8e44ad", "#ffffff"),
        ];
    }
}
