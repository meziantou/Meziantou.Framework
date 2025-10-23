namespace Meziantou.Framework;

public static class CultureInfoUtilities
{
    public static void SetCurrentThreadCulture(CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        var currentThread = Thread.CurrentThread;
        currentThread.CurrentCulture = cultureInfo;
        currentThread.CurrentUICulture = cultureInfo;
    }

    public static void UseCulture(CultureInfo cultureInfo, Action action)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);
        ArgumentNullException.ThrowIfNull(action);

        var currentThread = Thread.CurrentThread;
        var currentCulture = currentThread.CurrentCulture;
        var currentUiCulture = currentThread.CurrentUICulture;

        try
        {
            currentThread.CurrentCulture = cultureInfo;
            currentThread.CurrentUICulture = cultureInfo;

            action();
        }
        finally
        {
            currentThread.CurrentCulture = currentCulture;
            currentThread.CurrentUICulture = currentUiCulture;
        }
    }

    public static void UseCulture(string cultureName, Action action)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(action);

        var culture = GetCulture(cultureName) ?? throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));
        UseCulture(culture, action);
    }

    public static T UseCulture<T>(CultureInfo cultureInfo, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);
        ArgumentNullException.ThrowIfNull(action);

        var currentThread = Thread.CurrentThread;
        var currentCulture = currentThread.CurrentCulture;
        var currentUiCulture = currentThread.CurrentUICulture;

        try
        {
            currentThread.CurrentCulture = cultureInfo;
            currentThread.CurrentUICulture = cultureInfo;

            return action();
        }
        finally
        {
            currentThread.CurrentCulture = currentCulture;
            currentThread.CurrentUICulture = currentUiCulture;
        }
    }

    public static T UseCulture<T>(string cultureName, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(action);

        var culture = GetCulture(cultureName) ?? throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));

        return UseCulture(culture, action);
    }

    public static CultureInfo? GetCulture(string? name)
    {
        if (name is null)
            return null;

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
        }

        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return CultureInfo.GetCultureInfo(i);

        return null;
    }

    public static CultureInfo GetNeutralCulture(this CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        if (cultureInfo.IsNeutralCulture)
            return cultureInfo;

        return cultureInfo.Parent;
    }

    public static bool NeutralEquals(this CultureInfo a, CultureInfo b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.GetNeutralCulture().Equals(b.GetNeutralCulture());
    }
}
