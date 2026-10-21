using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// The targets of the DNS libraries, which all read untrusted input: Meziantou.Framework.DnsServer decodes the queries of
/// any client (<see cref="DnsServerFuzzTargets"/>), Meziantou.Framework.DnsClient decodes and validates the responses of
/// a server or of an attacker spoofing it (<see cref="DnsClientFuzzTargets"/>), the two libraries have their own wire reader
/// and encoder, which must agree (<see cref="DnsInteropFuzzTargets"/>), and Meziantou.Framework.DnsFilter parses filter lists
/// downloaded from third parties (<see cref="DnsFilterFuzzTargets"/>).
/// </summary>
public static class DnsFuzzTargets
{
    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("dns-server-message", DnsServerFuzzTargets.Message) { Seeds = DnsSeeds.GetMessageSeeds, Dictionary = DnsSeeds.WireTokens },
        new("dns-server-request", DnsServerFuzzTargets.Request) { Seeds = DnsSeeds.GetRequestSeeds, Dictionary = DnsSeeds.WireTokens },
        new("dns-client-response", DnsClientFuzzTargets.Response) { Seeds = DnsSeeds.GetResponseSeeds, Dictionary = DnsSeeds.WireTokens },
        new("dns-client-dnssec", DnsClientFuzzTargets.Dnssec) { Seeds = DnsSeeds.GetDnssecSeeds, Dictionary = DnsSeeds.WireTokens },
        new("dns-client-dnssec-signed", DnsClientFuzzTargets.SignedDnssec) { Seeds = DnsSeeds.GetDnssecSeeds, Dictionary = DnsSeeds.WireTokens },
        new("dns-wire-differential", DnsInteropFuzzTargets.Decoders) { Seeds = DnsSeeds.GetDifferentialSeeds, Dictionary = DnsSeeds.WireTokens },
        new("dns-message-roundtrip", DnsInteropFuzzTargets.RoundTrip) { Seeds = DnsSeeds.GetRoundTripSeeds },
        new("dns-filter-list", DnsFilterFuzzTargets.List) { Seeds = DnsSeeds.GetFilterListSeeds, Dictionary = DnsSeeds.FilterTokens },
        new("dns-filter-engine", DnsFilterFuzzTargets.Engine) { Seeds = DnsSeeds.GetFilterEngineSeeds, Dictionary = DnsSeeds.FilterTokens },
    ];
}
