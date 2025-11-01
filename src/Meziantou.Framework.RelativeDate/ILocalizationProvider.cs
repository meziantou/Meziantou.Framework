namespace Meziantou.Framework;

/// <summary>
/// Provides an interface for retrieving localized strings for relative date formatting.
/// </summary>
/// <remarks>
/// Implement this interface to provide custom localization for <see cref="RelativeDate"/>.
/// The default implementation uses embedded resource files for English and French translations.
/// </remarks>
public interface ILocalizationProvider
{
    /// <summary>Gets a localized string for the specified resource name and culture.</summary>
    /// <param name="name">The name of the resource string (e.g., "Now", "OneSecondAgo", "InManyDays").</param>
    /// <param name="culture">The culture for which to retrieve the localized string. If <see langword="null"/>, the current culture is used.</param>
    /// <returns>The localized string, or an empty string if the resource is not found.</returns>
    string GetString(string name, CultureInfo? culture);
}
