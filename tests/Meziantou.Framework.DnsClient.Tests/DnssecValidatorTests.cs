using System.Net;
using System.Security.Cryptography;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnssecValidatorTests
{
    [Fact]
    public async Task ValidateAsync_SecurePositiveA()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.PositiveAResponse);

        Assert.Equal(DnssecValidationStatus.Secure, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ValidateAsync_SecureCnameAndA()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.CnameResponse);

        Assert.Equal(DnssecValidationStatus.Secure, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_SecureNxDomain()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.NxDomainResponse);

        Assert.Equal(DnssecValidationStatus.Secure, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_SecureNoData()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.NoDataResponse);

        Assert.Equal(DnssecValidationStatus.Secure, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_InsecureUnsignedDelegation()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.InsecureUnsignedResponse);

        Assert.Equal(DnssecValidationStatus.Insecure, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code is DnssecValidationIssueCode.MissingDs);
    }

    [Fact]
    public async Task ValidateAsync_BogusExpiredSignature()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.ExpiredSignatureResponse);

        Assert.Equal(DnssecValidationStatus.Bogus, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code is DnssecValidationIssueCode.SignatureExpired);
    }

    [Fact]
    public async Task ValidateAsync_BogusDsMismatch()
    {
        var fixture = DnssecTestFixture.Create(dsMismatch: true);
        var result = await fixture.ValidateAsync(fixture.PositiveAResponse);

        Assert.Equal(DnssecValidationStatus.Bogus, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code is DnssecValidationIssueCode.DigestMismatch);
    }

    [Fact]
    public async Task ValidateAsync_BogusWhenSignerNameIsNotOwnerAncestor()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.CrossZoneSignedResponse);

        Assert.Equal(DnssecValidationStatus.Bogus, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code is DnssecValidationIssueCode.InvalidData);
    }

    [Fact]
    public async Task ValidateAsync_BogusWhenDnskeyDoesNotHaveZoneKeyFlag()
    {
        var fixture = DnssecTestFixture.Create(exampleKeyFlags: 1);
        var result = await fixture.ValidateAsync(fixture.PositiveAResponse);

        Assert.Equal(DnssecValidationStatus.Bogus, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedAlgorithm()
    {
        var fixture = DnssecTestFixture.Create();
        var result = await fixture.ValidateAsync(fixture.UnsupportedAlgorithmResponse);

        Assert.Equal(DnssecValidationStatus.Indeterminate, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code is DnssecValidationIssueCode.UnsupportedAlgorithm);
    }

    [Fact]
    public void ComputeKeyTag_AndDsDigest_ForIanaRootKsk2017()
    {
        var key = CreateRecord(
            new DnsDnskeyRecord
            {
                Flags = 257,
                Protocol = 3,
                Algorithm = 8,
                PublicKey = Convert.FromBase64String("AwEAAaz/tAm8yTn4Mfeh5eyI96WSVexTBAvkMgJzkKTOiW1vkIbzxeF3+/4RgWOq7HrxRixHlFlExOLAJr5emLvN7SWXgnLh4+B5xQlNVz8Og8kvArMtNROxVQuCaSnIDdD5LKyWbRd2n9WGe2R8PzgCmr3EgVLrjyBxWezF0jLHwVN8efS3rCj/EWgvIWgb9tarpVUDK/b58Da+sqqls3eNbuv7pr+eoZG+SrDK6nWeL3c6H5Apxz7LjVc1uTIdsIXxuOLYA4/ilBmSVIzuDWfdRUfhHdY6+cn8HFRm+2hM8AnXGXws9555KrUB5qihylGa8subX2Nn6UwNR1AkUTV74bU="),
            },
            ".",
            DnsQueryType.DNSKEY);

        Assert.Equal(20326, DnssecCanonicalizer.ComputeKeyTag(key));
        Assert.Equal("E06D44B80B8F1D39A95C0B0D7C65D08458E880409BBC683457104237C7F8EC8D", Convert.ToHexString(DnssecCanonicalizer.ComputeDigest(".", key, digestType: 2)));
    }

    [Fact]
    public void ComputeNsec3Hash_ReturnsEmptyWhenIterationCountExceedsCap()
    {
        var accepted = new DnsNsec3Record
        {
            HashAlgorithm = 1,
            Iterations = DnssecCanonicalizer.MaxNsec3Iterations,
        };
        var rejected = new DnsNsec3Record
        {
            HashAlgorithm = 1,
            Iterations = DnssecCanonicalizer.MaxNsec3Iterations + 1,
        };

        Assert.NotEmpty(DnssecCanonicalizer.ComputeNsec3Hash("www.example.com", accepted));
        Assert.Empty(DnssecCanonicalizer.ComputeNsec3Hash("www.example.com", rejected));
    }

    [Fact]
    public void GetSignedData_UsesWildcardOwnerWhenRrsigLabelsAreLowerThanOwnerLabels()
    {
        var record = CreateRecord(new DnsARecord { Address = IPAddress.Parse("192.0.2.10") }, "www.example.com", DnsQueryType.A);
        var signature = CreateRecord(
            new DnsRrsigRecord
            {
                TypeCovered = DnsQueryType.A,
                Algorithm = 8,
                Labels = 2,
                OriginalTtl = 3600,
                SignatureExpiration = 1,
                SignatureInception = 0,
                KeyTag = 1,
                SignerName = "example.com",
            },
            "www.example.com",
            DnsQueryType.RRSIG);

        var signedData = DnssecCanonicalizer.GetSignedData([record], signature);

        Assert.Contains((byte)'*', signedData);
    }

    private sealed class DnssecTestFixture
    {
        private DnssecTestFixture(
            IReadOnlyDictionary<QueryKey, DnsResponseMessage> responses,
            IReadOnlyList<DnssecTrustAnchor> trustAnchors,
            TimeProvider timeProvider,
            DnsResponseMessage positiveAResponse,
            DnsResponseMessage cnameResponse,
            DnsResponseMessage nxDomainResponse,
            DnsResponseMessage noDataResponse,
            DnsResponseMessage insecureUnsignedResponse,
            DnsResponseMessage expiredSignatureResponse,
            DnsResponseMessage crossZoneSignedResponse,
            DnsResponseMessage unsupportedAlgorithmResponse)
        {
            Responses = responses;
            TrustAnchors = trustAnchors;
            TimeProvider = timeProvider;
            PositiveAResponse = positiveAResponse;
            CnameResponse = cnameResponse;
            NxDomainResponse = nxDomainResponse;
            NoDataResponse = noDataResponse;
            InsecureUnsignedResponse = insecureUnsignedResponse;
            ExpiredSignatureResponse = expiredSignatureResponse;
            CrossZoneSignedResponse = crossZoneSignedResponse;
            UnsupportedAlgorithmResponse = unsupportedAlgorithmResponse;
        }

        private IReadOnlyDictionary<QueryKey, DnsResponseMessage> Responses { get; }

        private IReadOnlyList<DnssecTrustAnchor> TrustAnchors { get; }

        private TimeProvider TimeProvider { get; }

        public DnsResponseMessage PositiveAResponse { get; }

        public DnsResponseMessage CnameResponse { get; }

        public DnsResponseMessage NxDomainResponse { get; }

        public DnsResponseMessage NoDataResponse { get; }

        public DnsResponseMessage InsecureUnsignedResponse { get; }

        public DnsResponseMessage ExpiredSignatureResponse { get; }

        public DnsResponseMessage CrossZoneSignedResponse { get; }

        public DnsResponseMessage UnsupportedAlgorithmResponse { get; }

        public static DnssecTestFixture Create(bool dsMismatch = false, ushort? exampleKeyFlags = null)
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var timeProvider = new FixedTimeProvider(now);

            using var rootRsa = RSA.Create(2048);
            using var comRsa = RSA.Create(2048);
            using var exampleRsa = RSA.Create(2048);
            using var attackerRsa = RSA.Create(2048);

            var rootKey = CreateDnskey(".", rootRsa);
            var comKey = CreateDnskey("com", comRsa);
            var exampleKey = CreateDnskey("example.com", exampleRsa);
            var attackerKey = CreateDnskey("attacker.example.com", attackerRsa);
            if (exampleKeyFlags is not null)
            {
                exampleKey.Flags = exampleKeyFlags.Value;
            }

            var trustAnchor = new DnssecTrustAnchor(".", DnssecCanonicalizer.ComputeKeyTag(rootKey), rootKey.Algorithm, 2, DnssecCanonicalizer.ComputeDigest(".", rootKey, digestType: 2));

            var responses = new Dictionary<QueryKey, DnsResponseMessage>();
            var rootDnskeySignature = CreateSignature([rootKey], DnsQueryType.DNSKEY, ".", rootKey, rootRsa, now);
            responses.Add(new(".", DnsQueryType.DNSKEY), CreateMessage(".", DnsQueryType.DNSKEY, [rootKey, rootDnskeySignature]));

            var comDs = CreateDs("com", comKey);
            var comDsSignature = CreateSignature([comDs], DnsQueryType.DS, ".", rootKey, rootRsa, now);
            responses.Add(new("com", DnsQueryType.DS), CreateMessage("com", DnsQueryType.DS, [comDs, comDsSignature]));

            var comDnskeySignature = CreateSignature([comKey], DnsQueryType.DNSKEY, "com", comKey, comRsa, now);
            responses.Add(new("com", DnsQueryType.DNSKEY), CreateMessage("com", DnsQueryType.DNSKEY, [comKey, comDnskeySignature]));

            var exampleDs = CreateDs("example.com", exampleKey);
            if (dsMismatch)
            {
                exampleDs.Digest = SHA256.HashData("wrong digest"u8);
            }

            var exampleDsSignature = CreateSignature([exampleDs], DnsQueryType.DS, "com", comKey, comRsa, now);
            responses.Add(new("example.com", DnsQueryType.DS), CreateMessage("example.com", DnsQueryType.DS, [exampleDs, exampleDsSignature]));

            var exampleDnskeySignature = CreateSignature([exampleKey], DnsQueryType.DNSKEY, "example.com", exampleKey, exampleRsa, now);
            responses.Add(new("example.com", DnsQueryType.DNSKEY), CreateMessage("example.com", DnsQueryType.DNSKEY, [exampleKey, exampleDnskeySignature]));

            var attackerDs = CreateDs("attacker.example.com", attackerKey);
            var attackerDsSignature = CreateSignature([attackerDs], DnsQueryType.DS, "example.com", exampleKey, exampleRsa, now);
            responses.Add(new("attacker.example.com", DnsQueryType.DS), CreateMessage("attacker.example.com", DnsQueryType.DS, [attackerDs, attackerDsSignature]));

            var attackerDnskeySignature = CreateSignature([attackerKey], DnsQueryType.DNSKEY, "attacker.example.com", attackerKey, attackerRsa, now);
            responses.Add(new("attacker.example.com", DnsQueryType.DNSKEY), CreateMessage("attacker.example.com", DnsQueryType.DNSKEY, [attackerKey, attackerDnskeySignature]));

            var unsignedDsDenial = CreateNsec("unsigned.com", "z.com", [DnsQueryType.NS, DnsQueryType.NSEC, DnsQueryType.RRSIG]);
            var unsignedDsDenialSignature = CreateSignature([unsignedDsDenial], DnsQueryType.NSEC, "com", comKey, comRsa, now);
            responses.Add(new("unsigned.com", DnsQueryType.DS), CreateMessage("unsigned.com", DnsQueryType.DS, [], [unsignedDsDenial, unsignedDsDenialSignature]));

            var a = CreateRecord(new DnsARecord { Address = IPAddress.Parse("192.0.2.1") }, "www.example.com", DnsQueryType.A);
            var aSignature = CreateSignature([a], DnsQueryType.A, "example.com", exampleKey, exampleRsa, now);
            var positiveAResponse = CreateMessage("www.example.com", DnsQueryType.A, [a, aSignature]);
            var crossZoneSignature = CreateSignature([a], DnsQueryType.A, "attacker.example.com", attackerKey, attackerRsa, now);
            var crossZoneSignedResponse = CreateMessage("www.example.com", DnsQueryType.A, [a, crossZoneSignature]);

            var alias = CreateRecord(new DnsCnameRecord { CanonicalName = "www.example.com" }, "alias.example.com", DnsQueryType.CNAME);
            var aliasSignature = CreateSignature([alias], DnsQueryType.CNAME, "example.com", exampleKey, exampleRsa, now);
            var cnameResponse = CreateMessage("alias.example.com", DnsQueryType.A, [alias, aliasSignature, a, aSignature]);

            var nxDomainNsec = CreateNsec("a.example.com", "z.example.com", [DnsQueryType.NSEC, DnsQueryType.RRSIG]);
            var nxDomainNsecSignature = CreateSignature([nxDomainNsec], DnsQueryType.NSEC, "example.com", exampleKey, exampleRsa, now);
            var nxDomainResponse = CreateMessage("missing.example.com", DnsQueryType.A, [], [nxDomainNsec, nxDomainNsecSignature], DnsResponseCode.NameError);

            var noDataNsec = CreateNsec("www.example.com", "z.example.com", [DnsQueryType.A, DnsQueryType.NSEC, DnsQueryType.RRSIG]);
            var noDataNsecSignature = CreateSignature([noDataNsec], DnsQueryType.NSEC, "example.com", exampleKey, exampleRsa, now);
            var noDataResponse = CreateMessage("www.example.com", DnsQueryType.MX, [], [noDataNsec, noDataNsecSignature]);

            var unsignedA = CreateRecord(new DnsARecord { Address = IPAddress.Parse("192.0.2.200") }, "unsigned.com", DnsQueryType.A);
            var insecureUnsignedResponse = CreateMessage("unsigned.com", DnsQueryType.A, [unsignedA]);

            var expiredSignature = CreateSignature([a], DnsQueryType.A, "example.com", exampleKey, exampleRsa, now, TimeSpan.FromHours(-3), TimeSpan.FromHours(-2));
            var expiredSignatureResponse = CreateMessage("www.example.com", DnsQueryType.A, [a, expiredSignature]);

            var unsupportedSignature = CreateUnsupportedSignature(a, exampleKey, now);
            var unsupportedAlgorithmResponse = CreateMessage("www.example.com", DnsQueryType.A, [a, unsupportedSignature]);

            return new(responses, [trustAnchor], timeProvider, positiveAResponse, cnameResponse, nxDomainResponse, noDataResponse, insecureUnsignedResponse, expiredSignatureResponse, crossZoneSignedResponse, unsupportedAlgorithmResponse);
        }

        public async Task<DnssecValidationResult> ValidateAsync(DnsResponseMessage response)
        {
            var validator = new DnssecValidator(ResolveAsync, TrustAnchors, TimeProvider);
            return await validator.ValidateAsync(response, CancellationToken.None);
        }

        private Task<DnsResponseMessage> ResolveAsync(DnsQueryMessage query, CancellationToken cancellationToken)
        {
            var question = query.Questions[0];
            var key = new QueryKey(DnssecCanonicalizer.ToDisplayName(question.Name), question.Type);
            return Task.FromResult(Responses[key]);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private readonly record struct QueryKey(string Name, DnsQueryType Type);

    private static DnsDnskeyRecord CreateDnskey(string name, RSA rsa)
    {
        return CreateRecord(
            new DnsDnskeyRecord
            {
                Flags = 257,
                Protocol = 3,
                Algorithm = 8,
                PublicKey = ExportDnskeyPublicKey(rsa),
            },
            name,
            DnsQueryType.DNSKEY);
    }

    private static byte[] ExportDnskeyPublicKey(RSA rsa)
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var exponent = parameters.Exponent!;
        var modulus = parameters.Modulus!;
        var lengthPrefix = exponent.Length < 256
            ? new[] { (byte)exponent.Length }
            : [0, (byte)(exponent.Length >> 8), (byte)exponent.Length];

        return [.. lengthPrefix, .. exponent, .. modulus];
    }

    private static DnsDsRecord CreateDs(string name, DnsDnskeyRecord key)
    {
        return CreateRecord(
            new DnsDsRecord
            {
                KeyTag = DnssecCanonicalizer.ComputeKeyTag(key),
                Algorithm = key.Algorithm,
                DigestType = 2,
                Digest = DnssecCanonicalizer.ComputeDigest(name, key, digestType: 2),
            },
            name,
            DnsQueryType.DS);
    }

    private static DnsNsecRecord CreateNsec(string name, string nextName, IReadOnlyList<DnsQueryType> types)
    {
        return CreateRecord(
            new DnsNsecRecord
            {
                NextDomainName = nextName,
                TypeBitMaps = types,
            },
            name,
            DnsQueryType.NSEC);
    }

    private static DnsRrsigRecord CreateSignature(
        IReadOnlyList<DnsRecord> rrset,
        DnsQueryType typeCovered,
        string signerName,
        DnsDnskeyRecord signerKey,
        RSA signerRsa,
        DateTimeOffset now,
        TimeSpan? inceptionOffset = null,
        TimeSpan? expirationOffset = null)
    {
        var signature = CreateRecord(
            new DnsRrsigRecord
            {
                TypeCovered = typeCovered,
                Algorithm = signerKey.Algorithm,
                Labels = (byte)DnssecCanonicalizer.CountLabels(rrset[0].Name),
                OriginalTtl = rrset[0].TimeToLive,
                SignatureExpiration = checked((uint)now.Add(expirationOffset ?? TimeSpan.FromHours(1)).ToUnixTimeSeconds()),
                SignatureInception = checked((uint)now.Add(inceptionOffset ?? TimeSpan.FromHours(-1)).ToUnixTimeSeconds()),
                KeyTag = DnssecCanonicalizer.ComputeKeyTag(signerKey),
                SignerName = signerName,
            },
            rrset[0].Name,
            DnsQueryType.RRSIG);

        signature.Signature = signerRsa.SignData(DnssecCanonicalizer.GetSignedData(rrset, signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signature;
    }

    private static DnsRrsigRecord CreateUnsupportedSignature(DnsRecord record, DnsDnskeyRecord signerKey, DateTimeOffset now)
    {
        return CreateRecord(
            new DnsRrsigRecord
            {
                TypeCovered = record.RecordType,
                Algorithm = 15,
                Labels = (byte)DnssecCanonicalizer.CountLabels(record.Name),
                OriginalTtl = record.TimeToLive,
                SignatureExpiration = checked((uint)now.AddHours(1).ToUnixTimeSeconds()),
                SignatureInception = checked((uint)now.AddHours(-1).ToUnixTimeSeconds()),
                KeyTag = DnssecCanonicalizer.ComputeKeyTag(signerKey),
                SignerName = "example.com",
                Signature = [1, 2, 3],
            },
            record.Name,
            DnsQueryType.RRSIG);
    }

    private static DnsResponseMessage CreateMessage(
        string questionName,
        DnsQueryType questionType,
        IReadOnlyList<DnsRecord> answers,
        IReadOnlyList<DnsRecord>? authorities = null,
        DnsResponseCode responseCode = DnsResponseCode.NoError)
    {
        authorities ??= [];
        var header = new DnsResponseHeader
        {
            Id = 1,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
            ResponseCode = responseCode,
            QuestionCount = 1,
            AnswerCount = (ushort)answers.Count,
            AuthorityCount = (ushort)authorities.Count,
        };

        return new(header)
        {
            Questions = [new DnsQuestion(questionName, questionType)],
            Answers = answers,
            Authorities = authorities,
        };
    }

    private static T CreateRecord<T>(T record, string name, DnsQueryType type)
        where T : DnsRecord
    {
        record.Name = DnssecCanonicalizer.ToDisplayName(name);
        record.RecordType = type;
        record.RecordClass = DnsQueryClass.IN;
        record.TimeToLive = 3600;
        return record;
    }
}
