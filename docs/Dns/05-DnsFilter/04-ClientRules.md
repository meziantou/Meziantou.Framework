---
title: Client rules
description: Apply rules to some clients only, by IP address, network, name or tag, with the $client and $ctag modifiers.
---

The same list can apply different rules to different clients: block more for the children's devices, or exempt a
server. The engine does not know the clients: pass what you know about the one that sent the query, in a
[DnsClientInfo](cref:Meziantou.Framework.DnsFilter.DnsClientInfo), to [Evaluate](cref:Meziantou.Framework.DnsFilter.DnsFilterEngine.Evaluate).

| [DnsClientInfo](cref:Meziantou.Framework.DnsFilter.DnsClientInfo) property | Used by |
| --- | --- |
| [Address](cref:Meziantou.Framework.DnsFilter.DnsClientInfo.Address) | `$client` with an IP address or a network |
| [Name](cref:Meziantou.Framework.DnsFilter.DnsClientInfo.Name) | `$client` with a name |
| [Tags](cref:Meziantou.Framework.DnsFilter.DnsClientInfo.Tags) | `$ctag` |

A rule scoped on clients does not match when the client information it needs is missing: with
`engine.Evaluate(domain)`, `$client` and `$ctag` rules never apply.

## By address or network

`$client` takes IP addresses and networks in CIDR notation:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Client;format=md-fenced-code-block;dedent=true -->
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

## By name

`$client` also takes names. The engine compares them with [DnsClientInfo.Name](cref:Meziantou.Framework.DnsFilter.DnsClientInfo.Name); it does not resolve them. Quote a name
that contains spaces or separators, and separate the values with `|`:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_ClientName;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("||video.example.com^$client='Kids tablet'|10.0.0.0/8", DnsFilterListFormat.AdBlock);
var engine = new DnsFilterEngine(ruleSet);

// The engine does not resolve names: pass the name you know for the client
var tablet = new DnsClientInfo { Name = "Kids tablet", Address = IPAddress.Parse("192.168.1.30") };
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("video.example.com", DnsFilterQueryType.A, tablet).Action);

var vpnClient = new DnsClientInfo { Address = IPAddress.Parse("10.1.2.3") };
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("video.example.com", DnsFilterQueryType.A, vpnClient).Action);

// Without client information, a rule scoped on clients does not match
Assert.False(engine.Evaluate("video.example.com").IsMatched);
```
<!-- endref -->

## Tags

`$ctag` matches the tags of the client. Tags are strings you choose, such as `device_phone`, `os_android` or
`user_child`, and a client can have several:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_ClientTag;format=md-fenced-code-block;dedent=true -->
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

## Exclusions

`~` before a value excludes it, in `$client`, `$ctag` and `$dnstype`. The rule then applies to every client, tag or
type except those:

<!-- ref:../../../tests/Meziantou.Framework.DnsFilter.Tests/DnsFilterEngineTests.cs;region=DnsFilter_Exclusions;format=md-fenced-code-block;dedent=true -->
```csharp
var ruleSet = new DnsFilterRuleSet();
ruleSet.AddFromList("""
    ||ipv6-only.example.com^$dnstype=~AAAA
    ||games.example.com^$ctag=~user_admin
    ||office.example.com^$client=~192.168.1.10
    """, DnsFilterListFormat.AdBlock);
var engine = new DnsFilterEngine(ruleSet);

// ~ excludes a query type, a tag or a client
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("ipv6-only.example.com", DnsFilterQueryType.A).Action);
Assert.False(engine.Evaluate("ipv6-only.example.com", DnsFilterQueryType.AAAA).IsMatched);

var child = new DnsClientInfo { Tags = ["user_child"] };
var admin = new DnsClientInfo { Tags = ["user_admin"] };
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("games.example.com", DnsFilterQueryType.A, child).Action);
Assert.False(engine.Evaluate("games.example.com", DnsFilterQueryType.A, admin).IsMatched);

var laptop = new DnsClientInfo { Address = IPAddress.Parse("192.168.1.20") };
var boss = new DnsClientInfo { Address = IPAddress.Parse("192.168.1.10") };
Assert.Equal(DnsFilterAction.Block, engine.Evaluate("office.example.com", DnsFilterQueryType.A, laptop).Action);
Assert.False(engine.Evaluate("office.example.com", DnsFilterQueryType.A, boss).IsMatched);
```
<!-- endref -->
