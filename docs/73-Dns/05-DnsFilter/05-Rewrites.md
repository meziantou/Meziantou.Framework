---
title: Rewrites
description: Answer a query with an address, a CNAME, another record or a response code with $dnsrewrite, and turn the result into a DNS response.
---

A `$dnsrewrite` rule does not block: it tells the caller what to answer. The result has `Action` set to `Rewrite`,
and `Rewrite` describes the answer:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_DnsRewrite;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$dnsrewrite=1.2.3.4", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);
var result = engine.Evaluate("example.com");

// Not Block: the caller must synthesize an answer
Assert.Equal(DnsFilterAction.Rewrite, result.Action);
Assert.NotNull(result.Rewrite);
Assert.Equal(DnsFilterRewriteResponseCode.NoError, result.Rewrite.ResponseCode);
Assert.Equal(DnsFilterQueryType.A, result.Rewrite.RecordType);
Assert.Equal("1.2.3.4", result.Rewrite.Value);
```
<!-- endref -->

## Forms of the value

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_RewriteForms;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    ||nas.example.com^$dnsrewrite=192.168.1.5
    ||nas6.example.com^$dnsrewrite=fd00::5
    ||www.example.org^$dnsrewrite=example.net
    ||blocked.example.com^$dnsrewrite=REFUSED
    ||mail.example.com^$dnsrewrite=NOERROR;MX;10 mx.example.com
    """, DnsFilterListFormat.AdBlock);
var engine = new DnsFilterEngine(ruleSet);

DnsFilterRewriteRule? Rewrite(string domain) => engine.Evaluate(domain).Rewrite;

// An address: an A or AAAA answer
Assert.Equal(DnsFilterQueryType.A, Rewrite("nas.example.com")?.RecordType);
Assert.Equal(DnsFilterQueryType.AAAA, Rewrite("nas6.example.com")?.RecordType);

// A domain: a CNAME answer
Assert.Equal(DnsFilterQueryType.CNAME, Rewrite("www.example.org")?.RecordType);
Assert.Equal("example.net", Rewrite("www.example.org")?.Value);

// A response code alone: no answer
Assert.Equal(DnsFilterRewriteResponseCode.Refused, Rewrite("blocked.example.com")?.ResponseCode);
Assert.Null(Rewrite("blocked.example.com")?.RecordType);

// The full form: response code;record type;value
Assert.Equal(DnsFilterQueryType.MX, Rewrite("mail.example.com")?.RecordType);
Assert.Equal("10 mx.example.com", Rewrite("mail.example.com")?.Value);
```
<!-- endref -->

| Value | `ResponseCode` | `RecordType` | `Value` |
| --- | --- | --- | --- |
| An IPv4 address, `$dnsrewrite=192.168.1.5` | `NoError` | `A` | The address |
| An IPv6 address, `$dnsrewrite=fd00::5` | `NoError` | `AAAA` | The address |
| A domain, `$dnsrewrite=example.net` | `NoError` | `CNAME` | The domain |
| A response code, `$dnsrewrite=REFUSED` | `Refused` | `null` | `null` |
| `code;type;value`, `$dnsrewrite=NOERROR;MX;10 mx.example.com` | The code | The type | The value, as text |

The response codes are `NOERROR`, `NXDOMAIN`, `REFUSED` and `SERVFAIL`. In the full form, the type and the value can be
empty, for instance `NXDOMAIN;;`. The value of an `A`, `AAAA`, `CNAME`, `PTR`, `NS` or `DNAME` rewrite is checked when
the list is parsed, and a rule with an invalid value is skipped; the value of the other types is kept as text.

## Answer the query

The engine only describes the answer: the caller builds the DNS response. For a `Rewrite` result:

1. Set the response code from `Rewrite.ResponseCode`.
2. When `RecordType` is set and matches the question (or the question is `ANY`), add a record of that type with
   `Value` as its data: parse the address for `A` and `AAAA`, use the name for `CNAME`, and parse the text for the
   other types.
3. When `RecordType` is set but differs from the question, answer with the response code and no record: the name
   exists, with no record of the queried type.

For a `Block` result, answer the way your clients expect. `Meziantou.DnsProxy` answers NXDOMAIN; answering `0.0.0.0`
(or `::`) for A and AAAA queries is the other common choice. See [DnsProxy](../06-DnsProxy/01-Overview.md).

When several rewrite rules match a name, only the one with the highest priority applies: rewrites do not combine into
one answer with several records.
