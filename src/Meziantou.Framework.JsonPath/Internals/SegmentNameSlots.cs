namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// The distinct names of the name selectors of a segment, each in a slot. It lets the evaluator match the members
/// of an object against every name selector in a single pass over the object.
/// </summary>
internal sealed class SegmentNameSlots
{
    /// <summary>Above this number of names, a dictionary finds a slot faster than comparing every name.</summary>
    private const int MaxLinearSearchNames = 4;

    private readonly Dictionary<string, int>? _slots;

    private SegmentNameSlots(string[] names, int[] selectorSlots)
    {
        Names = names;
        SelectorSlots = selectorSlots;
        if (names.Length > MaxLinearSearchNames)
        {
            _slots = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
            for (var i = 0; i < names.Length; i++)
            {
                _slots.Add(names[i], i);
            }
        }
    }

    /// <summary>Gets the distinct names, in the order of their first selector.</summary>
    public string[] Names { get; }

    /// <summary>Gets, for each selector of the segment, the slot of its name, or -1 when it is not a name selector.</summary>
    public int[] SelectorSlots { get; }

    /// <summary>Creates the slots of the name selectors of a segment.</summary>
    /// <param name="selectors">The selectors of the segment.</param>
    /// <returns>The slots, or <see langword="null"/> when no selector is a name selector.</returns>
    public static SegmentNameSlots? Create(Selector[] selectors)
    {
        List<string>? names = null;
        var selectorSlots = new int[selectors.Length];
        for (var i = 0; i < selectors.Length; i++)
        {
            if (selectors[i] is not NameSelector nameSelector)
            {
                selectorSlots[i] = -1;
                continue;
            }

            names ??= [];
            var slot = names.IndexOf(nameSelector.Name);
            if (slot < 0)
            {
                slot = names.Count;
                names.Add(nameSelector.Name);
            }

            selectorSlots[i] = slot;
        }

        return names is null ? null : new SegmentNameSlots([.. names], selectorSlots);
    }

    /// <summary>Gets the slot of a name.</summary>
    /// <param name="name">A member name.</param>
    /// <returns>The slot of <paramref name="name"/>, or -1 when no name selector selects it.</returns>
    public int GetSlot(string name)
    {
        if (_slots is not null)
            return _slots.TryGetValue(name, out var slot) ? slot : -1;

        var names = Names;
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
