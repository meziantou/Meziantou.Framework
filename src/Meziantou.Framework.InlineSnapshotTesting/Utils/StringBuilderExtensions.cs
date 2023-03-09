using System.Text;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
internal static class StringBuilderExtensions
{
#if NETSTANDARD2_0
    public static StringBuilder Append(this StringBuilder stringBuilder, ReadOnlySpan<char> value)
    {
        stringBuilder.Append(value.ToString());
        return stringBuilder;
    }
#endif
}
