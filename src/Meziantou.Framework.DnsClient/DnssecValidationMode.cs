namespace Meziantou.Framework.DnsClient;

/// <summary>Specifies how DNSSEC validation is performed.</summary>
public enum DnssecValidationMode
{
    /// <summary>DNSSEC records can be requested and parsed, but responses are not locally validated.</summary>
    None,

    /// <summary>Responses are locally validated against configured DNSSEC trust anchors.</summary>
    Local,
}
