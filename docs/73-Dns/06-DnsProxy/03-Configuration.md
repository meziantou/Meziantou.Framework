---
title: Configuration
description: Configure the upstream resolvers, the filter lists, your own records, the cache, the rate limit and DNSSEC.
---

Every setting is in the `DnsProxy` section of the configuration. See
[Change the settings](02-RunningTheProxy.md#change-the-settings) for the ways to set them; the examples use
command-line arguments.

## Upstreams

`Upstreams` lists the resolvers the proxy forwards to. Each entry has a `Url`, a `Name` (shown on the diagnostics page)
and a `Priority`:

```bash
Meziantou.DnsProxy --DnsProxy:Upstreams:0:Name=Primary --DnsProxy:Upstreams:0:Url=https://dns.example.net/dns-query --DnsProxy:Upstreams:1:Name=Fallback --DnsProxy:Upstreams:1:Url=tls://dns.example.org --DnsProxy:Upstreams:1:Priority=1
```

The scheme of the URL selects the transport:

| URL | Transport |
| --- | --- |
| `https://host/path`, `h3://host/path` | DNS over HTTPS, to that URL |
| `quic://host`, `quic://host:port` | DNS over QUIC, port 853 by default |
| `tls://host`, `tls://host:port` | DNS over TLS, port 853 by default |
| `tcp://host`, `udp://host` | Plain TCP or UDP, port 53 by default |

- The proxy tries the upstreams in `Priority` order, lowest first, for every query. A transport failure, a timeout
  (5 seconds), `ServerFailure` or `Refused` moves on to the next one. `NoError` and `NameError` are answers: they are
  returned at once. When every upstream fails, the client gets the last `ServerFailure` or `Refused`, or
  `ServerFailure`.
- There is no health tracking: an upstream that is down is tried again for each query, before the next one.
- Where QUIC is not supported, a `quic://host` upstream is replaced by DNS over HTTPS to `https://host/dns-query`,
  with a warning in the log.
- A URL with another scheme, or without a scheme, stops the proxy at startup.

By default, the proxy uses six public resolvers, over DNS over HTTPS and DNS over QUIC; the
[`appsettings.json` of the project](https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.DnsProxy/appsettings.json)
lists them.

### Bootstrap servers

An upstream URL has a host name, which must be resolved without the proxy itself. `BootstrapDnsServers` lists the IP
addresses of the DNS servers that resolve it, over UDP:

- For DNS over HTTPS upstreams, the host name is resolved for each new connection, and the result is cached for five
  minutes.
- For TLS, QUIC, TCP and UDP upstreams, the host name is resolved once, at startup; the proxy does not start when it
  cannot be resolved.
- Entries that are not IP addresses are ignored. Without any valid entry, the system resolver is used.

## Filter lists

`Filters` lists the block lists. Each entry has a `Url` and a `Format` (`AutoDetect`, `Hosts`, `DomainsOnly` or
`AdBlock`; see [Formats](../05-DnsFilter/02-FilterLists.md#formats)):

```bash
Meziantou.DnsProxy --DnsProxy:Filters:0:Url=https://lists.example.net/ads.txt --DnsProxy:Filters:0:Format=AdBlock
```

| Setting | Default | Use |
| --- | --- | --- |
| `FilterRefreshInterval` | 30 minutes | How often the lists are downloaded again. |
| `FilterDownloadTimeout` | 2 minutes | The longest download of one list. |
| `MaxFilterListSizeInBytes` | 64 MB | The largest list. A larger one is rejected. |
| `BlockListCacheFolderPath` | `meziantou/dnsproxy/block-lists` in the local application data folder | Where the downloaded lists are kept. |

- At startup, the proxy loads the lists from the cache folder, so filtering works at once, then downloads them.
- When a download fails (error, timeout, size limit), the proxy uses the cached copy of that list and logs a warning.
  A list that has never been downloaded has no rules until a download succeeds.
- The lines that are not valid rules are skipped, and their number is logged with a few examples. See
  [See the lines that were skipped](../05-DnsFilter/02-FilterLists.md#see-the-lines-that-were-skipped).
- The URL must be `http` or `https`: local files are not supported.
- A blocked query gets NXDOMAIN. An exception rule (`@@`) lets the query go to the cache and the upstreams.
- `$client` rules match by client address only: the proxy does not know the names of the clients, nor any tags, so
  `$client` rules with a name and `$ctag` rules never match. See [Client rules](../05-DnsFilter/04-ClientRules.md).
- A `$dnsrewrite` rule is answered with a TTL of 60 seconds, for the types A, AAAA, CNAME, PTR, NS, TXT, MX, SRV and
  CAA. The proxy only answers queries of the type of the rewrite: a CNAME rewrite answers CNAME queries, and an A query
  for the same name gets an empty `NoError` answer. A rewrite of a type outside this list, such as SVCB, is answered as
  a block.

By default, the proxy uses two public block lists, one in Adblock format and one hosts file.

## Custom records

`CustomRecords` defines your own records, for the names of your local network. Each entry has a `Domain`, a `Type`
(`A` by default) and a `Value`, or several `Values`:

```bash
Meziantou.DnsProxy --DnsProxy:CustomRecords:0:Domain=nas.home.arpa --DnsProxy:CustomRecords:0:Value=192.168.1.11 --DnsProxy:CustomRecords:1:Domain=nas.home.arpa --DnsProxy:CustomRecords:1:Type=AAAA --DnsProxy:CustomRecords:1:Value=fd00::11
```

| `Type` | `Value` |
| --- | --- |
| `A` | An IPv4 address: `192.168.1.11` |
| `AAAA` | An IPv6 address: `fd00::11` |
| `CNAME`, `PTR`, `NS` | A domain name |
| `TXT` | The text |
| `MX` | The preference and the host: `10 mail.home.arpa` |
| `SRV` | The priority, the weight, the port and the target: `10 5 443 service.home.arpa` |
| `CAA` | The flags, the tag and the value: `0 issue ca.example.net` |

- Custom records are answered first, before the filter lists, the cache and the upstreams, with a TTL of 60 seconds.
  Block lists and the pause of the filtering do not affect them.
- The name must match exactly, without case: there are no wildcards, and subdomains are not included.
- A query for a type the name has no custom record of goes on to the filter lists and the upstreams. A CNAME custom
  record therefore does not answer A queries.
- An entry with an unknown type or an invalid value is skipped with a warning.

## Cache

| Setting | Default | Use |
| --- | --- | --- |
| `PositiveCacheDuration` | 5 minutes | The longest time an answer is cached. |
| `NegativeCacheDuration` | 5 minutes | The longest time an NXDOMAIN or empty answer is cached. |
| `MaximumCacheDuration` | 1 hour | The longest time any answer is cached. |
| `MaxCacheEntries` | 10000 | The number of cached answers. 0 disables the cache. |

- An answer is cached for the lowest TTL of its records, capped by these durations. A negative answer is cached for the
  TTL of its SOA record, capped the same way, or for `NegativeCacheDuration` without SOA.
- `ServerFailure`, `Refused` and the other error codes are not cached, nor are blocked, rewritten and custom answers.
- A cached answer is served with its remaining lifetime as the TTL of its records.
- When the cache is full, the expired answers are removed first, then the oldest ones.

## Rate limit

`MaxDnsQueriesPerClientPerMinute` (600 by default) limits the queries of each client IP address, in fixed one-minute
windows. The queries over the limit get `Refused`. 0 disables the limit.

## DNSSEC

The proxy forwards the DNSSEC OK flag of the client to the upstream, and returns the DNSSEC records (`RRSIG`, `NSEC`,
`NSEC3`, `DS`, `DNSKEY`) to the client, so a validating client can check the answers itself. It never sets the AD
flag, since it does not tell the client that it validated an answer.

`DnssecValidationMode` (`None` by default) is passed to the upstream clients. With `Local`, they validate each answer
locally, but the proxy does not use the result: a `Bogus` answer is still returned to the client. `Local` also sets the
CD flag on the upstream queries, which asks the upstream resolvers not to validate. Keep the default `None` unless you
need the validation work for another reason.
