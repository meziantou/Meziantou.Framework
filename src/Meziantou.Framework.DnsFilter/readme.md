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

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    0.0.0.0 ads.example.com
    0.0.0.0 tracking.example.org
    """, DnsFilterListFormat.Hosts);

var engine = new DnsFilterEngine(ruleSet);
var result = engine.Evaluate("ads.example.com");
// result.IsMatched == true, result.Action == DnsFilterAction.Block
// An unmatched query returns DnsFilterAction.None, never Block.
```

### Load an AdGuard-style filter list

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    ||ads.example.com^
    ||tracking.example.org^
    @@||safe.example.com^
    """, DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

engine.Evaluate("ads.example.com");
// Blocked

engine.Evaluate("sub.ads.example.com");
// Blocked (subdomain match with || syntax)

engine.Evaluate("safe.example.com");
// Allowed (exception rule)
```

### Combine multiple lists

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList(hostsFileContent, DnsFilterListFormat.Hosts);
ruleSet.AddFromList(adGuardListContent, DnsFilterListFormat.AdBlock);
ruleSet.AddFromList(domainsOnlyContent, DnsFilterListFormat.DomainsOnly);

var engine = new DnsFilterEngine(ruleSet);
```

### Filter by DNS query type

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$dnstype=AAAA", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

engine.Evaluate("example.com", DnsFilterQueryType.AAAA);
// Blocked

engine.Evaluate("example.com", DnsFilterQueryType.A);
// Not matched
```

### Client-aware filtering with `$client`

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$client=192.168.1.0/24", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

var client = new DnsClientInfo { Address = IPAddress.Parse("192.168.1.50") };
engine.Evaluate("example.com", DnsFilterQueryType.A, client);
// Blocked

var otherClient = new DnsClientInfo { Address = IPAddress.Parse("10.0.0.1") };
engine.Evaluate("example.com", DnsFilterQueryType.A, otherClient);
// Not matched
```

### Tag-based filtering with `$ctag`

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$ctag=device_phone", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

var phoneClient = new DnsClientInfo { Tags = ["device_phone", "os_android"] };
engine.Evaluate("example.com", DnsFilterQueryType.A, phoneClient);
// Blocked

var pcClient = new DnsClientInfo { Tags = ["device_pc"] };
engine.Evaluate("example.com", DnsFilterQueryType.A, pcClient);
// Not matched
```

### Reload rules at runtime

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList(initialList);
var engine = new DnsFilterEngine(ruleSet);

// Later, replace the rules atomically (thread-safe)
var newRuleSet = new DnsFilterRuleSet();
newRuleSet.AddFromList(updatedList);
engine.Reload(newRuleSet);
```

### DNS rewriting with `$dnsrewrite`

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$dnsrewrite=1.2.3.4", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);
var result = engine.Evaluate("example.com");
// result.Action == DnsFilterAction.Rewrite  (not Block — the caller must synthesize an answer)
// result.Rewrite.ResponseCode == DnsFilterRewriteResponseCode.NoError
// result.Rewrite.RecordType == DnsFilterQueryType.A
// result.Rewrite.Value == "1.2.3.4"
```

### See which lines were skipped

`AddFromList` returns a diagnostic for every line that did not become a rule, so a list that
silently fails to parse is visible instead of just producing fewer rules.

```csharp
var ruleSet = new DnsFilterRuleSet();
var diagnostics = ruleSet.AddFromList(listText);

foreach (var diagnostic in diagnostics)
{
    // Line 42: UnsupportedModifier 'third-party' (||example.com^$third-party)
    Console.WriteLine(diagnostic);
}
```

`DnsFilterListReader.ParseWithDiagnostics` exposes the same information, plus the format that
auto-detection settled on.

### Build rules without going through list text

```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.Add(DnsFilterRule.CreateBlock("ads.example.com", includeSubdomains: true));
ruleSet.Add(DnsFilterRule.CreateAllow("safe.ads.example.com"));
```
