namespace Meziantou.Framework.CodeDom.Tests;

internal static class Extensions
{
    public static T As<T>(this object o)
    {
        return (T)o;
    }
}
