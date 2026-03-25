# Meziantou.Framework.DnsFilter

A standalone DNS filter list parser and matching engine supporting hosts files, domains-only lists, and AdGuard/Adblock DNS filtering syntax. Inspired by the filtering capabilities of AdGuard Home and Pi-hole.

## Features

- **Multiple list formats**: Hosts files (`0.0.0.0 domain`), domains-only lists, and AdGuard/Adblock DNS filtering syntax (`||domain^`)
- **Auto-detection**: Automatically detects the filter list format when not specified
- **AdGuard modifiers**: `$important`, `$badfilter`, `$dnstype`, `$denyallow`, `$dnsrewrite`, `$client`, `$ctag`
- **Priority resolution**: Follows the AdGuard priority pipeline — `$important` block → `$important` allow → `@@` exception → normal block
- **Client-aware filtering**: Filter rules by client IP address, CIDR range, client name (`$client`), or client tags (`$ctag`)
- **Efficient matching**: Hash-based exact domain and suffix lookups with regex fallback for wildcard and pattern rules
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
// result.Rewrite.ResponseCode == DnsFilterRewriteResponseCode.NoError
// result.Rewrite.RecordType == DnsFilterQueryType.A
// result.Rewrite.Value == "1.2.3.4"
```
