using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;

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
        Assert.Equal(13, bytes.Length);
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
        Assert.Equal(13, bytes.Length);
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

        var bytes = DnsMessageEncoder.EncodeQuery(query);

        Assert.True(bytes.Length >= 12); // At least header

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

        var bytes = DnsMessageEncoder.EncodeQuery(query);

        // ARCOUNT should be 1 (for OPT record)
        Assert.Equal(0x00, bytes[10]);
        Assert.Equal(0x01, bytes[11]);
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
}
