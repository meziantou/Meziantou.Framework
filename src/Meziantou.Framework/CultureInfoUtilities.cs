namespace Meziantou.Framework;

/// <summary>
/// Provides utility methods for working with <see cref="CultureInfo"/>.
/// </summary>
/// <example>
/// <code>
/// CultureInfoUtilities.UseCulture("fr-FR", () =>
/// {
///     Console.WriteLine(DateTime.Now.ToString("D"));
/// });
/// </code>
/// </example>
public static class CultureInfoUtilities
{
    /// <summary>Sets the current thread's culture and UI culture.</summary>
    public static void SetCurrentThreadCulture(CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        var currentThread = Thread.CurrentThread;
        currentThread.CurrentCulture = cultureInfo;
        currentThread.CurrentUICulture = cultureInfo;
    }

    /// <summary>Executes an action with a temporary culture, then restores the original culture.</summary>
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

    /// <summary>Executes an action with a temporary culture specified by name, then restores the original culture.</summary>
    public static void UseCulture(string cultureName, Action action)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(action);

        var culture = GetCulture(cultureName) ?? throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));
        UseCulture(culture, action);
    }

    /// <summary>Executes a function with a temporary culture, then restores the original culture and returns the result.</summary>
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

    /// <summary>Executes a function with a temporary culture specified by name, then restores the original culture and returns the result.</summary>
    public static T UseCulture<T>(string cultureName, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(action);

        var culture = GetCulture(cultureName) ?? throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));

        return UseCulture(culture, action);
    }

    /// <summary>Gets a culture by name or LCID, returning null if not found.</summary>
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

    /// <summary>Gets the neutral culture for a specific culture (e.g., "en" for "en-US").</summary>
    public static CultureInfo GetNeutralCulture(this CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        if (cultureInfo.IsNeutralCulture)
            return cultureInfo;

        return cultureInfo.Parent;
    }

    /// <summary>Determines whether two cultures have the same neutral culture.</summary>
    public static bool NeutralEquals(this CultureInfo a, CultureInfo b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.GetNeutralCulture().Equals(b.GetNeutralCulture());
    }
}
