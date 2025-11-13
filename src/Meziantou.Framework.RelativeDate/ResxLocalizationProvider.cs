using System.Resources;

namespace Meziantou.Framework;

/// <summary>Provides localization using embedded resource files (.resx) for relative date formatting.</summary>
/// <remarks>
/// This is the default localization provider for <see cref="RelativeDate"/>.
/// It includes translations for English, French and Spanish.
/// </remarks>
internal sealed class ResxLocalizationProvider : ILocalizationProvider
{
    private static readonly ResourceManager ResourceManager = new("Meziantou.Framework.RelativeDates", typeof(RelativeDate).Assembly);

    /// <summary>Gets the singleton instance of the <see cref="ResxLocalizationProvider"/>.</summary>
    public static ILocalizationProvider Instance { get; } = new ResxLocalizationProvider();

    /// <inheritdoc />
    public string GetString(string name, CultureInfo? culture)
    {
        return ResourceManager.GetString(name, culture) ?? "";
    }
}
