namespace Meziantou.Framework;

/// <summary>Provides an interface for retrieving localized strings for relative date formatting.</summary>
/// <remarks>
/// Implement this interface to provide custom localization for <see cref="RelativeDate"/>.
/// The default implementation uses embedded resource files for Dutch, English, French, German, Italian, Japanese, Korean, Portuguese, Simplified Chinese, Spanish and Turkish translations.
/// </remarks>
public interface ILocalizationProvider
{
    /// <summary>Gets a localized string for the specified resource name and culture.</summary>
    /// <param name="name">The name of the resource string (e.g., "Now", "OneSecondAgo", "InManyDays").</param>
    /// <param name="culture">The culture for which to retrieve the localized string. If <see langword="null"/>, the current culture is used.</param>
    /// <returns>The localized string, or an empty string if the resource is not found, in which case the neutral culture is queried instead.</returns>
    /// <remarks>
    /// A string for a resource that reports a count must contain the <c>{0}</c> placeholder. It is substituted literally rather than treated as a composite format string, so braces need no escaping.
    /// </remarks>
    string GetString(string name, CultureInfo? culture);
}
