namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for the <c>Image.Identify</c> and <c>Image.IdentifyAsync</c> methods.</summary>
public sealed class ImageIdentifyOptions
{
    /// <summary>Gets the default options (<see cref="ImageIdentifyMode.Header"/> mode).</summary>
    public static ImageIdentifyOptions Default { get; } = new();

    /// <summary>Gets the configuration whose resource limits apply while identifying. Defaults to <see cref="ImageConfiguration.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageConfiguration Configuration
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageConfiguration.Default;

    /// <summary>Gets how much of the file is examined. Defaults to <see cref="ImageIdentifyMode.Header"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageIdentifyMode"/>.</exception>
    public ImageIdentifyMode Mode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The identify mode is not valid.");

            field = value;
        }
    }
}
