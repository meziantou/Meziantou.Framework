using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Meziantou.Framework.Fuzzing;
using ClientCanonicalizer = Meziantou.Framework.DnsClient.Internal.DnssecCanonicalizer;
using ClientDecoder = Meziantou.Framework.DnsClient.Protocol.DnsMessageEncoder;
using ClientRecords = Meziantou.Framework.DnsClient.Response.Records;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// The seeds of the targets: messages with every record type the libraries parse, encoded with the server's encoder, the
/// edge cases of the wire format the unit tests cover (compression pointers, truncated messages, extended response codes),
/// and the filter lists and queried names of the DnsFilter unit tests.
/// </summary>
internal static partial class DnsSeeds
{
    // 2026-01-01T00:00:00Z, within the validity of the RRSIG records of the seeds
    private const uint SeedTime = 1_767_225_600;

    // libFuzzer dictionaries hold bytes below 0x80 only (the tokens are written as UTF-8)
    public static IReadOnlyList<string> WireTokens { get; } =
    [
        "\u0000\u0001\u0000\u0001", "\u0000\u001C\u0000\u0001", "\u0000\u0005\u0000\u0001", "\u0000\u000F\u0000\u0001", "\u0000\u0010\u0000\u0001", "\u0000\u0006\u0000\u0001", "\u0000\u0002\u0000\u0001", "\u0000\u000C\u0000\u0001",
        "\u0000!", "\u0000#", "\u0000)", "\u0000+", "\u0000.", "\u0000/", "\u00000", "\u00002", "\u00003", "\u00004", "\u0000,", "\u0000@", "\u0000A",
        "\u0000\u001D", "\u0000\u000D", "\u0000\u0011", "\u0000'", "\u0000;", "\u0000<", "\u0001\u0000", "\u0001\u0001", "\u0000ÿ", "\u0000\u0003", "\u0000\u0004",
        "\u0000\u0000)\u0010\u0000\u0000\u0000\u0000\u0000\u0000\u0000", "\u0000\u0000)\u0004\u0000\u0001\u0000\u0000\u0000\u0000\u0000", "\u0000\u0000\u0000\u0000",
        "\u0007example\u0003com\u0000", "\u0003www", "\u0003com\u0000", "\u0001*", "\u0000", "\u0001\u0000\u0000\u0001\u0000\u0000\u0000\u0000\u0000\u0000", "\u0001\u0000\u0000\u0001\u0000\u0000\u0000\u0000\u0000\u0001",
        "\u0001\u0003\u0008", "\u0001\u0003\u000D", "\u0001\u0001\u0000\u0000", "\u0002h2", "\u0005issue",
    ];

    // The tokens of TSIG records, cookies and zone transfers
    public static IReadOnlyList<string> TransferTokens { get; } =
    [
        .. WireTokens,
        "\u000Bhmac-sha256\u0000", "\u0009hmac-sha1\u0000", "\u0008hmac-md5\u0007sig-alg\u0003reg\u0003int\u0000", "\u0008tsig-key\u0000", "\u0001,",
        "\u0000\u0006\u0000\u0001", "\u0000\n\u0000\u0008", "\u0000\n\u0000\u0010", "\u0000\n\u0000\u0018", "\u0000\u0000\u0000\u0001", "\u0000\u0000\u0000\u0002",
    ];

    public static IReadOnlyList<string> FilterTokens { get; } =
    [
        "||", "^", "@@", "$", ",", "|", "~", "*", "/", "\\", "#", "!", "[Adblock Plus 2.0]", "0.0.0.0 ", "127.0.0.1 ", "::1 ", "\n", "\r\n", "\t", "'", "\"",
        "important", "badfilter", "dnstype=", "denyallow=", "dnsrewrite=", "client=", "ctag=", "NOERROR", "NXDOMAIN", "REFUSED", "SERVFAIL", ";A;", ";AAAA;", ";CNAME;", ";TXT;", "TYPE65",
        "example.com", "ads.", "xn--", "localhost", "192.168.0.0/24", "2001:db8::/32", "device_pc", "user_child", "(?:", ")", "[a-z]", "\\d+", "\\.", "^(", "$/", ".*", "+", "?", "{2,}",
    ];

    public static IEnumerable<FuzzSeed> GetMessageSeeds() => WithPrefix(GetMessages(), [0], [1], [3]);

    public static IEnumerable<FuzzSeed> GetRequestSeeds()
    {
        foreach (var (name, message) in GetMessages())
        {
            yield return new FuzzSeed("udp-default-" + name, (byte[])[0, 0x00, .. message]);
            yield return new FuzzSeed("tcp-answer-" + name, (byte[])[1, 0x01, .. message]);
            yield return new FuzzSeed("udp-echo-" + name, (byte[])[0, 0x0A, .. message]);
            yield return new FuzzSeed("udp-large-" + name, (byte[])[0, 0x03, .. message]);
            yield return new FuzzSeed("https-noformerr-" + name, (byte[])[3, 0x05, .. message]);
        }
    }

    public static IEnumerable<FuzzSeed> GetDnsOverHttpsSeeds()
    {
        foreach (var (name, message) in GetMessages())
        {
            yield return new FuzzSeed("post-answer-" + name, (byte[])[0x18, .. message]);
            yield return new FuzzSeed("post-parameters-default-" + name, (byte[])[0x01, .. message]);
            yield return new FuzzSeed("post-json-" + name, (byte[])[0x02, .. message]);
            yield return new FuzzSeed("get-base64url-large-" + name, (byte[])[0x24, .. message]);
            yield return new FuzzSeed("get-parameter-answer-" + name, (byte[])[0x15, .. FuzzText.Encode(Base64Url.EncodeToString(message))]);
        }

        yield return new FuzzSeed("get-without-parameter", (byte[])[0x06]);
        yield return new FuzzSeed("get-empty-parameter", (byte[])[0x07]);
    }

    public static IEnumerable<FuzzSeed> GetResponseSeeds() => WithPrefix(GetMessages(), [0], [3], [4]);

    public static IEnumerable<FuzzSeed> GetDifferentialSeeds() => WithPrefix(GetMessages(), Array.Empty<byte>());

    public static IEnumerable<FuzzSeed> GetRoundTripSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return new FuzzSeed("ascending", Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        yield return new FuzzSeed("descending", Enumerable.Range(0, 256).Select(value => (byte)(255 - value)).ToArray());
        yield return new FuzzSeed("ones", Enumerable.Repeat((byte)0xFF, 512).ToArray());
        // Pseudo-random bytes (xorshift), so that the seeds do not change between runs
        var state = 0x9E3779B9u;
        for (var seed = 0; seed < 32; seed++)
        {
            var bytes = new byte[64 + (seed * 16)];
            for (var i = 0; i < bytes.Length; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                bytes[i] = (byte)state;
            }

            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"random-{seed}"), bytes);
        }
    }

    public static IEnumerable<FuzzSeed> GetDnssecSeeds()
    {
        var root = CreateResponse(".", DnsQueryType.DNSKEY, [Record(".", DnsQueryType.DNSKEY, Dnskey(257, 13)), Record(".", DnsQueryType.RRSIG, Rrsig(DnsQueryType.DNSKEY, 13, 0, "."))]);
        var comDs = CreateResponse("com", DnsQueryType.DS, [Record("com", DnsQueryType.DS, Ds(13)), Record("com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.DS, 13, 1, "."))]);
        var comDnskey = CreateResponse("com", DnsQueryType.DNSKEY, [Record("com", DnsQueryType.DNSKEY, Dnskey(257, 13)), Record("com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.DNSKEY, 13, 1, "com"))]);
        var exampleDs = CreateResponse("example.com", DnsQueryType.DS, [Record("example.com", DnsQueryType.DS, Ds(13)), Record("example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.DS, 13, 2, "com"))]);
        var exampleDnskey = CreateResponse("example.com", DnsQueryType.DNSKEY, [Record("example.com", DnsQueryType.DNSKEY, Dnskey(257, 13)), Record("example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.DNSKEY, 13, 2, "example.com"))]);
        byte[][] chain = [root, comDs, comDnskey, exampleDs, exampleDnskey];

        var positive = CreateResponse("www.example.com", DnsQueryType.A, [Record("www.example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.1") }), Record("www.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.A, 13, 3, "example.com"))]);
        var cname = CreateResponse("alias.example.com", DnsQueryType.A,
        [
            Record("alias.example.com", DnsQueryType.CNAME, new DnsCnameRecordData { CanonicalName = "www.example.com" }),
            Record("alias.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.CNAME, 13, 3, "example.com")),
            Record("www.example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.1") }),
            Record("www.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.A, 13, 3, "example.com")),
        ]);
        var wildcard = CreateResponse("a.b.example.com", DnsQueryType.TXT, [Record("a.b.example.com", DnsQueryType.TXT, new DnsTxtRecordData { Text = ["wildcard"] }), Record("a.b.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.TXT, 13, 2, "example.com"))], DnsResponseCode.NoError,
        [
            Record("a.example.com", DnsQueryType.NSEC, new DnsNsecRecordData { NextDomainName = "c.example.com", TypeBitMaps = [DnsQueryType.TXT, DnsQueryType.RRSIG, DnsQueryType.NSEC] }),
            Record("a.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.NSEC, 13, 3, "example.com")),
        ]);
        var nxdomainNsec = CreateResponse("missing.example.com", DnsQueryType.A, [], DnsResponseCode.NameError,
        [
            Record("example.com", DnsQueryType.NSEC, new DnsNsecRecordData { NextDomainName = "www.example.com", TypeBitMaps = [DnsQueryType.A, DnsQueryType.NS, DnsQueryType.SOA, DnsQueryType.RRSIG, DnsQueryType.NSEC, DnsQueryType.DNSKEY] }),
            Record("example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.NSEC, 13, 2, "example.com")),
        ]);
        var nxdomainNsec3 = CreateResponse("missing.example.com", DnsQueryType.A, [], DnsResponseCode.NameError,
        [
            Record("0p9mhaveqvm6t7vbl5lop2u3t2rp3tom.example.com", DnsQueryType.NSEC3, new DnsNsec3RecordData { HashAlgorithm = 1, Iterations = 0, Salt = [], NextHashedOwnerName = new byte[20], TypeBitMaps = [DnsQueryType.A, DnsQueryType.RRSIG] }),
            Record("0p9mhaveqvm6t7vbl5lop2u3t2rp3tom.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.NSEC3, 13, 3, "example.com")),
            Record("example.com", DnsQueryType.SOA, new DnsSoaRecordData { PrimaryNameServer = "ns.example.com", ResponsibleMailbox = "hostmaster.example.com", Serial = 1, Refresh = 3600, Retry = 600, Expire = 86400, Minimum = 300 }),
        ]);
        var nodata = CreateResponse("www.example.com", DnsQueryType.AAAA, [], DnsResponseCode.NoError,
        [
            Record("www.example.com", DnsQueryType.NSEC, new DnsNsecRecordData { NextDomainName = "z.example.com", TypeBitMaps = [DnsQueryType.A, DnsQueryType.RRSIG, DnsQueryType.NSEC] }),
            Record("www.example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.NSEC, 13, 3, "example.com")),
        ]);
        var unsigned = CreateResponse("www.example.org", DnsQueryType.A, [Record("www.example.org", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.2") })]);
        var orgNoDs = CreateResponse("org", DnsQueryType.DS, [], DnsResponseCode.NoError,
        [
            Record("org", DnsQueryType.NSEC, new DnsNsecRecordData { NextDomainName = "org.", TypeBitMaps = [DnsQueryType.NS, DnsQueryType.RRSIG, DnsQueryType.NSEC] }),
            Record("org", DnsQueryType.RRSIG, Rrsig(DnsQueryType.NSEC, 13, 1, ".")),
        ]);

        yield return Dnssec("positive", positive, chain);
        yield return Dnssec("cname", cname, chain);
        yield return Dnssec("wildcard", wildcard, chain);
        yield return Dnssec("nxdomain-nsec", nxdomainNsec, chain);
        yield return Dnssec("nxdomain-nsec3", nxdomainNsec3, chain);
        yield return Dnssec("nodata", nodata, chain);
        yield return Dnssec("unsigned", unsigned, [root, orgNoDs]);
        yield return Dnssec("truncated", [.. positive[..2], (byte)(positive[2] | 0x02), .. positive[3..]], chain);
    }

    public static IEnumerable<FuzzSeed> GetFilterListSeeds()
    {
        var index = 0;
        foreach (var text in GetFilterTestLiterals().Select(literal => literal.Value).Distinct(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"auto-{index}"), [(byte)DnsFilter.DnsFilterListFormat.AutoDetect], text);
            if (text.AsSpan().IndexOfAny("|$@^*/") >= 0)
            {
                yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"adblock-{index}"), [(byte)DnsFilter.DnsFilterListFormat.AdBlock], text);
            }

            index++;
        }
    }

    /// <summary>A seed for each test of the engine: the lists it loads, then the names it evaluates.</summary>
    public static IEnumerable<FuzzSeed> GetFilterEngineSeeds()
    {
        var source = FuzzResources.ReadText(typeof(DnsSeeds).Assembly, "DnsFilterEngineTests.cs");
        var index = 0;
        foreach (var test in TestMethodRegex().Split(source))
        {
            var lists = new List<string>();
            var queries = new List<(string Domain, ushort Type)>();
            foreach (var literal in CSharpStringLiterals.Extract(test))
            {
                var before = test[..literal.Start].TrimEnd();
                var after = test[literal.End..];
                if (before.EndsWith("AddFromList(", StringComparison.Ordinal))
                {
                    lists.Add(literal.Value);
                }
                else if (before.EndsWith("Evaluate(", StringComparison.Ordinal))
                {
                    var type = QueryTypeRegex().Match(after) is { Success: true } match && Enum.TryParse<DnsFilter.DnsFilterQueryType>(match.Groups["type"].Value, ignoreCase: false, out var parsed) ? (ushort)parsed : (ushort)DnsFilter.DnsFilterQueryType.A;
                    queries.Add((literal.Value, type));
                }
            }

            if (lists.Count is 0 || queries.Count is 0)
                continue;

            var data = new List<byte> { (byte)DnsFilter.DnsFilterListFormat.AutoDetect };
            data.AddRange(FuzzText.Encode(string.Join('\n', lists)));
            for (var i = 0; i < queries.Count; i++)
            {
                data.Add(0);
                data.Add((byte)(queries[i].Type >> 8));
                data.Add((byte)queries[i].Type);
                data.Add((byte)i);
                data.AddRange(FuzzText.Encode(queries[i].Domain));
            }

            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"test-{index++}"), data.ToArray());
        }
    }

    private static IEnumerable<CSharpStringLiterals.Literal> GetFilterTestLiterals()
    {
        return CSharpStringLiterals.Extract(FuzzResources.ReadText(typeof(DnsSeeds).Assembly, "DnsFilterListReaderTests.cs"))
            .Concat(CSharpStringLiterals.Extract(FuzzResources.ReadText(typeof(DnsSeeds).Assembly, "DnsFilterEngineTests.cs")));
    }

    /// <summary>The examples of RFC 8080 section 6 (with the signatures of erratum 4935), verified and signed again.</summary>
    public static IEnumerable<FuzzSeed> GetEdDsaSeeds()
    {
        (byte Algorithm, string PrivateKey, string PublicKey, ushort KeyTag, string Signature)[] examples =
        [
            (15, "ODIyNjAzODQ2MjgwODAxMjI2NDUxOTAyMDQxNDIyNjI=", "l02Woi0iS8Aa25FQkUd9RMzZHJpBoRQwAQEX1SxZJA4=", 3613, "oL9krJun7xfBOIWcGHi7mag5/hdZrKWw15jPGrHpjQeRAvTdszaPD+QLs3fx8A4M3e23mRZ9VrbpMngwcrqNAg=="),
            (15, "DSSF3o0s0f+ElWzj9E/Osxw8hLpk55chkmx0LYN5WiY=", "zPnZ/QwEe7S8C5SPz2OfS5RR40ATk2/rYnE9xHIEijs=", 35217, "zXQ0bkYgQTEFyfLyi9QoiY6D8ZdYo4wyUhVioYZXFdT410QPRITQSqJSnzQoSm5poJ7gD7AQR0O7KuI5k2pcBg=="),
            (16, "xZ+5Cgm463xugtkY5B0Jx6erFTXp13rYegst0qRtNsOYnaVpMx0Z/c5EiA9x8wWbDDct/U3FhYWA", "3kgROaDjrh0H2iuixWBrc8g2EpBBLCdGzHmn+G2MpTPhpj/OiBVHHSfPodx1FYYUcJKm1MDpJtIA", 9713, "3cPAHkmlnxcDHMyg7vFC34l0blBhuG1qpwLmjInI8w1CMB29FkEAIJUA0amxWndkmnBZ6SKiwZSAxGILn/NBtOXft0+Gj7FSvOKxE/07+4RQvE581N3Aj/JtIyaiYVdnYtyMWbSNyGEY2213WKsJlwEA"),
            (16, "WEykD3ht3MHkU8iH4uVOLz8JLwtRBSqiBoM6fF72+Mrp/u5gjxuB1DV6NnPO2BlZdz4hdSTkOdOA", "kkreGWoccSDmUBGAe7+zsbG6ZAFQp+syPmYUurBRQc3tDjeMCJcVMRDmgcNLp5HlHAMy12VoISsA", 38353, "E1/oLjSGIbmLny/4fcgM1z4oL6aqo+izT3urCyHyvEp4Sp8Syg1eI+lJ57CSnZqjJP41O/9l4m0AsQ4f7qI1gVnML8vWWiyW2KXhT9kuAICUSxv5OWbf81Rq7Yu60npabODB0QFPb/rkW3kUZmQ0YQUA"),
        ];

        var index = 0;
        foreach (var example in examples)
        {
            var mx = new ClientRecords.DnsMxRecord { Name = "example.com", RecordType = Meziantou.Framework.DnsClient.Query.DnsQueryType.MX, RecordClass = Meziantou.Framework.DnsClient.Query.DnsQueryClass.IN, TimeToLive = 3600, Preference = 10, Exchange = "mail.example.com" };
            var rrsig = new ClientRecords.DnsRrsigRecord
            {
                Name = "example.com",
                RecordType = Meziantou.Framework.DnsClient.Query.DnsQueryType.RRSIG,
                RecordClass = Meziantou.Framework.DnsClient.Query.DnsQueryClass.IN,
                TimeToLive = 3600,
                TypeCovered = Meziantou.Framework.DnsClient.Query.DnsQueryType.MX,
                Algorithm = example.Algorithm,
                Labels = 2,
                OriginalTtl = 3600,
                SignatureExpiration = 1440021600,
                SignatureInception = 1438207200,
                KeyTag = example.KeyTag,
                SignerName = "example.com",
                Signature = Convert.FromBase64String(example.Signature),
            };
            var signedData = ClientCanonicalizer.GetSignedData([mx], rrsig);
            var selector = example.Algorithm is 15 ? (byte)0 : (byte)1;
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"rfc8080-verify-{index}"), (byte[])[selector, .. Convert.FromBase64String(example.PublicKey), .. rrsig.Signature, .. signedData]);
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"rfc8080-sign-{index}"), (byte[])[(byte)(selector | 2), (byte)index, .. Convert.FromBase64String(example.PrivateKey), .. signedData]);
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"rfc8080-sign-empty-{index}"), (byte[])[(byte)(selector | 2), 0x55, .. Convert.FromBase64String(example.PrivateKey)]);
            index++;
        }
    }

    /// <summary>The responses of the unit tests: the zone transfer dnspython signed (without its TSIG records), and messages with every record type.</summary>
    public static IEnumerable<FuzzSeed> GetTsigSeeds()
    {
        byte[][] transfer = [.. GetDnspythonTransfer().Select(RemoveTsigRecord)];
        for (byte algorithm = 0; algorithm < 5; algorithm++)
        {
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"dnspython-axfr-{algorithm}"), (byte[])[algorithm, 0, 7, .. LengthPrefixed(transfer)]);
        }

        yield return new FuzzSeed("dnspython-axfr-unsigned-middle", (byte[])[0, 0b10, 40, .. LengthPrefixed(transfer)]);
        yield return new FuzzSeed("dnspython-axfr-signed", (byte[])[0, 0, 0, .. LengthPrefixed(GetDnspythonTransfer())]);
        foreach (var (name, message) in GetMessages())
        {
            yield return new FuzzSeed("message-" + name, (byte[])[3, 0, 13, .. LengthPrefixed([message])]);
        }
    }

    /// <summary>The zone transfers of the unit tests: AXFR in three messages, IXFR with differences, IXFR up to date, refused.</summary>
    public static IEnumerable<FuzzSeed> GetZoneTransferSeeds()
    {
        var soa1 = Record("example.com", DnsQueryType.SOA, Soa(1));
        var soa2 = Record("example.com", DnsQueryType.SOA, Soa(2));
        var oldAddress = Record("www.example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.10") });
        var newAddress = Record("www.example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.11") });
        var ns = Record("example.com", DnsQueryType.NS, new DnsNsRecordData { NameServer = "ns.example.com" });

        yield return TransferSeed("axfr", serial: null,
        [
            TransferMessage(DnsQueryType.AXFR, [soa2, ns]),
            TransferMessage(question: null, [newAddress]),
            TransferMessage(question: null, [soa2]),
        ]);
        yield return TransferSeed("axfr-single-message", serial: null, [TransferMessage(DnsQueryType.AXFR, [soa2, ns, newAddress, soa2])]);
        yield return TransferSeed("axfr-empty-zone", serial: null, [TransferMessage(DnsQueryType.AXFR, [soa2, soa2])]);
        yield return TransferSeed("axfr-refused", serial: null, [TransferMessage(DnsQueryType.AXFR, [], DnsResponseCode.Refused)]);
        yield return TransferSeed("axfr-truncated", serial: null, [TransferMessage(DnsQueryType.AXFR, [soa2, ns])]);
        yield return TransferSeed("ixfr", serial: 1,
        [
            TransferMessage(DnsQueryType.IXFR, [soa2]),
            TransferMessage(question: null, [soa1, oldAddress, soa2, newAddress, soa2]),
        ]);
        yield return TransferSeed("ixfr-up-to-date", serial: 2, [TransferMessage(DnsQueryType.IXFR, [soa2])]);
        yield return TransferSeed("ixfr-full", serial: 1, [TransferMessage(DnsQueryType.IXFR, [soa2, ns, newAddress, soa2])]);
    }

    /// <summary>Responses with a COOKIE option: with a server cookie, the client cookie alone, another client cookie, an invalid length, two options.</summary>
    public static IEnumerable<FuzzSeed> GetCookieSeeds()
    {
        byte[] clientCookie = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[][] options =
        [
            [.. clientCookie, 9, 9, 9, 9, 9, 9, 9, 9],
            [.. clientCookie, .. Enumerable.Repeat((byte)7, 32)],
            clientCookie,
            [8, 7, 6, 5, 4, 3, 2, 1, 9, 9, 9, 9, 9, 9, 9, 9],
            [.. clientCookie, 9, 9, 9, 9],
        ];

        var index = 0;
        foreach (var option in options)
        {
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"cookie-{index++}"), (byte[])[.. clientCookie, .. CookieResponse([option])]);
        }

        yield return new FuzzSeed("cookie-twice", (byte[])[.. clientCookie, .. CookieResponse([clientCookie, clientCookie])]);
        yield return new FuzzSeed("cookie-badcookie", (byte[])[.. clientCookie, .. CookieResponse([options[0]], DnsResponseCode.BadCookie)]);
        yield return new FuzzSeed("no-cookie", (byte[])[.. clientCookie, .. CookieResponse([])]);
    }

    private static byte[] CookieResponse(byte[][] cookies, DnsResponseCode responseCode = DnsResponseCode.NoError)
    {
        var message = new DnsMessage { Id = 7, IsResponse = true, RecursionDesired = true, RecursionAvailable = true, ResponseCode = responseCode };
        message.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));
        message.Answers.Add(Record("example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.1") }));
        message.AdditionalRecords.Add(new DnsResourceRecord { Name = "", Type = DnsQueryType.OPT, Class = (DnsQueryClass)1232, TimeToLive = (uint)((ushort)responseCode >> 4) << 24, Data = new DnsOptRecordData { Options = [.. cookies.Select(cookie => new DnsEdnsOption { Code = 10, Data = cookie })] } });
        return DnsMessageEncoder.EncodeResponse(message);
    }

    private static byte[] TransferMessage(DnsQueryType? question, DnsResourceRecord[] answers, DnsResponseCode responseCode = DnsResponseCode.NoError)
    {
        var message = new DnsMessage { Id = 0, IsResponse = true, IsAuthoritative = true, ResponseCode = responseCode };
        if (question is { } type)
        {
            message.Questions.Add(new DnsQuestion("example.com", type));
        }

        foreach (var answer in answers)
        {
            message.Answers.Add(answer);
        }

        return DnsMessageEncoder.EncodeResponse(message);
    }

    private static FuzzSeed TransferSeed(string name, uint? serial, byte[][] messages)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, serial ?? 0);
        return new FuzzSeed(name, (byte[])[serial is null ? (byte)0 : (byte)1, .. buffer, .. LengthPrefixed(messages)]);
    }

    private static DnsSoaRecordData Soa(uint serial) => new() { PrimaryNameServer = "ns.example.com", ResponsibleMailbox = "hostmaster.example.com", Serial = serial, Refresh = 3600, Retry = 600, Expire = 86400, Minimum = 60 };

    private static byte[] LengthPrefixed(IEnumerable<byte[]> messages)
    {
        var data = new List<byte>();
        foreach (var message in messages)
        {
            data.Add((byte)(message.Length >> 8));
            data.Add((byte)message.Length);
            data.AddRange(message);
        }

        return [.. data];
    }

    /// <summary>The three messages of an AXFR that dnspython 2.7 signed with HMAC-SHA256 (the vectors of the unit tests).</summary>
    private static byte[][] GetDnspythonTransfer() =>
    [
        Convert.FromHexString("432184000001000200000001076578616d706c6503636f6d0000fc0001c00c0006000100000e100026026e73c00c0a686f73746d6173746572c00c0000000700000e1000000258000151800000003cc00c0002000100000e100002c02908747369672d6b65790000fa00ff00000000003d0b686d61632d7368613235360000006553f100012c0020d247f4658e06146bff77a37806e896bfa053b137daf05c1ecb285000caf8e141432100000000"),
        Convert.FromHexString("43218400000000010000000103777777076578616d706c6503636f6d000001000100000e100004c000020108747369672d6b65790000fa00ff00000000003d0b686d61632d7368613235360000006553f100012c00207f6da90bf09e390c438b1a6021f68d4e911de1dd29277451cb06ee925db92bbe432100000000"),
        Convert.FromHexString("432184000000000200000001046d61696c076578616d706c6503636f6d000001000100000e100004c0000202c0110006000100000e100026026e73c0110a686f73746d6173746572c0110000000700000e1000000258000151800000003c08747369672d6b65790000fa00ff00000000003d0b686d61632d7368613235360000006553f100012c00200f3e1a8f4b118e59f16ccfa202b77db88cfc1d82349a3265835933bc9abd8a19432100000000"),
    ];

    private static byte[] RemoveTsigRecord(byte[] message)
    {
        var tsig = (ClientRecords.DnsTsigRecord)ClientDecoder.DecodeResponse(message).AdditionalRecords[^1];
        var result = message.AsSpan(0, tsig.MessageOffset).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10), (ushort)(BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(10)) - 1));
        return result;
    }

    private static IEnumerable<FuzzSeed> WithPrefix(IEnumerable<(string Name, byte[] Message)> messages, params byte[][] prefixes)
    {
        foreach (var (name, message) in messages)
        {
            foreach (var prefix in prefixes)
            {
                yield return new FuzzSeed(prefix.Length is 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{prefix[0]}-{name}"), (byte[])[.. prefix, .. message]);
            }
        }
    }

    private static FuzzSeed Dnssec(string name, byte[] response, byte[][] chain)
    {
        var data = new List<byte>();
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, SeedTime);
        data.AddRange(buffer.ToArray());
        foreach (var message in (byte[][])[response, .. chain])
        {
            data.Add((byte)(message.Length >> 8));
            data.Add((byte)message.Length);
            data.AddRange(message);
        }

        return new FuzzSeed(name, data.ToArray());
    }

    private static IEnumerable<(string Name, byte[] Message)> GetMessages()
    {
        var query = new DnsMessage { Id = 0x1234, RecursionDesired = true, EdnsOptions = new DnsEdnsOptions { UdpPayloadSize = 1232, DnssecOk = true } };
        query.Questions.Add(new DnsQuestion("www.example.com", DnsQueryType.A));
        yield return ("query-edns", DnsMessageEncoder.EncodeResponse(query));

        var plainQuery = new DnsMessage { Id = 1, RecursionDesired = true };
        plainQuery.Questions.Add(new DnsQuestion("example.com", DnsQueryType.AAAA));
        plainQuery.Questions.Add(new DnsQuestion("Example.COM", DnsQueryType.MX, DnsQueryClass.CH));
        yield return ("query-two-questions", DnsMessageEncoder.EncodeResponse(plainQuery));

        var badVersion = new DnsMessage { Id = 2, EdnsOptions = new DnsEdnsOptions { Version = 1 } };
        badVersion.Questions.Add(new DnsQuestion("example.com", DnsQueryType.TXT));
        yield return ("query-edns-version-1", DnsMessageEncoder.EncodeResponse(badVersion));

        var withCookie = new DnsMessage { Id = 3, RecursionDesired = true, OpCode = DnsOpCode.Notify };
        withCookie.Questions.Add(new DnsQuestion("example.com", DnsQueryType.SOA));
        withCookie.AdditionalRecords.Add(new DnsResourceRecord { Name = "", Type = DnsQueryType.OPT, Class = (DnsQueryClass)4096, Data = new DnsOptRecordData { Options = [new DnsEdnsOption { Code = 10, Data = [1, 2, 3, 4, 5, 6, 7, 8] }, new DnsEdnsOption { Code = 8, Data = [0, 1, 24, 0, 192, 0, 2] }] } });
        yield return ("query-edns-options", DnsMessageEncoder.EncodeResponse(withCookie));

        yield return ("response-all-types", DnsMessageEncoder.EncodeResponse(CreateAllTypesResponse()));

        var extended = new DnsMessage { Id = 4, IsResponse = true, ResponseCode = DnsResponseCode.BadVersion, EdnsOptions = new DnsEdnsOptions() };
        extended.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));
        yield return ("response-badvers", DnsMessageEncoder.EncodeResponse(extended));

        var large = new DnsMessage { Id = 5, IsResponse = true, RecursionDesired = true, RecursionAvailable = true };
        large.Questions.Add(new DnsQuestion("txt.example.com", DnsQueryType.TXT));
        for (var i = 0; i < 20; i++)
        {
            large.Answers.Add(Record("txt.example.com", DnsQueryType.TXT, new DnsTxtRecordData { Text = [new string((char)('a' + i), 100)] }));
        }

        yield return ("response-large", DnsMessageEncoder.EncodeResponse(large));

        var nameError = new DnsMessage { Id = 6, IsResponse = true, ResponseCode = DnsResponseCode.NameError, IsAuthoritative = true };
        nameError.Questions.Add(query.Questions[0]);
        nameError.Authorities.Add(Record("example.com", DnsQueryType.SOA, new DnsSoaRecordData { PrimaryNameServer = "ns.example.com", ResponsibleMailbox = "hostmaster.example.com", Serial = 2026010101, Refresh = 3600, Retry = 600, Expire = 604800, Minimum = 300 }));
        yield return ("response-nxdomain", DnsMessageEncoder.EncodeResponse(nameError));

        // The edge cases of the wire format: a pointer back to the question name, a pointer chain, a forward pointer
        // (rejected), a label holding a dot, a binary label, a truncated header and record, a record longer than its data
        byte[] header = [0xAB, 0xCD, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00];
        byte[] question = [7, .. "example"u8, 3, .. "com"u8, 0, 0x00, 0x01, 0x00, 0x01];
        yield return ("pointer-to-question", [.. header, .. question, 3, .. "www"u8, 0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 192, 0, 2, 1]);
        yield return ("pointer-chain", [.. header, .. question, 3, .. "www"u8, 0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x02, 0xC0, 0x1D]);
        yield return ("forward-pointer", [.. header[..5], 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x0E, 1, (byte)'a', 0, 0x00, 0x01, 0x00, 0x01]);
        yield return ("label-with-dot", [.. header[..5], 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 8, .. "evil.com"u8, 0, 0x00, 0x01, 0x00, 0x01]);
        yield return ("binary-label", [.. header[..5], 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 3, (byte)'a', 0xFF, (byte)'\\', 0, 0x00, 0x01, 0x00, 0x01]);
        yield return ("truncated-header", header[..11]);
        yield return ("truncated-record", [.. header, .. question, 0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 192, 0]);
        yield return ("long-record-data", [.. header, .. question, 0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x06, 192, 0, 2, 1, 0xFF, 0xFF]);
        yield return ("nsec-window-too-long", [.. header[..5], 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2F, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x24, 0x00, 0x00, 0x21, .. new byte[33]]);
        yield return ("response-qr", [0x00, 0x07, 0x81, 0x80, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, .. question]);
        yield return ("two-opt-records", [.. header[..5], 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x29, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x29, 0x04, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00]);
    }

    private static DnsMessage CreateAllTypesResponse()
    {
        var message = new DnsMessage { Id = 0xBEEF, IsResponse = true, IsAuthoritative = true, RecursionDesired = true, RecursionAvailable = true, AuthenticatedData = true, EdnsOptions = new DnsEdnsOptions { DnssecOk = true } };
        message.Questions.Add(new DnsQuestion("example.com", DnsQueryType.ANY));
        message.Answers.Add(Record("example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.1") }));
        message.Answers.Add(Record("example.com", DnsQueryType.AAAA, new DnsAaaaRecordData { Address = IPAddress.Parse("2001:db8::1") }));
        message.Answers.Add(Record("www.example.com", DnsQueryType.CNAME, new DnsCnameRecordData { CanonicalName = "example.com" }));
        message.Answers.Add(Record("example.com", DnsQueryType.MX, new DnsMxRecordData { Preference = 10, Exchange = "mail.example.com" }));
        message.Answers.Add(Record("example.com", DnsQueryType.NS, new DnsNsRecordData { NameServer = "ns1.example.com" }));
        message.Answers.Add(Record("1.2.0.192.in-addr.arpa", DnsQueryType.PTR, new DnsPtrRecordData { DomainName = "host.example.com" }));
        message.Answers.Add(Record("example.com", DnsQueryType.SOA, new DnsSoaRecordData { PrimaryNameServer = "ns1.example.com", ResponsibleMailbox = "hostmaster.example.com", Serial = 2026010101, Refresh = 7200, Retry = 3600, Expire = 1209600, Minimum = 3600 }));
        message.Answers.Add(Record("example.com", DnsQueryType.TXT, new DnsTxtRecordData { Text = ["v=spf1 -all", "", "héllo wörld"] }));
        message.Answers.Add(Record("_sip._tcp.example.com", DnsQueryType.SRV, new DnsSrvRecordData { Priority = 10, Weight = 60, Port = 5060, Target = "sip.example.com" }));
        message.Answers.Add(Record("example.com", DnsQueryType.NAPTR, new DnsNaptrRecordData { Order = 100, Preference = 10, Flags = "U", Services = "E2U+sip", Regexp = "!^.*$!sip:info@example.com!", Replacement = "" }));
        message.Answers.Add(Record("example.com", DnsQueryType.CAA, new DnsCaaRecordData { Flags = 0, Tag = "issue", Value = "letsencrypt.org" }));
        message.Answers.Add(Record("example.com", DnsQueryType.DNSKEY, Dnskey(257, 13)));
        message.Answers.Add(Record("example.com", DnsQueryType.DS, Ds(13)));
        message.Answers.Add(Record("example.com", DnsQueryType.RRSIG, Rrsig(DnsQueryType.A, 13, 2, "example.com")));
        message.Answers.Add(Record("example.com", DnsQueryType.NSEC, new DnsNsecRecordData { NextDomainName = "a.example.com", TypeBitMaps = [DnsQueryType.A, DnsQueryType.MX, DnsQueryType.RRSIG, DnsQueryType.NSEC, DnsQueryType.CAA] }));
        message.Answers.Add(Record("example.com", DnsQueryType.NSEC3, new DnsNsec3RecordData { HashAlgorithm = 1, Flags = 1, Iterations = 10, Salt = [0xAB, 0xCD], NextHashedOwnerName = [.. Enumerable.Range(0, 20).Select(value => (byte)value)], TypeBitMaps = [DnsQueryType.A, DnsQueryType.RRSIG] }));
        message.Answers.Add(Record("example.com", DnsQueryType.NSEC3PARAM, new DnsNsec3ParamRecordData { HashAlgorithm = 1, Iterations = 10, Salt = [0xAB, 0xCD] }));
        message.Answers.Add(Record("_443._tcp.example.com", DnsQueryType.TLSA, new DnsTlsaRecordData { CertificateUsage = 3, Selector = 1, MatchingType = 1, CertificateAssociationData = [.. Enumerable.Range(0, 32).Select(value => (byte)value)] }));
        message.Answers.Add(Record("example.com", DnsQueryType.SSHFP, new DnsSshfpRecordData { Algorithm = 4, FingerprintType = 2, Fingerprint = [.. Enumerable.Range(0, 32).Select(value => (byte)value)] }));
        message.Answers.Add(Record("example.com", DnsQueryType.HTTPS, new DnsSvcbRecordData { Priority = 1, TargetName = "", Parameters = [new DnsSvcParam { Key = 1, Value = [2, (byte)'h', (byte)'2'] }, new DnsSvcParam { Key = 3, Value = [0x01, 0xBB] }] }));
        message.Answers.Add(Record("_dns.example.com", DnsQueryType.SVCB, new DnsSvcbRecordData { Priority = 0, TargetName = "dns.example.com", Parameters = [] }));
        message.Answers.Add(Record("example.com", DnsQueryType.LOC, new DnsLocRecordData { Version = 0, Size = 0x12, HorizontalPrecision = 0x16, VerticalPrecision = 0x13, Latitude = 0x8B3E8A3C, Longitude = 0x7A8BE9F2, Altitude = 0x00989680 }));
        message.Answers.Add(Record("example.com", DnsQueryType.HINFO, new DnsHinfoRecordData { Cpu = "INTEL-386", Os = "LINUX" }));
        message.Answers.Add(Record("example.com", DnsQueryType.RP, new DnsRpRecordData { Mailbox = "admin.example.com", TxtDomainName = "info.example.com" }));
        message.Answers.Add(Record("old.example.com", DnsQueryType.DNAME, new DnsDnameRecordData { Target = "example.net" }));
        message.Answers.Add(Record("_http._tcp.example.com", DnsQueryType.URI, new DnsUriRecordData { Priority = 10, Weight = 1, Target = "https://example.com/" }));
        message.Answers.Add(Record("example.com", DnsQueryType.CDS, new DnsUnknownRecordData { Data = [0x12, 0x34, 13, 2, .. Enumerable.Range(0, 32).Select(value => (byte)value)] }));
        message.Answers.Add(Record("example.com", (DnsQueryType)65280, new DnsUnknownRecordData { Data = [1, 2, 3] }));
        message.Authorities.Add(Record("example.com", DnsQueryType.NS, new DnsNsRecordData { NameServer = "ns2.example.net" }));
        message.AdditionalRecords.Add(Record("ns1.example.com", DnsQueryType.A, new DnsARecordData { Address = IPAddress.Parse("192.0.2.53") }));
        return message;
    }

    private static byte[] CreateResponse(string name, DnsQueryType type, DnsResourceRecord[] answers, DnsResponseCode responseCode = DnsResponseCode.NoError, DnsResourceRecord[]? authorities = null)
    {
        var message = new DnsMessage { Id = 0x4242, IsResponse = true, RecursionDesired = true, RecursionAvailable = true, CheckingDisabled = true, ResponseCode = responseCode, EdnsOptions = new DnsEdnsOptions { DnssecOk = true } };
        message.Questions.Add(new DnsQuestion(name, type));
        foreach (var answer in answers)
        {
            message.Answers.Add(answer);
        }

        foreach (var authority in authorities ?? [])
        {
            message.Authorities.Add(authority);
        }

        return DnsMessageEncoder.EncodeResponse(message);
    }

    private static DnsResourceRecord Record(string name, DnsQueryType type, DnsResourceRecordData data) => new() { Name = name, Type = type, TimeToLive = 3600, Data = data };

    private static DnsDnskeyRecordData Dnskey(ushort flags, byte algorithm) => new() { Flags = flags, Protocol = 3, Algorithm = algorithm, PublicKey = [.. Enumerable.Range(1, algorithm is 8 ? 260 : 64).Select(value => (byte)value)] };

    private static DnsDsRecordData Ds(byte algorithm) => new() { KeyTag = 12345, Algorithm = algorithm, DigestType = 2, Digest = [.. Enumerable.Range(0, 32).Select(value => (byte)value)] };

    private static DnsRrsigRecordData Rrsig(DnsQueryType typeCovered, byte algorithm, byte labels, string signerName) => new()
    {
        TypeCovered = typeCovered,
        Algorithm = algorithm,
        Labels = labels,
        OriginalTtl = 3600,
        SignatureExpiration = SeedTime + 86400,
        SignatureInception = SeedTime - 86400,
        KeyTag = 12345,
        SignerName = signerName,
        Signature = [.. Enumerable.Range(0, algorithm is 8 ? 256 : 64).Select(value => (byte)value)],
    };

    [GeneratedRegex(@"\[(Fact|Theory)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TestMethodRegex();

    [GeneratedRegex(@"^\s*,\s*DnsFilterQueryType\.(?<type>\w+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex QueryTypeRegex();
}
