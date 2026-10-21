# Meziantou.Framework.DnsFilter

A standalone DNS filter list parser and matching engine supporting hosts files, domains-only lists, and AdGuard/Adblock DNS filtering syntax. Inspired by the filtering capabilities of AdGuard Home and Pi-hole.

## Features

- **Multiple list formats**: Hosts files (`0.0.0.0 domain`), domains-only lists, and AdGuard/Adblock DNS filtering syntax (`||domain^`)
- **Auto-detection**: Automatically detects the filter list format when not specified
- **AdGuard modifiers**: `$important`, `$badfilter`, `$dnstype`, `$denyallow`, `$dnsrewrite`, `$client`, `$ctag`
- **Priority resolution**: `$important` allow → `$important` block → `@@` exception → normal block. Within a level, a `$dnsrewrite` rule wins, then the more specific rule.
- **Client-aware filtering**: Filter rules by client IP address, CIDR range, client name (`$client`), or client tags (`$ctag`)
- **Efficient matching**: Hash-based exact domain and suffix lookups. Wildcard rules anchored on a concrete domain suffix (`||ads-*.example.com^`) are reached through the same suffix index; only patterns with no indexable suffix are evaluated on every query, and those are gated behind a literal prefilter
- **Strict parsing**: A rule carrying an unsupported modifier, or a modifier value that cannot be parsed, is discarded rather than silently applied without it. `ParseWithDiagnostics` reports every skipped line
- **Punycode**: Rule patterns and queried names are both normalized to A-labels, so an internationalized domain matches the name DNS actually carries
- **Thread-safe**: Concurrent query evaluation with atomic rule-set replacement via `Reload()`

## Usage

### Load a hosts-file blocklist

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_HostsFile;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    0.0.0.0 ads.example.com
    0.0.0.0 tracking.example.org
    """, DnsFilterListFormat.Hosts);

var engine = new DnsFilterEngine(ruleSet);
var result = engine.Evaluate("ads.example.com");
Assert.True(result.IsMatched);
Assert.Equal(DnsFilterAction.Block, result.Action);

// An unmatched query returns DnsFilterAction.None, never Block.
Assert.Equal(DnsFilterAction.None, engine.Evaluate("example.com").Action);
```
<!-- endref -->

### Load an AdGuard-style filter list

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_AdGuard;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    ||ads.example.com^
    ||tracking.example.org^
    @@||safe.example.com^
    """, DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

Assert.Equal(DnsFilterAction.Block, engine.Evaluate("ads.example.com").Action);

// Subdomain match with || syntax
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("sub.ads.example.com").Action);

// Exception rule
Assert.Equal(DnsFilterAction.Allow, engine.Evaluate("safe.example.com").Action);
```
<!-- endref -->

### Combine multiple lists

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_CombineLists;format=md-fenced-code-block;dedent=true -->
```csharp
var hostsFileContent = "0.0.0.0 ads.example.com";
var adGuardListContent = "||tracking.example.org^";
var domainsOnlyContent = "malware.example.net";

var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList(hostsFileContent, DnsFilterListFormat.Hosts);
ruleSet.AddFromList(adGuardListContent, DnsFilterListFormat.AdBlock);
ruleSet.AddFromList(domainsOnlyContent, DnsFilterListFormat.DomainsOnly);

var engine = new DnsFilterEngine(ruleSet);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("ads.example.com").Action);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("tracking.example.org").Action);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("malware.example.net").Action);
```
<!-- endref -->

### Filter by DNS query type

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_DnsType;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$dnstype=AAAA", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

Assert.Equal(DnsFilterAction.Block, engine.Evaluate("example.com", DnsFilterQueryType.AAAA).Action);
Assert.False(engine.Evaluate("example.com", DnsFilterQueryType.A).IsMatched);
```
<!-- endref -->

### Client-aware filtering with `$client`

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Client;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$client=192.168.1.0/24", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

var client = new DnsClientInfo { Address = IPAddress.Parse("192.168.1.50") };
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("example.com", DnsFilterQueryType.A, client).Action);

var otherClient = new DnsClientInfo { Address = IPAddress.Parse("10.0.0.1") };
Assert.False(engine.Evaluate("example.com", DnsFilterQueryType.A, otherClient).IsMatched);
```
<!-- endref -->

### Tag-based filtering with `$ctag`

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_ClientTag;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$ctag=device_phone", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

var phoneClient = new DnsClientInfo { Tags = ["device_phone", "os_android"] };
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("example.com", DnsFilterQueryType.A, phoneClient).Action);

var pcClient = new DnsClientInfo { Tags = ["device_pc"] };
Assert.False(engine.Evaluate("example.com", DnsFilterQueryType.A, pcClient).IsMatched);
```
<!-- endref -->

### Reload rules at runtime

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Reload;format=md-fenced-code-block;dedent=true -->
```csharp
var initialList = "||ads.example.com^";
var updatedList = "||tracking.example.org^";

var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList(initialList);
var engine = new DnsFilterEngine(ruleSet);

// Later, replace the rules atomically (thread-safe)
var newRuleSet = new DnsFilterRuleSet();
newRuleSet.AddFromList(updatedList);
engine.Reload(newRuleSet);

Assert.False(engine.Evaluate("ads.example.com").IsMatched);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("tracking.example.org").Action);
```
<!-- endref -->

### DNS rewriting with `$dnsrewrite`

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_DnsRewrite;format=md-fenced-code-block;dedent=true -->
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

### See which lines were skipped

`AddFromList` returns a diagnostic for every line that did not become a rule, so a list that
silently fails to parse is visible instead of just producing fewer rules.

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var listText = """
    ||ads.example.com^
    ||example.com^$third-party
    """;

var ruleSet = new DnsFilterRuleSet();
var diagnostics = ruleSet.AddFromList(listText);

var diagnostic = Assert.Single(diagnostics);
Assert.Equal("Line 2: UnsupportedModifier 'third-party' (||example.com^$third-party)", diagnostic.ToString());
```
<!-- endref -->

`DnsFilterListReader.ParseWithDiagnostics` exposes the same information, plus the format that
auto-detection settled on.

### Build rules without going through list text

<!-- ref:../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_BuildRules;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.Add(DnsFilterRule.CreateBlock("ads.example.com", includeSubdomains: true));
ruleSet.Add(DnsFilterRule.CreateAllow("safe.ads.example.com"));

var engine = new DnsFilterEngine(ruleSet);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("tracker.ads.example.com").Action);
Assert.Equal(DnsFilterAction.Allow, engine.Evaluate("safe.ads.example.com").Action);
```
<!-- endref -->
