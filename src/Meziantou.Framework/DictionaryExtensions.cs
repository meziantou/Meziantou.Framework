using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Dictionary{TKey, TValue}"/>.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>Gets the value associated with the specified key or adds it if it doesn't exist.</summary>
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        where TKey : notnull
    {
        ref var dictionaryValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out var exists);
        if (exists)
            return dictionaryValue!;

        dictionaryValue = value;
        return value;
    }

    /// <summary>Gets the value associated with the specified key or adds it using the factory function if it doesn't exist.</summary>
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> valueFactory)
        where TKey : notnull
    {
        ref var dictionaryValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out var exists);
        if (exists)
            return dictionaryValue!;

        dictionaryValue = valueFactory(key);
        return dictionaryValue;
    }

    /// <summary>Attempts to update the value associated with the specified key.</summary>
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

    /// <summary>Attempts to update the value associated with the specified key using the factory function.</summary>
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
