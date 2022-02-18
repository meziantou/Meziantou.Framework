#if NET461
namespace TestUtilities
{
    internal static class StringExtensions
    {
        public static string Replace(this string str, string oldValue, string newValue, StringComparison stringComparison)
        {
            var working = str;
            var index = working.IndexOf(oldValue, stringComparison);
            while (index != -1)
            {
                working = working.Remove(index, oldValue.Length);
                working = working.Insert(index, newValue);
                index += newValue.Length;
                index = working.IndexOf(oldValue, index, stringComparison);
            }

            return working;
        }
    }
}
#endif
