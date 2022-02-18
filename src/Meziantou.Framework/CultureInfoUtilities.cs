using System.Globalization;

namespace Meziantou.Framework;

public static class CultureInfoUtilities
{
    public static void SetCurrentThreadCulture(CultureInfo cultureInfo!!)
    {
        var currentThread = Thread.CurrentThread;
        currentThread.CurrentCulture = cultureInfo;
        currentThread.CurrentUICulture = cultureInfo;
    }

    public static void UseCulture(CultureInfo cultureInfo!!, Action action!!)
    {
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

    public static void UseCulture(string cultureName!!, Action action)
    {
        var culture = GetCulture(cultureName);
        if (culture == null)
            throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));

        UseCulture(culture, action);
    }

    public static T UseCulture<T>(CultureInfo cultureInfo!!, Func<T> action!!)
    {
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

    public static T UseCulture<T>(string cultureName!!, Func<T> action)
    {
        var culture = GetCulture(cultureName);
        if (culture == null)
            throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));

        return UseCulture(culture, action);
    }

    public static CultureInfo? GetCulture(string? name)
    {
        if (name == null)
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

    public static CultureInfo GetNeutralCulture(this CultureInfo cultureInfo!!)
    {
        if (cultureInfo.IsNeutralCulture)
            return cultureInfo;

        return cultureInfo.Parent;
    }

    public static bool NeutralEquals(this CultureInfo a, CultureInfo b)
    {
        if (a == null && b == null)
            return true;

        if (a == null || b == null)
            return false;

        return a.GetNeutralCulture().Equals(b.GetNeutralCulture());
    }
}
