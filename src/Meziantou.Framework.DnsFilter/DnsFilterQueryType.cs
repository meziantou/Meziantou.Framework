namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Specifies the type of DNS query for use with the <c>$dnstype</c> modifier.
/// Values correspond to IANA DNS Parameters registry types commonly used in filter rules.
/// </summary>
/// <remarks>
/// This enum is an <b>open</b> set of <see cref="ushort"/> record-type codes, not a closed list.
/// Callers should cast the raw QTYPE from the wire straight through
/// (<c>(DnsFilterQueryType)qtype</c>) even when it is not a named member; matching compares
/// numeric values, so unnamed types behave correctly. Never substitute
/// <see cref="ANY"/> for a type you do not recognize — that would make <c>$dnstype=ANY</c>
/// rules fire on it and <c>$dnstype=~ANY</c> rules spare it.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "PTR is the standard DNS record type name")]
public enum DnsFilterQueryType : ushort
{
    A = 1,
    NS = 2,
    CNAME = 5,
    SOA = 6,
    PTR = 12,
    HINFO = 13,
    MX = 15,
    TXT = 16,
    AAAA = 28,
    LOC = 29,
    SRV = 33,
    NAPTR = 35,
    CERT = 37,
    DNAME = 39,
    DS = 43,
    SSHFP = 44,
    RRSIG = 46,
    NSEC = 47,
    DNSKEY = 48,
    NSEC3 = 50,
    TLSA = 52,
    SVCB = 64,
    HTTPS = 65,
    SPF = 99,
    ANY = 255,
    URI = 256,
    CAA = 257,
}
