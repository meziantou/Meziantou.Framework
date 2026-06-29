using System.Collections;
using System.Security.Cryptography;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Internal;

internal static class DnssecCanonicalizer
{
    public const ushort MaxNsec3Iterations = 100;

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is ".")
            return "";

        return name.TrimEnd('.').ToLowerInvariant();
    }

    public static string ToDisplayName(string name)
    {
        var normalized = NormalizeName(name);
        return normalized.Length is 0 ? "." : normalized;
    }

    public static string GetParentName(string name)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length is 0)
            return "";

        var index = normalized.IndexOf('.', StringComparison.Ordinal);
        return index < 0 ? "" : normalized[(index + 1)..];
    }

    public static int CountLabels(string name)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length is 0)
            return 0;

        var count = 1;
        foreach (var ch in normalized)
        {
            if (ch == '.')
            {
                count++;
            }
        }

        return count;
    }

    public static byte[] GetSignedData(IReadOnlyList<DnsRecord> rrset, DnsRrsigRecord signature)
    {
        var writer = new DnsWireWriter(1024);
        WriteRrsigCoveredFields(ref writer, signature);

        var records = rrset
            .Select(record => GetCanonicalRecordData(record, signature))
            .Order(ByteArrayComparer.Instance)
            .ToArray();

        foreach (var record in records)
        {
            writer.WriteBytes(record);
        }

        return writer.ToArray();
    }

    public static ushort ComputeKeyTag(DnsDnskeyRecord key)
    {
        var data = GetCanonicalRData(key);
        var accumulator = 0;

        for (var i = 0; i < data.Length; i++)
        {
            accumulator += (i & 1) is 0 ? data[i] << 8 : data[i];
        }

        accumulator += (accumulator >> 16) & 0xFFFF;
        return (ushort)(accumulator & 0xFFFF);
    }

    public static byte[] ComputeDigest(string ownerName, DnsDnskeyRecord key, byte digestType)
    {
        var writer = new DnsWireWriter(512);
        WriteCanonicalDomainName(ref writer, ownerName);
        writer.WriteBytes(GetCanonicalRData(key));
        var data = writer.ToArray();

        return digestType switch
        {
#pragma warning disable CA5350 // DNSSEC DS digest type 1 is SHA-1 by specification.
            1 => SHA1.HashData(data),
#pragma warning restore CA5350
            2 => SHA256.HashData(data),
            4 => SHA384.HashData(data),
            _ => [],
        };
    }

    public static bool IsSupportedDigest(byte digestType)
    {
        return digestType is 1 or 2 or 4;
    }

    public static byte[] GetCanonicalRData(DnsRecord record)
    {
        var writer = new DnsWireWriter(Math.Max(record.DataLength, (ushort)32));
        WriteCanonicalRData(ref writer, record);
        return writer.ToArray();
    }

    public static bool NsecCovers(string ownerName, string nextName, string name)
    {
        var ownerToNext = CompareCanonicalNames(ownerName, nextName);
        if (ownerToNext < 0)
            return CompareCanonicalNames(ownerName, name) < 0 && CompareCanonicalNames(name, nextName) < 0;

        return CompareCanonicalNames(ownerName, name) < 0 || CompareCanonicalNames(name, nextName) < 0;
    }

    public static bool Nsec3Covers(ReadOnlySpan<byte> ownerHash, ReadOnlySpan<byte> nextHash, ReadOnlySpan<byte> hash)
    {
        var ownerToNext = ownerHash.SequenceCompareTo(nextHash);
        if (ownerToNext < 0)
            return ownerHash.SequenceCompareTo(hash) < 0 && hash.SequenceCompareTo(nextHash) < 0;

        return ownerHash.SequenceCompareTo(hash) < 0 || hash.SequenceCompareTo(nextHash) < 0;
    }

    public static byte[] ComputeNsec3Hash(string name, DnsNsec3Record record)
    {
        if (record.HashAlgorithm is not 1 || record.Iterations > MaxNsec3Iterations)
            return [];

        var owner = GetCanonicalDomainNameBytes(name);
        var salt = record.Salt.AsSpan();
        Span<byte> buffer = stackalloc byte[owner.Length + salt.Length];
        owner.CopyTo(buffer);
        salt.CopyTo(buffer[owner.Length..]);
#pragma warning disable CA5350 // NSEC3 hash algorithm 1 is SHA-1 by specification.
        var hash = SHA1.HashData(buffer);

        Span<byte> iterationBuffer = stackalloc byte[SHA1.HashSizeInBytes + salt.Length];
        for (var i = 0; i < record.Iterations; i++)
        {
            hash.CopyTo(iterationBuffer);
            salt.CopyTo(iterationBuffer[hash.Length..]);
            hash = SHA1.HashData(iterationBuffer);
        }
#pragma warning restore CA5350

        return hash;
    }

    public static byte[] DecodeBase32Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var output = new List<byte>((value.Length * 5) / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var ch in value)
        {
            if (ch is '=')
                break;

            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'A' and <= 'V' => ch - 'A' + 10,
                >= 'a' and <= 'v' => ch - 'a' + 10,
                _ => -1,
            };

            if (digit < 0)
                return [];

            buffer = (buffer << 5) | digit;
            bits += 5;

            while (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
                buffer &= (1 << bits) - 1;
            }
        }

        return output.ToArray();
    }

    public static int CompareCanonicalNames(string left, string right)
    {
        var leftLabels = GetLabels(left);
        var rightLabels = GetLabels(right);
        var leftIndex = leftLabels.Length - 1;
        var rightIndex = rightLabels.Length - 1;

        while (leftIndex >= 0 && rightIndex >= 0)
        {
            var comparison = CompareCanonicalLabels(leftLabels[leftIndex], rightLabels[rightIndex]);
            if (comparison != 0)
                return comparison;

            leftIndex--;
            rightIndex--;
        }

        if (leftIndex == rightIndex)
            return 0;

        return leftIndex < rightIndex ? -1 : 1;
    }

    public static bool IsAncestorOrEqual(string ancestorName, string name)
    {
        var ancestorLabels = GetLabels(ancestorName);
        var labels = GetLabels(name);
        if (ancestorLabels.Length > labels.Length)
            return false;

        for (var i = 1; i <= ancestorLabels.Length; i++)
        {
            if (CompareCanonicalLabels(ancestorLabels[^i], labels[^i]) != 0)
                return false;
        }

        return true;
    }

    private static byte[] GetCanonicalRecordData(DnsRecord record, DnsRrsigRecord signature)
    {
        var writer = new DnsWireWriter(Math.Max(record.DataLength + 32, (ushort)128));
        WriteCanonicalDomainName(ref writer, GetSignedOwnerName(record.Name, signature.Labels));
        writer.WriteUInt16((ushort)record.RecordType);
        writer.WriteUInt16((ushort)record.RecordClass);
        writer.WriteUInt32(signature.OriginalTtl);

        var rdata = GetCanonicalRData(record);
        writer.WriteUInt16((ushort)rdata.Length);
        writer.WriteBytes(rdata);
        return writer.ToArray();
    }

    private static string GetSignedOwnerName(string name, byte labels)
    {
        var normalized = NormalizeName(name);
        var currentLabels = CountLabels(normalized);
        if (labels >= currentLabels)
            return normalized;

        var split = GetLabels(normalized);
        var suffix = labels is 0 ? "" : string.Join('.', split.Skip(split.Length - labels));
        return suffix.Length is 0 ? "*" : "*." + suffix;
    }

    private static void WriteRrsigCoveredFields(ref DnsWireWriter writer, DnsRrsigRecord signature)
    {
        writer.WriteUInt16((ushort)signature.TypeCovered);
        writer.WriteByte(signature.Algorithm);
        writer.WriteByte(signature.Labels);
        writer.WriteUInt32(signature.OriginalTtl);
        writer.WriteUInt32(signature.SignatureExpiration);
        writer.WriteUInt32(signature.SignatureInception);
        writer.WriteUInt16(signature.KeyTag);
        WriteCanonicalDomainName(ref writer, signature.SignerName);
    }

    private static void WriteCanonicalRData(ref DnsWireWriter writer, DnsRecord record)
    {
        switch (record)
        {
            case DnsARecord aRecord:
                writer.WriteBytes(aRecord.Address.GetAddressBytes());
                break;

            case DnsAaaaRecord aaaaRecord:
                writer.WriteBytes(aaaaRecord.Address.GetAddressBytes());
                break;

            case DnsCnameRecord cnameRecord:
                WriteCanonicalDomainName(ref writer, cnameRecord.CanonicalName);
                break;

            case DnsDnameRecord dnameRecord:
                WriteCanonicalDomainName(ref writer, dnameRecord.Target);
                break;

            case DnsDnskeyRecord dnskeyRecord:
                writer.WriteUInt16(dnskeyRecord.Flags);
                writer.WriteByte(dnskeyRecord.Protocol);
                writer.WriteByte(dnskeyRecord.Algorithm);
                writer.WriteBytes(dnskeyRecord.PublicKey);
                break;

            case DnsDsRecord dsRecord:
                writer.WriteUInt16(dsRecord.KeyTag);
                writer.WriteByte(dsRecord.Algorithm);
                writer.WriteByte(dsRecord.DigestType);
                writer.WriteBytes(dsRecord.Digest);
                break;

            case DnsMxRecord mxRecord:
                writer.WriteUInt16(mxRecord.Preference);
                WriteCanonicalDomainName(ref writer, mxRecord.Exchange);
                break;

            case DnsNsRecord nsRecord:
                WriteCanonicalDomainName(ref writer, nsRecord.NameServer);
                break;

            case DnsPtrRecord ptrRecord:
                WriteCanonicalDomainName(ref writer, ptrRecord.DomainName);
                break;

            case DnsRrsigRecord rrsigRecord:
                WriteRrsigCoveredFields(ref writer, rrsigRecord);
                writer.WriteBytes(rrsigRecord.Signature);
                break;

            case DnsSoaRecord soaRecord:
                WriteCanonicalDomainName(ref writer, soaRecord.PrimaryNameServer);
                WriteCanonicalDomainName(ref writer, soaRecord.ResponsibleMailbox);
                writer.WriteUInt32(soaRecord.Serial);
                writer.WriteUInt32(unchecked((uint)soaRecord.Refresh));
                writer.WriteUInt32(unchecked((uint)soaRecord.Retry));
                writer.WriteUInt32(unchecked((uint)soaRecord.Expire));
                writer.WriteUInt32(soaRecord.Minimum);
                break;

            case DnsSrvRecord srvRecord:
                writer.WriteUInt16(srvRecord.Priority);
                writer.WriteUInt16(srvRecord.Weight);
                writer.WriteUInt16(srvRecord.Port);
                WriteCanonicalDomainName(ref writer, srvRecord.Target);
                break;

            case DnsTxtRecord txtRecord:
                WriteRawOrText(ref writer, txtRecord);
                break;

            case DnsCaaRecord caaRecord:
                writer.WriteByte(caaRecord.Flags);
                WriteCharacterString(ref writer, caaRecord.Tag, Encoding.ASCII);
                writer.WriteBytes(Encoding.ASCII.GetBytes(caaRecord.Value));
                break;

            case DnsNaptrRecord naptrRecord:
                writer.WriteUInt16(naptrRecord.Order);
                writer.WriteUInt16(naptrRecord.Preference);
                WriteCharacterString(ref writer, naptrRecord.Flags, Encoding.ASCII);
                WriteCharacterString(ref writer, naptrRecord.Services, Encoding.ASCII);
                WriteCharacterString(ref writer, naptrRecord.Regexp, Encoding.UTF8);
                WriteCanonicalDomainName(ref writer, naptrRecord.Replacement);
                break;

            case DnsNsecRecord nsecRecord:
                WriteCanonicalDomainName(ref writer, nsecRecord.NextDomainName);
                WriteTypeBitMaps(ref writer, nsecRecord.TypeBitMaps);
                break;

            case DnsNsec3Record nsec3Record:
                writer.WriteByte(nsec3Record.HashAlgorithm);
                writer.WriteByte(nsec3Record.Flags);
                writer.WriteUInt16(nsec3Record.Iterations);
                writer.WriteByte((byte)nsec3Record.Salt.Length);
                writer.WriteBytes(nsec3Record.Salt);
                writer.WriteByte((byte)nsec3Record.NextHashedOwnerName.Length);
                writer.WriteBytes(nsec3Record.NextHashedOwnerName);
                WriteTypeBitMaps(ref writer, nsec3Record.TypeBitMaps);
                break;

            case DnsNsec3ParamRecord nsec3ParamRecord:
                writer.WriteByte(nsec3ParamRecord.HashAlgorithm);
                writer.WriteByte(nsec3ParamRecord.Flags);
                writer.WriteUInt16(nsec3ParamRecord.Iterations);
                writer.WriteByte((byte)nsec3ParamRecord.Salt.Length);
                writer.WriteBytes(nsec3ParamRecord.Salt);
                break;

            case DnsTlsaRecord tlsaRecord:
                writer.WriteByte(tlsaRecord.CertificateUsage);
                writer.WriteByte(tlsaRecord.Selector);
                writer.WriteByte(tlsaRecord.MatchingType);
                writer.WriteBytes(tlsaRecord.CertificateAssociationData);
                break;

            case DnsSshfpRecord sshfpRecord:
                writer.WriteByte(sshfpRecord.Algorithm);
                writer.WriteByte(sshfpRecord.FingerprintType);
                writer.WriteBytes(sshfpRecord.Fingerprint);
                break;

            case DnsSvcbRecord svcbRecord:
                writer.WriteUInt16(svcbRecord.Priority);
                WriteCanonicalDomainName(ref writer, svcbRecord.TargetName);
                foreach (var parameter in svcbRecord.Parameters.OrderBy(parameter => parameter.Key))
                {
                    writer.WriteUInt16(parameter.Key);
                    writer.WriteUInt16((ushort)parameter.Value.Length);
                    writer.WriteBytes(parameter.Value);
                }

                break;

            case DnsLocRecord locRecord:
                writer.WriteByte(locRecord.Version);
                writer.WriteByte(locRecord.Size);
                writer.WriteByte(locRecord.HorizontalPrecision);
                writer.WriteByte(locRecord.VerticalPrecision);
                writer.WriteUInt32(locRecord.Latitude);
                writer.WriteUInt32(locRecord.Longitude);
                writer.WriteUInt32(locRecord.Altitude);
                break;

            case DnsHinfoRecord hinfoRecord:
                WriteCharacterString(ref writer, hinfoRecord.Cpu, Encoding.ASCII);
                WriteCharacterString(ref writer, hinfoRecord.Os, Encoding.ASCII);
                break;

            case DnsRpRecord rpRecord:
                WriteCanonicalDomainName(ref writer, rpRecord.Mailbox);
                WriteCanonicalDomainName(ref writer, rpRecord.TxtDomainName);
                break;

            case DnsUriRecord uriRecord:
                writer.WriteUInt16(uriRecord.Priority);
                writer.WriteUInt16(uriRecord.Weight);
                writer.WriteBytes(Encoding.UTF8.GetBytes(uriRecord.Target));
                break;

            case DnsUnknownRecord unknownRecord:
                writer.WriteBytes(unknownRecord.Data);
                break;

            default:
                writer.WriteBytes(record.RawData);
                break;
        }
    }

    private static void WriteRawOrText(ref DnsWireWriter writer, DnsTxtRecord record)
    {
        if (record.RawData.Length > 0)
        {
            writer.WriteBytes(record.RawData);
            return;
        }

        foreach (var text in record.Text)
        {
            WriteCharacterString(ref writer, text, Encoding.UTF8);
        }
    }

    private static void WriteTypeBitMaps(ref DnsWireWriter writer, IReadOnlyList<DnsQueryType> types)
    {
        foreach (var window in types.Select(type => (ushort)type).Distinct().Order().GroupBy(type => type / 256))
        {
            var values = window.ToArray();
            var bitmapLength = (values.Max() % 256 / 8) + 1;
            var bitmap = new byte[bitmapLength];

            foreach (var value in values)
            {
                var offset = value % 256;
                bitmap[offset / 8] |= (byte)(1 << (7 - (offset % 8)));
            }

            writer.WriteByte((byte)window.Key);
            writer.WriteByte((byte)bitmapLength);
            writer.WriteBytes(bitmap);
        }
    }

    private static void WriteCharacterString(ref DnsWireWriter writer, string value, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value);
        if (bytes.Length > 255)
            throw new DnsProtocolException("DNS character-string values cannot exceed 255 bytes.");

        writer.WriteByte((byte)bytes.Length);
        writer.WriteBytes(bytes);
    }

    private static byte[] GetCanonicalDomainNameBytes(string name)
    {
        var writer = new DnsWireWriter(256);
        WriteCanonicalDomainName(ref writer, name);
        return writer.ToArray();
    }

    private static void WriteCanonicalDomainName(ref DnsWireWriter writer, string name)
    {
        writer.WriteDomainName(NormalizeName(name));
    }

    private static string[] GetLabels(string name)
    {
        var normalized = NormalizeName(name);
        return normalized.Length is 0 ? [] : normalized.Split('.');
    }

    private static int CompareCanonicalLabels(string left, string right)
    {
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            return StructuralComparisons.StructuralComparer.Compare(x, y);
        }
    }
}
