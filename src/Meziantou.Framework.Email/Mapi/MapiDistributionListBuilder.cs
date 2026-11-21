using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A distribution list (IPM.DistList) to add to a folder of a <see cref="PstWriter"/>. Its members are one-off addresses.</summary>
public sealed class MapiDistributionListBuilder : MapiMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="MapiDistributionListBuilder"/> class.</summary>
    public MapiDistributionListBuilder()
        : base("IPM.DistList")
    {
    }

    /// <summary>Gets or sets the name of the list.</summary>
    public string? Name
    {
        get => Properties.GetValue<string>(NamedProperties.DistributionListName);
        set
        {
            Properties.SetValue(NamedProperties.DistributionListName, value);
            Properties.SetValue(PropertyIds.DisplayName, MapiPropertyType.Unicode, value);
            Subject = value;
        }
    }

    /// <summary>Gets the members of the list. The writer replaces the members copied from another file when this list is not empty.</summary>
    public IList<MapiDistributionListMember> Members { get; } = [];

    internal void Complete()
    {
        if (Members.Count == 0)
        {
            return;
        }

        var oneOffs = Members.Select(member => EntryIds.CreateOneOff(member.DisplayName, member.AddressType, member.EmailAddress)).ToArray();
        var wrapped = oneOffs.Select(EntryIds.CreateWrappedOneOff).ToArray();
        Properties.SetValue(NamedProperties.DistributionListOneOffMembers, oneOffs);
        Properties.SetValue(NamedProperties.DistributionListMembers, wrapped);

        // [MS-OXOCNTC] 3.1.5.11: the CRC of the concatenated members
        uint checksum = 0;
        foreach (var member in wrapped)
        {
            checksum = PstCrc.Compute(member, checksum);
        }

        Properties.SetValue(NamedProperties.DistributionListChecksum, unchecked((int)checksum));
    }
}
