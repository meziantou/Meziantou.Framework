---
title: Rule syntax
description: Write Adblock-style DNS rules - patterns, exceptions, priorities, and the $important, $badfilter, $denyallow and $dnstype modifiers.
---

The [AdBlock](cref:Meziantou.Framework.DnsFilter.DnsFilterListFormat.AdBlock) format is the syntax of DNS filter lists for ad blockers: a pattern, optionally preceded by `@@` for an
exception, and optionally followed by `$` and comma-separated modifiers.

## Patterns

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_PatternSyntax;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    ! A comment
    ||ads.example.com^
    |exact.example.com^
    ||ads-*.example.net^
    /^track[0-9]+\.example\.org$/
    """, DnsFilterListFormat.AdBlock);
var engine = new DnsFilterEngine(ruleSet);

// ||: the domain and its subdomains
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("ads.example.com").Action);
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("cdn.ads.example.com").Action);
Assert.False(engine.Evaluate("badads.example.com").IsMatched);

// A single |: only the domain itself
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("exact.example.com").Action);
Assert.False(engine.Evaluate("www.exact.example.com").IsMatched);

// * matches any characters
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("ads-eu.example.net").Action);

// /regex/ is matched against the whole name
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("track42.example.org").Action);
Assert.False(engine.Evaluate("tracker.example.org").IsMatched);
```
<!-- endref -->

| Pattern | Matches |
| --- | --- |
| `||example.com^` | `example.com` and all its subdomains, not `badexample.com` |
| `|example.com^`, `example.com` | `example.com` only |
| `||ads-*.example.com^` | `*` stands for any characters: `ads-eu.example.com`, `x.ads-1.example.com` |
| `/^ads[0-9]+\.example\.com$/` | Names that match the regular expression |
| `@@` + any pattern | An exception: the matching names are allowed. See [Priorities](#priorities). |

- The final `^` and `|` are optional: they mark the end of the name.
- Rules anchored on a domain are looked up in a hash table, whatever the size of the list. Wildcard rules that end with
  a domain (`||ads-*.example.com^`) use the same index; only regular expressions and wildcards without a fixed suffix
  are tried on every query, after a quick check of the literal text they need.
- Regular expressions are .NET regular expressions, case-insensitive, run with the non-backtracking engine when they
  allow it, so a hostile pattern cannot stall the engine. They match anywhere in the name: anchor them with `^` and
  `$`. Prefer domain rules when you can: they are faster and cannot match more than you mean.

## Priorities

When several rules match, the engine picks one, by priority, from highest to lowest:

1. An `$important` exception (`@@...$important`)
2. An `$important` block
3. An exception (`@@...`)
4. A normal block

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Priority;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    ||example.com^
    @@||www.example.com^
    ||cdn.example.com^$important
    @@||safe.cdn.example.com^$important
    """, DnsFilterListFormat.AdBlock);
var engine = new DnsFilterEngine(ruleSet);

// An exception (@@) beats a normal block
Assert.Equal(DnsFilterAction.Allow, engine.Evaluate("www.example.com").Action);

// $important beats an exception...
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("cdn.example.com").Action);

// ...unless the exception is $important too
var result = engine.Evaluate("safe.cdn.example.com");
Assert.Equal(DnsFilterAction.Allow, result.Action);
Assert.Equal("@@||safe.cdn.example.com^$important", result.MatchingRule?.OriginalText);
```
<!-- endref -->

Within a level, a rule with `$dnsrewrite` wins over a plain block, then the more specific rule wins.
[MatchingRule](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.MatchingRule) is the rule that decided, which helps to understand why a domain is blocked.

## Disable a rule of another list

`$badfilter` disables the rule that has the same text without the modifier. Use it in your own list to cancel a rule of
a list you do not control, without editing that list:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_BadFilter;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();

// A list you do not control
ruleSet.AddFromList("||cdn.example.com^", DnsFilterListFormat.AdBlock);

// Your own list: $badfilter disables the rule that has the same text
ruleSet.AddFromList("||cdn.example.com^$badfilter", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);
Assert.False(engine.Evaluate("cdn.example.com").IsMatched);
```
<!-- endref -->

The texts must match, modifiers included: `||cdn.example.com^$badfilter` disables `||cdn.example.com^`, not
`||cdn.example.com^$important`. An exception is the other way to unblock a domain: it keeps the rule, and can be
overridden by `$important`.

## Block a domain except some subdomains

`$denyallow` lists domains, separated by `|`, that the rule does not apply to:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_DenyAllow;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$denyallow=www.example.com|api.example.com", DnsFilterListFormat.AdBlock);
var engine = new DnsFilterEngine(ruleSet);

Assert.Equal(DnsFilterAction.Block, engine.Evaluate("ads.example.com").Action);

// The domains of $denyallow, and their subdomains, are not blocked by this rule
Assert.False(engine.Evaluate("www.example.com").IsMatched);
Assert.False(engine.Evaluate("v2.api.example.com").IsMatched);
```
<!-- endref -->

## Filter by query type

`$dnstype` limits a rule to some query types, separated by `|`, or excludes types with `~`:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_DnsType;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||example.com^$dnstype=AAAA", DnsFilterListFormat.AdBlock);

var engine = new DnsFilterEngine(ruleSet);

Assert.Equal(DnsFilterAction.Block, engine.Evaluate("example.com", DnsFilterQueryType.AAAA).Action);
Assert.False(engine.Evaluate("example.com", DnsFilterQueryType.A).IsMatched);
```
<!-- endref -->

- `$dnstype=A|AAAA` applies to A and AAAA queries only; `$dnstype=~HTTPS` to every type except HTTPS.
- Types are names ([AAAA](cref:Meziantou.Framework.DnsFilter.DnsFilterQueryType.AAAA)) or numbers (`65`, `TYPE65`), so types without a name in [DnsFilterQueryType](cref:Meziantou.Framework.DnsFilter.DnsFilterQueryType) work too: pass
  the query type as a number cast to [DnsFilterQueryType](cref:Meziantou.Framework.DnsFilter.DnsFilterQueryType) to [Evaluate](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Evaluate).

## Supported modifiers

| Modifier | Effect |
| --- | --- |
| `$important` | Raises the priority of the rule. See [Priorities](#priorities). |
| `$badfilter` | Disables the rule with the same text. |
| `$denyallow=a.com|b.com` | Excludes domains from the rule. |
| `$dnstype=A|~AAAA` | Limits the rule to query types. |
| `$client=...` | Limits the rule to clients. See [Client rules](04-ClientRules.md). |
| `$ctag=...` | Limits the rule to tagged clients. See [Client rules](04-ClientRules.md#tags). |
| `$dnsrewrite=...` | Answers instead of blocking. See [Rewrites](05-Rewrites.md). |

A rule with any other modifier, such as the web modifiers `$third-party` or `$script`, is skipped and reported. See
[See the lines that were skipped](02-FilterLists.md#see-the-lines-that-were-skipped).
