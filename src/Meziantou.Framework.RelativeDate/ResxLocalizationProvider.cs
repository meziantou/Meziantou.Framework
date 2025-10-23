using System.Resources;

namespace Meziantou.Framework;

internal sealed class ResxLocalizationProvider : ILocalizationProvider
{
    private static readonly ResourceManager ResourceManager = new("Meziantou.Framework.RelativeDates", typeof(RelativeDate).Assembly);

    public static ILocalizationProvider Instance { get; } = new ResxLocalizationProvider();

    public string GetString(string name, CultureInfo? culture)
    {
        return ResourceManager.GetString(name, culture) ?? "";
    }
}
