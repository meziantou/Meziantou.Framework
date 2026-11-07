---
title: DNSSEC
description: Request DNSSEC records, read the resolver's validation flag, or validate the chain of trust locally against the root anchors.
---

DNSSEC signs DNS records, so a client can check that an answer comes from the owner of the zone and was not modified
on the way. The client offers three levels:

| Level | Option | What you get |
| --- | --- | --- |
| Nothing | (default) | The answer, without signatures. |
| Signatures | `DnssecOk = true` | The answer with its RRSIG records, and the AD flag of the resolver. You check the signatures yourself, or trust the resolver. |
| Local validation | `DnssecValidationMode = DnssecValidationMode.Local` | The client checks the chain of trust from the root anchors, and reports the result in [DnssecValidationResult](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationResult). |

The examples use `dns.example.net` for the resolver you use; it must return DNSSEC records (most public resolvers do).

## Request the signatures

[DnssecOk](cref:Meziantou.Framework.DnsClient.DnsClientOptions.DnssecOk) sets the DNSSEC OK (DO) flag of the EDNS record, so the resolver adds the RRSIG records to the answer.
`Header.AuthenticatedData` is the AD flag: the resolver says that it validated the answer:

<!-- ref:../../../samples/Meziantou.Framework.DnsClient.Samples/ReadmeExamples.cs;region=DnsClient_DnssecRecords;format=md-fenced-code-block;dedent=true -->
```csharp
// Sets the DNSSEC OK flag, so the resolver returns the signatures with the answer
using var client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https, new DnsClientOptions
{
    DnssecOk = true,
});

var response = await client.QueryAsync("example.com", DnsQueryType.A);

// The AD flag: the resolver says it validated the answer. The client does not check it.
var validatedByResolver = response.Header.AuthenticatedData;

foreach (var signature in response.Answers.OfType<DnsRrsigRecord>())
{
    Console.WriteLine($"{signature.TypeCovered} signed by {signature.SignerName} (key {signature.KeyTag})");
}
```
<!-- endref -->

The AD flag is only as trustworthy as the resolver and the path to it: anyone who can modify the response can set it.
Rely on it only over an encrypted transport to a resolver you trust, or validate locally.

To read the keys and the delegation records themselves, query them: [DnsQueryType.DNSKEY](cref:Meziantou.Framework.DnsClient.Query.DnsQueryType.DNSKEY) returns [DnsDnskeyRecord](cref:Meziantou.Framework.DnsClient.Response.Records.DnsDnskeyRecord),
[DnsQueryType.DS](cref:Meziantou.Framework.DnsClient.Query.DnsQueryType.DS) returns [DnsDsRecord](cref:Meziantou.Framework.DnsClient.Response.Records.DnsDsRecord), and NSEC and NSEC3 records come with negative answers.

## Validate locally

With [DnssecValidationMode.Local](cref:Meziantou.Framework.DnsClient.DnssecValidationMode.Local), the client validates every response: it checks the signatures of the answer, then
queries the DNSKEY and DS records of each zone up to the root, and compares the root keys with the trust anchors:

<!-- ref:../../../samples/Meziantou.Framework.DnsClient.Samples/ReadmeExamples.cs;region=DnsClient_LocalDnssecValidation;format=md-fenced-code-block;dedent=true -->
```csharp
using var client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https, new DnsClientOptions
{
    DnssecValidationMode = DnssecValidationMode.Local,
});

var response = await client.QueryAsync("example.com", DnsQueryType.A);
var validation = response.DnssecValidationResult;
switch (validation.Status)
{
    case DnssecValidationStatus.Secure:
        // Signed by a chain of trust that starts at the root anchors, and answers the question
        break;

    case DnssecValidationStatus.Insecure:
        // The zone is provably unsigned
        break;

    case DnssecValidationStatus.Bogus:
    case DnssecValidationStatus.Indeterminate:
        foreach (var issue in validation.Issues)
        {
            Console.WriteLine($"{issue.Code} {issue.Name} {issue.RecordType}: {issue.Message}");
        }

        break;
}
```
<!-- endref -->

| [DnssecValidationStatus](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus) | Meaning |
| --- | --- |
| [Secure](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Secure) | The answer is signed by a chain of trust that starts at the trust anchors, and it answers the question that was asked. |
| [Insecure](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Insecure) | The zone is provably unsigned: a signed parent proves that it has no DS record. |
| [Bogus](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Bogus) | Validation failed: a signature is wrong or expired, a record is missing, or a denial of existence does not hold. |
| [Indeterminate](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Indeterminate) | Validation could not be completed, for instance because a query of the chain failed or an algorithm is not supported. |
| [NotValidated](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.NotValidated) | Local validation is off. |

- The client does not reject a [Bogus](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Bogus) response: it returns it with the status. Decide what to do with it, usually
  treat it as a failure.
- [Issues](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationResult.Issues) explains the status: each [DnssecValidationIssue](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssue) has a [Code](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssue.Code) ([SignatureExpired](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssueCode.SignatureExpired), [MissingDs](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssueCode.MissingDs),
  [DigestMismatch](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssueCode.DigestMismatch), ...), a [Message](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssue.Message), and the [Name](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssue.Name) and [RecordType](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssue.RecordType) concerned.
- Negative answers (NXDOMAIN and NODATA) are validated too, with their NSEC or NSEC3 proofs.

### How it works

- Local validation sets the DO flag, so the resolver returns the signatures, and the CD (checking disabled) flag, so it
  returns the records even when its own validation fails: the client wants to judge for itself.
- The chain queries go to the same server, over the same transport, as the query. A validation sends at most 48 of
  them; past that, the status is [Indeterminate](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Indeterminate) with the issue [QueryBudgetExceeded](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationIssueCode.QueryBudgetExceeded).
- [Timeout](cref:Meziantou.Framework.DnsClient.DnsClientOptions.Timeout) covers the query and its whole validation, chain queries included: raise it when you validate locally. A
  validation that runs out of time throws `TimeoutException`.
- It requires EDNS: creating a client with `EnableEdns = false` and local validation throws `ArgumentException`.
- The supported signature algorithms are RSA/SHA-256 (8), RSA/SHA-512 (10), ECDSA P-256/SHA-256 (13) and ECDSA
  P-384/SHA-384 (14); the supported DS digests are SHA-1 (1), SHA-256 (2) and SHA-384 (4). A zone signed only with
  another algorithm validates as [Indeterminate](cref:Meziantou.Framework.DnsClient.Response.DnssecValidationStatus.Indeterminate).
- Signature validity periods are checked against [TimeProvider](cref:Meziantou.Framework.DnsClient.DnsClientOptions.TimeProvider), the system clock by default. A wrong clock makes valid
  signatures look expired or not yet valid.

### Trust anchors

[DnssecTrustAnchors](cref:Meziantou.Framework.DnsClient.DnssecTrustAnchors) defaults to [DnssecTrustAnchors.Root](cref:Meziantou.Framework.DnsClient.DnssecTrustAnchors.Root), the IANA root key digests that ship with the library.
Replace the list to validate against your own anchors, for instance in a test environment with its own signed root;
each [DnssecTrustAnchor](cref:Meziantou.Framework.DnsClient.DnssecTrustAnchor) is a DS record: a name, a key tag, an algorithm, a digest type and a digest. The root anchors
change only when the root key is rolled over; update the package to get a new one.
