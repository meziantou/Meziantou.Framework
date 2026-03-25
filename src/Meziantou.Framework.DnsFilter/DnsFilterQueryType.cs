using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Specifies the type of DNS query for use with the <c>$dnstype</c> modifier.
/// Values correspond to IANA DNS Parameters registry types commonly used in filter rules.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "PTR is the standard DNS record type name")]
public enum DnsFilterQueryType : ushort
{
    A = 1,
    NS = 2,
    CNAME = 5,
    SOA = 6,
    PTR = 12,
    MX = 15,
    TXT = 16,
    AAAA = 28,
    SRV = 33,
    HTTPS = 65,
    ANY = 255,
}
