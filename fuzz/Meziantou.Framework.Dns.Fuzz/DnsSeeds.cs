using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Meziantou.Framework.Fuzzing;
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
