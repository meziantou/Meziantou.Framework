using System.Net;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Builds a DNS message that both libraries can encode from the bytes of an input: every name has valid ASCII labels
/// without dots (any other byte below 0x80), every character string fits in 255 bytes, and the record data matches its
/// type. Missing bytes read as 0, so every input gives a message.
/// </summary>
internal sealed class DnsMessageGenerator
{
    private const string LabelCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_*";

    private static readonly DnsQueryClass[] Classes = [DnsQueryClass.IN, DnsQueryClass.IN, DnsQueryClass.CH, DnsQueryClass.HS, DnsQueryClass.ANY];

    private static readonly DnsQueryType[] UnknownTypes = [DnsQueryType.NULL, DnsQueryType.SPF, DnsQueryType.CDS, DnsQueryType.CDNSKEY, (DnsQueryType)65280];

    private static readonly DnsQueryType[] QuestionTypes = [DnsQueryType.A, DnsQueryType.AAAA, DnsQueryType.MX, DnsQueryType.TXT, DnsQueryType.HTTPS, DnsQueryType.DNSKEY, DnsQueryType.ANY, DnsQueryType.PTR];

    private readonly byte[] _data;
    private int _position;

    private DnsMessageGenerator(byte[] data)
    {
        _data = data;
    }

    /// <summary>Gets the wire labels of each name the generator produced, by the text the server uses for it.</summary>
    public Dictionary<string, byte[][]> Labels { get; } = new(StringComparer.Ordinal);

    public static (DnsMessage Message, DnsMessageGenerator Generator) Generate(ReadOnlySpan<byte> data)
    {
        var generator = new DnsMessageGenerator(data.ToArray());
        return (generator.CreateMessage(), generator);
    }

    private DnsMessage CreateMessage()
    {
        var message = new DnsMessage { Id = ReadUInt16() };
        var flags = ReadUInt16();
        message.IsResponse = (flags & 0x8000) is not 0;
        message.OpCode = (DnsOpCode)((flags >> 11) & 0x0F);
        message.IsAuthoritative = (flags & 0x0400) is not 0;
        message.IsTruncated = (flags & 0x0200) is not 0;
        message.RecursionDesired = (flags & 0x0100) is not 0;
        message.RecursionAvailable = (flags & 0x0080) is not 0;
        message.AuthenticatedData = (flags & 0x0020) is not 0;
        message.CheckingDisabled = (flags & 0x0010) is not 0;

        var hasEdns = (flags & 0x0040) is not 0;
        var responseCode = ReadUInt16() & (hasEdns ? 0xFFF : 0x0F);
        message.ResponseCode = (DnsResponseCode)responseCode;
        if (hasEdns)
        {
            var ednsFlags = ReadByte();
            message.EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = ReadUInt16(),
                Version = (byte)(ednsFlags & 0x03),
                DnssecOk = (ednsFlags & 0x80) is not 0,

                // The decoder reads it from the OPT record, where the encoder writes the upper bits of the response code
                ExtendedRCode = (byte)(responseCode >> 4),
            };
        }

        var questionCount = ReadByte() % 4;
        for (var i = 0; i < questionCount; i++)
        {
            var type = (ReadByte() & 0x80) is 0 ? QuestionTypes[ReadByte() % QuestionTypes.Length] : (DnsQueryType)ReadUInt16();
            message.Questions.Add(new DnsQuestion(ReadName(), type, ReadClass()));
        }

        AddRecords(message.Answers, ReadByte() % 8);
        AddRecords(message.Authorities, ReadByte() % 4);
        AddRecords(message.AdditionalRecords, ReadByte() % 4);
        return message;
    }

    private void AddRecords(IList<DnsResourceRecord> records, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var name = ReadName();
            var (type, data) = ReadRecordData();
            records.Add(new DnsResourceRecord { Name = name, Type = type, Class = ReadClass(), TimeToLive = ReadUInt32(), Data = data });
        }
    }

    private (DnsQueryType Type, DnsResourceRecordData Data) ReadRecordData()
    {
        return (ReadByte() % 27) switch
        {
            0 => (DnsQueryType.A, new DnsARecordData { Address = new IPAddress(ReadBytes(4, 4)) }),
            1 => (DnsQueryType.AAAA, new DnsAaaaRecordData { Address = new IPAddress(ReadBytes(16, 16)) }),
            2 => (DnsQueryType.CNAME, new DnsCnameRecordData { CanonicalName = ReadName() }),
            3 => (DnsQueryType.MX, new DnsMxRecordData { Preference = ReadUInt16(), Exchange = ReadName() }),
            4 => (DnsQueryType.NS, new DnsNsRecordData { NameServer = ReadName() }),
            5 => (DnsQueryType.PTR, new DnsPtrRecordData { DomainName = ReadName() }),
            6 => (DnsQueryType.SOA, new DnsSoaRecordData { PrimaryNameServer = ReadName(), ResponsibleMailbox = ReadName(), Serial = ReadUInt32(), Refresh = (int)ReadUInt32(), Retry = (int)ReadUInt32(), Expire = (int)ReadUInt32(), Minimum = ReadUInt32() }),
            7 => (DnsQueryType.TXT, new DnsTxtRecordData { Text = [.. Enumerable.Range(0, ReadByte() % 4).Select(_ => ReadUtf8String())] }),
            8 => (DnsQueryType.SRV, new DnsSrvRecordData { Priority = ReadUInt16(), Weight = ReadUInt16(), Port = ReadUInt16(), Target = ReadName() }),
            9 => (DnsQueryType.NAPTR, new DnsNaptrRecordData { Order = ReadUInt16(), Preference = ReadUInt16(), Flags = ReadAsciiString(), Services = ReadAsciiString(), Regexp = ReadUtf8String(), Replacement = ReadName() }),
            10 => (DnsQueryType.CAA, new DnsCaaRecordData { Flags = ReadByte(), Tag = ReadAsciiString(), Value = ReadAsciiString() }),
            11 => (DnsQueryType.DNSKEY, new DnsDnskeyRecordData { Flags = ReadUInt16(), Protocol = ReadByte(), Algorithm = ReadByte(), PublicKey = ReadBytes(0, 64) }),
            12 => (DnsQueryType.DS, new DnsDsRecordData { KeyTag = ReadUInt16(), Algorithm = ReadByte(), DigestType = ReadByte(), Digest = ReadBytes(0, 48) }),
            13 => (DnsQueryType.RRSIG, new DnsRrsigRecordData { TypeCovered = (DnsQueryType)ReadUInt16(), Algorithm = ReadByte(), Labels = ReadByte(), OriginalTtl = ReadUInt32(), SignatureExpiration = ReadUInt32(), SignatureInception = ReadUInt32(), KeyTag = ReadUInt16(), SignerName = ReadName(), Signature = ReadBytes(0, 64) }),
            14 => (DnsQueryType.NSEC, new DnsNsecRecordData { NextDomainName = ReadName(), TypeBitMaps = ReadTypes() }),
            15 => (DnsQueryType.NSEC3, new DnsNsec3RecordData { HashAlgorithm = ReadByte(), Flags = ReadByte(), Iterations = ReadUInt16(), Salt = ReadBytes(0, 16), NextHashedOwnerName = ReadBytes(0, 32), TypeBitMaps = ReadTypes() }),
            16 => (DnsQueryType.NSEC3PARAM, new DnsNsec3ParamRecordData { HashAlgorithm = ReadByte(), Flags = ReadByte(), Iterations = ReadUInt16(), Salt = ReadBytes(0, 16) }),
            17 => (DnsQueryType.TLSA, new DnsTlsaRecordData { CertificateUsage = ReadByte(), Selector = ReadByte(), MatchingType = ReadByte(), CertificateAssociationData = ReadBytes(0, 64) }),
            18 => (DnsQueryType.SSHFP, new DnsSshfpRecordData { Algorithm = ReadByte(), FingerprintType = ReadByte(), Fingerprint = ReadBytes(0, 32) }),
            19 => ((ReadByte() & 1) is 0 ? DnsQueryType.SVCB : DnsQueryType.HTTPS, new DnsSvcbRecordData { Priority = ReadUInt16(), TargetName = ReadName(), Parameters = [.. Enumerable.Range(0, ReadByte() % 4).Select(_ => new DnsSvcParam { Key = ReadUInt16(), Value = ReadBytes(0, 16) })] }),
            20 => (DnsQueryType.LOC, new DnsLocRecordData { Version = ReadByte(), Size = ReadByte(), HorizontalPrecision = ReadByte(), VerticalPrecision = ReadByte(), Latitude = ReadUInt32(), Longitude = ReadUInt32(), Altitude = ReadUInt32() }),
            21 => (DnsQueryType.HINFO, new DnsHinfoRecordData { Cpu = ReadAsciiString(), Os = ReadAsciiString() }),
            22 => (DnsQueryType.RP, new DnsRpRecordData { Mailbox = ReadName(), TxtDomainName = ReadName() }),
            23 => (DnsQueryType.DNAME, new DnsDnameRecordData { Target = ReadName() }),
            24 => (DnsQueryType.URI, new DnsUriRecordData { Priority = ReadUInt16(), Weight = ReadUInt16(), Target = ReadUtf8String() }),

            // CDS and CDNSKEY have the format of DS and DNSKEY (at least 4 bytes), which the client parses
            _ => (UnknownTypes[ReadByte() % UnknownTypes.Length], new DnsUnknownRecordData { Data = ReadBytes(4, 32) }),
        };
    }

    private string ReadName()
    {
        var labelCount = ReadByte() % 5;
        var labels = new List<byte[]>(labelCount);
        var encodedLength = 1;
        for (var i = 0; i < labelCount; i++)
        {
            var lengthByte = ReadByte();
            var length = (lengthByte & 0x80) is 0 ? 1 + (lengthByte % 12) : 1 + (lengthByte % 63);
            if (encodedLength + length + 1 > 255)
                break;

            var label = new byte[length];
            for (var j = 0; j < length; j++)
            {
                var value = ReadByte();

                // Mostly the characters of host names, sometimes any ASCII byte but the dot, which the server cannot represent
                label[j] = (value & 0x80) is 0 ? (byte)LabelCharacters[value % LabelCharacters.Length] : (byte)(value & 0x7F);
                if (label[j] is (byte)'.')
                {
                    label[j] = (byte)'-';
                }
            }

            encodedLength += length + 1;
            labels.Add(label);
        }

        var name = string.Join('.', labels.Select(label => Encoding.ASCII.GetString(label)));
        Labels[name] = [.. labels];
        return name;
    }

    private DnsQueryClass ReadClass()
    {
        var value = ReadByte();
        return (value & 0x80) is 0 ? Classes[value % Classes.Length] : (DnsQueryClass)ReadUInt16();
    }

    private List<DnsQueryType> ReadTypes()
    {
        // The encoder writes a set: sorted, without duplicates
        var count = ReadByte() % 6;
        var types = new SortedSet<ushort>();
        for (var i = 0; i < count; i++)
        {
            var value = ReadByte();
            types.Add((value & 0x80) is 0 ? (ushort)(value % 64) : ReadUInt16());
        }

        return [.. types.Select(type => (DnsQueryType)type)];
    }

    private string ReadAsciiString()
    {
        var bytes = ReadBytes(0, 32);
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] &= 0x7F;
        }

        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>Reads a text from at most 85 bytes, which is at most 255 bytes once encoded (an invalid byte becomes U+FFFD).</summary>
    private string ReadUtf8String() => Encoding.UTF8.GetString(ReadBytes(0, 85));

    private byte ReadByte() => _position < _data.Length ? _data[_position++] : (byte)0;

    private ushort ReadUInt16() => (ushort)((ReadByte() << 8) | ReadByte());

    private uint ReadUInt32() => ((uint)ReadUInt16() << 16) | ReadUInt16();

    private byte[] ReadBytes(int minLength, int maxLength)
    {
        var length = minLength == maxLength ? minLength : minLength + (ReadByte() % (maxLength - minLength + 1));
        var result = new byte[length];
        var available = Math.Min(length, _data.Length - _position);
        _data.AsSpan(_position, available).CopyTo(result);
        _position += available;
        return result;
    }
}
