#pragma warning disable CS8717 // A member returning a [MaybeNull] value introduces a null value for a type parameter.

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for retrieving and converting dictionary values.
/// <example>
/// <code><![CDATA[
/// var dictionary = new Dictionary<string, object>
/// {
///     ["age"] = "25",
///     ["active"] = true
/// };
///
/// // Get and convert values with type safety
/// var age = dictionary.GetValueOrDefault("age", 0);
/// Console.WriteLine(age); // 25 (as int)
///
/// // TryGetValueOrDefault for better control
/// if (dictionary.TryGetValueOrDefault("age", out int ageValue))
/// {
///     Console.WriteLine($"Age: {ageValue}");
/// }
/// ]]></code>
/// </example>
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>Gets a value from the dictionary and converts it to the specified type, or returns a default value if the key is not found or conversion fails.</summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <typeparam name="TResult">The target type to convert the value to.</typeparam>
    /// <param name="dict">The dictionary to retrieve the value from.</param>
    /// <param name="key">The key of the value to retrieve.</param>
    /// <param name="defaultValue">The value to return if the key is not found or conversion fails.</param>
    /// <returns>The converted value if found and conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static TResult GetValueOrDefault<TKey, TValue, TResult>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key, TResult defaultValue)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(dict);

        if (TryGetValueOrDefault(dict, key, out TResult? result))
            return result!;

        return defaultValue;
    }

    /// <summary>Attempts to get a value from the dictionary and convert it to the specified type.</summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <typeparam name="TResult">The target type to convert the value to.</typeparam>
    /// <param name="dict">The dictionary to retrieve the value from.</param>
    /// <param name="key">The key of the value to retrieve.</param>
    /// <param name="value">When this method returns, contains the converted value if the key was found and conversion succeeded, or the default value if the key was not found or conversion failed.</param>
    /// <returns><see langword="true"/> if the key was found and conversion succeeded; otherwise, <see langword="false"/>.</returns>
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
