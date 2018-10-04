using System;
using System.Collections.Generic;

namespace Meziantou.Framework
{
    public static class DictionaryExtensions
    {
        public static TResult GetValueOrDefault<TKey, TValue, TResult>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, TResult defaultValue)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (TryGetValueOrDefault(dict, key, out TResult result))
                return result;

            return defaultValue;
        }

        public static bool TryGetValueOrDefault<TKey, TValue, TResult>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, out TResult value)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var v))
            {
                if (ConvertUtilities.TryChangeType(v, out value))
                    return true;
            }

            value = default;
            return false;
        }
    }
}
