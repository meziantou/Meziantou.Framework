using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Internal;

internal sealed class DnssecValidator
{
    private readonly Func<DnsQueryMessage, CancellationToken, Task<DnsResponseMessage>> _queryAsync;
    private readonly IReadOnlyList<DnssecTrustAnchor> _trustAnchors;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, KeyValidationResult> _keyCache = new(StringComparer.Ordinal);

    public DnssecValidator(
        Func<DnsQueryMessage, CancellationToken, Task<DnsResponseMessage>> queryAsync,
        IReadOnlyList<DnssecTrustAnchor> trustAnchors,
        TimeProvider timeProvider)
    {
        _queryAsync = queryAsync;
        _trustAnchors = trustAnchors;
        _timeProvider = timeProvider;
    }

    public async Task<DnssecValidationResult> ValidateAsync(DnsResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Header.IsTruncated)
        {
            return CreateResult(DnssecValidationStatus.Indeterminate, new DnssecValidationIssue(DnssecValidationIssueCode.TruncatedResponse, "The DNS response is truncated."));
        }

        if (response.Questions.Count is 0)
        {
            return CreateResult(DnssecValidationStatus.Indeterminate, new DnssecValidationIssue(DnssecValidationIssueCode.MissingQuestion, "The DNS response does not contain a question."));
        }

        var question = response.Questions[0];
        if (response.Header.ResponseCode is DnsResponseCode.NameError)
        {
            var denial = await ValidateDenialAsync(response, question.Name, question.Type, knownKeys: null, cancellationToken).ConfigureAwait(false);
            return ToResult(denial);
        }

        if (response.Header.ResponseCode is not DnsResponseCode.NoError)
        {
            return CreateResult(DnssecValidationStatus.Indeterminate, new DnssecValidationIssue(DnssecValidationIssueCode.InvalidData, $"DNSSEC validation is not defined for response code {response.Header.ResponseCode}.", question.Name, question.Type));
        }

        var answerRrsets = GroupRrsets(response.Answers).ToArray();
        if (answerRrsets.Length is 0)
        {
            var denial = await ValidateDenialAsync(response, question.Name, question.Type, knownKeys: null, cancellationToken).ConfigureAwait(false);
            return ToResult(denial);
        }

        var outcomes = new List<ValidationOutcome>(answerRrsets.Length);
        foreach (var rrset in answerRrsets)
        {
            outcomes.Add(await ValidateRrsetAsync(rrset.Records, rrset.Signatures, cancellationToken).ConfigureAwait(false));
        }

        return ToResult(CombineAll(outcomes));
    }

    private async Task<ValidationOutcome> ValidateRrsetAsync(IReadOnlyList<DnsRecord> rrset, IReadOnlyList<DnsRrsigRecord> signatures, CancellationToken cancellationToken)
    {
        if (rrset.Count is 0)
            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.MissingRecord, "The RRset is empty."));

        var record = rrset[0];
        if (signatures.Count is 0)
        {
            var insecure = await FindInsecureDelegationAsync(record.Name, cancellationToken).ConfigureAwait(false);
            if (insecure.Status is DnssecValidationStatus.Insecure)
                return insecure;

            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.MissingRrsig, "The RRset does not contain a matching RRSIG.", record.Name, record.RecordType));
        }

        var outcomes = new List<ValidationOutcome>(signatures.Count);
        foreach (var signature in signatures)
        {
            var keys = await GetValidatedDnsKeysAsync(signature.SignerName, cancellationToken).ConfigureAwait(false);
            if (keys.Status is not DnssecValidationStatus.Secure)
            {
                outcomes.Add(ValidationOutcome.From(keys.Status, keys.Issues));
                continue;
            }

            outcomes.Add(VerifyRrsetWithKeys(rrset, [signature], keys.Keys));
        }

        return CombineAny(outcomes);
    }

    private async Task<KeyValidationResult> GetValidatedDnsKeysAsync(string zoneName, CancellationToken cancellationToken)
    {
        var normalizedZoneName = DnssecCanonicalizer.NormalizeName(zoneName);
        if (_keyCache.TryGetValue(normalizedZoneName, out var cachedResult))
            return cachedResult;

        KeyValidationResult result;
        if (normalizedZoneName.Length is 0)
        {
            result = await ValidateRootDnsKeysAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await ValidateDelegatedDnsKeysAsync(normalizedZoneName, cancellationToken).ConfigureAwait(false);
        }

        _keyCache[normalizedZoneName] = result;
        return result;
    }

    private async Task<KeyValidationResult> ValidateRootDnsKeysAsync(CancellationToken cancellationToken)
    {
        var response = await QueryAsync(".", DnsQueryType.DNSKEY, cancellationToken).ConfigureAwait(false);
        var keys = GetRecords<DnsDnskeyRecord>(response.Answers, "").ToArray();
        if (keys.Length is 0)
            return KeyValidationResult.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDnskey, "The root DNSKEY RRset is missing.", ".", DnsQueryType.DNSKEY));

        var trustedKeys = keys
            .Where(key => _trustAnchors.Any(anchor => IsTrustAnchorMatch(anchor, "", key)))
            .ToArray();
        if (trustedKeys.Length is 0)
            return KeyValidationResult.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDs, "No configured trust anchor matches the root DNSKEY RRset.", ".", DnsQueryType.DNSKEY));

        var signatures = GetSignatures(response.Answers, "", DnsQueryType.DNSKEY).ToArray();
        var validation = VerifyRrsetWithKeys(keys, signatures, trustedKeys);
        return validation.Status is DnssecValidationStatus.Secure
            ? KeyValidationResult.Secure(keys)
            : KeyValidationResult.From(validation.Status, validation.Issues);
    }

    private async Task<KeyValidationResult> ValidateDelegatedDnsKeysAsync(string zoneName, CancellationToken cancellationToken)
    {
        var parentName = DnssecCanonicalizer.GetParentName(zoneName);
        var parentKeys = await GetValidatedDnsKeysAsync(parentName, cancellationToken).ConfigureAwait(false);
        if (parentKeys.Status is DnssecValidationStatus.Insecure)
            return parentKeys;

        if (parentKeys.Status is not DnssecValidationStatus.Secure)
            return parentKeys;

        var dsResponse = await QueryAsync(zoneName, DnsQueryType.DS, cancellationToken).ConfigureAwait(false);
        var dsRecords = GetRecords<DnsDsRecord>(dsResponse.Answers, zoneName).ToArray();
        if (dsRecords.Length is 0)
        {
            var denial = await ValidateDenialAsync(dsResponse, zoneName, DnsQueryType.DS, parentKeys.Keys, cancellationToken).ConfigureAwait(false);
            return denial.Status is DnssecValidationStatus.Secure
                ? KeyValidationResult.Insecure(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDs, "The delegation is authenticated as unsigned.", zoneName, DnsQueryType.DS))
                : KeyValidationResult.From(denial.Status, denial.Issues);
        }

        var dsSignatures = GetSignatures(dsResponse.Answers, zoneName, DnsQueryType.DS).ToArray();
        var dsValidation = VerifyRrsetWithKeys(dsRecords, dsSignatures, parentKeys.Keys);
        if (dsValidation.Status is not DnssecValidationStatus.Secure)
            return KeyValidationResult.From(dsValidation.Status, dsValidation.Issues);

        var dnskeyResponse = await QueryAsync(zoneName, DnsQueryType.DNSKEY, cancellationToken).ConfigureAwait(false);
        var keys = GetRecords<DnsDnskeyRecord>(dnskeyResponse.Answers, zoneName).ToArray();
        if (keys.Length is 0)
            return KeyValidationResult.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDnskey, "The DNSKEY RRset is missing.", zoneName, DnsQueryType.DNSKEY));

        var digestIssues = new List<DnssecValidationIssue>();
        var matchingKeys = keys.Where(key => IsDsMatch(zoneName, key, dsRecords, digestIssues)).ToArray();
        if (matchingKeys.Length is 0)
        {
            return digestIssues.Count is 0
                ? KeyValidationResult.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.DigestMismatch, "No DNSKEY matches the DS RRset.", zoneName, DnsQueryType.DNSKEY))
                : KeyValidationResult.Bogus(digestIssues);
        }

        var dnskeySignatures = GetSignatures(dnskeyResponse.Answers, zoneName, DnsQueryType.DNSKEY).ToArray();
        var dnskeyValidation = VerifyRrsetWithKeys(keys, dnskeySignatures, matchingKeys);
        return dnskeyValidation.Status is DnssecValidationStatus.Secure
            ? KeyValidationResult.Secure(keys)
            : KeyValidationResult.From(dnskeyValidation.Status, dnskeyValidation.Issues);
    }

    private async Task<ValidationOutcome> FindInsecureDelegationAsync(string name, CancellationToken cancellationToken)
    {
        var zoneName = DnssecCanonicalizer.NormalizeName(name);
        while (zoneName.Length > 0)
        {
            var parentName = DnssecCanonicalizer.GetParentName(zoneName);
            var parentKeys = await GetValidatedDnsKeysAsync(parentName, cancellationToken).ConfigureAwait(false);
            if (parentKeys.Status is DnssecValidationStatus.Secure)
            {
                var dsResponse = await QueryAsync(zoneName, DnsQueryType.DS, cancellationToken).ConfigureAwait(false);
                var dsRecords = GetRecords<DnsDsRecord>(dsResponse.Answers, zoneName).ToArray();
                if (dsRecords.Length is 0)
                {
                    var denial = await ValidateDenialAsync(dsResponse, zoneName, DnsQueryType.DS, parentKeys.Keys, cancellationToken).ConfigureAwait(false);
                    if (denial.Status is DnssecValidationStatus.Secure)
                        return ValidationOutcome.Insecure(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDs, "The closest delegation is authenticated as unsigned.", zoneName, DnsQueryType.DS));
                }
            }
            else if (parentKeys.Status is DnssecValidationStatus.Insecure)
            {
                return ValidationOutcome.Insecure(parentKeys.Issues);
            }
            else
            {
                return ValidationOutcome.From(parentKeys.Status, parentKeys.Issues);
            }

            zoneName = parentName;
        }

        return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.TrustChainIncomplete, "No authenticated insecure delegation was found.", name));
    }

    private async Task<ValidationOutcome> ValidateDenialAsync(DnsResponseMessage response, string questionName, DnsQueryType questionType, IReadOnlyList<DnsDnskeyRecord>? knownKeys, CancellationToken cancellationToken)
    {
        var nsecRrsets = GroupRrsets(response.Authorities)
            .Where(rrset => rrset.Type is DnsQueryType.NSEC or DnsQueryType.NSEC3)
            .ToArray();
        if (nsecRrsets.Length is 0)
        {
            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "No NSEC or NSEC3 denial records were present.", questionName, questionType));
        }

        var outcomes = new List<ValidationOutcome>(nsecRrsets.Length);
        var proofFound = false;

        foreach (var rrset in nsecRrsets)
        {
            var validation = knownKeys is null
                ? await ValidateRrsetAsync(rrset.Records, rrset.Signatures, cancellationToken).ConfigureAwait(false)
                : VerifyRrsetWithKeys(rrset.Records, rrset.Signatures, knownKeys);
            outcomes.Add(validation);

            if (validation.Status is DnssecValidationStatus.Secure && ProvesDenial(response.Header.ResponseCode, questionName, questionType, rrset.Records))
            {
                proofFound = true;
            }
        }

        var combined = CombineAll(outcomes);
        if (combined.Status is not DnssecValidationStatus.Secure)
            return combined;

        return proofFound
            ? ValidationOutcome.Secure()
            : ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "The denial records are signed but do not prove the requested denial.", questionName, questionType));
    }

    private ValidationOutcome VerifyRrsetWithKeys(IReadOnlyList<DnsRecord> rrset, IReadOnlyList<DnsRrsigRecord> signatures, IReadOnlyList<DnsDnskeyRecord> keys)
    {
        if (rrset.Count is 0)
            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.MissingRecord, "The RRset is empty."));

        var record = rrset[0];
        if (signatures.Count is 0)
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.MissingRrsig, "The RRset does not contain a matching RRSIG.", record.Name, record.RecordType));

        var outcomes = new List<ValidationOutcome>(signatures.Count);
        foreach (var signature in signatures)
        {
            outcomes.Add(VerifySignature(rrset, signature, keys));
        }

        return CombineAny(outcomes);
    }

    private ValidationOutcome VerifySignature(IReadOnlyList<DnsRecord> rrset, DnsRrsigRecord signature, IReadOnlyList<DnsDnskeyRecord> keys)
    {
        var record = rrset[0];
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (!DnssecCanonicalizer.IsAncestorOrEqual(signature.SignerName, record.Name))
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidData, "The RRSIG signer name is not an ancestor of the RRset owner name.", record.Name, record.RecordType));
        }

        if (now < signature.SignatureInception)
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.SignatureNotYetValid, "The RRSIG inception time is in the future.", record.Name, record.RecordType));
        }

        if (now > signature.SignatureExpiration)
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.SignatureExpired, "The RRSIG has expired.", record.Name, record.RecordType));
        }

        if (!DnssecCrypto.IsSupportedAlgorithm(signature.Algorithm))
        {
            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.UnsupportedAlgorithm, $"DNSSEC algorithm {signature.Algorithm} is not supported.", record.Name, record.RecordType));
        }

        var candidateKeys = keys
            .Where(key => IsDnskeyUsableForZoneSigning(key) && key.Algorithm == signature.Algorithm && DnssecCanonicalizer.ComputeKeyTag(key) == signature.KeyTag)
            .ToArray();
        if (candidateKeys.Length is 0)
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDnskey, "No DNSKEY matches the RRSIG key tag and algorithm.", record.Name, record.RecordType));
        }

        var signedData = DnssecCanonicalizer.GetSignedData(rrset, signature);
        foreach (var key in candidateKeys)
        {
            var verification = DnssecCrypto.VerifySignature(key, signedData, signature.Signature);
            if (verification is DnssecSignatureVerificationStatus.Valid)
                return ValidationOutcome.Secure();

            if (verification is DnssecSignatureVerificationStatus.UnsupportedAlgorithm)
            {
                return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.UnsupportedAlgorithm, $"DNSSEC algorithm {key.Algorithm} is not supported.", record.Name, record.RecordType));
            }
        }

        return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.SignatureVerificationFailed, "No RRSIG signature could be verified with the matching DNSKEY.", record.Name, record.RecordType));
    }

    private async Task<DnsResponseMessage> QueryAsync(string name, DnsQueryType type, CancellationToken cancellationToken)
    {
        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
            CheckingDisabled = true,
            EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = 4096,
                DnssecOk = true,
            },
        };
        query.Questions.Add(new DnsQuestion(DnssecCanonicalizer.ToDisplayName(name), type));

        return await _queryAsync(query, cancellationToken).ConfigureAwait(false);
    }

    private static bool ProvesDenial(DnsResponseCode responseCode, string questionName, DnsQueryType questionType, IReadOnlyList<DnsRecord> records)
    {
        foreach (var record in records)
        {
            if (record is DnsNsecRecord nsecRecord && NsecProvesDenial(responseCode, questionName, questionType, nsecRecord))
                return true;

            if (record is DnsNsec3Record nsec3Record && Nsec3ProvesDenial(responseCode, questionName, questionType, nsec3Record))
                return true;
        }

        return false;
    }

    private static bool NsecProvesDenial(DnsResponseCode responseCode, string questionName, DnsQueryType questionType, DnsNsecRecord record)
    {
        var ownerName = DnssecCanonicalizer.NormalizeName(record.Name);
        var normalizedQuestionName = DnssecCanonicalizer.NormalizeName(questionName);

        if (ownerName == normalizedQuestionName)
        {
            return !record.TypeBitMaps.Contains(questionType) && !record.TypeBitMaps.Contains(DnsQueryType.CNAME);
        }

        return responseCode is DnsResponseCode.NameError && DnssecCanonicalizer.NsecCovers(ownerName, record.NextDomainName, normalizedQuestionName);
    }

    private static bool Nsec3ProvesDenial(DnsResponseCode responseCode, string questionName, DnsQueryType questionType, DnsNsec3Record record)
    {
        var ownerHashLabel = DnssecCanonicalizer.NormalizeName(record.Name).Split('.')[0];
        var ownerHash = DnssecCanonicalizer.DecodeBase32Hex(ownerHashLabel);
        if (ownerHash.Length is 0)
            return false;

        var questionHash = DnssecCanonicalizer.ComputeNsec3Hash(questionName, record);
        if (questionHash.Length is 0)
            return false;

        if (ownerHash.AsSpan().SequenceEqual(questionHash))
            return !record.TypeBitMaps.Contains(questionType) && !record.TypeBitMaps.Contains(DnsQueryType.CNAME);

        return responseCode is DnsResponseCode.NameError && DnssecCanonicalizer.Nsec3Covers(ownerHash, record.NextHashedOwnerName, questionHash);
    }

    private static IEnumerable<DnsRecordRrset> GroupRrsets(IReadOnlyList<DnsRecord> records)
    {
        return records
            .Where(record => record is not DnsRrsigRecord and not DnsOptRecord)
            .GroupBy(record => new RrsetKey(DnssecCanonicalizer.NormalizeName(record.Name), record.RecordType, record.RecordClass))
            .Select(group => new DnsRecordRrset(
                group.Key.Name,
                group.Key.Type,
                group.Key.RecordClass,
                group.ToArray(),
                GetSignatures(records, group.Key.Name, group.Key.Type).ToArray()));
    }

    private static IEnumerable<T> GetRecords<T>(IReadOnlyList<DnsRecord> records, string name)
        where T : DnsRecord
    {
        var normalizedName = DnssecCanonicalizer.NormalizeName(name);
        return records.OfType<T>().Where(record => DnssecCanonicalizer.NormalizeName(record.Name) == normalizedName);
    }

    private static IEnumerable<DnsRrsigRecord> GetSignatures(IReadOnlyList<DnsRecord> records, string name, DnsQueryType type)
    {
        var normalizedName = DnssecCanonicalizer.NormalizeName(name);
        return records
            .OfType<DnsRrsigRecord>()
            .Where(record => record.TypeCovered == type && DnssecCanonicalizer.NormalizeName(record.Name) == normalizedName);
    }

    private static bool IsTrustAnchorMatch(DnssecTrustAnchor anchor, string ownerName, DnsDnskeyRecord key)
    {
        return IsDnskeyUsableForZoneSigning(key)
            && DnssecCanonicalizer.NormalizeName(anchor.Name) == DnssecCanonicalizer.NormalizeName(ownerName)
            && anchor.KeyTag == DnssecCanonicalizer.ComputeKeyTag(key)
            && anchor.Algorithm == key.Algorithm
            && DnssecCanonicalizer.IsSupportedDigest(anchor.DigestType)
            && DnssecCanonicalizer.ComputeDigest(ownerName, key, anchor.DigestType).AsSpan().SequenceEqual(anchor.Digest);
    }

    private static bool IsDsMatch(string ownerName, DnsDnskeyRecord key, IReadOnlyList<DnsDsRecord> dsRecords, List<DnssecValidationIssue> issues)
    {
        if (!IsDnskeyUsableForZoneSigning(key))
            return false;

        foreach (var ds in dsRecords)
        {
            if (ds.KeyTag != DnssecCanonicalizer.ComputeKeyTag(key) || ds.Algorithm != key.Algorithm)
                continue;

            if (!DnssecCanonicalizer.IsSupportedDigest(ds.DigestType))
            {
                issues.Add(new DnssecValidationIssue(DnssecValidationIssueCode.UnsupportedDigest, $"DS digest type {ds.DigestType} is not supported.", ownerName, DnsQueryType.DS));
                continue;
            }

            if (DnssecCanonicalizer.ComputeDigest(ownerName, key, ds.DigestType).AsSpan().SequenceEqual(ds.Digest))
                return true;

            issues.Add(new DnssecValidationIssue(DnssecValidationIssueCode.DigestMismatch, "The DS digest does not match the DNSKEY.", ownerName, DnsQueryType.DNSKEY));
        }

        return false;
    }

    private static bool IsDnskeyUsableForZoneSigning(DnsDnskeyRecord key)
    {
        const ushort ZoneKeyFlag = 0x0100;
        const ushort RevokeFlag = 0x0080;

        return key.Protocol is 3
            && (key.Flags & ZoneKeyFlag) is ZoneKeyFlag
            && (key.Flags & RevokeFlag) is 0;
    }

    private static DnssecValidationResult ToResult(ValidationOutcome outcome)
    {
        return new(outcome.Status, outcome.Issues);
    }

    private static DnssecValidationResult CreateResult(DnssecValidationStatus status, DnssecValidationIssue issue)
    {
        return new(status, [issue]);
    }

    private static ValidationOutcome CombineAny(IReadOnlyList<ValidationOutcome> outcomes)
    {
        if (outcomes.Count is 0)
            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.MissingRecord, "No DNSSEC validation outcomes were produced."));

        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Secure))
            return ValidationOutcome.Secure();

        var issues = outcomes.SelectMany(outcome => outcome.Issues).ToArray();
        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Bogus))
            return ValidationOutcome.From(DnssecValidationStatus.Bogus, issues);

        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Indeterminate))
            return ValidationOutcome.From(DnssecValidationStatus.Indeterminate, issues);

        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Insecure))
            return ValidationOutcome.From(DnssecValidationStatus.Insecure, issues);

        return ValidationOutcome.From(DnssecValidationStatus.NotValidated, issues);
    }

    private static ValidationOutcome CombineAll(IReadOnlyList<ValidationOutcome> outcomes)
    {
        if (outcomes.Count is 0)
            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.MissingRecord, "No DNSSEC validation outcomes were produced."));

        var issues = outcomes.SelectMany(outcome => outcome.Issues).ToArray();
        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Bogus))
            return ValidationOutcome.From(DnssecValidationStatus.Bogus, issues);

        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Indeterminate))
            return ValidationOutcome.From(DnssecValidationStatus.Indeterminate, issues);

        if (outcomes.Any(outcome => outcome.Status is DnssecValidationStatus.Insecure))
            return ValidationOutcome.From(DnssecValidationStatus.Insecure, issues);

        if (outcomes.All(outcome => outcome.Status is DnssecValidationStatus.Secure))
            return ValidationOutcome.Secure();

        return ValidationOutcome.From(DnssecValidationStatus.NotValidated, issues);
    }

    private readonly record struct RrsetKey(string Name, DnsQueryType Type, DnsQueryClass RecordClass);

    private sealed record DnsRecordRrset(string Name, DnsQueryType Type, DnsQueryClass RecordClass, IReadOnlyList<DnsRecord> Records, IReadOnlyList<DnsRrsigRecord> Signatures);

    private sealed class KeyValidationResult
    {
        private KeyValidationResult(DnssecValidationStatus status, IReadOnlyList<DnssecValidationIssue> issues, IReadOnlyList<DnsDnskeyRecord> keys)
        {
            Status = status;
            Issues = issues;
            Keys = keys;
        }

        public DnssecValidationStatus Status { get; }

        public IReadOnlyList<DnssecValidationIssue> Issues { get; }

        public IReadOnlyList<DnsDnskeyRecord> Keys { get; }

        public static KeyValidationResult Secure(IReadOnlyList<DnsDnskeyRecord> keys) => new(DnssecValidationStatus.Secure, [], keys);

        public static KeyValidationResult Insecure(params DnssecValidationIssue[] issues) => new(DnssecValidationStatus.Insecure, issues, []);

        public static KeyValidationResult Insecure(IReadOnlyList<DnssecValidationIssue> issues) => new(DnssecValidationStatus.Insecure, issues, []);

        public static KeyValidationResult Bogus(params DnssecValidationIssue[] issues) => new(DnssecValidationStatus.Bogus, issues, []);

        public static KeyValidationResult Bogus(IReadOnlyList<DnssecValidationIssue> issues) => new(DnssecValidationStatus.Bogus, issues, []);

        public static KeyValidationResult Indeterminate(params DnssecValidationIssue[] issues) => new(DnssecValidationStatus.Indeterminate, issues, []);

        public static KeyValidationResult From(DnssecValidationStatus status, IReadOnlyList<DnssecValidationIssue> issues) => new(status, issues, []);
    }

    private sealed class ValidationOutcome
    {
        private ValidationOutcome(DnssecValidationStatus status, IReadOnlyList<DnssecValidationIssue> issues)
        {
            Status = status;
            Issues = issues;
        }

        public DnssecValidationStatus Status { get; }

        public IReadOnlyList<DnssecValidationIssue> Issues { get; }

        public static ValidationOutcome Secure() => new(DnssecValidationStatus.Secure, []);

        public static ValidationOutcome Insecure(params DnssecValidationIssue[] issues) => new(DnssecValidationStatus.Insecure, issues);

        public static ValidationOutcome Insecure(IReadOnlyList<DnssecValidationIssue> issues) => new(DnssecValidationStatus.Insecure, issues);

        public static ValidationOutcome Bogus(params DnssecValidationIssue[] issues) => new(DnssecValidationStatus.Bogus, issues);

        public static ValidationOutcome Bogus(IReadOnlyList<DnssecValidationIssue> issues) => new(DnssecValidationStatus.Bogus, issues);

        public static ValidationOutcome Indeterminate(params DnssecValidationIssue[] issues) => new(DnssecValidationStatus.Indeterminate, issues);

        public static ValidationOutcome From(DnssecValidationStatus status, IReadOnlyList<DnssecValidationIssue> issues) => new(status, issues);
    }
}
