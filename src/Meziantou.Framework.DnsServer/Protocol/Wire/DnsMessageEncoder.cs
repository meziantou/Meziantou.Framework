using System.Net;
using Meziantou.Framework.DnsServer.Protocol.Records;

namespace Meziantou.Framework.DnsServer.Protocol.Wire;

internal static class DnsMessageEncoder
{
    /// <summary>The largest response the length-prefixed transports (RFC 7766, RFC 9250) can frame.</summary>
    public const int MaxMessageSize = ushort.MaxValue;

    /// <summary>The highest value a 12-bit RCODE can hold once the EDNS extension bits are included.</summary>
    private const int MaxResponseCode = 0xFFF;

    public static DnsMessage DecodeQuery(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            throw new DnsProtocolException("DNS message is too short (minimum 12 bytes for header).");

        var reader = new DnsWireReader(data);

        var message = new DnsMessage
        {
            Id = reader.ReadUInt16(),
        };

        var flags = reader.ReadUInt16();
        message.IsResponse = (flags & 0x8000) != 0;
        message.OpCode = (DnsOpCode)((flags >> 11) & 0x0F);
        message.IsAuthoritative = (flags & 0x0400) != 0;
        message.IsTruncated = (flags & 0x0200) != 0;
        message.RecursionDesired = (flags & 0x0100) != 0;
        message.RecursionAvailable = (flags & 0x0080) != 0;
        message.AuthenticatedData = (flags & 0x0020) != 0;
        message.CheckingDisabled = (flags & 0x0010) != 0;
        message.ResponseCode = (DnsResponseCode)(flags & 0x000F);

        var questionCount = reader.ReadUInt16();
        var answerCount = reader.ReadUInt16();
        var authorityCount = reader.ReadUInt16();
        var additionalCount = reader.ReadUInt16();

        for (var i = 0; i < questionCount; i++)
        {
            var name = reader.ReadDomainName();
            var type = (DnsQueryType)reader.ReadUInt16();
            var queryClass = (DnsQueryClass)reader.ReadUInt16();
            message.Questions.Add(new DnsQuestion(name, type, queryClass));
        }

        ReadRecordsInto(message.Answers, ref reader, answerCount);
        ReadRecordsInto(message.Authorities, ref reader, authorityCount);
        ReadRecordsInto(message.AdditionalRecords, ref reader, additionalCount);

        // Extract EDNS from additional records
        for (var i = message.AdditionalRecords.Count - 1; i >= 0; i--)
        {
            if (message.AdditionalRecords[i].Type is DnsQueryType.OPT)
            {
                var optRecord = message.AdditionalRecords[i];
                var extendedRCode = (byte)(optRecord.TimeToLive >> 24);
                message.EdnsOptions = new DnsEdnsOptions
                {
                    UdpPayloadSize = (ushort)optRecord.Class,
                    ExtendedRCode = extendedRCode,
                    Version = (byte)((optRecord.TimeToLive >> 16) & 0xFF),
                    DnssecOk = (optRecord.TimeToLive & 0x8000) != 0,
                };

                // RFC 6891 6.1.3: the OPT record carries the upper 8 bits of a 12-bit RCODE.
                message.ResponseCode = (DnsResponseCode)((extendedRCode << 4) | ((ushort)message.ResponseCode & 0x0F));
                message.AdditionalRecords.RemoveAt(i);
                break;
            }
        }

        return message;
    }

    public static byte[] EncodeResponse(DnsMessage message)
    {
        var responseCode = (ushort)message.ResponseCode;
        if (responseCode > MaxResponseCode)
            throw new DnsProtocolException($"Response code {responseCode} is out of range. DNS response codes must be between 0 and {MaxResponseCode}.");

        // RFC 6891 6.1.3: anything above 15 needs an OPT record to carry its upper bits.
        var edns = message.EdnsOptions;
        if (responseCode > 0x0F && edns is null)
        {
            edns = new DnsEdnsOptions();
        }

        var writer = new DnsWireWriter(512);

        writer.WriteUInt16(message.Id);

        ushort flags = 0;
        if (message.IsResponse)
            flags |= 0x8000;
        flags |= (ushort)(((byte)message.OpCode & 0x0F) << 11);
        if (message.IsAuthoritative)
            flags |= 0x0400;
        if (message.IsTruncated)
            flags |= 0x0200;
        if (message.RecursionDesired)
            flags |= 0x0100;
        if (message.RecursionAvailable)
            flags |= 0x0080;
        if (message.AuthenticatedData)
            flags |= 0x0020;
        if (message.CheckingDisabled)
            flags |= 0x0010;
        flags |= (ushort)(responseCode & 0x000F);

        writer.WriteUInt16(flags);
        writer.WriteUInt16(ToCount(message.Questions.Count, "question"));
        writer.WriteUInt16(ToCount(message.Answers.Count, "answer"));
        writer.WriteUInt16(ToCount(message.Authorities.Count, "authority"));
        writer.WriteUInt16(ToCount(message.AdditionalRecords.Count + (edns is not null ? 1 : 0), "additional"));

        foreach (var question in message.Questions)
        {
            writer.WriteCompressibleDomainName(question.Name);
            writer.WriteUInt16((ushort)question.Type);
            writer.WriteUInt16((ushort)question.QueryClass);
        }

        WriteRecords(ref writer, message.Answers);
        WriteRecords(ref writer, message.Authorities);
        WriteRecords(ref writer, message.AdditionalRecords);

        if (edns is not null)
        {
            writer.WriteByte(0); // NAME: root domain
            writer.WriteUInt16((ushort)DnsQueryType.OPT);
            writer.WriteUInt16(edns.UdpPayloadSize);

            // The upper 8 bits of the RCODE always come from ResponseCode, so that a handler only
            // has to set one property to produce a correct extended response code.
            uint ttl = (uint)((responseCode >> 4) << 24) | (uint)(edns.Version << 16);
            if (edns.DnssecOk)
                ttl |= 0x8000;
            writer.WriteUInt32(ttl);
            writer.WriteUInt16(0); // RDLENGTH
        }

        return writer.ToArray();
    }

    /// <summary>
    /// Encodes a response that fits within <paramref name="maxSize"/> bytes, setting the TC bit and
    /// dropping sections as needed (RFC 1035 4.1.1). <paramref name="message"/> is updated in place to
    /// match the bytes that are returned.
    /// </summary>
    public static byte[] EncodeResponse(DnsMessage message, int maxSize)
    {
        var bytes = EncodeResponse(message);
        if (bytes.Length <= maxSize)
            return bytes;

        message.IsTruncated = true;
        message.Answers.Clear();
        message.Authorities.Clear();
        message.AdditionalRecords.Clear();

        bytes = EncodeResponse(message);
        if (bytes.Length <= maxSize)
            return bytes;

        // An over-long question section can still push the message past the limit on its own.
        message.Questions.Clear();

        bytes = EncodeResponse(message);
        if (bytes.Length <= maxSize)
            return bytes;

        throw new DnsProtocolException($"The DNS response header alone is {bytes.Length} bytes, which exceeds the {maxSize}-byte limit of this transport.");
    }

    private static ushort ToCount(int count, string section)
    {
        if (count > ushort.MaxValue)
            throw new DnsProtocolException($"A DNS message cannot contain more than {ushort.MaxValue} {section} records, but {count} were provided.");

        return (ushort)count;
    }

    private static void WriteRecords(ref DnsWireWriter writer, IList<DnsResourceRecord> records)
    {
        foreach (var record in records)
        {
            writer.WriteCompressibleDomainName(record.Name);
            writer.WriteUInt16((ushort)record.Type);
            writer.WriteUInt16((ushort)record.Class);
            writer.WriteUInt32(record.TimeToLive);

            var rdLengthPosition = writer.Position;
            writer.WriteUInt16(0); // placeholder for RDLENGTH

            var rdataStart = writer.Position;
            WriteRecordData(ref writer, record.Data);
            var rdLength = writer.Position - rdataStart;
            if (rdLength > ushort.MaxValue)
                throw new DnsProtocolException($"The data of the {record.Type} record for '{record.Name}' is {rdLength} bytes, which exceeds the {ushort.MaxValue}-byte maximum.");

            writer.WriteUInt16At((ushort)rdLength, rdLengthPosition);
        }
    }

    private static void WriteRecordData(ref DnsWireWriter writer, DnsResourceRecordData? data)
    {
        if (data is null)
            return;

        // Only the record types defined in RFC 1035 may compress names inside their data;
        // RFC 3597 3 forbids it for every type defined afterwards.
        switch (data)
        {
            case DnsARecordData a:
                writer.WriteBytes(a.Address.GetAddressBytes());
                break;

            case DnsAaaaRecordData aaaa:
                writer.WriteBytes(aaaa.Address.GetAddressBytes());
                break;

            case DnsCnameRecordData cname:
                writer.WriteCompressibleDomainName(cname.CanonicalName);
                break;

            case DnsMxRecordData mx:
                writer.WriteUInt16(mx.Preference);
                writer.WriteCompressibleDomainName(mx.Exchange);
                break;

            case DnsNsRecordData ns:
                writer.WriteCompressibleDomainName(ns.NameServer);
                break;

            case DnsPtrRecordData ptr:
                writer.WriteCompressibleDomainName(ptr.DomainName);
                break;

            case DnsSoaRecordData soa:
                writer.WriteCompressibleDomainName(soa.PrimaryNameServer);
                writer.WriteCompressibleDomainName(soa.ResponsibleMailbox);
                writer.WriteUInt32(soa.Serial);
                writer.WriteInt32(soa.Refresh);
                writer.WriteInt32(soa.Retry);
                writer.WriteInt32(soa.Expire);
                writer.WriteUInt32(soa.Minimum);
                break;

            case DnsTxtRecordData txt:
                foreach (var text in txt.Text)
                {
                    writer.WriteCharacterString(text);
                }
                break;

            case DnsSrvRecordData srv:
                writer.WriteUInt16(srv.Priority);
                writer.WriteUInt16(srv.Weight);
                writer.WriteUInt16(srv.Port);
                writer.WriteDomainName(srv.Target);
                break;

            case DnsNaptrRecordData naptr:
                writer.WriteUInt16(naptr.Order);
                writer.WriteUInt16(naptr.Preference);
                writer.WriteAsciiCharacterString(naptr.Flags);
                writer.WriteAsciiCharacterString(naptr.Services);
                writer.WriteCharacterString(naptr.Regexp);
                writer.WriteDomainName(naptr.Replacement);
                break;

            case DnsCaaRecordData caa:
                writer.WriteByte(caa.Flags);
                writer.WriteAsciiCharacterString(caa.Tag);
                writer.WriteAsciiString(caa.Value);
                break;

            case DnsDnskeyRecordData dnskey:
                writer.WriteUInt16(dnskey.Flags);
                writer.WriteByte(dnskey.Protocol);
                writer.WriteByte(dnskey.Algorithm);
                writer.WriteBytes(dnskey.PublicKey);
                break;

            case DnsDsRecordData ds:
                writer.WriteUInt16(ds.KeyTag);
                writer.WriteByte(ds.Algorithm);
                writer.WriteByte(ds.DigestType);
                writer.WriteBytes(ds.Digest);
                break;

            case DnsRrsigRecordData rrsig:
                writer.WriteUInt16((ushort)rrsig.TypeCovered);
                writer.WriteByte(rrsig.Algorithm);
                writer.WriteByte(rrsig.Labels);
                writer.WriteUInt32(rrsig.OriginalTtl);
                writer.WriteUInt32(rrsig.SignatureExpiration);
                writer.WriteUInt32(rrsig.SignatureInception);
                writer.WriteUInt16(rrsig.KeyTag);
                writer.WriteDomainName(rrsig.SignerName);
                writer.WriteBytes(rrsig.Signature);
                break;

            case DnsNsecRecordData nsec:
                writer.WriteDomainName(nsec.NextDomainName);
                WriteTypeBitMaps(ref writer, nsec.TypeBitMaps);
                break;

            case DnsNsec3RecordData nsec3:
                writer.WriteByte(nsec3.HashAlgorithm);
                writer.WriteByte(nsec3.Flags);
                writer.WriteUInt16(nsec3.Iterations);
                writer.WriteByte((byte)nsec3.Salt.Length);
                writer.WriteBytes(nsec3.Salt);
                writer.WriteByte((byte)nsec3.NextHashedOwnerName.Length);
                writer.WriteBytes(nsec3.NextHashedOwnerName);
                WriteTypeBitMaps(ref writer, nsec3.TypeBitMaps);
                break;

            case DnsNsec3ParamRecordData nsec3Param:
                writer.WriteByte(nsec3Param.HashAlgorithm);
                writer.WriteByte(nsec3Param.Flags);
                writer.WriteUInt16(nsec3Param.Iterations);
                writer.WriteByte((byte)nsec3Param.Salt.Length);
                writer.WriteBytes(nsec3Param.Salt);
                break;

            case DnsTlsaRecordData tlsa:
                writer.WriteByte(tlsa.CertificateUsage);
                writer.WriteByte(tlsa.Selector);
                writer.WriteByte(tlsa.MatchingType);
                writer.WriteBytes(tlsa.CertificateAssociationData);
                break;

            case DnsSshfpRecordData sshfp:
                writer.WriteByte(sshfp.Algorithm);
                writer.WriteByte(sshfp.FingerprintType);
                writer.WriteBytes(sshfp.Fingerprint);
                break;

            case DnsSvcbRecordData svcb:
                writer.WriteUInt16(svcb.Priority);
                writer.WriteDomainName(svcb.TargetName);
                foreach (var param in svcb.Parameters)
                {
                    writer.WriteUInt16(param.Key);
                    writer.WriteUInt16((ushort)param.Value.Length);
                    writer.WriteBytes(param.Value);
                }
                break;

            case DnsLocRecordData loc:
                writer.WriteByte(loc.Version);
                writer.WriteByte(loc.Size);
                writer.WriteByte(loc.HorizontalPrecision);
                writer.WriteByte(loc.VerticalPrecision);
                writer.WriteUInt32(loc.Latitude);
                writer.WriteUInt32(loc.Longitude);
                writer.WriteUInt32(loc.Altitude);
                break;

            case DnsHinfoRecordData hinfo:
                writer.WriteAsciiCharacterString(hinfo.Cpu);
                writer.WriteAsciiCharacterString(hinfo.Os);
                break;

            case DnsRpRecordData rp:
                writer.WriteDomainName(rp.Mailbox);
                writer.WriteDomainName(rp.TxtDomainName);
                break;

            case DnsDnameRecordData dname:
                writer.WriteDomainName(dname.Target);
                break;

            case DnsOptRecordData opt:
                foreach (var option in opt.Options)
                {
                    writer.WriteUInt16(option.Code);
                    writer.WriteUInt16((ushort)option.Data.Length);
                    writer.WriteBytes(option.Data);
                }
                break;

            case DnsUriRecordData uri:
                writer.WriteUInt16(uri.Priority);
                writer.WriteUInt16(uri.Weight);
                writer.WriteBytes(Encoding.UTF8.GetBytes(uri.Target));
                break;

            case DnsUnknownRecordData unknown:
                writer.WriteBytes(unknown.Data);
                break;
        }
    }

    private static void ReadRecordsInto(IList<DnsResourceRecord> records, ref DnsWireReader reader, ushort count)
    {
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadDomainName();
            var type = (DnsQueryType)reader.ReadUInt16();
            var recordClass = (DnsQueryClass)reader.ReadUInt16();
            var ttl = reader.ReadUInt32();
            var rdLength = reader.ReadUInt16();

            // Scope the reads to this record so a bad length cannot walk into the next one.
            var previousLimit = reader.PushLimit(rdLength);
            var data = ParseRecordData(ref reader, type);
            reader.Skip(reader.Remaining);
            reader.PopLimit(previousLimit);

            records.Add(new DnsResourceRecord
            {
                Name = name,
                Type = type,
                Class = recordClass,
                TimeToLive = ttl,
                Data = data,
            });
        }
    }

    private static DnsResourceRecordData ParseRecordData(ref DnsWireReader reader, DnsQueryType type)
    {
        return type switch
        {
            DnsQueryType.A => ParseARecord(ref reader),
            DnsQueryType.AAAA => ParseAaaaRecord(ref reader),
            DnsQueryType.CNAME => ParseCnameRecord(ref reader),
            DnsQueryType.MX => ParseMxRecord(ref reader),
            DnsQueryType.NS => ParseNsRecord(ref reader),
            DnsQueryType.PTR => ParsePtrRecord(ref reader),
            DnsQueryType.SOA => ParseSoaRecord(ref reader),
            DnsQueryType.SRV => ParseSrvRecord(ref reader),
            DnsQueryType.TXT => ParseTxtRecord(ref reader),
            DnsQueryType.CAA => ParseCaaRecord(ref reader),
            DnsQueryType.NAPTR => ParseNaptrRecord(ref reader),
            DnsQueryType.DNSKEY => ParseDnskeyRecord(ref reader),
            DnsQueryType.DS => ParseDsRecord(ref reader),
            DnsQueryType.RRSIG => ParseRrsigRecord(ref reader),
            DnsQueryType.NSEC => ParseNsecRecord(ref reader),
            DnsQueryType.NSEC3 => ParseNsec3Record(ref reader),
            DnsQueryType.NSEC3PARAM => ParseNsec3ParamRecord(ref reader),
            DnsQueryType.TLSA => ParseTlsaRecord(ref reader),
            DnsQueryType.SSHFP => ParseSshfpRecord(ref reader),
            DnsQueryType.SVCB or DnsQueryType.HTTPS => ParseSvcbRecord(ref reader),
            DnsQueryType.LOC => ParseLocRecord(ref reader),
            DnsQueryType.HINFO => ParseHinfoRecord(ref reader),
            DnsQueryType.RP => ParseRpRecord(ref reader),
            DnsQueryType.DNAME => ParseDnameRecord(ref reader),
            DnsQueryType.OPT => ParseOptRecord(ref reader),
            DnsQueryType.URI => ParseUriRecord(ref reader),
            _ => ParseUnknownRecord(ref reader),
        };
    }

    private static DnsARecordData ParseARecord(ref DnsWireReader reader)
    {
        return new DnsARecordData { Address = new IPAddress(reader.ReadBytes(4)) };
    }

    private static DnsAaaaRecordData ParseAaaaRecord(ref DnsWireReader reader)
    {
        return new DnsAaaaRecordData { Address = new IPAddress(reader.ReadBytes(16)) };
    }

    private static DnsCnameRecordData ParseCnameRecord(ref DnsWireReader reader)
    {
        return new DnsCnameRecordData { CanonicalName = reader.ReadDomainName() };
    }

    private static DnsMxRecordData ParseMxRecord(ref DnsWireReader reader)
    {
        return new DnsMxRecordData
        {
            Preference = reader.ReadUInt16(),
            Exchange = reader.ReadDomainName(),
        };
    }

    private static DnsNsRecordData ParseNsRecord(ref DnsWireReader reader)
    {
        return new DnsNsRecordData { NameServer = reader.ReadDomainName() };
    }

    private static DnsPtrRecordData ParsePtrRecord(ref DnsWireReader reader)
    {
        return new DnsPtrRecordData { DomainName = reader.ReadDomainName() };
    }

    private static DnsSoaRecordData ParseSoaRecord(ref DnsWireReader reader)
    {
        return new DnsSoaRecordData
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

    private static DnsSrvRecordData ParseSrvRecord(ref DnsWireReader reader)
    {
        return new DnsSrvRecordData
        {
            Priority = reader.ReadUInt16(),
            Weight = reader.ReadUInt16(),
            Port = reader.ReadUInt16(),
            Target = reader.ReadDomainName(),
        };
    }

    private static DnsTxtRecordData ParseTxtRecord(ref DnsWireReader reader)
    {
        var texts = new List<string>();
        while (reader.Remaining > 0)
        {
            var length = reader.ReadByte();
            var text = Encoding.UTF8.GetString(reader.ReadBytes(length));
            texts.Add(text);
        }

        return new DnsTxtRecordData { Text = texts };
    }

    private static DnsCaaRecordData ParseCaaRecord(ref DnsWireReader reader)
    {
        var flags = reader.ReadByte();
        var tagLength = reader.ReadByte();
        var tag = Encoding.ASCII.GetString(reader.ReadBytes(tagLength));
        var value = Encoding.ASCII.GetString(reader.ReadBytes(reader.Remaining));

        return new DnsCaaRecordData { Flags = flags, Tag = tag, Value = value };
    }

    private static DnsNaptrRecordData ParseNaptrRecord(ref DnsWireReader reader)
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

        return new DnsNaptrRecordData
        {
            Order = order,
            Preference = preference,
            Flags = flags,
            Services = services,
            Regexp = regexp,
            Replacement = replacement,
        };
    }

    private static DnsDnskeyRecordData ParseDnskeyRecord(ref DnsWireReader reader)
    {
        return new DnsDnskeyRecordData
        {
            Flags = reader.ReadUInt16(),
            Protocol = reader.ReadByte(),
            Algorithm = reader.ReadByte(),
            PublicKey = reader.ReadBytes(reader.Remaining).ToArray(),
        };
    }

    private static DnsDsRecordData ParseDsRecord(ref DnsWireReader reader)
    {
        return new DnsDsRecordData
        {
            KeyTag = reader.ReadUInt16(),
            Algorithm = reader.ReadByte(),
            DigestType = reader.ReadByte(),
            Digest = reader.ReadBytes(reader.Remaining).ToArray(),
        };
    }

    private static DnsRrsigRecordData ParseRrsigRecord(ref DnsWireReader reader)
    {
        var typeCovered = (DnsQueryType)reader.ReadUInt16();
        var algorithm = reader.ReadByte();
        var labels = reader.ReadByte();
        var originalTtl = reader.ReadUInt32();
        var expiration = reader.ReadUInt32();
        var inception = reader.ReadUInt32();
        var keyTag = reader.ReadUInt16();
        var signerName = reader.ReadDomainName();
        var signature = reader.ReadBytes(reader.Remaining).ToArray();

        return new DnsRrsigRecordData
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

    private static DnsNsecRecordData ParseNsecRecord(ref DnsWireReader reader)
    {
        var nextDomainName = reader.ReadDomainName();
        var typeBitMaps = ParseTypeBitMaps(ref reader);

        return new DnsNsecRecordData
        {
            NextDomainName = nextDomainName,
            TypeBitMaps = typeBitMaps,
        };
    }

    private static DnsNsec3RecordData ParseNsec3Record(ref DnsWireReader reader)
    {
        var hashAlgorithm = reader.ReadByte();
        var flags = reader.ReadByte();
        var iterations = reader.ReadUInt16();
        var saltLength = reader.ReadByte();
        var salt = reader.ReadBytes(saltLength).ToArray();
        var hashLength = reader.ReadByte();
        var nextHashedOwnerName = reader.ReadBytes(hashLength).ToArray();
        var typeBitMaps = ParseTypeBitMaps(ref reader);

        return new DnsNsec3RecordData
        {
            HashAlgorithm = hashAlgorithm,
            Flags = flags,
            Iterations = iterations,
            Salt = salt,
            NextHashedOwnerName = nextHashedOwnerName,
            TypeBitMaps = typeBitMaps,
        };
    }

    private static DnsNsec3ParamRecordData ParseNsec3ParamRecord(ref DnsWireReader reader)
    {
        return new DnsNsec3ParamRecordData
        {
            HashAlgorithm = reader.ReadByte(),
            Flags = reader.ReadByte(),
            Iterations = reader.ReadUInt16(),
            Salt = reader.ReadBytes(reader.ReadByte()).ToArray(),
        };
    }

    private static DnsTlsaRecordData ParseTlsaRecord(ref DnsWireReader reader)
    {
        return new DnsTlsaRecordData
        {
            CertificateUsage = reader.ReadByte(),
            Selector = reader.ReadByte(),
            MatchingType = reader.ReadByte(),
            CertificateAssociationData = reader.ReadBytes(reader.Remaining).ToArray(),
        };
    }

    private static DnsSshfpRecordData ParseSshfpRecord(ref DnsWireReader reader)
    {
        return new DnsSshfpRecordData
        {
            Algorithm = reader.ReadByte(),
            FingerprintType = reader.ReadByte(),
            Fingerprint = reader.ReadBytes(reader.Remaining).ToArray(),
        };
    }

    private static DnsSvcbRecordData ParseSvcbRecord(ref DnsWireReader reader)
    {
        var priority = reader.ReadUInt16();
        var targetName = reader.ReadDomainName();

        var parameters = new List<DnsSvcParam>();
        while (reader.Remaining > 0)
        {
            var key = reader.ReadUInt16();
            var valueLength = reader.ReadUInt16();
            var value = reader.ReadBytes(valueLength).ToArray();
            parameters.Add(new DnsSvcParam { Key = key, Value = value });
        }

        return new DnsSvcbRecordData
        {
            Priority = priority,
            TargetName = targetName,
            Parameters = parameters,
        };
    }

    private static DnsLocRecordData ParseLocRecord(ref DnsWireReader reader)
    {
        return new DnsLocRecordData
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

    private static DnsHinfoRecordData ParseHinfoRecord(ref DnsWireReader reader)
    {
        var cpuLength = reader.ReadByte();
        var cpu = Encoding.ASCII.GetString(reader.ReadBytes(cpuLength));
        var osLength = reader.ReadByte();
        var os = Encoding.ASCII.GetString(reader.ReadBytes(osLength));

        return new DnsHinfoRecordData { Cpu = cpu, Os = os };
    }

    private static DnsRpRecordData ParseRpRecord(ref DnsWireReader reader)
    {
        return new DnsRpRecordData
        {
            Mailbox = reader.ReadDomainName(),
            TxtDomainName = reader.ReadDomainName(),
        };
    }

    private static DnsDnameRecordData ParseDnameRecord(ref DnsWireReader reader)
    {
        return new DnsDnameRecordData { Target = reader.ReadDomainName() };
    }

    private static DnsOptRecordData ParseOptRecord(ref DnsWireReader reader)
    {
        var options = new List<DnsEdnsOption>();
        while (reader.Remaining > 0)
        {
            var code = reader.ReadUInt16();
            var length = reader.ReadUInt16();
            var data = reader.ReadBytes(length).ToArray();
            options.Add(new DnsEdnsOption { Code = code, Data = data });
        }

        return new DnsOptRecordData { Options = options };
    }

    private static DnsUriRecordData ParseUriRecord(ref DnsWireReader reader)
    {
        return new DnsUriRecordData
        {
            Priority = reader.ReadUInt16(),
            Weight = reader.ReadUInt16(),
            Target = Encoding.UTF8.GetString(reader.ReadBytes(reader.Remaining)),
        };
    }

    private static DnsUnknownRecordData ParseUnknownRecord(ref DnsWireReader reader)
    {
        return new DnsUnknownRecordData { Data = reader.ReadBytes(reader.Remaining).ToArray() };
    }

    private static List<DnsQueryType> ParseTypeBitMaps(ref DnsWireReader reader)
    {
        var types = new List<DnsQueryType>();

        while (reader.Remaining > 0)
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

    private static void WriteTypeBitMaps(ref DnsWireWriter writer, IReadOnlyList<DnsQueryType> types)
    {
        if (types.Count is 0)
            return;

        // Group types by window (each window covers 256 type values)
        var windows = new SortedDictionary<byte, byte[]>();
        foreach (var type in types)
        {
            var typeValue = (ushort)type;
            var window = (byte)(typeValue / 256);
            var offset = typeValue % 256;
            var byteIndex = offset / 8;
            var bitIndex = 7 - (offset % 8);

            if (!windows.TryGetValue(window, out var bitmap))
            {
                bitmap = new byte[32]; // max 32 bytes per window
                windows[window] = bitmap;
            }

            bitmap[byteIndex] |= (byte)(1 << bitIndex);
        }

        foreach (var (window, bitmap) in windows)
        {
            // Find the last non-zero byte
            var lastNonZero = bitmap.Length - 1;
            while (lastNonZero >= 0 && bitmap[lastNonZero] is 0)
            {
                lastNonZero--;
            }

            if (lastNonZero < 0)
                continue;

            var bitmapLength = lastNonZero + 1;
            writer.WriteByte(window);
            writer.WriteByte((byte)bitmapLength);
            writer.WriteBytes(bitmap.AsSpan(0, bitmapLength));
        }
    }
}
