namespace Meziantou.Framework;

/// <summary>
/// Provides access to the current localization provider for <see cref="RelativeDate"/> formatting.
/// </summary>
/// <example>
/// Implement <see cref="ILocalizationProvider"/>:
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs" ref:region="LocalizationProvider_CustomProvider" ref:dedent="true">
/// private sealed class UpperCaseLocalizationProvider : ILocalizationProvider
/// {
///     public string GetString(string name, CultureInfo? culture)
///     {
///         return ResxLocalizationProvider.Instance.GetString(name, culture).ToUpperInvariant();
///     }
/// }
/// </code>
/// Use it for all <see cref="RelativeDate"/> instances:
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.RelativeDate.Tests/RelativeDateTests.cs" ref:region="LocalizationProvider_Example" ref:dedent="true">
/// // Use custom localization provider
/// LocalizationProvider.Current = new UpperCaseLocalizationProvider();
///
/// var date = RelativeDate.Get(DateTime.UtcNow.AddHours(-2));
/// Assert.Equal("2 HOURS AGO", date.ToString(null, CultureInfo.InvariantCulture));
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
