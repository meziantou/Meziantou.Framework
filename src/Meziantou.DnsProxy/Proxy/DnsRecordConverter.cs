using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;

namespace Meziantou.DnsProxy.Proxy;

internal static class DnsRecordConverter
{
    public static DnsResourceRecord ConvertToServerRecord(DnsRecord record)
    {
        return new DnsResourceRecord
        {
            Name = record.Name,
            Type = (DnsQueryType)record.RecordType,
            Class = (DnsQueryClass)record.RecordClass,
            TimeToLive = record.TimeToLive,
            Data = ConvertRecordData(record),
        };
    }

    private static DnsResourceRecordData? ConvertRecordData(DnsRecord record)
    {
        return record switch
        {
            DnsARecord a => new DnsARecordData { Address = a.Address },
            DnsAaaaRecord aaaa => new DnsAaaaRecordData { Address = aaaa.Address },
            DnsCnameRecord cname => new DnsCnameRecordData { CanonicalName = cname.CanonicalName },
            DnsMxRecord mx => new DnsMxRecordData { Preference = mx.Preference, Exchange = mx.Exchange },
            DnsNsRecord ns => new DnsNsRecordData { NameServer = ns.NameServer },
            DnsPtrRecord ptr => new DnsPtrRecordData { DomainName = ptr.DomainName },
            DnsSoaRecord soa => new DnsSoaRecordData
            {
                PrimaryNameServer = soa.PrimaryNameServer,
                ResponsibleMailbox = soa.ResponsibleMailbox,
                Serial = soa.Serial,
                Refresh = soa.Refresh,
                Retry = soa.Retry,
                Expire = soa.Expire,
                Minimum = soa.Minimum,
            },
            DnsTxtRecord txt => new DnsTxtRecordData { Text = txt.Text },
            DnsSrvRecord srv => new DnsSrvRecordData { Priority = srv.Priority, Weight = srv.Weight, Port = srv.Port, Target = srv.Target },
            DnsNaptrRecord naptr => new DnsNaptrRecordData
            {
                Order = naptr.Order,
                Preference = naptr.Preference,
                Flags = naptr.Flags,
                Services = naptr.Services,
                Regexp = naptr.Regexp,
                Replacement = naptr.Replacement,
            },
            DnsCaaRecord caa => new DnsCaaRecordData { Flags = caa.Flags, Tag = caa.Tag, Value = caa.Value },
            DnsDnskeyRecord dnskey => new DnsDnskeyRecordData { Flags = dnskey.Flags, Protocol = dnskey.Protocol, Algorithm = dnskey.Algorithm, PublicKey = dnskey.PublicKey },
            DnsDsRecord ds => new DnsDsRecordData { KeyTag = ds.KeyTag, Algorithm = ds.Algorithm, DigestType = ds.DigestType, Digest = ds.Digest },
            DnsRrsigRecord rrsig => new DnsRrsigRecordData
            {
                TypeCovered = (DnsQueryType)rrsig.TypeCovered,
                Algorithm = rrsig.Algorithm,
                Labels = rrsig.Labels,
                OriginalTtl = rrsig.OriginalTtl,
                SignatureExpiration = rrsig.SignatureExpiration,
                SignatureInception = rrsig.SignatureInception,
                KeyTag = rrsig.KeyTag,
                SignerName = rrsig.SignerName,
                Signature = rrsig.Signature,
            },
            DnsNsecRecord nsec => new DnsNsecRecordData { NextDomainName = nsec.NextDomainName, TypeBitMaps = nsec.TypeBitMaps.Select(t => (DnsQueryType)t).ToArray() },
            DnsNsec3Record nsec3 => new DnsNsec3RecordData
            {
                HashAlgorithm = nsec3.HashAlgorithm,
                Flags = nsec3.Flags,
                Iterations = nsec3.Iterations,
                Salt = nsec3.Salt,
                NextHashedOwnerName = nsec3.NextHashedOwnerName,
                TypeBitMaps = nsec3.TypeBitMaps.Select(t => (DnsQueryType)t).ToArray(),
            },
            DnsNsec3ParamRecord nsec3Param => new DnsNsec3ParamRecordData
            {
                HashAlgorithm = nsec3Param.HashAlgorithm,
                Flags = nsec3Param.Flags,
                Iterations = nsec3Param.Iterations,
                Salt = nsec3Param.Salt,
            },
            DnsTlsaRecord tlsa => new DnsTlsaRecordData
            {
                CertificateUsage = tlsa.CertificateUsage,
                Selector = tlsa.Selector,
                MatchingType = tlsa.MatchingType,
                CertificateAssociationData = tlsa.CertificateAssociationData,
            },
            DnsSshfpRecord sshfp => new DnsSshfpRecordData { Algorithm = sshfp.Algorithm, FingerprintType = sshfp.FingerprintType, Fingerprint = sshfp.Fingerprint },
            DnsSvcbRecord svcb => new DnsSvcbRecordData
            {
                Priority = svcb.Priority,
                TargetName = svcb.TargetName,
                Parameters = svcb.Parameters.Select(p => new Meziantou.Framework.DnsServer.Protocol.Records.DnsSvcParam { Key = p.Key, Value = p.Value }).ToArray(),
            },
            DnsLocRecord loc => new DnsLocRecordData
            {
                Version = loc.Version,
                Size = loc.Size,
                HorizontalPrecision = loc.HorizontalPrecision,
                VerticalPrecision = loc.VerticalPrecision,
                Latitude = loc.Latitude,
                Longitude = loc.Longitude,
                Altitude = loc.Altitude,
            },
            DnsHinfoRecord hinfo => new DnsHinfoRecordData { Cpu = hinfo.Cpu, Os = hinfo.Os },
            DnsRpRecord rp => new DnsRpRecordData { Mailbox = rp.Mailbox, TxtDomainName = rp.TxtDomainName },
            DnsDnameRecord dname => new DnsDnameRecordData { Target = dname.Target },
            DnsOptRecord opt => new DnsOptRecordData
            {
                Options = opt.Options.Select(o => new Meziantou.Framework.DnsServer.Protocol.Records.DnsEdnsOption { Code = o.Code, Data = o.Data }).ToArray(),
            },
            DnsUriRecord uri => new DnsUriRecordData { Priority = uri.Priority, Weight = uri.Weight, Target = uri.Target },
            DnsUnknownRecord unknown => new DnsUnknownRecordData { Data = unknown.Data },
            _ => null,
        };
    }
}
