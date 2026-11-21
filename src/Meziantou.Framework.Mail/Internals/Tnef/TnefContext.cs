namespace Meziantou.Framework.Mail.Internals.Tnef;

/// <summary>What the objects of a TNEF stream share: the options, and the identifiers the reader assigns to the named properties (a TNEF stream gives the names, not identifiers).</summary>
internal sealed class TnefContext
{
    private readonly Dictionary<PstNamedProperty, ushort> _ids = [];
    private readonly List<PstNamedProperty> _names = [];

    public TnefContext(PstReadOptions options)
    {
        Options = options;
    }

    public PstReadOptions Options { get; }

    public ushort GetOrAddId(PstNamedProperty name)
    {
        if (!_ids.TryGetValue(name, out var id))
        {
            if (_names.Count >= 0x7FFF)
            {
                throw new MailResourceLimitException("The TNEF stream has more than 32,767 named properties.");
            }

            id = (ushort)(0x8000 + _names.Count);
            _ids.Add(name, id);
            _names.Add(name);
        }

        return id;
    }

    public PstNamedProperty? GetName(ushort id) => id >= 0x8000 && id - 0x8000 < _names.Count ? _names[id - 0x8000] : null;

    public ushort? GetId(PstNamedProperty name) => _ids.TryGetValue(name, out var id) ? id : null;
}
