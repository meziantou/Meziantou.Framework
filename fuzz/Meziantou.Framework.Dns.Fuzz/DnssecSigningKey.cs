using System.Security.Cryptography;
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// An ECDSA P-256 key (DNSSEC algorithm 13) that signs the messages of an input, so that a fuzzed chain of trust
/// validates: a fuzzer cannot compute signatures. The key is created once per process; the signatures differ between
/// runs (ECDSA is randomized), but whether they verify does not.
/// </summary>
internal static class DnssecSigningKey
{
    private const byte Algorithm = 13;
    private const ushort KeySigningKeyFlags = 257;

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly byte[] PublicKey = GetPublicKey();
    private static readonly DnsDnskeyRecord Dnskey = new() { Flags = KeySigningKeyFlags, Protocol = 3, Algorithm = Algorithm, PublicKey = PublicKey };
    private static readonly ushort KeyTag = DnssecCanonicalizer.ComputeKeyTag(Dnskey);

    /// <summary>Gets the trust anchor of the root zone for the key.</summary>
    public static DnssecTrustAnchor TrustAnchor { get; } = new(".", KeyTag, Algorithm, 2, DnssecCanonicalizer.ComputeDigest("", Dnskey, 2));

    /// <summary>Gives the algorithm 13 DNSKEY records the public key, the DS records its digest, and the RRSIG records a signature of the RRset they cover.</summary>
    public static void Sign(DnsResponseMessage message)
    {
        IReadOnlyList<DnsRecord>[] sections = [message.Answers, message.Authorities, message.AdditionalRecords];
        foreach (var record in sections.SelectMany(section => section))
        {
            switch (record)
            {
                case DnsDnskeyRecord { Algorithm: Algorithm } dnskey:
                    dnskey.PublicKey = PublicKey;
                    dnskey.RawData = [(byte)(dnskey.Flags >> 8), (byte)dnskey.Flags, dnskey.Protocol, dnskey.Algorithm, .. PublicKey];
                    break;

                // The DS of a key signing key: the fuzzer gives the DNSKEY of the child zone flags 257 and protocol 3
                case DnsDsRecord { Algorithm: Algorithm } ds when DnssecCanonicalizer.IsSupportedDigest(ds.DigestType):
                    ds.KeyTag = KeyTag;
                    ds.Digest = DnssecCanonicalizer.ComputeDigest(ds.Name, Dnskey, ds.DigestType);
                    ds.RawData = [(byte)(KeyTag >> 8), (byte)KeyTag, ds.Algorithm, ds.DigestType, .. ds.Digest];
                    break;
            }
        }

        // The RRsets are grouped as the validator groups them: by normalized owner name and type, within a section
        foreach (var section in sections)
        {
            foreach (var signature in section.OfType<DnsRrsigRecord>().Where(signature => signature.Algorithm is Algorithm))
            {
                signature.KeyTag = KeyTag;
                var ownerName = DnssecCanonicalizer.NormalizeName(signature.Name);
                var rrset = section.Where(record => record is not DnsRrsigRecord and not DnsOptRecord && record.RecordType == signature.TypeCovered && DnssecCanonicalizer.NormalizeName(record.Name) == ownerName).ToArray();
                if (rrset.Length is 0)
                    continue;

                signature.Signature = Key.SignData(DnssecCanonicalizer.GetSignedData(rrset, signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
        }
    }

    private static byte[] GetPublicKey()
    {
        var parameters = Key.ExportParameters(includePrivateParameters: false);
        return [.. parameters.Q.X!, .. parameters.Q.Y!];
    }
}
