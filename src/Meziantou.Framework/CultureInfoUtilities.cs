namespace Meziantou.Framework;

/// <summary>
/// Provides utility methods for working with <see cref="CultureInfo"/>.
/// </summary>
public static class CultureInfoUtilities
{
    /// <summary>
    /// Sets the current thread's culture and UI culture to the specified culture.
    /// </summary>
    /// <param name="cultureInfo">The culture to set.</param>
    public static void SetCurrentThreadCulture(CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        var currentThread = Thread.CurrentThread;
        currentThread.CurrentCulture = cultureInfo;
        currentThread.CurrentUICulture = cultureInfo;
    }

    /// <summary>
    /// Executes an action with a temporarily set culture, then restores the previous culture.
    /// </summary>
    /// <param name="cultureInfo">The culture to use during the action execution.</param>
    /// <param name="action">The action to execute.</param>
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

    /// <summary>
    /// Executes an action with a temporarily set culture by name, then restores the previous culture.
    /// </summary>
    /// <param name="cultureName">The name of the culture to use during the action execution.</param>
    /// <param name="action">The action to execute.</param>
    /// <exception cref="ArgumentException">Thrown when the culture name is not found.</exception>
    public static void UseCulture(string cultureName, Action action)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(action);

        var culture = GetCulture(cultureName) ?? throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));
        UseCulture(culture, action);
    }

    /// <summary>
    /// Executes a function with a temporarily set culture and returns its result, then restores the previous culture.
    /// </summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="cultureInfo">The culture to use during the function execution.</param>
    /// <param name="action">The function to execute.</param>
    /// <returns>The result of the function.</returns>
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

    /// <summary>
    /// Executes a function with a temporarily set culture by name and returns its result, then restores the previous culture.
    /// </summary>
    /// <typeparam name="T">The type of the return value.</typeparam>
    /// <param name="cultureName">The name of the culture to use during the function execution.</param>
    /// <param name="action">The function to execute.</param>
    /// <returns>The result of the function.</returns>
    /// <exception cref="ArgumentException">Thrown when the culture name is not found.</exception>
    public static T UseCulture<T>(string cultureName, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(action);

        var culture = GetCulture(cultureName) ?? throw new ArgumentException($"Culture '{cultureName}' not found.", nameof(cultureName));

        return UseCulture(culture, action);
    }

    /// <summary>
    /// Gets a <see cref="CultureInfo"/> by name or LCID. Returns <see langword="null"/> if the culture is not found.
    /// </summary>
    /// <param name="name">The culture name or LCID as a string.</param>
    /// <returns>The <see cref="CultureInfo"/> if found; otherwise, <see langword="null"/>.</returns>
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

    /// <summary>
    /// Gets the neutral culture for the specified culture.
    /// </summary>
    /// <param name="cultureInfo">The culture to get the neutral culture for.</param>
    /// <returns>The neutral culture if not already neutral; otherwise, returns the same culture.</returns>
    public static CultureInfo GetNeutralCulture(this CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        if (cultureInfo.IsNeutralCulture)
            return cultureInfo;

        return cultureInfo.Parent;
    }

    /// <summary>
    /// Determines whether two cultures have the same neutral culture.
    /// </summary>
    /// <param name="a">The first culture to compare.</param>
    /// <param name="b">The second culture to compare.</param>
    /// <returns><see langword="true"/> if both cultures have the same neutral culture; otherwise, <see langword="false"/>.</returns>
    public static bool NeutralEquals(this CultureInfo a, CultureInfo b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.GetNeutralCulture().Equals(b.GetNeutralCulture());
    }
}
