namespace Meziantou.Framework.DnsClient.Internal;

/// <summary>What a denial of existence is being asked to prove.</summary>
internal enum DenialKind
{
    /// <summary>That a name, or a type at a name, does not exist. NSEC3 opt-out spans cannot prove this.</summary>
    NameOrData,

    /// <summary>That no secure delegation exists at a name. NSEC3 opt-out spans do prove this.</summary>
    Delegation,
}
