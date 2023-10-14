namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static class Utils
{
    public static T SafeGet<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch
        {
            return default;
        }
    }
}
