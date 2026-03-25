using System.Net;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Xunit;

namespace Meziantou.Framework.DnsServer.Tests;

public sealed class DnsMessageEncoderTests
{
    [Fact]
    public void RoundTrip_SimpleQuery()
    {
        var query = CreateSimpleQuery("example.com", DnsQueryType.A);
        var bytes = DnsMessageEncoder.EncodeResponse(query);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Equal(query.Id, decoded.Id);
        Assert.Single(decoded.Questions);
        Assert.Equal("example.com", decoded.Questions[0].Name);
        Assert.Equal(DnsQueryType.A, decoded.Questions[0].Type);
        Assert.Equal(DnsQueryClass.IN, decoded.Questions[0].QueryClass);
    }

    [Fact]
    public void RoundTrip_HeaderFlags()
    {
        var message = new DnsMessage
        {
            Id = 0x1234,
            IsResponse = true,
            OpCode = DnsOpCode.Query,
            IsAuthoritative = true,
            IsTruncated = false,
            RecursionDesired = true,
            RecursionAvailable = true,
            AuthenticatedData = true,
            CheckingDisabled = false,
            ResponseCode = DnsResponseCode.NoError,
        };
        message.Questions.Add(new DnsQuestion("test.example.com", DnsQueryType.A));

        var bytes = DnsMessageEncoder.EncodeResponse(message);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Equal(0x1234, decoded.Id);
        Assert.True(decoded.IsResponse);
        Assert.Equal(DnsOpCode.Query, decoded.OpCode);
        Assert.True(decoded.IsAuthoritative);
        Assert.False(decoded.IsTruncated);
        Assert.True(decoded.RecursionDesired);
        Assert.True(decoded.RecursionAvailable);
        Assert.True(decoded.AuthenticatedData);
        Assert.False(decoded.CheckingDisabled);
        Assert.Equal(DnsResponseCode.NoError, decoded.ResponseCode);
    }

    [Fact]
    public void RoundTrip_ARecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.A,
            new DnsARecordData { Address = IPAddress.Parse("192.168.1.1") });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Single(decoded.Answers);
        var record = decoded.Answers[0];
        Assert.Equal("example.com", record.Name);
        Assert.Equal(DnsQueryType.A, record.Type);
        Assert.Equal(DnsQueryClass.IN, record.Class);
        Assert.Equal(300u, record.TimeToLive);

        var data = Assert.IsType<DnsARecordData>(record.Data);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), data.Address);
    }

    [Fact]
    public void RoundTrip_AaaaRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.AAAA,
            new DnsAaaaRecordData { Address = IPAddress.Parse("2001:db8::1") });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsAaaaRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(IPAddress.Parse("2001:db8::1"), data.Address);
    }

    [Fact]
    public void RoundTrip_CnameRecord()
    {
        var response = CreateResponseWithAnswer(
            "www.example.com",
            DnsQueryType.CNAME,
            new DnsCnameRecordData { CanonicalName = "example.com" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsCnameRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("example.com", data.CanonicalName);
    }

    [Fact]
    public void RoundTrip_MxRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.MX,
            new DnsMxRecordData { Preference = 10, Exchange = "mail.example.com" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsMxRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(10, data.Preference);
        Assert.Equal("mail.example.com", data.Exchange);
    }

    [Fact]
    public void RoundTrip_NsRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.NS,
            new DnsNsRecordData { NameServer = "ns1.example.com" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsNsRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("ns1.example.com", data.NameServer);
    }

    [Fact]
    public void RoundTrip_PtrRecord()
    {
        var response = CreateResponseWithAnswer(
            "1.1.168.192.in-addr.arpa",
            DnsQueryType.PTR,
            new DnsPtrRecordData { DomainName = "host.example.com" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsPtrRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("host.example.com", data.DomainName);
    }

    [Fact]
    public void RoundTrip_SoaRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.SOA,
            new DnsSoaRecordData
            {
                PrimaryNameServer = "ns1.example.com",
                ResponsibleMailbox = "admin.example.com",
                Serial = 2024010101,
                Refresh = 3600,
                Retry = 900,
                Expire = 604800,
                Minimum = 300,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsSoaRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("ns1.example.com", data.PrimaryNameServer);
        Assert.Equal("admin.example.com", data.ResponsibleMailbox);
        Assert.Equal(2024010101u, data.Serial);
        Assert.Equal(3600, data.Refresh);
        Assert.Equal(900, data.Retry);
        Assert.Equal(604800, data.Expire);
        Assert.Equal(300u, data.Minimum);
    }

    [Fact]
    public void RoundTrip_TxtRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.TXT,
            new DnsTxtRecordData { Text = ["v=spf1 include:_spf.google.com ~all", "hello world"] });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsTxtRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(2, data.Text.Count);
        Assert.Equal("v=spf1 include:_spf.google.com ~all", data.Text[0]);
        Assert.Equal("hello world", data.Text[1]);
    }

    [Fact]
    public void RoundTrip_SrvRecord()
    {
        var response = CreateResponseWithAnswer(
            "_sip._tcp.example.com",
            DnsQueryType.SRV,
            new DnsSrvRecordData
            {
                Priority = 10,
                Weight = 60,
                Port = 5060,
                Target = "sip.example.com",
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsSrvRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(10, data.Priority);
        Assert.Equal(60, data.Weight);
        Assert.Equal(5060, data.Port);
        Assert.Equal("sip.example.com", data.Target);
    }

    [Fact]
    public void RoundTrip_CaaRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.CAA,
            new DnsCaaRecordData
            {
                Flags = 0,
                Tag = "issue",
                Value = "letsencrypt.org",
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsCaaRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(0, data.Flags);
        Assert.Equal("issue", data.Tag);
        Assert.Equal("letsencrypt.org", data.Value);
    }

    [Fact]
    public void RoundTrip_NaptrRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.NAPTR,
            new DnsNaptrRecordData
            {
                Order = 100,
                Preference = 10,
                Flags = "S",
                Services = "SIP+D2T",
                Regexp = "",
                Replacement = "_sip._tcp.example.com",
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsNaptrRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(100, data.Order);
        Assert.Equal(10, data.Preference);
        Assert.Equal("S", data.Flags);
        Assert.Equal("SIP+D2T", data.Services);
        Assert.Equal("", data.Regexp);
        Assert.Equal("_sip._tcp.example.com", data.Replacement);
    }

    [Fact]
    public void RoundTrip_HinfoRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.HINFO,
            new DnsHinfoRecordData { Cpu = "Intel", Os = "Linux" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsHinfoRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("Intel", data.Cpu);
        Assert.Equal("Linux", data.Os);
    }

    [Fact]
    public void RoundTrip_RpRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.RP,
            new DnsRpRecordData { Mailbox = "admin.example.com", TxtDomainName = "info.example.com" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsRpRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("admin.example.com", data.Mailbox);
        Assert.Equal("info.example.com", data.TxtDomainName);
    }

    [Fact]
    public void RoundTrip_DnameRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.DNAME,
            new DnsDnameRecordData { Target = "other.example.com" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsDnameRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("other.example.com", data.Target);
    }

    [Fact]
    public void RoundTrip_LocRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.LOC,
            new DnsLocRecordData
            {
                Version = 0,
                Size = 0x12,
                HorizontalPrecision = 0x16,
                VerticalPrecision = 0x13,
                Latitude = 2_147_483_648 + 5_100_000,
                Longitude = 2_147_483_648 - 700_000,
                Altitude = 10_000_000 + 50_000,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsLocRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(0, data.Version);
        Assert.Equal(0x12, data.Size);
        Assert.Equal(0x16, data.HorizontalPrecision);
        Assert.Equal(0x13, data.VerticalPrecision);
    }

    [Fact]
    public void RoundTrip_UriRecord()
    {
        var response = CreateResponseWithAnswer(
            "_http._tcp.example.com",
            DnsQueryType.URI,
            new DnsUriRecordData
            {
                Priority = 10,
                Weight = 1,
                Target = "https://example.com/path",
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsUriRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(10, data.Priority);
        Assert.Equal(1, data.Weight);
        Assert.Equal("https://example.com/path", data.Target);
    }

    [Fact]
    public void RoundTrip_DnskeyRecord()
    {
        byte[] publicKey = [0x01, 0x02, 0x03, 0x04, 0x05];
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.DNSKEY,
            new DnsDnskeyRecordData
            {
                Flags = 257,
                Protocol = 3,
                Algorithm = 13,
                PublicKey = publicKey,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsDnskeyRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(257, data.Flags);
        Assert.Equal(3, data.Protocol);
        Assert.Equal(13, data.Algorithm);
        Assert.Equal(publicKey, data.PublicKey);
    }

    [Fact]
    public void RoundTrip_DsRecord()
    {
        byte[] digest = [0xAB, 0xCD, 0xEF, 0x01, 0x23];
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.DS,
            new DnsDsRecordData
            {
                KeyTag = 12345,
                Algorithm = 13,
                DigestType = 2,
                Digest = digest,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsDsRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(12345, data.KeyTag);
        Assert.Equal(13, data.Algorithm);
        Assert.Equal(2, data.DigestType);
        Assert.Equal(digest, data.Digest);
    }

    [Fact]
    public void RoundTrip_TlsaRecord()
    {
        byte[] certData = [0x01, 0x02, 0x03, 0x04];
        var response = CreateResponseWithAnswer(
            "_443._tcp.example.com",
            DnsQueryType.TLSA,
            new DnsTlsaRecordData
            {
                CertificateUsage = 3,
                Selector = 1,
                MatchingType = 1,
                CertificateAssociationData = certData,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsTlsaRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(3, data.CertificateUsage);
        Assert.Equal(1, data.Selector);
        Assert.Equal(1, data.MatchingType);
        Assert.Equal(certData, data.CertificateAssociationData);
    }

    [Fact]
    public void RoundTrip_SshfpRecord()
    {
        byte[] fingerprint = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04];
        var response = CreateResponseWithAnswer(
            "host.example.com",
            DnsQueryType.SSHFP,
            new DnsSshfpRecordData
            {
                Algorithm = 1,
                FingerprintType = 2,
                Fingerprint = fingerprint,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsSshfpRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(1, data.Algorithm);
        Assert.Equal(2, data.FingerprintType);
        Assert.Equal(fingerprint, data.Fingerprint);
    }

    [Fact]
    public void RoundTrip_SvcbRecord()
    {
        byte[] paramValue = [0x01, 0x02];
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.SVCB,
            new DnsSvcbRecordData
            {
                Priority = 1,
                TargetName = "svc.example.com",
                Parameters = [new DnsSvcParam { Key = 1, Value = paramValue }],
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsSvcbRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(1, data.Priority);
        Assert.Equal("svc.example.com", data.TargetName);
        Assert.Single(data.Parameters);
        Assert.Equal(1, data.Parameters[0].Key);
        Assert.Equal(paramValue, data.Parameters[0].Value);
    }

    [Fact]
    public void RoundTrip_Nsec3ParamRecord()
    {
        byte[] salt = [0x01, 0x02, 0x03];
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.NSEC3PARAM,
            new DnsNsec3ParamRecordData
            {
                HashAlgorithm = 1,
                Flags = 0,
                Iterations = 10,
                Salt = salt,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsNsec3ParamRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(1, data.HashAlgorithm);
        Assert.Equal(0, data.Flags);
        Assert.Equal(10, data.Iterations);
        Assert.Equal(salt, data.Salt);
    }

    [Fact]
    public void RoundTrip_NsecRecord()
    {
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.NSEC,
            new DnsNsecRecordData
            {
                NextDomainName = "next.example.com",
                TypeBitMaps = [DnsQueryType.A, DnsQueryType.AAAA, DnsQueryType.MX],
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsNsecRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal("next.example.com", data.NextDomainName);
        Assert.Contains(DnsQueryType.A, data.TypeBitMaps);
        Assert.Contains(DnsQueryType.AAAA, data.TypeBitMaps);
        Assert.Contains(DnsQueryType.MX, data.TypeBitMaps);
        Assert.Equal(3, data.TypeBitMaps.Count);
    }

    [Fact]
    public void RoundTrip_Nsec3Record()
    {
        byte[] salt = [0xAA, 0xBB];
        byte[] nextHash = [0x01, 0x02, 0x03, 0x04, 0x05];
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.NSEC3,
            new DnsNsec3RecordData
            {
                HashAlgorithm = 1,
                Flags = 1,
                Iterations = 12,
                Salt = salt,
                NextHashedOwnerName = nextHash,
                TypeBitMaps = [DnsQueryType.A, DnsQueryType.NS],
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsNsec3RecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(1, data.HashAlgorithm);
        Assert.Equal(1, data.Flags);
        Assert.Equal(12, data.Iterations);
        Assert.Equal(salt, data.Salt);
        Assert.Equal(nextHash, data.NextHashedOwnerName);
        Assert.Contains(DnsQueryType.A, data.TypeBitMaps);
        Assert.Contains(DnsQueryType.NS, data.TypeBitMaps);
    }

    [Fact]
    public void RoundTrip_RrsigRecord()
    {
        byte[] signature = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        var response = CreateResponseWithAnswer(
            "example.com",
            DnsQueryType.RRSIG,
            new DnsRrsigRecordData
            {
                TypeCovered = DnsQueryType.A,
                Algorithm = 13,
                Labels = 2,
                OriginalTtl = 3600,
                SignatureExpiration = 1700000000,
                SignatureInception = 1690000000,
                KeyTag = 12345,
                SignerName = "example.com",
                Signature = signature,
            });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsRrsigRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(DnsQueryType.A, data.TypeCovered);
        Assert.Equal(13, data.Algorithm);
        Assert.Equal(2, data.Labels);
        Assert.Equal(3600u, data.OriginalTtl);
        Assert.Equal(1700000000u, data.SignatureExpiration);
        Assert.Equal(1690000000u, data.SignatureInception);
        Assert.Equal(12345, data.KeyTag);
        Assert.Equal("example.com", data.SignerName);
        Assert.Equal(signature, data.Signature);
    }

    [Fact]
    public void RoundTrip_EdnsOptions()
    {
        var message = new DnsMessage
        {
            Id = 1,
            IsResponse = true,
            ResponseCode = DnsResponseCode.NoError,
            EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = 4096,
                Version = 0,
                DnssecOk = true,
            },
        };
        message.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));

        var bytes = DnsMessageEncoder.EncodeResponse(message);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.NotNull(decoded.EdnsOptions);
        Assert.Equal(4096, decoded.EdnsOptions.UdpPayloadSize);
        Assert.Equal(0, decoded.EdnsOptions.Version);
        Assert.True(decoded.EdnsOptions.DnssecOk);
    }

    [Fact]
    public void RoundTrip_MultipleRecords()
    {
        var response = CreateSimpleQuery("example.com", DnsQueryType.A);
        response.IsResponse = true;
        response.Answers.Add(new DnsResourceRecord
        {
            Name = "example.com",
            Type = DnsQueryType.A,
            Class = DnsQueryClass.IN,
            TimeToLive = 300,
            Data = new DnsARecordData { Address = IPAddress.Parse("192.168.1.1") },
        });
        response.Answers.Add(new DnsResourceRecord
        {
            Name = "example.com",
            Type = DnsQueryType.A,
            Class = DnsQueryClass.IN,
            TimeToLive = 300,
            Data = new DnsARecordData { Address = IPAddress.Parse("192.168.1.2") },
        });
        response.Authorities.Add(new DnsResourceRecord
        {
            Name = "example.com",
            Type = DnsQueryType.NS,
            Class = DnsQueryClass.IN,
            TimeToLive = 3600,
            Data = new DnsNsRecordData { NameServer = "ns1.example.com" },
        });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Equal(2, decoded.Answers.Count);
        Assert.Single(decoded.Authorities);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), ((DnsARecordData)decoded.Answers[0].Data!).Address);
        Assert.Equal(IPAddress.Parse("192.168.1.2"), ((DnsARecordData)decoded.Answers[1].Data!).Address);
        Assert.Equal("ns1.example.com", ((DnsNsRecordData)decoded.Authorities[0].Data!).NameServer);
    }

    [Fact]
    public void RoundTrip_ResponseCodes()
    {
        foreach (var rcode in new[] { DnsResponseCode.NoError, DnsResponseCode.FormError, DnsResponseCode.ServerFailure, DnsResponseCode.NameError, DnsResponseCode.NotImplemented, DnsResponseCode.Refused })
        {
            var message = new DnsMessage
            {
                Id = 1,
                IsResponse = true,
                ResponseCode = rcode,
            };
            message.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));

            var bytes = DnsMessageEncoder.EncodeResponse(message);
            var decoded = DnsMessageEncoder.DecodeQuery(bytes);

            Assert.Equal(rcode, decoded.ResponseCode);
        }
    }

    [Fact]
    public void RoundTrip_UnknownRecordType()
    {
        byte[] rawData = [0x01, 0x02, 0x03, 0x04, 0x05];
        var response = CreateResponseWithAnswer(
            "example.com",
            (DnsQueryType)65000,
            new DnsUnknownRecordData { Data = rawData });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        var data = Assert.IsType<DnsUnknownRecordData>(Assert.Single(decoded.Answers).Data);
        Assert.Equal(rawData, data.Data);
    }

    [Fact]
    public void RoundTrip_DomainNameWithTrailingDot()
    {
        var response = CreateResponseWithAnswer(
            "example.com.",
            DnsQueryType.A,
            new DnsARecordData { Address = IPAddress.Parse("10.0.0.1") });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        // Domain names are stored without trailing dot in the decoded form
        Assert.Equal("example.com", decoded.Answers[0].Name);
    }

    [Fact]
    public void RoundTrip_RootDomain()
    {
        var response = CreateResponseWithAnswer(
            ".",
            DnsQueryType.NS,
            new DnsNsRecordData { NameServer = "a.root-servers.net" });

        var bytes = DnsMessageEncoder.EncodeResponse(response);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Equal("", decoded.Answers[0].Name);
    }

    [Fact]
    public void DecodeQuery_TooShort_Throws()
    {
        var bytes = new byte[11]; // Less than 12-byte header
        Assert.Throws<DnsProtocolException>(() => DnsMessageEncoder.DecodeQuery(bytes));
    }

    [Fact]
    public void RoundTrip_QueryClassCH()
    {
        var query = new DnsMessage { Id = 42 };
        query.Questions.Add(new DnsQuestion("version.bind", DnsQueryType.TXT, DnsQueryClass.CH));

        var bytes = DnsMessageEncoder.EncodeResponse(query);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Equal(DnsQueryClass.CH, decoded.Questions[0].QueryClass);
    }

    [Fact]
    public void RoundTrip_MultipleQuestions()
    {
        var query = new DnsMessage { Id = 100 };
        query.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));
        query.Questions.Add(new DnsQuestion("example.com", DnsQueryType.AAAA));

        var bytes = DnsMessageEncoder.EncodeResponse(query);
        var decoded = DnsMessageEncoder.DecodeQuery(bytes);

        Assert.Equal(2, decoded.Questions.Count);
        Assert.Equal(DnsQueryType.A, decoded.Questions[0].Type);
        Assert.Equal(DnsQueryType.AAAA, decoded.Questions[1].Type);
    }

    private static DnsMessage CreateSimpleQuery(string name, DnsQueryType type)
    {
        var message = new DnsMessage { Id = 1 };
        message.Questions.Add(new DnsQuestion(name, type));

        return message;
    }

    private static DnsMessage CreateResponseWithAnswer(string name, DnsQueryType type, DnsResourceRecordData data)
    {
        var message = new DnsMessage
        {
            Id = 1,
            IsResponse = true,
            ResponseCode = DnsResponseCode.NoError,
        };
        message.Questions.Add(new DnsQuestion(name, type));
        message.Answers.Add(new DnsResourceRecord
        {
            Name = name,
            Type = type,
            Class = DnsQueryClass.IN,
            TimeToLive = 300,
            Data = data,
        });

        return message;
    }
}
