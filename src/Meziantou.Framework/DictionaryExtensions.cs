using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;
public static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        where TKey : notnull
    {
        ref var dictionaryValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out var exists);
        if (exists)
            return dictionaryValue;

        dictionaryValue = value;
        return value;
    }

    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> valueFactory)
        where TKey : notnull
    {
        ref var dictionaryValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out var exists);
        if (exists)
            return dictionaryValue;

        dictionaryValue = valueFactory(key);
        return dictionaryValue;
    }

    public static bool TryUpdate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        where TKey : notnull
    {
        ref var dictionaryValue = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
        if (!Unsafe.IsNullRef(ref dictionaryValue))
        {
            dictionaryValue = value;
            return true;
        }

        return false;
    }

    public static bool TryUpdate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue, TValue> valueFactory)
        where TKey : notnull
    {
        ref var dictionaryValue = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
        if (!Unsafe.IsNullRef(ref dictionaryValue))
        {
            dictionaryValue = valueFactory(key, dictionaryValue);
            return true;
        }

        return false;
    }
}
