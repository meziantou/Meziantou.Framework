using System.Globalization;
using System.Resources;

namespace Meziantou.Framework;

public static class LocalizationProvider
{
    private static ILocalizationProvider s_current = ResxLocalizationProvider.Instance;

    public static ILocalizationProvider Current
    {
        get => s_current;
        set => s_current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
