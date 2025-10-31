namespace Meziantou.Framework;

/// <summary>
/// Provides localized strings for relative date formatting.
/// </summary>
public interface ILocalizationProvider
{
    /// <summary>
    /// Gets a localized string by name.
    /// </summary>
    /// <param name="name">The name of the string resource.</param>
    /// <param name="culture">The culture to use for localization.</param>
    /// <returns>The localized string.</returns>
    string GetString(string name, CultureInfo? culture);
}
