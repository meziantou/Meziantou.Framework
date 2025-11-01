namespace Meziantou.Framework;

/// <summary>
/// Provides access to the current localization provider for <see cref="RelativeDate"/> formatting.
/// </summary>
/// <example>
/// <code>
/// // Use custom localization provider
/// LocalizationProvider.Current = new MyCustomLocalizationProvider();
/// 
/// var date = RelativeDate.Get(DateTime.UtcNow.AddHours(-2));
/// Console.WriteLine(date); // Uses custom localization
/// 
/// // Restore default
/// LocalizationProvider.Current = ResxLocalizationProvider.Instance;
/// </code>
/// </example>
public static class LocalizationProvider
{
    /// <summary>Gets or sets the current localization provider used by <see cref="RelativeDate"/>.</summary>
    /// <value>The current <see cref="ILocalizationProvider"/>. The default is <see cref="ResxLocalizationProvider"/>.</value>
    /// <exception cref="ArgumentNullException">Thrown when attempting to set the value to <see langword="null"/>.</exception>
    public static ILocalizationProvider Current
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = ResxLocalizationProvider.Instance;
}
