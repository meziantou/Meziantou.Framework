using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>A distribution list (IPM.DistList): a named group of addresses and contacts.</summary>
public sealed class PstDistributionList : PstMessage
{
    internal PstDistributionList(PstContext context, NdbNode node, PstPropertyCollection properties, int embeddingDepth)
        : base(context, node, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the name of the list.</summary>
    public string? Name => Properties.GetString(NamedProperties.DistributionListName.Name) ?? Properties.GetString(PropertyIds.DisplayName) ?? Subject;

    /// <summary>Gets the members of the list, from their one-off entry identifiers, or from the entry identifiers of the contacts they refer to.</summary>
    public IReadOnlyList<PstDistributionListMember> Members
    {
        get
        {
            var oneOffs = Properties.GetValueOrDefault<byte[][]>(NamedProperties.DistributionListOneOffMembers.Name) ?? [];
            var members = Properties.GetValueOrDefault<byte[][]>(NamedProperties.DistributionListMembers.Name) ?? [];
            var count = Math.Max(oneOffs.Length, members.Length);
            var result = new List<PstDistributionListMember>(count);
            for (var i = 0; i < count; i++)
            {
                var member = (i < oneOffs.Length ? EntryIds.ParseOneOff(oneOffs[i]) : null) ?? (i < members.Length ? EntryIds.ParseWrappedOneOff(members[i]) : null);
                if (member is not null)
                {
                    member.EntryId = i < members.Length ? members[i] : null;
                    result.Add(member);
                }
            }

            return result;
        }
    }
}
