namespace Meziantou.Framework.HumanReadable.Utils;
internal static class StringBuilderExtensions
{
#if NETSTANDARD2_0 || NET471
    public static System.Text.StringBuilder Append(this System.Text.StringBuilder stringBuilder, ReadOnlySpan<char> value)
    {
        stringBuilder.Append(value.ToString());
        return stringBuilder;
    }
#endif
}
