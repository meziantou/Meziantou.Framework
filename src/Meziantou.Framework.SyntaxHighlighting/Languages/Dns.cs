using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Dns
{
    private const string Ipv6Re =
        @"((([0-9A-Fa-f]{1,4}:){7}([0-9A-Fa-f]{1,4}|:))|(([0-9A-Fa-f]{1,4}:){6}(:[0-9A-Fa-f]{1,4}|((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})|:))|" +
        @"(([0-9A-Fa-f]{1,4}:){5}(((:[0-9A-Fa-f]{1,4}){1,2})|:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})|:))|" +
        @"(([0-9A-Fa-f]{1,4}:){4}(((:[0-9A-Fa-f]{1,4}){1,3})|((:[0-9A-Fa-f]{1,4})?:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|" +
        @"(([0-9A-Fa-f]{1,4}:){3}(((:[0-9A-Fa-f]{1,4}){1,4})|((:[0-9A-Fa-f]{1,4}){0,2}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|" +
        @"(([0-9A-Fa-f]{1,4}:){2}(((:[0-9A-Fa-f]{1,4}){1,5})|((:[0-9A-Fa-f]{1,4}){0,3}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|" +
        @"(([0-9A-Fa-f]{1,4}:){1}(((:[0-9A-Fa-f]{1,4}){1,6})|((:[0-9A-Fa-f]{1,4}){0,4}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|" +
        @"(:(((:[0-9A-Fa-f]{1,4}){1,7})|((:[0-9A-Fa-f]{1,4}){0,5}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:)))\b";

    // Deviation from highlight.js, whose separator is `\.` in a JavaScript string, that is any character: a run of
    // small numbers (`1 7200 3600`) was highlighted as a single number, spaces included.
    private const string Ipv4Re = @"((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\b";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        return new Mode
        {
            Keywords = Keywords.FromWords(
            [
                "IN", "A", "AAAA", "AFSDB", "APL", "CAA", "CDNSKEY", "CDS", "CERT", "CNAME", "DHCID", "DLV", "DNAME", "DNSKEY", "DS",
                "HIP", "IPSECKEY", "KEY", "KX", "LOC", "MX", "NAPTR", "NS", "NSEC", "NSEC3", "NSEC3PARAM", "PTR", "RRSIG", "RP",
                "SIG", "SOA", "SRV", "SSHFP", "TA", "TKEY", "TLSA", "TSIG", "TXT",
            ]),
            Contains =
            [
                CommonModes.Comment(";", "$"),
                new Mode { Scope = "meta", Begin = @"^\$(TTL|GENERATE|INCLUDE|ORIGIN)\b" },
                new Mode { Scope = "number", Begin = Ipv6Re },
                new Mode { Scope = "number", Begin = Ipv4Re },
                new Mode { Scope = "number", Begin = @"\b\d+[dhwm]?" },
            ],
        };
    }
}
