namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A pair iterable (https://webidl.spec.whatwg.org/#idl-iterable) whose value pairs to iterate over can be read by index,
/// without computing the pairs before the index (e.g. the values of a computed style map are reified on demand).
/// </summary>
internal interface IIndexedPairIterable<TKey, TValue>
{
    /// <summary>Returns the pair at the index of the current value pairs to iterate over, or false when the index is out of range.</summary>
    bool TryGetPair(int index, out KeyValuePair<TKey, TValue> pair);
}

/// <summary>Reads the value pairs to iterate over of the pair iterables of the bindings.</summary>
internal static class PairIterable
{
    /// <summary>
    /// Returns the pair at the index of the current value pairs to iterate over of a pair iterable, or false when the index
    /// is out of range. The default iterator objects read one pair at each step, as the pairs are live.
    /// </summary>
    public static bool TryGetPair<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source, int index, out KeyValuePair<TKey, TValue> pair)
    {
        if (index >= 0)
        {
            if (source is IIndexedPairIterable<TKey, TValue> indexed)
                return indexed.TryGetPair(index, out pair);

            foreach (var item in source)
            {
                if (index-- == 0)
                {
                    pair = item;
                    return true;
                }
            }
        }

        pair = default;
        return false;
    }
}
