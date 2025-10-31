namespace Meziantou.Framework;

/// <summary>
/// Provides access to the current localization provider for relative date formatting.
/// </summary>
public static class LocalizationProvider
{
    /// <summary>
    /// Gets or sets the current localization provider.
    /// </summary>
    public static ILocalizationProvider Current
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = ResxLocalizationProvider.Instance;
}
