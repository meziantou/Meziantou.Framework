namespace Meziantou.Framework;

public static class LocalizationProvider
{
    public static ILocalizationProvider Current
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = ResxLocalizationProvider.Instance;
}
