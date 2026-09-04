using Meziantou.Framework.DnsClient.Protocol;
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
    private readonly ushort _ednsUdpPayloadSize;

    /// <summary>
    /// Caps how many auxiliary queries one validation may issue. A hostile response can carry hundreds of RRSIGs with
    /// unrelated signer names, each of which would otherwise start its own walk to the root.
    /// </summary>
    private int _queryBudget = DefaultQueryBudget;
    private string? _lastQueryFailure;

    private const int DefaultQueryBudget = 48;

    public DnssecValidator(
        Func<DnsQueryMessage, CancellationToken, Task<DnsResponseMessage>> queryAsync,
        IReadOnlyList<DnssecTrustAnchor> trustAnchors,
        TimeProvider timeProvider,
        ushort ednsUdpPayloadSize)
    {
        _queryAsync = queryAsync;
        _trustAnchors = trustAnchors;
        _timeProvider = timeProvider;
        _ednsUdpPayloadSize = ednsUdpPayloadSize;
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
        var denialKind = question.Type is DnsQueryType.DS ? DenialKind.Delegation : DenialKind.NameOrData;

        if (response.Header.ResponseCode is DnsResponseCode.NameError)
        {
            var denial = await ValidateDenialAsync(response, question.Name, question.Type, knownKeys: null, denialKind, cancellationToken).ConfigureAwait(false);
            return ToResult(denial);
        }

        if (response.Header.ResponseCode is not DnsResponseCode.NoError)
        {
            return CreateResult(DnssecValidationStatus.Indeterminate, new DnssecValidationIssue(DnssecValidationIssueCode.InvalidData, $"DNSSEC validation is not defined for response code {response.Header.ResponseCode}.", question.Name, question.Type));
        }

        var answerRrsets = GroupRrsets(response.Answers).ToArray();
        if (answerRrsets.Length is 0)
        {
            var denial = await ValidateDenialAsync(response, question.Name, question.Type, knownKeys: null, denialKind, cancellationToken).ConfigureAwait(false);
            return ToResult(denial);
        }

        // A signed RRset only answers *this* query if it is reachable from the question name, following any CNAME or
        // DNAME hops. Without this an attacker could return a genuinely signed RRset from a zone they control and have
        // it reported as Secure.
        var relevant = SelectRrsetsAnsweringQuestion(answerRrsets, question);
        if (relevant.Count is 0)
        {
            return CreateResult(DnssecValidationStatus.Bogus, new DnssecValidationIssue(DnssecValidationIssueCode.InvalidData, "No answer RRset corresponds to the question that was asked.", question.Name, question.Type));
        }

        var outcomes = new List<ValidationOutcome>(relevant.Count);
        foreach (var rrset in relevant)
        {
            outcomes.Add(await ValidateRrsetAsync(rrset.Records, rrset.Signatures, cancellationToken).ConfigureAwait(false));

            // RFC 4035 5.3.4: an RRset synthesized from a wildcard needs a denial proving the queried name has no
            // exact match, otherwise a captured wildcard signature can be replayed for a name with real data.
            if (IsWildcardExpansion(rrset))
            {
                outcomes.Add(await ValidateWildcardDenialAsync(response, rrset.Name, rrset.Type, cancellationToken).ConfigureAwait(false));
            }
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
            // Check the signer/owner relationship *before* any network I/O. The signer name is attacker-controlled, so
            // fetching keys for it first would let one response fan out into a walk to the root per bogus signature.
            if (!DnssecCanonicalizer.IsAncestorOrEqual(signature.SignerName, record.Name))
            {
                outcomes.Add(ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidData, "The RRSIG signer name is not an ancestor of the RRset owner name.", record.Name, record.RecordType)));
                continue;
            }

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
        var response = await TryQueryAsync(".", DnsQueryType.DNSKEY, cancellationToken).ConfigureAwait(false);
        if (response is null)
            return KeyValidationResult.Indeterminate(CreateQueryFailureIssue(".", DnsQueryType.DNSKEY));

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

        var dsResponse = await TryQueryAsync(zoneName, DnsQueryType.DS, cancellationToken).ConfigureAwait(false);
        if (dsResponse is null)
            return KeyValidationResult.Indeterminate(CreateQueryFailureIssue(zoneName, DnsQueryType.DS));

        var dsRecords = GetRecords<DnsDsRecord>(dsResponse.Answers, zoneName).ToArray();
        if (dsRecords.Length is 0)
        {
            var denial = await ValidateDenialAsync(dsResponse, zoneName, DnsQueryType.DS, parentKeys.Keys, DenialKind.Delegation, cancellationToken).ConfigureAwait(false);
            return denial.Status is DnssecValidationStatus.Secure
                ? KeyValidationResult.Insecure(new DnssecValidationIssue(DnssecValidationIssueCode.MissingDs, "The delegation is authenticated as unsigned.", zoneName, DnsQueryType.DS))
                : KeyValidationResult.From(denial.Status, denial.Issues);
        }

        var dsSignatures = GetSignatures(dsResponse.Answers, zoneName, DnsQueryType.DS).ToArray();
        var dsValidation = VerifyRrsetWithKeys(dsRecords, dsSignatures, parentKeys.Keys);
        if (dsValidation.Status is not DnssecValidationStatus.Secure)
            return KeyValidationResult.From(dsValidation.Status, dsValidation.Issues);

        var dnskeyResponse = await TryQueryAsync(zoneName, DnsQueryType.DNSKEY, cancellationToken).ConfigureAwait(false);
        if (dnskeyResponse is null)
            return KeyValidationResult.Indeterminate(CreateQueryFailureIssue(zoneName, DnsQueryType.DNSKEY));

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
                var dsResponse = await TryQueryAsync(zoneName, DnsQueryType.DS, cancellationToken).ConfigureAwait(false);
                if (dsResponse is null)
                    return ValidationOutcome.Indeterminate(CreateQueryFailureIssue(zoneName, DnsQueryType.DS));

                var dsRecords = GetRecords<DnsDsRecord>(dsResponse.Answers, zoneName).ToArray();
                if (dsRecords.Length is 0)
                {
                    var denial = await ValidateDenialAsync(dsResponse, zoneName, DnsQueryType.DS, parentKeys.Keys, DenialKind.Delegation, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Validates a denial of existence (NXDOMAIN or NODATA) per RFC 4035 section 5.4 and RFC 5155 section 8.
    /// </summary>
    /// <param name="denialKind">
    /// Whether this proves a name/type does not exist, or only that a secure delegation does not exist. NSEC3 opt-out
    /// records prove the latter but never the former.
    /// </param>
    private async Task<ValidationOutcome> ValidateDenialAsync(
        DnsResponseMessage response,
        string questionName,
        DnsQueryType questionType,
        IReadOnlyList<DnsDnskeyRecord>? knownKeys,
        DenialKind denialKind,
        CancellationToken cancellationToken)
    {
        var nsecRrsets = GroupRrsets(response.Authorities)
            .Where(rrset => rrset.Type is DnsQueryType.NSEC or DnsQueryType.NSEC3)
            .ToArray();
        if (nsecRrsets.Length is 0)
        {
            // An unsigned zone legitimately has no denial records; prove the delegation is unsigned before giving up.
            var insecure = await FindInsecureDelegationAsync(questionName, cancellationToken).ConfigureAwait(false);
            if (insecure.Status is DnssecValidationStatus.Insecure)
                return insecure;

            return ValidationOutcome.Indeterminate(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "No NSEC or NSEC3 denial records were present.", questionName, questionType));
        }

        var outcomes = new List<ValidationOutcome>(nsecRrsets.Length);
        var nsecRecords = new List<DnsNsecRecord>();
        var nsec3Records = new List<DnsNsec3Record>();
        var boundToQuestion = false;

        foreach (var rrset in nsecRrsets)
        {
            // A denial proof only speaks for the zone that signed it. Without this check a signed NSEC/NSEC3 taken
            // from any zone the attacker controls could be replayed to deny the existence of any other name.
            if (!SignsForQuestion(rrset.Signatures, questionName))
                continue;

            var validation = knownKeys is null
                ? await ValidateRrsetAsync(rrset.Records, rrset.Signatures, cancellationToken).ConfigureAwait(false)
                : VerifyRrsetWithKeys(rrset.Records, rrset.Signatures, knownKeys);
            outcomes.Add(validation);
            if (validation.Status is not DnssecValidationStatus.Secure)
                continue;

            boundToQuestion = true;
            foreach (var record in rrset.Records)
            {
                switch (record)
                {
                    case DnsNsecRecord nsec:
                        nsecRecords.Add(nsec);
                        break;

                    case DnsNsec3Record nsec3:
                        nsec3Records.Add(nsec3);
                        break;
                }
            }
        }

        if (!boundToQuestion)
        {
            if (outcomes.Count is 0)
            {
                return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "The denial records are not signed by a zone that is authoritative for the queried name.", questionName, questionType));
            }

            return CombineAll(outcomes);
        }

        var combined = CombineAll(outcomes);
        if (combined.Status is not DnssecValidationStatus.Secure)
            return combined;

        var proven = response.Header.ResponseCode is DnsResponseCode.NameError
            ? ProvesNameError(questionName, nsecRecords, nsec3Records, denialKind)
            : ProvesNoData(questionName, questionType, nsecRecords, nsec3Records, denialKind);

        return proven
            ? ValidationOutcome.Secure()
            : ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "The denial records are signed but do not prove the requested denial.", questionName, questionType));
    }

    /// <summary>Determines whether a wildcard-expanded answer is accompanied by a proof that the queried name has no exact match (RFC 4035 section 5.3.4).</summary>
    private async Task<ValidationOutcome> ValidateWildcardDenialAsync(DnsResponseMessage response, string questionName, DnsQueryType questionType, CancellationToken cancellationToken)
    {
        var nsecRrsets = GroupRrsets(response.Authorities)
            .Where(rrset => rrset.Type is DnsQueryType.NSEC or DnsQueryType.NSEC3)
            .ToArray();
        if (nsecRrsets.Length is 0)
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "The answer was synthesized from a wildcard but the response carries no NSEC or NSEC3 proving the queried name does not exist.", questionName, questionType));
        }

        var outcomes = new List<ValidationOutcome>(nsecRrsets.Length);
        var nsecRecords = new List<DnsNsecRecord>();
        var nsec3Records = new List<DnsNsec3Record>();

        foreach (var rrset in nsecRrsets)
        {
            if (!SignsForQuestion(rrset.Signatures, questionName))
                continue;

            var validation = await ValidateRrsetAsync(rrset.Records, rrset.Signatures, cancellationToken).ConfigureAwait(false);
            outcomes.Add(validation);
            if (validation.Status is not DnssecValidationStatus.Secure)
                continue;

            foreach (var record in rrset.Records)
            {
                switch (record)
                {
                    case DnsNsecRecord nsec:
                        nsecRecords.Add(nsec);
                        break;

                    case DnsNsec3Record nsec3:
                        nsec3Records.Add(nsec3);
                        break;
                }
            }
        }

        var combined = outcomes.Count is 0
            ? ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "The wildcard denial records are not signed by a zone that is authoritative for the queried name.", questionName, questionType))
            : CombineAll(outcomes);
        if (combined.Status is not DnssecValidationStatus.Secure)
            return combined;

        return CoversName(questionName, nsecRecords, nsec3Records, DenialKind.NameOrData)
            ? ValidationOutcome.Secure()
            : ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidDenialProof, "The answer was synthesized from a wildcard but no NSEC or NSEC3 proves the queried name does not exist.", questionName, questionType));
    }

    /// <summary>Determines whether every signature on a denial RRset was made by a zone at or above the queried name.</summary>
    private static bool SignsForQuestion(IReadOnlyList<DnsRrsigRecord> signatures, string questionName)
    {
        return signatures.Count > 0
            && signatures.All(signature => DnssecCanonicalizer.IsAncestorOrEqual(signature.SignerName, questionName));
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
        var now = unchecked((uint)_timeProvider.GetUtcNow().ToUnixTimeSeconds());
        if (!DnssecCanonicalizer.IsAncestorOrEqual(signature.SignerName, record.Name))
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.InvalidData, "The RRSIG signer name is not an ancestor of the RRset owner name.", record.Name, record.RecordType));
        }

        // RFC 4034 3.1.5: the 32-bit timestamps use RFC 1982 serial arithmetic, so they must be compared as signed
        // differences rather than as absolute values. A plain comparison breaks when the field wraps in 2106.
        if (IsBefore(now, signature.SignatureInception))
        {
            return ValidationOutcome.Bogus(new DnssecValidationIssue(DnssecValidationIssueCode.SignatureNotYetValid, "The RRSIG inception time is in the future.", record.Name, record.RecordType));
        }

        if (IsBefore(signature.SignatureExpiration, now))
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

    /// <summary>
    /// Issues an auxiliary query for the chain of trust. Failures are reported as a <see langword="null"/> result so
    /// the caller can produce an Indeterminate verdict: a transient network error while fetching a DS or DNSKEY must
    /// not throw away the answer the caller actually asked for.
    /// </summary>
    /// <summary>Serial-number comparison (RFC 1982): true when <paramref name="left"/> precedes <paramref name="right"/>.</summary>
    private static bool IsBefore(uint left, uint right) => unchecked((int)(left - right)) < 0;

    private async Task<DnsResponseMessage?> TryQueryAsync(string name, DnsQueryType type, CancellationToken cancellationToken)
    {
        if (_queryBudget <= 0)
            return null;

        _queryBudget--;

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
            // CheckingDisabled and DnssecOk are required by the algorithm: we validate locally and therefore need the
            // records themselves rather than the upstream resolver's verdict.
            CheckingDisabled = true,
            EdnsOptions = new DnsEdnsOptions
            {
                UdpPayloadSize = _ednsUdpPayloadSize,
                DnssecOk = true,
            },
        };
        query.Questions.Add(new DnsQuestion(DnssecCanonicalizer.ToDisplayName(name), type));

        try
        {
            return await _queryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the caller's decision and must not be downgraded to a verdict.
            throw;
        }
#pragma warning disable CA1031 // Any failure to fetch a chain record is an Indeterminate verdict, not an exception.
        catch (Exception ex)
        {
            _lastQueryFailure = $"The query for {DnssecCanonicalizer.ToDisplayName(name)} {type} failed: {ex.Message}";
            return null;
        }
#pragma warning restore CA1031
    }

    private DnssecValidationIssue CreateQueryFailureIssue(string name, DnsQueryType type)
    {
        var message = _lastQueryFailure ?? $"The query budget was exhausted before {DnssecCanonicalizer.ToDisplayName(name)} {type} could be resolved.";
        var code = _lastQueryFailure is null ? DnssecValidationIssueCode.QueryBudgetExceeded : DnssecValidationIssueCode.ChainQueryFailed;
        _lastQueryFailure = null;
        return new DnssecValidationIssue(code, message, DnssecCanonicalizer.ToDisplayName(name), type);
    }

    /// <summary>Proves that the name exists but carries no record of the queried type (RFC 4035 section 5.4, RFC 5155 section 8.5/8.6).</summary>
    private static bool ProvesNoData(string questionName, DnsQueryType questionType, IReadOnlyList<DnsNsecRecord> nsecRecords, IReadOnlyList<DnsNsec3Record> nsec3Records, DenialKind denialKind)
    {
        foreach (var nsec in nsecRecords)
        {
            if (DnssecCanonicalizer.CompareCanonicalNames(nsec.Name, questionName) is 0)
                return !nsec.TypeBitMaps.Contains(questionType) && !nsec.TypeBitMaps.Contains(DnsQueryType.CNAME);
        }

        foreach (var nsec3 in nsec3Records)
        {
            if (Nsec3Matches(nsec3, questionName))
                return !nsec3.TypeBitMaps.Contains(questionType) && !nsec3.TypeBitMaps.Contains(DnsQueryType.CNAME);
        }

        // RFC 5155 section 8.6: a NODATA proof for a name covered by an opt-out span is only valid for DS queries.
        if (denialKind is DenialKind.Delegation && questionType is DnsQueryType.DS)
            return CoversName(questionName, nsecRecords, nsec3Records, denialKind);

        return false;
    }

    /// <summary>
    /// Proves that the name does not exist. RFC 4035 section 5.4 requires two proofs: one covering the queried name and
    /// one showing that no wildcard at the closest encloser could have synthesized an answer.
    /// </summary>
    private static bool ProvesNameError(string questionName, IReadOnlyList<DnsNsecRecord> nsecRecords, IReadOnlyList<DnsNsec3Record> nsec3Records, DenialKind denialKind)
    {
        if (!CoversName(questionName, nsecRecords, nsec3Records, denialKind))
            return false;

        var closestEncloser = GetClosestEncloser(questionName, nsecRecords, nsec3Records);
        if (closestEncloser is null)
            return false;

        var wildcard = closestEncloser.Length is 0 ? "*" : "*." + closestEncloser;
        return CoversOrMatchesAbsentWildcard(wildcard, nsecRecords, nsec3Records, denialKind);
    }

    /// <summary>Determines whether some NSEC/NSEC3 proves the name does not exist (its hash or canonical position falls inside a gap).</summary>
    private static bool CoversName(string name, IReadOnlyList<DnsNsecRecord> nsecRecords, IReadOnlyList<DnsNsec3Record> nsec3Records, DenialKind denialKind)
    {
        foreach (var nsec in nsecRecords)
        {
            if (DnssecCanonicalizer.NsecCovers(nsec.Name, nsec.NextDomainName, name))
                return true;
        }

        foreach (var nsec3 in nsec3Records)
        {
            if (!IsOptOutUsable(nsec3, denialKind))
                continue;

            if (Nsec3Covers(nsec3, name))
                return true;
        }

        return false;
    }

    /// <summary>Determines whether the wildcard name is proven absent, either by an exact NSEC/NSEC3 with no relevant types or by a covering gap.</summary>
    private static bool CoversOrMatchesAbsentWildcard(string wildcard, IReadOnlyList<DnsNsecRecord> nsecRecords, IReadOnlyList<DnsNsec3Record> nsec3Records, DenialKind denialKind)
    {
        foreach (var nsec in nsecRecords)
        {
            if (DnssecCanonicalizer.CompareCanonicalNames(nsec.Name, wildcard) is 0)
                return true;
        }

        foreach (var nsec3 in nsec3Records)
        {
            if (Nsec3Matches(nsec3, wildcard))
                return true;
        }

        return CoversName(wildcard, nsecRecords, nsec3Records, denialKind);
    }

    /// <summary>
    /// Determines the closest encloser: the deepest ancestor of <paramref name="questionName"/> that provably exists.
    /// </summary>
    /// <remarks>
    /// For NSEC3 (RFC 5155 section 8.3) it is the deepest ancestor whose hash matches an NSEC3 owner. For NSEC it is
    /// derived from the covering record: the owner and next names both exist in the zone, so the longest suffix either
    /// shares with the queried name is an existing ancestor. Deriving it this way rather than demanding an exact owner
    /// match is what makes minimally-covering NSEC zones (RFC 4470) validate.
    /// </remarks>
    private static string? GetClosestEncloser(string questionName, IReadOnlyList<DnsNsecRecord> nsecRecords, IReadOnlyList<DnsNsec3Record> nsec3Records)
    {
        string? closest = null;

        foreach (var nsec in nsecRecords)
        {
            if (!DnssecCanonicalizer.NsecCovers(nsec.Name, nsec.NextDomainName, questionName))
                continue;

            closest = Deeper(closest, DnssecCanonicalizer.GetLongestCommonSuffix(questionName, nsec.Name));
            closest = Deeper(closest, DnssecCanonicalizer.GetLongestCommonSuffix(questionName, nsec.NextDomainName));
        }

        if (nsec3Records.Count > 0)
        {
            var candidate = DnssecCanonicalizer.GetParentName(DnssecCanonicalizer.NormalizeName(questionName));
            while (true)
            {
                foreach (var nsec3 in nsec3Records)
                {
                    if (Nsec3Matches(nsec3, candidate))
                        return Deeper(closest, candidate) ?? candidate;
                }

                if (candidate.Length is 0)
                    break;

                candidate = DnssecCanonicalizer.GetParentName(candidate);
            }
        }

        // The closest encloser must be a proper ancestor: the queried name itself does not exist.
        if (closest is not null && DnssecCanonicalizer.CountLabels(closest) >= DnssecCanonicalizer.CountLabels(questionName))
            return null;

        return closest;
    }

    private static string? Deeper(string? left, string right)
    {
        if (left is null)
            return right;

        return DnssecCanonicalizer.CountLabels(right) > DnssecCanonicalizer.CountLabels(left) ? right : left;
    }

    private static bool Nsec3Matches(DnsNsec3Record record, string name)
    {
        var ownerHash = GetNsec3OwnerHash(record);
        if (ownerHash.Length is 0)
            return false;

        var hash = DnssecCanonicalizer.ComputeNsec3Hash(name, record);
        return hash.Length > 0 && ownerHash.AsSpan().SequenceEqual(hash);
    }

    private static bool Nsec3Covers(DnsNsec3Record record, string name)
    {
        var ownerHash = GetNsec3OwnerHash(record);
        if (ownerHash.Length is 0)
            return false;

        var hash = DnssecCanonicalizer.ComputeNsec3Hash(name, record);
        return hash.Length > 0 && DnssecCanonicalizer.Nsec3Covers(ownerHash, record.NextHashedOwnerName, hash);
    }

    /// <summary>
    /// RFC 5155 section 6: an NSEC3 with the Opt-Out flag set only proves that no *signed delegation* exists in its
    /// span. Unsigned names may exist there, so it can never prove that a name does not exist.
    /// </summary>
    private static bool IsOptOutUsable(DnsNsec3Record record, DenialKind denialKind)
    {
        const byte OptOutFlag = 0x01;
        return (record.Flags & OptOutFlag) is 0 || denialKind is DenialKind.Delegation;
    }

    private static byte[] GetNsec3OwnerHash(DnsNsec3Record record)
    {
        var label = DnsNameComparer.SplitLabels(DnssecCanonicalizer.NormalizeName(record.Name));
        return label.Length is 0 ? [] : DnssecCanonicalizer.DecodeBase32Hex(label[0]);
    }

    /// <summary>
    /// Selects the answer RRsets that actually answer the question, following the CNAME/DNAME chain from the queried
    /// name. RRsets that are not reachable from the question are discarded rather than contributing to the verdict.
    /// </summary>
    private static List<DnsRecordRrset> SelectRrsetsAnsweringQuestion(IReadOnlyList<DnsRecordRrset> rrsets, DnsQuestion question)
    {
        var selected = new List<DnsRecordRrset>();
        var target = DnssecCanonicalizer.NormalizeName(question.Name);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (visited.Add(target))
        {
            var match = rrsets.FirstOrDefault(rrset =>
                rrset.Type == question.Type && DnssecCanonicalizer.CompareCanonicalNames(rrset.Name, target) is 0);
            if (match is not null)
            {
                selected.Add(match);
                break;
            }

            var cname = rrsets.FirstOrDefault(rrset =>
                rrset.Type is DnsQueryType.CNAME && DnssecCanonicalizer.CompareCanonicalNames(rrset.Name, target) is 0);
            if (cname is not null)
            {
                selected.Add(cname);
                target = DnssecCanonicalizer.NormalizeName(((DnsCnameRecord)cname.Records[0]).CanonicalName);
                continue;
            }

            var dname = rrsets.FirstOrDefault(rrset =>
                rrset.Type is DnsQueryType.DNAME && DnssecCanonicalizer.IsAncestorOrEqual(rrset.Name, target));
            if (dname is not null)
            {
                selected.Add(dname);

                // The DNAME's synthesized CNAME, if present, carries the chain forward; otherwise the chain ends here.
                var synthesized = rrsets.FirstOrDefault(rrset =>
                    rrset.Type is DnsQueryType.CNAME && DnssecCanonicalizer.CompareCanonicalNames(rrset.Name, target) is 0);
                if (synthesized is null)
                    break;

                continue;
            }

            break;
        }

        return selected;
    }

    /// <summary>
    /// Determines whether an RRset was synthesized from a wildcard, i.e. every RRSIG covers fewer labels than the
    /// owner name has. RFC 4034 3.1.3 also forbids a label count greater than the owner's.
    /// </summary>
    private static bool IsWildcardExpansion(DnsRecordRrset rrset)
    {
        var ownerLabels = DnssecCanonicalizer.CountLabels(rrset.Name);
        return rrset.Signatures.Count > 0 && rrset.Signatures.All(signature => signature.Labels < ownerLabels);
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
            && DnssecCanonicalizer.ComputeDigest(ownerName, key, anchor.DigestType).AsSpan().SequenceEqual(anchor.Digest.Span);
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
