namespace Meziantou.Framework.CodeDom;

internal static class StringExtensions
{
#if NETSTANDARD2_0
    public static bool Contains(this string str, string subString, System.StringComparison stringComparison)
    {
        return str.IndexOf(subString, stringComparison) >= 0;
    }
#endif
}
