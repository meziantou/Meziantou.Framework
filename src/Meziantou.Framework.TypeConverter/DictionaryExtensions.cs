#pragma warning disable CS8717 // A member returning a [MaybeNull] value introduces a null value for a type parameter.

namespace Meziantou.Framework;

public static class DictionaryExtensions
{
    public static TResult GetValueOrDefault<TKey, TValue, TResult>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, TResult defaultValue)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (TryGetValueOrDefault(dict, key, out TResult? result))
            return result!;

        return defaultValue;
    }

    public static bool TryGetValueOrDefault<TKey, TValue, TResult>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, [MaybeNullWhen(returnValue: false)] out TResult value)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (dict.TryGetValue(key, out var v))
        {
            if (ConvertUtilities.TryChangeType(v, out value))
                return true;
        }

        value = default!;
        return false;
    }
}
