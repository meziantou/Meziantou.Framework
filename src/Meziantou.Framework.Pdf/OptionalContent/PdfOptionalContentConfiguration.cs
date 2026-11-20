using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An optional content configuration (ISO 32000-2, 8.11.4.3): the state of the groups, those the user cannot change,
/// and the groups that behave as radio buttons. The default configuration (<c>/D</c>) gives the state of the groups when
/// the document is opened; the others (<c>/Configs</c>) are alternatives a viewer can offer.
/// </summary>
public sealed class PdfOptionalContentConfiguration
{
    private static readonly PdfName BaseStateName = PdfName.Create("BaseState");
    private static readonly PdfName OnName = PdfName.Create("ON");
    private static readonly PdfName OffName = PdfName.Create("OFF");
    private static readonly PdfName LockedName = PdfName.Create("Locked");
    private static readonly PdfName RBGroupsName = PdfName.Create("RBGroups");
    private static readonly PdfName CreatorName = PdfName.Create("Creator");

    private readonly PdfDocument _document;

    internal PdfOptionalContentConfiguration(PdfDocument document, PdfDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the configuration dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets or sets the name of the configuration (<c>/Name</c>).</summary>
    public string? Name
    {
        get => Dictionary.GetText(PdfNames.Name);
        set => Dictionary.SetText(PdfNames.Name, value);
    }

    /// <summary>Gets or sets the name of the application or the person that created the configuration (<c>/Creator</c>).</summary>
    public string? Creator
    {
        get => Dictionary.GetText(CreatorName);
        set => Dictionary.SetText(CreatorName, value);
    }

    /// <summary>Gets or sets the state of the groups the configuration does not list (<c>/BaseState</c>). The default is <see cref="PdfOptionalContentBaseState.On"/>.</summary>
    public PdfOptionalContentBaseState BaseState
    {
        get => Dictionary.GetName(BaseStateName)?.Value switch
        {
            "OFF" => PdfOptionalContentBaseState.Off,
            "Unchanged" => PdfOptionalContentBaseState.Unchanged,
            _ => PdfOptionalContentBaseState.On,
        };
        set => Dictionary[BaseStateName] = value switch
        {
            PdfOptionalContentBaseState.On => null,
            PdfOptionalContentBaseState.Off => PdfName.Create("OFF"),
            PdfOptionalContentBaseState.Unchanged => PdfName.Create("Unchanged"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The base state is not valid"),
        };
    }

    /// <summary>Gets the groups the user cannot turn on or off (<c>/Locked</c>).</summary>
    public IReadOnlyList<PdfOptionalContentGroup> LockedGroups => ReadGroups(Dictionary.Get<PdfArray>(LockedName));

    /// <summary>Gets the sets of groups that behave as radio buttons (<c>/RBGroups</c>): turning a group of a set on turns the others off.</summary>
    public IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>> RadioButtonGroups
    {
        get
        {
            var result = new List<IReadOnlyList<PdfOptionalContentGroup>>();
            if (Dictionary.Get<PdfArray>(RBGroupsName) is { } sets)
            {
                for (var i = 0; i < sets.Count && i < PdfOptionalContentProperties.MaxGroups; i++)
                {
                    if (sets.GetResolved(i) is PdfArray set)
                    {
                        result.Add(ReadGroups(set));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Indicates whether a group is visible in this configuration: its base state, unless the configuration lists it on or off.</summary>
    /// <param name="group">The group.</param>
    /// <returns><see langword="true"/> when the group is on; <see langword="true"/> as well for <see cref="PdfOptionalContentBaseState.Unchanged"/> and a group not listed.</returns>
    public bool IsVisible(PdfOptionalContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (Contains(Dictionary.Get<PdfArray>(OffName), group))
            return false;

        if (Contains(Dictionary.Get<PdfArray>(OnName), group))
            return true;

        return BaseState != PdfOptionalContentBaseState.Off;
    }

    /// <summary>Sets the state of a group in this configuration (<c>/ON</c>, <c>/OFF</c>).</summary>
    /// <param name="group">The group.</param>
    /// <param name="visible">Whether the group is on.</param>
    /// <exception cref="ArgumentException">The group belongs to another document.</exception>
    public void SetVisible(PdfOptionalContentGroup group, bool visible)
    {
        Check(group);
        Remove(Dictionary.Get<PdfArray>(OnName), group);
        Remove(Dictionary.Get<PdfArray>(OffName), group);
        var key = visible ? OnName : OffName;
        if (visible == (BaseState == PdfOptionalContentBaseState.On))
            return;

        if (Dictionary.Get<PdfArray>(key) is not { } array)
        {
            array = [];
            Dictionary[key] = array;
        }

        array.Add(group.Reference);
    }

    /// <summary>Locks or unlocks a group (<c>/Locked</c>): the user cannot turn a locked group on or off.</summary>
    /// <param name="group">The group.</param>
    /// <param name="locked">Whether the group is locked.</param>
    /// <exception cref="ArgumentException">The group belongs to another document.</exception>
    public void SetLocked(PdfOptionalContentGroup group, bool locked)
    {
        Check(group);
        var array = Dictionary.Get<PdfArray>(LockedName);
        Remove(array, group);
        if (!locked)
            return;

        if (array is null)
        {
            array = [];
            Dictionary[LockedName] = array;
        }

        array.Add(group.Reference);
    }

    /// <summary>Adds a set of groups that behave as radio buttons (<c>/RBGroups</c>).</summary>
    /// <param name="groups">The groups.</param>
    /// <exception cref="ArgumentException">A group belongs to another document, or there are fewer than 2 groups.</exception>
    public void AddRadioButtonGroup(params IEnumerable<PdfOptionalContentGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var list = groups.ToList();
        if (list.Count < 2)
            throw new ArgumentException("A radio-button group needs at least 2 groups", nameof(groups));

        foreach (var group in list)
        {
            Check(group);
        }

        if (Dictionary.Get<PdfArray>(RBGroupsName) is not { } sets)
        {
            sets = [];
            Dictionary[RBGroupsName] = sets;
        }

        sets.Add(new PdfArray(list.Select(group => (PdfObject)group.Reference)));
    }

    private List<PdfOptionalContentGroup> ReadGroups(PdfArray? array)
    {
        var result = new List<PdfOptionalContentGroup>();
        if (array is null)
            return result;

        for (var i = 0; i < array.Count && result.Count < PdfOptionalContentProperties.MaxGroups; i++)
        {
            if (array[i] is PdfReference reference && reference.Resolve() is PdfDictionary group)
            {
                result.Add(new PdfOptionalContentGroup(_document, group, reference));
            }
        }

        return result;
    }

    private void Check(PdfOptionalContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Document != _document)
            throw new ArgumentException("The group belongs to another document", nameof(group));
    }

    private static bool Contains(PdfArray? array, PdfOptionalContentGroup group)
    {
        if (array is null)
            return false;

        foreach (var item in array)
        {
            if (ReferenceEquals(item is null ? null : PdfReference.ResolveIfReference(item), group.Dictionary))
                return true;
        }

        return false;
    }

    private static void Remove(PdfArray? array, PdfOptionalContentGroup group)
    {
        if (array is null)
            return;

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(array.GetResolved(i), group.Dictionary))
            {
                array.RemoveAt(i);
            }
        }
    }
}
