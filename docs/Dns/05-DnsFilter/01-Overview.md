---
title: Overview
description: Load a block list into a rule set, evaluate domains with the engine, and read the result.
---

`Meziantou.Framework.DnsFilter` reads DNS filter lists (hosts files, lists of domains, and Adblock-style rules such as
`||ads.example.com^`) and evaluates domains against them. It does not send or answer DNS queries: call it from a DNS
server handler, a proxy, or any code that must decide whether a domain is allowed. `Meziantou.DnsProxy` uses it this
way. The types are in the [Meziantou.Framework.DnsFilter](cref:Meziantou.Framework.DnsFilter) namespace.

## Evaluate a domain

Add lists to a [DnsFilterRuleSet](cref:Meziantou.Framework.DnsFilter.DnsFilterRuleSet), build a [DnsFilterEngine](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine) from it, and call [Evaluate](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Evaluate):

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_AdGuard;format=md-fenced-code-block;dedent=true -->
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

[Evaluate](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Evaluate) takes the queried name, the query type ([DnsFilterQueryType.A](cref:Meziantou.Framework.DnsFilter.DnsFilterQueryType.A) by default) and information about the client
(see [Client rules](04-ClientRules.md)), and returns a [DnsFilterResult](cref:Meziantou.Framework.DnsFilter.DnsFilterResult):

| Member | Contains |
| --- | --- |
| [Action](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.Action) | [None](cref:Meziantou.Framework.DnsFilter.DnsFilterAction.None) (no rule matched), `Block`, [Allow](cref:Meziantou.Framework.DnsFilter.DnsFilterAction.Allow) (an exception rule matched) or [Rewrite](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.Rewrite) (see [Rewrites](05-Rewrites.md)) |
| [IsMatched](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.IsMatched) | `true` when a rule matched, whatever its action |
| [MatchingRule](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.MatchingRule) | The rule that decided, with its [OriginalText](cref:Meziantou.Framework.DnsFilter.DnsFilterRule.OriginalText) |
| [Rewrite](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.Rewrite) | The rewrite directive, when [Action](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.Action) is [Rewrite](cref:Meziantou.Framework.DnsFilter.DnsFilterResult.Rewrite) |

[None](cref:Meziantou.Framework.DnsFilter.DnsFilterAction.None) is the default value of [DnsFilterAction](cref:Meziantou.Framework.DnsFilter.DnsFilterAction): an unmatched query never reads as blocked. Treat [None](cref:Meziantou.Framework.DnsFilter.DnsFilterAction.None) and [Allow](cref:Meziantou.Framework.DnsFilter.DnsFilterAction.Allow)
the same way, by resolving the query, unless you need to know that an exception rule matched.

Matching ignores case and a trailing dot, and converts internationalized names to their punycode form, both in the
rules and in the queried name. [Evaluate](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Evaluate) does not throw for malformed or hostile names.

## Thread safety

- [Evaluate](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Evaluate) can be called concurrently.
- [Reload](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Reload) replaces the rules atomically, while other threads evaluate. See
  [Reload the rules](02-FilterLists.md#reload-the-rules).
- [DnsFilterRuleSet](cref:Meziantou.Framework.DnsFilter.DnsFilterRuleSet) is not thread-safe, but the engine takes a snapshot of it when it is built: changing the rule set
  afterwards does not change an engine built from it.

## Pages

- [Filter lists](02-FilterLists.md): the formats, combining lists, the lines that are skipped, and reloading.
- [Rule syntax](03-RuleSyntax.md): patterns, exceptions, priorities and modifiers.
- [Client rules](04-ClientRules.md): rules for some clients only, by address, name or tag.
- [Rewrites](05-Rewrites.md): answer with an address, a CNAME or a response code instead of blocking.
