using System.Net;
using System.Text;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Protocol;

internal static class DnsMessageEncoder
{
    [SuppressMessage("Security", "CA5394:Random is an insecure random number generator")]
    public static byte[] EncodeQuery(DnsQueryMessage query)
    {
        var writer = new DnsWireWriter(512);

        var id = query.Id ?? (ushort)Random.Shared.Next(0, ushort.MaxValue + 1);

        // Header
        writer.WriteUInt16(id);

        ushort flags = 0;
        // QR = 0 (query)
        flags |= (ushort)(((byte)query.OpCode & 0x0F) << 11);
        if (query.RecursionDesired)
            flags |= 1 << 8;
        if (query.CheckingDisabled)
            flags |= 1 << 4;

        writer.WriteUInt16(flags);
        writer.WriteUInt16((ushort)query.Questions.Count); // QDCOUNT
        writer.WriteUInt16(0); // ANCOUNT
        writer.WriteUInt16(0); // NSCOUNT

        var hasEdns = query.EdnsOptions is not null;
        writer.WriteUInt16(hasEdns ? (ushort)1 : (ushort)0); // ARCOUNT

        // Questions
        foreach (var question in query.Questions)
        {
            writer.WriteDomainName(question.Name);
            writer.WriteUInt16((ushort)question.Type);
            writer.WriteUInt16((ushort)question.QueryClass);
        }

        // EDNS(0) OPT record in Additional section
        if (query.EdnsOptions is { } edns)
        {
            writer.WriteByte(0); // NAME: root domain
            writer.WriteUInt16((ushort)DnsQueryType.OPT); // TYPE: OPT (41)
            writer.WriteUInt16(edns.UdpPayloadSize); // CLASS: UDP payload size
            writer.WriteByte(edns.ExtendedRCode); // Extended RCODE
            writer.WriteByte(edns.Version); // EDNS version
            ushort ednsFlags = 0;
            if (edns.DnssecOk)
                ednsFlags |= 0x8000; // DO flag
            writer.WriteUInt16(ednsFlags); // EDNS flags
            writer.WriteUInt16(0); // RDLENGTH: no options data
        }

        return writer.ToArray();
    }

    public static DnsResponseMessage DecodeResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            throw new DnsProtocolException("DNS message is too short (minimum 12 bytes for header).");

        var fullMessage = data;
        var reader = new DnsWireReader(data);

        // Header
        var header = new DnsResponseHeader
        {
            Id = reader.ReadUInt16(),
        };

        var flags = reader.ReadUInt16();
        header.IsResponse = (flags & 0x8000) != 0;
        header.OpCode = (DnsOpCode)((flags >> 11) & 0x0F);
        header.IsAuthoritative = (flags & 0x0400) != 0;
        header.IsTruncated = (flags & 0x0200) != 0;
        header.RecursionDesired = (flags & 0x0100) != 0;
        header.RecursionAvailable = (flags & 0x0080) != 0;
        header.AuthenticatedData = (flags & 0x0020) != 0;
        header.CheckingDisabled = (flags & 0x0010) != 0;
        header.ResponseCode = (DnsResponseCode)(flags & 0x000F);

        header.QuestionCount = reader.ReadUInt16();
        header.AnswerCount = reader.ReadUInt16();
        header.AuthorityCount = reader.ReadUInt16();
        header.AdditionalCount = reader.ReadUInt16();

        var response = new DnsResponseMessage(header);

        // Questions
        var questions = new List<DnsQuestion>(header.QuestionCount);
        for (var i = 0; i < header.QuestionCount; i++)
        {
            var name = reader.ReadDomainName();
            var type = (DnsQueryType)reader.ReadUInt16();
            var queryClass = (DnsQueryClass)reader.ReadUInt16();
            questions.Add(new DnsQuestion(name, type, queryClass));
        }

        response.Questions = questions;

        // Answer, Authority, Additional sections
        response.Answers = ReadRecords(ref reader, fullMessage, header.AnswerCount);
        response.Authorities = ReadRecords(ref reader, fullMessage, header.AuthorityCount);
        response.AdditionalRecords = ReadRecords(ref reader, fullMessage, header.AdditionalCount);

        return response;
    }

    private static List<DnsRecord> ReadRecords(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage, ushort count)
    {
        var records = new List<DnsRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadDomainName();
            var type = (DnsQueryType)reader.ReadUInt16();
            var recordClass = (DnsQueryClass)reader.ReadUInt16();
            var ttl = reader.ReadUInt32();
            var rdLength = reader.ReadUInt16();

            var rdataStart = reader.Position;
            var record = ParseRecord(ref reader, fullMessage, type, rdLength);

            record.Name = name;
            record.RecordType = type;
            record.RecordClass = recordClass;
            record.TimeToLive = ttl;
            record.DataLength = rdLength;

            // Ensure we consumed exactly rdLength bytes
            var consumed = reader.Position - rdataStart;
            if (consumed < rdLength)
            {
                reader.Skip(rdLength - consumed);
            }

            records.Add(record);
        }

        return records;
    }

    private static DnsRecord ParseRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage, DnsQueryType type, ushort rdLength)
    {
        return type switch
        {
            DnsQueryType.A => ParseARecord(ref reader),
            DnsQueryType.AAAA => ParseAaaaRecord(ref reader),
            DnsQueryType.CNAME => ParseCnameRecord(ref reader),
            DnsQueryType.MX => ParseMxRecord(ref reader, fullMessage),
            DnsQueryType.NS => ParseNsRecord(ref reader),
            DnsQueryType.PTR => ParsePtrRecord(ref reader),
            DnsQueryType.SOA => ParseSoaRecord(ref reader),
            DnsQueryType.SRV => ParseSrvRecord(ref reader, fullMessage),
            DnsQueryType.TXT => ParseTxtRecord(ref reader, rdLength),
            DnsQueryType.CAA => ParseCaaRecord(ref reader, rdLength),
            DnsQueryType.NAPTR => ParseNaptrRecord(ref reader, fullMessage),
            DnsQueryType.DNSKEY => ParseDnskeyRecord(ref reader, rdLength),
            DnsQueryType.DS => ParseDsRecord(ref reader, rdLength),
            DnsQueryType.RRSIG => ParseRrsigRecord(ref reader, fullMessage, rdLength),
            DnsQueryType.NSEC => ParseNsecRecord(ref reader, fullMessage, rdLength),
            DnsQueryType.NSEC3 => ParseNsec3Record(ref reader, rdLength),
            DnsQueryType.NSEC3PARAM => ParseNsec3ParamRecord(ref reader),
            DnsQueryType.TLSA => ParseTlsaRecord(ref reader, rdLength),
            DnsQueryType.SSHFP => ParseSshfpRecord(ref reader, rdLength),
            DnsQueryType.SVCB or DnsQueryType.HTTPS => ParseSvcbRecord(ref reader, fullMessage, rdLength),
            DnsQueryType.LOC => ParseLocRecord(ref reader),
            DnsQueryType.HINFO => ParseHinfoRecord(ref reader),
            DnsQueryType.RP => ParseRpRecord(ref reader),
            DnsQueryType.DNAME => ParseDnameRecord(ref reader),
            DnsQueryType.OPT => ParseOptRecord(ref reader, rdLength),
            DnsQueryType.URI => ParseUriRecord(ref reader, rdLength),
            _ => ParseUnknownRecord(ref reader, rdLength),
        };
    }

    private static DnsARecord ParseARecord(ref DnsWireReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return new DnsARecord { Address = new IPAddress(bytes) };
    }

    private static DnsAaaaRecord ParseAaaaRecord(ref DnsWireReader reader)
    {
        var bytes = reader.ReadBytes(16);
        return new DnsAaaaRecord { Address = new IPAddress(bytes) };
    }

    private static DnsCnameRecord ParseCnameRecord(ref DnsWireReader reader)
    {
        return new DnsCnameRecord { CanonicalName = reader.ReadDomainName() };
    }

    private static DnsMxRecord ParseMxRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage)
    {
        return new DnsMxRecord
        {
            Preference = reader.ReadUInt16(),
            Exchange = reader.ReadDomainName(),
        };
    }

    private static DnsNsRecord ParseNsRecord(ref DnsWireReader reader)
    {
        return new DnsNsRecord { NameServer = reader.ReadDomainName() };
    }

    private static DnsPtrRecord ParsePtrRecord(ref DnsWireReader reader)
    {
        return new DnsPtrRecord { DomainName = reader.ReadDomainName() };
    }

    private static DnsSoaRecord ParseSoaRecord(ref DnsWireReader reader)
    {
        return new DnsSoaRecord
        {
            PrimaryNameServer = reader.ReadDomainName(),
            ResponsibleMailbox = reader.ReadDomainName(),
            Serial = reader.ReadUInt32(),
            Refresh = reader.ReadInt32(),
            Retry = reader.ReadInt32(),
            Expire = reader.ReadInt32(),
            Minimum = reader.ReadUInt32(),
        };
    }

    private static DnsSrvRecord ParseSrvRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage)
    {
        return new DnsSrvRecord
        {
            Priority = reader.ReadUInt16(),
            Weight = reader.ReadUInt16(),
            Port = reader.ReadUInt16(),
            Target = reader.ReadDomainName(),
        };
    }

    private static DnsTxtRecord ParseTxtRecord(ref DnsWireReader reader, ushort rdLength)
    {
        var texts = new List<string>();
        var endPosition = reader.Position + rdLength;
        while (reader.Position < endPosition)
        {
            var length = reader.ReadByte();
            var text = Encoding.UTF8.GetString(reader.ReadBytes(length));
            texts.Add(text);
        }

        return new DnsTxtRecord { Text = texts };
    }

    private static DnsCaaRecord ParseCaaRecord(ref DnsWireReader reader, ushort rdLength)
    {
        var startPosition = reader.Position;
        var flags = reader.ReadByte();
        var tagLength = reader.ReadByte();
        var tag = Encoding.ASCII.GetString(reader.ReadBytes(tagLength));
        var valueLength = rdLength - 2 - tagLength;
        var value = Encoding.ASCII.GetString(reader.ReadBytes(valueLength));

        return new DnsCaaRecord { Flags = flags, Tag = tag, Value = value };
    }

    private static DnsNaptrRecord ParseNaptrRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage)
    {
        var order = reader.ReadUInt16();
        var preference = reader.ReadUInt16();
        var flagsLength = reader.ReadByte();
        var flags = Encoding.ASCII.GetString(reader.ReadBytes(flagsLength));
        var servicesLength = reader.ReadByte();
        var services = Encoding.ASCII.GetString(reader.ReadBytes(servicesLength));
        var regexpLength = reader.ReadByte();
        var regexp = Encoding.UTF8.GetString(reader.ReadBytes(regexpLength));
        var replacement = reader.ReadDomainName();

        return new DnsNaptrRecord
        {
            Order = order,
            Preference = preference,
            Flags = flags,
            Services = services,
            Regexp = regexp,
            Replacement = replacement,
        };
    }

    private static DnsDnskeyRecord ParseDnskeyRecord(ref DnsWireReader reader, ushort rdLength)
    {
        var flags = reader.ReadUInt16();
        var protocol = reader.ReadByte();
        var algorithm = reader.ReadByte();
        var publicKey = reader.ReadBytes(rdLength - 4).ToArray();

        return new DnsDnskeyRecord
        {
            Flags = flags,
            Protocol = protocol,
            Algorithm = algorithm,
            PublicKey = publicKey,
        };
    }

    private static DnsDsRecord ParseDsRecord(ref DnsWireReader reader, ushort rdLength)
    {
        var keyTag = reader.ReadUInt16();
        var algorithm = reader.ReadByte();
        var digestType = reader.ReadByte();
        var digest = reader.ReadBytes(rdLength - 4).ToArray();

        return new DnsDsRecord
        {
            KeyTag = keyTag,
            Algorithm = algorithm,
            DigestType = digestType,
            Digest = digest,
        };
    }

    private static DnsRrsigRecord ParseRrsigRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage, ushort rdLength)
    {
        var startPosition = reader.Position;
        var typeCovered = (DnsQueryType)reader.ReadUInt16();
        var algorithm = reader.ReadByte();
        var labels = reader.ReadByte();
        var originalTtl = reader.ReadUInt32();
        var expiration = reader.ReadUInt32();
        var inception = reader.ReadUInt32();
        var keyTag = reader.ReadUInt16();
        var signerName = reader.ReadDomainName();
        var signatureLength = rdLength - (reader.Position - startPosition);
        var signature = reader.ReadBytes(signatureLength).ToArray();

        return new DnsRrsigRecord
        {
            TypeCovered = typeCovered,
            Algorithm = algorithm,
            Labels = labels,
            OriginalTtl = originalTtl,
            SignatureExpiration = expiration,
            SignatureInception = inception,
            KeyTag = keyTag,
            SignerName = signerName,
            Signature = signature,
        };
    }

    private static DnsNsecRecord ParseNsecRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage, ushort rdLength)
    {
        var startPosition = reader.Position;
        var nextDomainName = reader.ReadDomainName();
        var remaining = rdLength - (reader.Position - startPosition);
        var typeBitMaps = ParseTypeBitMaps(ref reader, remaining);

        return new DnsNsecRecord
        {
            NextDomainName = nextDomainName,
            TypeBitMaps = typeBitMaps,
        };
    }

    private static DnsNsec3Record ParseNsec3Record(ref DnsWireReader reader, ushort rdLength)
    {
        var startPosition = reader.Position;
        var hashAlgorithm = reader.ReadByte();
        var flags = reader.ReadByte();
        var iterations = reader.ReadUInt16();
        var saltLength = reader.ReadByte();
        var salt = reader.ReadBytes(saltLength).ToArray();
        var hashLength = reader.ReadByte();
        var nextHashedOwnerName = reader.ReadBytes(hashLength).ToArray();
        var remaining = rdLength - (reader.Position - startPosition);
        var typeBitMaps = ParseTypeBitMaps(ref reader, remaining);

        return new DnsNsec3Record
        {
            HashAlgorithm = hashAlgorithm,
            Flags = flags,
            Iterations = iterations,
            Salt = salt,
            NextHashedOwnerName = nextHashedOwnerName,
            TypeBitMaps = typeBitMaps,
        };
    }

    private static DnsNsec3ParamRecord ParseNsec3ParamRecord(ref DnsWireReader reader)
    {
        var hashAlgorithm = reader.ReadByte();
        var flags = reader.ReadByte();
        var iterations = reader.ReadUInt16();
        var saltLength = reader.ReadByte();
        var salt = reader.ReadBytes(saltLength).ToArray();

        return new DnsNsec3ParamRecord
        {
            HashAlgorithm = hashAlgorithm,
            Flags = flags,
            Iterations = iterations,
            Salt = salt,
        };
    }

    private static DnsTlsaRecord ParseTlsaRecord(ref DnsWireReader reader, ushort rdLength)
    {
        return new DnsTlsaRecord
        {
            CertificateUsage = reader.ReadByte(),
            Selector = reader.ReadByte(),
            MatchingType = reader.ReadByte(),
            CertificateAssociationData = reader.ReadBytes(rdLength - 3).ToArray(),
        };
    }

    private static DnsSshfpRecord ParseSshfpRecord(ref DnsWireReader reader, ushort rdLength)
    {
        return new DnsSshfpRecord
        {
            Algorithm = reader.ReadByte(),
            FingerprintType = reader.ReadByte(),
            Fingerprint = reader.ReadBytes(rdLength - 2).ToArray(),
        };
    }

    private static DnsSvcbRecord ParseSvcbRecord(ref DnsWireReader reader, ReadOnlySpan<byte> fullMessage, ushort rdLength)
    {
        var startPosition = reader.Position;
        var priority = reader.ReadUInt16();
        var targetName = reader.ReadDomainName();

        var parameters = new List<DnsSvcParam>();
        var remaining = rdLength - (reader.Position - startPosition);
        while (remaining > 0)
        {
            var key = reader.ReadUInt16();
            var valueLength = reader.ReadUInt16();
            var value = reader.ReadBytes(valueLength).ToArray();
            parameters.Add(new DnsSvcParam { Key = key, Value = value });
            remaining -= 4 + valueLength;
        }

        return new DnsSvcbRecord
        {
            Priority = priority,
            TargetName = targetName,
            Parameters = parameters,
        };
    }

    private static DnsLocRecord ParseLocRecord(ref DnsWireReader reader)
    {
        return new DnsLocRecord
        {
            Version = reader.ReadByte(),
            Size = reader.ReadByte(),
            HorizontalPrecision = reader.ReadByte(),
            VerticalPrecision = reader.ReadByte(),
            Latitude = reader.ReadUInt32(),
            Longitude = reader.ReadUInt32(),
            Altitude = reader.ReadUInt32(),
        };
    }

    private static DnsHinfoRecord ParseHinfoRecord(ref DnsWireReader reader)
    {
        var cpuLength = reader.ReadByte();
        var cpu = Encoding.ASCII.GetString(reader.ReadBytes(cpuLength));
        var osLength = reader.ReadByte();
        var os = Encoding.ASCII.GetString(reader.ReadBytes(osLength));

        return new DnsHinfoRecord { Cpu = cpu, Os = os };
    }

    private static DnsRpRecord ParseRpRecord(ref DnsWireReader reader)
    {
        return new DnsRpRecord
        {
            Mailbox = reader.ReadDomainName(),
            TxtDomainName = reader.ReadDomainName(),
        };
    }

    private static DnsDnameRecord ParseDnameRecord(ref DnsWireReader reader)
    {
        return new DnsDnameRecord { Target = reader.ReadDomainName() };
    }

    private static DnsOptRecord ParseOptRecord(ref DnsWireReader reader, ushort rdLength)
    {
        var options = new List<DnsEdnsOption>();
        var endPosition = reader.Position + rdLength;
        while (reader.Position < endPosition)
        {
            var code = reader.ReadUInt16();
            var length = reader.ReadUInt16();
            var data = reader.ReadBytes(length).ToArray();
            options.Add(new DnsEdnsOption { Code = code, Data = data });
        }

        return new DnsOptRecord { Options = options };
    }

    private static DnsUriRecord ParseUriRecord(ref DnsWireReader reader, ushort rdLength)
    {
        var priority = reader.ReadUInt16();
        var weight = reader.ReadUInt16();
        var target = Encoding.UTF8.GetString(reader.ReadBytes(rdLength - 4));

        return new DnsUriRecord { Priority = priority, Weight = weight, Target = target };
    }

    private static DnsUnknownRecord ParseUnknownRecord(ref DnsWireReader reader, ushort rdLength)
    {
        return new DnsUnknownRecord { Data = reader.ReadBytes(rdLength).ToArray() };
    }

    private static List<DnsQueryType> ParseTypeBitMaps(ref DnsWireReader reader, int length)
    {
        var types = new List<DnsQueryType>();
        var endPosition = reader.Position + length;

        while (reader.Position < endPosition)
        {
            var windowBlock = reader.ReadByte();
            var bitmapLength = reader.ReadByte();
            var bitmap = reader.ReadBytes(bitmapLength);

            for (var i = 0; i < bitmapLength; i++)
            {
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((bitmap[i] & (1 << (7 - bit))) != 0)
                    {
                        var typeValue = (windowBlock * 256) + (i * 8) + bit;
                        types.Add((DnsQueryType)typeValue);
                    }
                }
            }
        }

        return types;
    }
}
