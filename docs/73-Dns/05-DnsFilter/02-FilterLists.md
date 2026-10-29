---
title: Filter lists
description: Load hosts files, domain lists and Adblock-style lists, combine them, see the lines that were skipped, and reload the rules.
---

## Formats

`AddFromList` and `DnsFilterListReader` take the text of a list and a `DnsFilterListFormat`:

| Format | Lines | Rules |
| --- | --- | --- |
| `Hosts` | `0.0.0.0 ads.example.com`: an IP address followed by one or more names | Blocks each name exactly, not its subdomains |
| `DomainsOnly` | `ads.example.com`: one name per line | Blocks each name exactly, not its subdomains |
| `AdBlock` | `||ads.example.com^`, `@@||safe.example.com^`, `/regex/`, with `$` modifiers | See [Rule syntax](03-RuleSyntax.md) |
| `AutoDetect` | Any of the above | The default: the format with the most matching lines wins |

- In a hosts file, the IP address does not matter: every entry is a block, as if it were `0.0.0.0`. To answer with an
  address, use a `$dnsrewrite` rule. See [Rewrites](05-Rewrites.md).
- Hosts entries for `localhost`, `localhost.localdomain`, `local`, `broadcasthost` and the `ip6-*` names are skipped, so
  a standard hosts file does not block them.
- Empty lines, lines that start with `#` or `!`, and list headers such as `[Adblock Plus 2.0]` are comments. Text
  after a `#` on a line is a comment too.

Pass the format when you know it. Auto-detection counts the lines of each kind, so one odd line in a large hosts file
does not change the format, but a short or mixed list can be detected as another format than you expect.

## Combine lists

A rule set holds the rules of every list you add, whatever their format:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_CombineLists;format=md-fenced-code-block;dedent=true -->
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

`AddFromList` also takes a `TextReader`, to read a large list from a file or a stream without loading it as one string.
The engine applies the rules of all the lists together: an exception rule of one list can allow a domain that another
list blocks. See [Priorities](03-RuleSyntax.md#priorities).

## See the lines that were skipped

A line that does not become a rule is reported, instead of silently producing fewer rules. `AddFromList` returns the
diagnostics:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Diagnostics;format=md-fenced-code-block;dedent=true -->
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

Parsing is strict: a rule with a modifier the library does not support, or a modifier value it cannot parse, is skipped
rather than applied without the modifier. Applying `||x^$dnstype=SVCB` without its modifier would block every record
type of the domain, and `@@||x^$script` would become an exception for the whole domain.

Each `DnsFilterParseDiagnostic` has the `LineNumber`, the `Line`, the `Error` and a `Detail`:

| `DnsFilterParseError` | Cause |
| --- | --- |
| `UnsupportedModifier` | The rule has a modifier the library does not implement, such as `$third-party` |
| `InvalidModifierValue` | A modifier value cannot be parsed, such as `$dnstype=FOO` |
| `InvalidRegex` | A `/regex/` rule is not a valid regular expression |
| `InvalidPattern` | The line is not a valid name, hosts entry or pattern |

`DnsFilterListReader.ParseWithDiagnostics` parses a list without a rule set, and also returns the format that
auto-detection chose. `DnsFilterListReader.Parse` returns the rules only:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_ParseWithDiagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var listText = """
    # A hosts file
    0.0.0.0 ads.example.com tracking.example.com
    127.0.0.1 localhost
    not a hosts line
    """;

var result = DnsFilterListReader.ParseWithDiagnostics(listText);

// AutoDetect chose the format from the content of the list
Assert.Equal(DnsFilterListFormat.Hosts, result.Format);

// One rule per domain; localhost entries are skipped
Assert.Equal(["ads.example.com", "tracking.example.com"], result.Rules.Select(rule => rule.ExactDomain));

var diagnostic = Assert.Single(result.Diagnostics);
Assert.Equal(4, diagnostic.LineNumber);
Assert.Equal(DnsFilterParseError.InvalidPattern, diagnostic.Error);

var ruleSet = new DnsFilterRuleSet();
ruleSet.AddRange(result.Rules);
```
<!-- endref -->

## Add rules from code

`DnsFilterRule.CreateBlock` and `DnsFilterRule.CreateAllow` create rules without list text, for instance from a
database or a user setting:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_BuildRules;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.Add(DnsFilterRule.CreateBlock("ads.example.com", includeSubdomains: true));
ruleSet.Add(DnsFilterRule.CreateAllow("safe.ads.example.com"));

var engine = new DnsFilterEngine(ruleSet);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("tracker.ads.example.com").Action);
Assert.Equal(DnsFilterAction.Allow, engine.Evaluate("safe.ads.example.com").Action);
```
<!-- endref -->

`includeSubdomains: true` is the equivalent of `||domain^`; without it, the rule matches the domain only.

## Reload the rules

Lists change. Build a new rule set and pass it to `Reload`: the engine swaps the rules atomically, so the queries being
evaluated see either the old rules or the new ones, never a mix:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Reload;format=md-fenced-code-block;dedent=true -->
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

Building the engine indexes the rules, which takes time for lists of hundreds of thousands of lines: build the new rule
set and call `Reload` in the background, not on the path of a query.
