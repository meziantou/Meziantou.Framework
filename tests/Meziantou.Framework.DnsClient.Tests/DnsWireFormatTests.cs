using System.Net;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnsWireFormatTests
{
    [Fact]
    public void WriteDomainName_SimpleLabel()
    {
        var writer = new DnsWireWriter();
        writer.WriteDomainName("example.com");
        var bytes = writer.ToArray();

        // \x07example\x03com\x00
        Assert.HasCount(13, bytes);
        Assert.Equal(7, bytes[0]);
        Assert.Equal((byte)'e', bytes[1]);
        Assert.Equal(3, bytes[8]);
        Assert.Equal((byte)'c', bytes[9]);
        Assert.Equal(0, bytes[12]);
    }

    [Fact]
    public void WriteDomainName_RootDomain()
    {
        var writer = new DnsWireWriter();
        writer.WriteDomainName(".");
        var bytes = writer.ToArray();

        Assert.Single(bytes);
        Assert.Equal(0, bytes[0]);
    }

    [Fact]
    public void WriteDomainName_TrailingDot()
    {
        var writer = new DnsWireWriter();
        writer.WriteDomainName("example.com.");
        var bytes = writer.ToArray();

        // Should be same as without trailing dot
        Assert.HasCount(13, bytes);
        Assert.Equal(0, bytes[12]);
    }

    [Fact]
    public void ReadDomainName_SimpleLabel()
    {
        byte[] data = [7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e', 3, (byte)'c', (byte)'o', (byte)'m', 0];
        var reader = new DnsWireReader(data);
        var name = reader.ReadDomainName();

        Assert.Equal("example.com", name);
    }

    [Fact]
    public void ReadDomainName_WithCompressionPointer()
    {
        // Build a message where the domain name uses a compression pointer
        // First: \x07example\x03com\x00 at offset 0 (13 bytes)
        // Then: \x03www followed by pointer to offset 0 (0xC0 0x00)
        byte[] data = [7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e', 3, (byte)'c', (byte)'o', (byte)'m', 0, 3, (byte)'w', (byte)'w', (byte)'w', 0xC0, 0x00];

        var reader = new DnsWireReader(data);
        reader.Skip(13); // Skip past the first name
        var name = reader.ReadDomainName();

        Assert.Equal("www.example.com", name);
    }

    [Fact]
    public void ReadDomainName_RootDomain()
    {
        byte[] data = [0];
        var reader = new DnsWireReader(data);
        var name = reader.ReadDomainName();

        Assert.Equal("", name);
    }

    [Fact]
    public void EncodeQuery_BasicQuery()
    {
        var query = new DnsQueryMessage
        {
            Id = 0x1234,
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A, DnsQueryClass.IN));

        var bytes = DnsMessageEncoder.EncodeQuery(query, out _);

        Assert.HasCountGreaterThanOrEqual(12, bytes); // At least header

        // Check header ID
        Assert.Equal(0x12, bytes[0]);
        Assert.Equal(0x34, bytes[1]);

        // Check flags: RD bit set (bit 8 of flags = 0x01 in second byte)
        Assert.Equal(0x01, bytes[2]);
        Assert.Equal(0x00, bytes[3]);

        // QDCOUNT = 1
        Assert.Equal(0x00, bytes[4]);
        Assert.Equal(0x01, bytes[5]);
    }

    [Fact]
    public void EncodeQuery_WithEdns()
    {
        var query = new DnsQueryMessage
        {
            Id = 0x0001,
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("test.com", DnsQueryType.AAAA));
        query.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = 4096,
            DnssecOk = true,
        };

        var bytes = DnsMessageEncoder.EncodeQuery(query, out _);

        // ARCOUNT should be 1 (for OPT record)
        Assert.Equal(0x00, bytes[10]);
        Assert.Equal(0x01, bytes[11]);
    }

    [Fact]
    public void DecodeResponse_OptRecord_PopulatesEdnsMetadata()
    {
        var writer = new DnsWireWriter(512);

        writer.WriteUInt16(0x1234);
        writer.WriteUInt16(0x8180);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);
        writer.WriteUInt16(1);

        writer.WriteByte(0);
        writer.WriteUInt16((ushort)DnsQueryType.OPT);
        writer.WriteUInt16(1232);
        writer.WriteByte(1);
        writer.WriteByte(0);
        writer.WriteUInt16(0x8000);
        writer.WriteUInt16(0);

        var response = DnsMessageEncoder.DecodeResponse(writer.ToArray());
        var opt = Assert.IsType<Response.Records.DnsOptRecord>(Assert.Single(response.AdditionalRecords));

        Assert.Equal(1232, opt.UdpPayloadSize);
        Assert.Equal(1, opt.ExtendedRCode);
        Assert.Equal(0, opt.EdnsVersion);
        Assert.True(opt.DnssecOk);
    }

    [Fact]
    public void DecodeResponse_Default_DoesNotPreserveRawRecordData()
    {
        var response = DnsMessageEncoder.DecodeResponse(CreateResponseWithARecord());
        var record = Assert.Single(response.Answers);

        Assert.Empty(record.RawData);
    }

    [Fact]
    public void DecodeResponse_WhenRequested_PreservesRawRecordData()
    {
        var response = DnsMessageEncoder.DecodeResponse(CreateResponseWithARecord(), preserveRawRecordData: true);
        var record = Assert.Single(response.Answers);

        Assert.Equal([1, 2, 3, 4], record.RawData);
    }

    [Fact]
    public void DecodeResponse_BasicResponse()
    {
        // Craft a minimal DNS response with 1 A record answer
        var writer = new DnsWireWriter(512);

        // Header
        writer.WriteUInt16(0x1234); // ID
        writer.WriteUInt16(0x8180); // QR=1, RD=1, RA=1
        writer.WriteUInt16(1);      // QDCOUNT
        writer.WriteUInt16(1);      // ANCOUNT
        writer.WriteUInt16(0);      // NSCOUNT
        writer.WriteUInt16(0);      // ARCOUNT

        // Question
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(1);  // QTYPE = A
        writer.WriteUInt16(1);  // QCLASS = IN

        // Answer: example.com A 1.2.3.4
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(1);  // TYPE = A
        writer.WriteUInt16(1);  // CLASS = IN
        writer.WriteUInt32(300); // TTL
        writer.WriteUInt16(4);  // RDLENGTH
        writer.WriteBytes([1, 2, 3, 4]); // RDATA

        var response = DnsMessageEncoder.DecodeResponse(writer.ToArray());

        Assert.Equal(0x1234, response.Header.Id);
        Assert.True(response.Header.IsResponse);
        Assert.True(response.Header.RecursionDesired);
        Assert.True(response.Header.RecursionAvailable);
        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.Single(response.Questions);
        Assert.Equal("example.com", response.Questions[0].Name);
        Assert.Single(response.Answers);

        var aRecord = Assert.IsType<Response.Records.DnsARecord>(response.Answers[0]);
        Assert.Equal(System.Net.IPAddress.Parse("1.2.3.4"), aRecord.Address);
        Assert.Equal("example.com", aRecord.Name);
        Assert.Equal((uint)300, aRecord.TimeToLive);
    }

    private static byte[] CreateResponseWithARecord()
    {
        var writer = new DnsWireWriter(512);

        writer.WriteUInt16(0x1234);
        writer.WriteUInt16(0x8180);
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);

        writer.WriteDomainName("example.com");
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);

        writer.WriteDomainName("example.com");
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);
        writer.WriteUInt32(300);
        writer.WriteUInt16(4);
        writer.WriteBytes([1, 2, 3, 4]);

        return writer.ToArray();
    }

    [Fact]
    public void DecodeResponse_MxRecord()
    {
        var writer = new DnsWireWriter(512);

        // Header
        writer.WriteUInt16(0x0001);
        writer.WriteUInt16(0x8180); // QR=1, RD=1, RA=1
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);

        // Question
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(15); // MX
        writer.WriteUInt16(1);

        // Answer: MX record
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(15); // MX
        writer.WriteUInt16(1);  // IN
        writer.WriteUInt32(3600);

        // MX RDATA: preference (2 bytes) + exchange domain name
        var rdataStart = writer.Position;
        writer.WriteUInt16(0); // placeholder for RDLENGTH
        var rdataContentStart = writer.Position;
        writer.WriteUInt16(10); // preference
        writer.WriteDomainName("mail.example.com");
        var rdLength = writer.Position - rdataContentStart;
        writer.WriteUInt16At((ushort)rdLength, rdataStart);

        var response = DnsMessageEncoder.DecodeResponse(writer.ToArray());

        Assert.Single(response.Answers);
        var mxRecord = Assert.IsType<Response.Records.DnsMxRecord>(response.Answers[0]);
        Assert.Equal(10, mxRecord.Preference);
        Assert.Equal("mail.example.com", mxRecord.Exchange);
    }

    [Fact]
    public void DecodeResponse_TxtRecord()
    {
        var writer = new DnsWireWriter(512);

        // Header
        writer.WriteUInt16(0x0001);
        writer.WriteUInt16(0x8180);
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);

        // Question
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(16); // TXT
        writer.WriteUInt16(1);

        // Answer: TXT record with "hello world"
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(16); // TXT
        writer.WriteUInt16(1);  // IN
        writer.WriteUInt32(3600);
        var text = "hello world"u8;
        writer.WriteUInt16((ushort)(1 + text.Length)); // RDLENGTH
        writer.WriteByte((byte)text.Length);
        writer.WriteBytes(text);

        var response = DnsMessageEncoder.DecodeResponse(writer.ToArray());

        Assert.Single(response.Answers);
        var txtRecord = Assert.IsType<Response.Records.DnsTxtRecord>(response.Answers[0]);
        Assert.Single(txtRecord.Text);
        Assert.Equal("hello world", txtRecord.Text[0]);
    }

    [Fact]
    public void DecodeResponse_SoaRecord()
    {
        var writer = new DnsWireWriter(512);

        // Header
        writer.WriteUInt16(0x0001);
        writer.WriteUInt16(0x8180);
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);

        // Question
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(6); // SOA
        writer.WriteUInt16(1);

        // Answer: SOA record
        writer.WriteDomainName("example.com");
        writer.WriteUInt16(6); // SOA
        writer.WriteUInt16(1); // IN
        writer.WriteUInt32(3600);

        var rdataStart = writer.Position;
        writer.WriteUInt16(0); // placeholder RDLENGTH
        var rdataContentStart = writer.Position;
        writer.WriteDomainName("ns1.example.com");
        writer.WriteDomainName("admin.example.com");
        writer.WriteUInt32(2024010100); // serial
        writer.WriteUInt32(3600);       // refresh
        writer.WriteUInt32(900);        // retry
        writer.WriteUInt32(604800);     // expire
        writer.WriteUInt32(86400);      // minimum
        var rdLength = writer.Position - rdataContentStart;
        writer.WriteUInt16At((ushort)rdLength, rdataStart);

        var response = DnsMessageEncoder.DecodeResponse(writer.ToArray());

        Assert.Single(response.Answers);
        var soaRecord = Assert.IsType<Response.Records.DnsSoaRecord>(response.Answers[0]);
        Assert.Equal("ns1.example.com", soaRecord.PrimaryNameServer);
        Assert.Equal("admin.example.com", soaRecord.ResponsibleMailbox);
        Assert.Equal(2024010100u, soaRecord.Serial);
        Assert.Equal(3600, soaRecord.Refresh);
        Assert.Equal(900, soaRecord.Retry);
        Assert.Equal(604800, soaRecord.Expire);
        Assert.Equal(86400u, soaRecord.Minimum);
    }

    [Theory]
    [InlineData(DnsQueryType.DNSKEY, 2, "DNSKEY shorter than its 4-byte fixed fields")]
    [InlineData(DnsQueryType.DS, 1, "DS shorter than its 4-byte fixed fields")]
    [InlineData(DnsQueryType.TLSA, 0, "TLSA shorter than its 3-byte fixed fields")]
    [InlineData(DnsQueryType.SSHFP, 1, "SSHFP shorter than its 2-byte fixed fields")]
    [InlineData(DnsQueryType.URI, 2, "URI shorter than its 4-byte fixed fields")]
    public void DecodeResponse_RecordShorterThanItsFixedFields_ThrowsDnsProtocolException(DnsQueryType type, ushort rdLength, string scenario)
    {
        var message = BuildSingleAnswerMessage(type, rdLength, rdata: [0, 0], trailing: [1, 2, 3, 4, 5, 6, 7, 8]);

        // A negative computed length must surface as DnsProtocolException, never as ArgumentOutOfRangeException.
        var exception = Record.Exception(() => DnsMessageEncoder.DecodeResponse(message));
        Assert.IsType<DnsProtocolException>(exception);
        Assert.NotNull(scenario);
    }

    [Fact]
    public void DecodeResponse_CaaTagLengthBeyondRdata_ThrowsDnsProtocolException()
    {
        var message = BuildSingleAnswerMessage(DnsQueryType.CAA, rdLength: 3, rdata: [0, 40, (byte)'x'], trailing: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.Throws<DnsProtocolException>(() => DnsMessageEncoder.DecodeResponse(message));
    }

    [Fact]
    public void DecodeResponse_RecordReadingPastItsRdLength_DoesNotDesynchronizeFollowingRecords()
    {
        // A TXT record declaring RDLENGTH=1 but a character-string length byte of 8 must not consume the next record.
        var message = new List<byte>
        {
            0x12, 0x34, 0x81, 0x80,
            0x00, 0x00, // QDCOUNT
            0x00, 0x02, // ANCOUNT
            0x00, 0x00,
            0x00, 0x00,
        };

        message.AddRange([0x00, 0x00, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x01, 0x08]); // TXT, RDLENGTH=1
        message.AddRange([0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 192, 0, 2, 1]); // A 192.0.2.1

        var exception = Record.Exception(() => DnsMessageEncoder.DecodeResponse([.. message]));
        if (exception is not null)
        {
            Assert.IsType<DnsProtocolException>(exception);
            return;
        }

        // If it parses, the second record must still be the A record it actually is.
        var response = DnsMessageEncoder.DecodeResponse([.. message]);
        Assert.Equal(DnsQueryType.A, response.Answers[1].RecordType);
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.IsType<DnsARecord>(response.Answers[1]).Address);
    }

    [Fact]
    public void ReadDomainName_ForwardPointer_ThrowsDnsProtocolException()
    {
        // A pointer must refer to an earlier position; a forward pointer is how compression loops are built.
        byte[] data = [0xC0, 0x04, 0x00, 0x00, 1, (byte)'a', 0];
        Assert.Throws<DnsProtocolException>(() =>
        {
            var reader = new DnsWireReader(data);
            return reader.ReadDomainName();
        });
    }

    [Fact]
    public void ReadDomainName_SelfReferentialPointer_ThrowsDnsProtocolException()
    {
        byte[] data = [1, (byte)'a', 0xC0, 0x02];
        Assert.Throws<DnsProtocolException>(() =>
        {
            var reader = new DnsWireReader(data);
            return reader.ReadDomainName();
        });
    }

    [Fact]
    public void ReadDomainName_PointerBeyondMessage_ThrowsDnsProtocolException()
    {
        // Must not silently truncate the name to whatever was read before the bad pointer.
        byte[] data = [1, (byte)'a', 0xC0, 0xFF, 0, 0, 0, 0];
        Assert.Throws<DnsProtocolException>(() =>
        {
            var reader = new DnsWireReader(data);
            return reader.ReadDomainName();
        });
    }

    [Fact]
    public void ReadDomainName_ExceedingMaximumLength_ThrowsDnsProtocolException()
    {
        var data = new List<byte>();
        for (var i = 0; i < 10; i++)
        {
            data.Add(63);
            data.AddRange(Enumerable.Repeat((byte)'a', 63));
        }

        data.Add(0);

        // 10 x 64 bytes is well past the RFC 1035 255-byte limit.
        Assert.Throws<DnsProtocolException>(() =>
        {
            var reader = new DnsWireReader([.. data]);
            return reader.ReadDomainName();
        });
    }

    [Fact]
    public void ReadDomainName_EscapesDotsAndNonPrintableOctetsSoDistinctNamesStayDistinct()
    {
        // One label containing a dot must not be confused with two labels.
        byte[] oneLabel = [8, (byte)'e', (byte)'v', (byte)'i', (byte)'l', (byte)'.', (byte)'c', (byte)'o', (byte)'m', 0];
        var reader = new DnsWireReader(oneLabel);
        Assert.Equal(@"evil\.com", reader.ReadDomainName());

        byte[] twoLabels = [4, (byte)'e', (byte)'v', (byte)'i', (byte)'l', 3, (byte)'c', (byte)'o', (byte)'m', 0];
        var otherReader = new DnsWireReader(twoLabels);
        Assert.Equal("evil.com", otherReader.ReadDomainName());

        byte[] binary = [3, (byte)'a', 0xFF, (byte)'b', 0];
        var binaryReader = new DnsWireReader(binary);
        Assert.Equal(@"a\255b", binaryReader.ReadDomainName());
    }

    [Fact]
    public void WriteDomainName_RoundTripsEscapedLabels()
    {
        var writer = new DnsWireWriter();
        writer.WriteDomainName(@"evil\.com");
        var bytes = writer.ToArray();

        Assert.Equal(8, bytes[0]);
        var reader = new DnsWireReader(bytes);
        Assert.Equal(@"evil\.com", reader.ReadDomainName());
    }

    [Fact]
    public void WriteDomainName_NameLongerThanTheMaximum_ThrowsDnsProtocolException()
    {
        var name = string.Join('.', Enumerable.Repeat(new string('a', 63), 10));

        Assert.Throws<DnsProtocolException>(() =>
        {
            var writer = new DnsWireWriter();
            writer.WriteDomainName(name);
        });
    }

    [Fact]
    public void WriteDomainName_NonAsciiName_ThrowsDnsProtocolException()
    {
        // Silently substituting '?' would query a different name than the caller asked for.
        Assert.Throws<DnsProtocolException>(() =>
        {
            var writer = new DnsWireWriter();
            writer.WriteDomainName("münchen.de");
        });
    }

    [Fact]
    public void DecodeResponse_TruncatedAtEveryOffset_ThrowsDnsProtocolExceptionOrSucceeds()
    {
        var full = BuildSingleAnswerMessage(DnsQueryType.A, rdLength: 4, rdata: [192, 0, 2, 1], trailing: []);

        for (var length = 0; length < full.Length; length++)
        {
            var truncated = full.AsSpan(0, length).ToArray();
            var exception = Record.Exception(() => DnsMessageEncoder.DecodeResponse(truncated));

            // Whatever happens, it must be the documented exception type - never an ArgumentException or a hang.
            if (exception is not null)
            {
                Assert.IsType<DnsProtocolException>(exception);
            }
        }
    }

    [Fact]
    public void DecodeResponse_AnswerCountLargerThanTheMessage_ThrowsDnsProtocolException()
    {
        byte[] message = [0x12, 0x34, 0x81, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];

        Assert.Throws<DnsProtocolException>(() => DnsMessageEncoder.DecodeResponse(message));
    }

    [Fact]
    public void DecodeResponse_NsecBitmapWindowLongerThanAllowed_ThrowsDnsProtocolException()
    {
        // RFC 4034 4.1.2 caps a bitmap window at 32 bytes; a larger one produces type codes above 65535.
        var rdata = new List<byte> { 0x00 }; // NEXT = root
        rdata.AddRange([0x00, 0xFF]); // window 0, bitmap length 255
        rdata.AddRange(Enumerable.Repeat((byte)0xFF, 255));

        var message = BuildSingleAnswerMessage(DnsQueryType.NSEC, (ushort)rdata.Count, [.. rdata], trailing: []);

        Assert.Throws<DnsProtocolException>(() => DnsMessageEncoder.DecodeResponse(message));
    }

    private static byte[] BuildSingleAnswerMessage(DnsQueryType type, ushort rdLength, byte[] rdata, byte[] trailing)
    {
        var bytes = new List<byte>
        {
            0x12, 0x34,
            0x81, 0x80,
            0x00, 0x00, // QDCOUNT
            0x00, 0x01, // ANCOUNT
            0x00, 0x00,
            0x00, 0x00,
            0x00, // NAME = root
        };

        bytes.AddRange([(byte)((ushort)type >> 8), (byte)(ushort)type]);
        bytes.AddRange([0x00, 0x01]); // CLASS IN
        bytes.AddRange([0x00, 0x00, 0x00, 0x3C]); // TTL
        bytes.AddRange([(byte)(rdLength >> 8), (byte)rdLength]);
        bytes.AddRange(rdata);
        bytes.AddRange(trailing);
        return [.. bytes];
    }

    [Fact]
    public void ParseSrvRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsSrvRecord>(DnsQueryType.SRV, [
            0x00, 0x0A, // priority 10
            0x00, 0x14, // weight 20
            0x1F, 0x90, // port 8080
            4, (byte)'h', (byte)'o', (byte)'s', (byte)'t', 0,
        ]);

        Assert.Equal(10, record.Priority);
        Assert.Equal(20, record.Weight);
        Assert.Equal(8080, record.Port);
        Assert.Equal("host", record.Target);
    }

    [Fact]
    public void ParseNaptrRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsNaptrRecord>(DnsQueryType.NAPTR, [
            0x00, 0x64, // order 100
            0x00, 0x0A, // preference 10
            1, (byte)'u',
            7, (byte)'E', (byte)'2', (byte)'U', (byte)'+', (byte)'s', (byte)'i', (byte)'p',
            3, (byte)'!', (byte)'^', (byte)'!',
            4, (byte)'h', (byte)'o', (byte)'s', (byte)'t', 0,
        ]);

        Assert.Equal(100, record.Order);
        Assert.Equal(10, record.Preference);
        Assert.Equal("u", record.Flags);
        Assert.Equal("E2U+sip", record.Services);
        Assert.Equal("!^!", record.Regexp);
        Assert.Equal("host", record.Replacement);
    }

    [Fact]
    public void ParseTlsaRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsTlsaRecord>(DnsQueryType.TLSA, [3, 1, 1, 0xAA, 0xBB]);

        Assert.Equal(3, record.CertificateUsage);
        Assert.Equal(1, record.Selector);
        Assert.Equal(1, record.MatchingType);
        Assert.Equal<byte>([0xAA, 0xBB], record.CertificateAssociationData);
    }

    [Fact]
    public void ParseSshfpRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsSshfpRecord>(DnsQueryType.SSHFP, [4, 2, 0x01, 0x02, 0x03]);

        Assert.Equal(4, record.Algorithm);
        Assert.Equal(2, record.FingerprintType);
        Assert.Equal<byte>([0x01, 0x02, 0x03], record.Fingerprint);
    }

    [Fact]
    public void ParseUriRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsUriRecord>(DnsQueryType.URI, [
            0x00, 0x0A, 0x00, 0x01,
            (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)'s', (byte)':', (byte)'/', (byte)'/', (byte)'a',
        ]);

        Assert.Equal(10, record.Priority);
        Assert.Equal(1, record.Weight);
        Assert.Equal("https://a", record.Target);
    }

    [Fact]
    public void ParseHinfoRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsHinfoRecord>(DnsQueryType.HINFO, [
            3, (byte)'a', (byte)'r', (byte)'m',
            5, (byte)'l', (byte)'i', (byte)'n', (byte)'u', (byte)'x',
        ]);

        Assert.Equal("arm", record.Cpu);
        Assert.Equal("linux", record.Os);
    }

    [Fact]
    public void ParseRpRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsRpRecord>(DnsQueryType.RP, [
            2, (byte)'m', (byte)'e', 0,
            3, (byte)'t', (byte)'x', (byte)'t', 0,
        ]);

        Assert.Equal("me", record.Mailbox);
        Assert.Equal("txt", record.TxtDomainName);
    }

    [Fact]
    public void ParseDnameRecord_ReadsTarget()
    {
        var record = ParseSingleAnswer<DnsDnameRecord>(DnsQueryType.DNAME, [6, (byte)'t', (byte)'a', (byte)'r', (byte)'g', (byte)'e', (byte)'t', 0]);

        Assert.Equal("target", record.Target);
    }

    [Fact]
    public void ParseNsec3ParamRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsNsec3ParamRecord>(DnsQueryType.NSEC3PARAM, [1, 0, 0x00, 0x0A, 2, 0xAB, 0xCD]);

        Assert.Equal(1, record.HashAlgorithm);
        Assert.Equal(0, record.Flags);
        Assert.Equal(10, record.Iterations);
        Assert.Equal<byte>([0xAB, 0xCD], record.Salt);
    }

    [Fact]
    public void ParseNsecRecord_ReadsTypeBitmapWindows()
    {
        // Window 0, one bitmap byte with bits for A (1) and NS (2); then window 1 with bit for type 256+1.
        var record = ParseSingleAnswer<DnsNsecRecord>(DnsQueryType.NSEC, [
            4, (byte)'n', (byte)'e', (byte)'x', (byte)'t', 0,
            0x00, 0x01, 0b0110_0000,
            0x01, 0x01, 0b0100_0000,
        ]);

        Assert.Equal("next", record.NextDomainName);
        Assert.Contains(DnsQueryType.A, record.TypeBitMaps);
        Assert.Contains(DnsQueryType.NS, record.TypeBitMaps);
        Assert.Contains((DnsQueryType)257, record.TypeBitMaps);
    }

    [Fact]
    public void ParseLocRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsLocRecord>(DnsQueryType.LOC, [
            0, 0x12, 0x16, 0x13,
            0x80, 0x00, 0x00, 0x01,
            0x80, 0x00, 0x00, 0x02,
            0x00, 0x98, 0x96, 0x80,
        ]);

        Assert.Equal(0, record.Version);
        Assert.Equal(0x12, record.Size);
        Assert.Equal(0x16, record.HorizontalPrecision);
        Assert.Equal(0x13, record.VerticalPrecision);
        Assert.Equal(0x80000001u, record.Latitude);
        Assert.Equal(0x80000002u, record.Longitude);
        Assert.Equal(10000000u, record.Altitude);
    }

    [Fact]
    public void ParseSvcbRecord_ReadsPriorityTargetAndParameters()
    {
        var record = ParseSingleAnswer<DnsSvcbRecord>(DnsQueryType.HTTPS, [
            0x00, 0x01,
            0x00, // target = root
            0x00, 0x01, 0x00, 0x03, (byte)'h', (byte)'2', (byte)'!',
        ]);

        Assert.Equal(1, record.Priority);
        Assert.Empty(record.TargetName);
        var parameter = Assert.Single(record.Parameters);
        Assert.Equal(1, parameter.Key);
        Assert.Equal<byte>([(byte)'h', (byte)'2', (byte)'!'], parameter.Value);
    }

    [Fact]
    public void ParseUnknownRecordType_KeepsRawData()
    {
        var record = ParseSingleAnswer<DnsUnknownRecord>((DnsQueryType)64999, [0xDE, 0xAD, 0xBE, 0xEF]);

        Assert.Equal<byte>([0xDE, 0xAD, 0xBE, 0xEF], record.Data);
    }

    [Fact]
    public void ParseAaaaRecord_ReadsAddress()
    {
        var address = IPAddress.Parse("2001:db8::1");
        var record = ParseSingleAnswer<DnsAaaaRecord>(DnsQueryType.AAAA, address.GetAddressBytes());

        Assert.Equal(address, record.Address);
    }

    [Fact]
    public void ParseCaaRecord_ReadsFieldsInOrder()
    {
        var record = ParseSingleAnswer<DnsCaaRecord>(DnsQueryType.CAA, [
            0,
            5, (byte)'i', (byte)'s', (byte)'s', (byte)'u', (byte)'e',
            (byte)'c', (byte)'a', (byte)'.', (byte)'x',
        ]);

        Assert.Equal(0, record.Flags);
        Assert.Equal("issue", record.Tag);
        Assert.Equal("ca.x", record.Value);
    }

    private static T ParseSingleAnswer<T>(DnsQueryType type, byte[] rdata)
        where T : DnsRecord
    {
        var message = BuildSingleAnswerMessage(type, (ushort)rdata.Length, rdata, trailing: []);
        var response = DnsMessageEncoder.DecodeResponse(message);
        return Assert.IsType<T>(Assert.Single(response.Answers));
    }
}
