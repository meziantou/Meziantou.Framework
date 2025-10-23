using Microsoft.Extensions.ObjectPool;

namespace Meziantou.Framework.CodeOwners;

internal static class StringBuilderPoolExtensions
{
    public static string ToStringAndReturn(this ObjectPool<StringBuilder> pool, StringBuilder stringBuilder)
    {
        var result = stringBuilder.ToString();
        pool.Return(stringBuilder);
        return result;
    }
}
