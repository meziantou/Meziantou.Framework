# Meziantou.DnsProxy

This project provides a DNS proxy service built with:

- `Meziantou.Framework.DnsServer` to accept queries from clients
- `Meziantou.Framework.DnsFilter` to apply filter lists
- `Meziantou.Framework.DnsClient` to forward queries to multiple upstream DNS servers

Pipeline:

`client -> access policy -> rate limit -> custom records -> filter -> cache/forward to remotes (in priority order) -> response to client`

Default configuration:

- DNS listeners: UDP/TCP on port `5053`, bound to loopback only (`BindAddresses` = `127.0.0.1`, `::1`)
- Allowed client networks: empty, meaning every client that can reach a listener is allowed (loopback is always allowed)
- Web diagnostics UI on port `5080`
- DNS over HTTPS listener: disabled by default (`DnsOverHttpsPort=0`; endpoint path `/dns-query`)
- DNS over TLS listener: disabled by default (`DnsOverTlsPort=0`)
- DNS over QUIC listener: disabled by default (`DnsOverQuicPort=0`)
- Filter list refresh interval: `00:30:00`
- Block list cache folder: local application data under `meziantou/dnsproxy/block-lists`
- DNS cache durations: positive `00:05:00`, negative `00:05:00`, maximum `01:00:00`
- DNS cache size: `10000` entries (`MaxCacheEntries`)
- Per-client rate limit: `600` queries per minute (`MaxDnsQueriesPerClientPerMinute`)
- Filter list download: `00:02:00` timeout, `67108864` bytes maximum per list
- DNSSEC validation: disabled by default (`DnssecValidationMode=None`; use `Local` to enable local validation)
- Bootstrap DNS servers: Quad9 (`9.9.9.9`, `149.112.112.112`, `2620:fe::fe`, `2620:fe::9`) and Cloudflare (`1.1.1.1`, `1.0.0.1`, `2606:4700:4700::1111`, `2606:4700:4700::1001`)
- Default filter lists:
  - AdGuard DNS filter (`https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt`)
  - StevenBlack hosts (`https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts`)
- Custom DNS records: `localhost A 127.0.0.1` and `localhost AAAA ::1`
- Remote DNS servers: Cloudflare H3, NextDNS DoQ, Quad9 DoQ, Cloudflare DoH, NextDNS DoH, and Quad9 DoH
- In-memory diagnostics history size: `10000` entries (the page renders the 200 most recent; use `/?limit=N` for more)

Enabling secure listeners (DoH/DoT/DoQ):

- Set one or more of:
  - `DnsProxy__DnsOverHttpsPort`
  - `DnsProxy__DnsOverTlsPort`
  - `DnsProxy__DnsOverQuicPort`
- Configure certificate:
  - `DnsProxy__CertificatePath`
  - `DnsProxy__CertificatePassword`
- Optional DoH path override:
  - `DnsProxy__DnsOverHttpsPath`

Notes:

- When `DnsOverHttpsPort` is enabled, the DoH endpoint is served over HTTPS on that port.
- When disabled (`0`), the existing HTTP endpoint remains available on `HttpPort` for diagnostics/testing.

Serving other machines:

- The DNS listeners bind to loopback by default. To serve other machines on the network, set the bind addresses
  explicitly and restrict which clients may query:
  - `DnsProxy__BindAddresses__0=0.0.0.0`
  - `DnsProxy__BindAddresses__1=::`
  - `DnsProxy__AllowedClientNetworks__0=192.168.1.0/24`
- `AllowedClientNetworks` accepts CIDR ranges or bare addresses. When the list is empty, every client that can reach a
  listener is allowed, so it should be set whenever the proxy is not bound to loopback. Loopback clients are always
  allowed so the diagnostics page and the local DNS over HTTPS endpoint keep working.
- Queries from a client outside the allowed networks are answered with `Refused`.

DNSSEC:

- The client's DNSSEC-OK (DO) bit is forwarded to the upstream server, and DNSSEC records (`RRSIG`, `NSEC`, `NSEC3`,
  `DS`, `DNSKEY`) are returned to the client, so a validating client can verify answers itself.
- The `AD` bit is not forwarded: the proxy does not validate signatures unless `DnssecValidationMode` is `Local`, so it
  does not report answers as authenticated.

Upstream failover:

- Upstreams are tried in `Priority` order. A transport failure, a `ServerFailure` or a `Refused` response moves on to
  the next upstream; `NoError` and `NameError` (NXDOMAIN) are real answers and are returned immediately.
- When every upstream is unhealthy, the last `ServerFailure`/`Refused` response is returned to the client.

Diagnostics:

- Open `/` in the browser to inspect recent DNS requests/responses and client information.
- The diagnostics listener is bound to loopback, `AllowedHosts` is set to `localhost` to block DNS rebinding, and the
  "disable filtering" action requires an antiforgery token.

Parallel instances:

- Override settings using environment variables such as:
  - `DnsProxy__DnsPort`
  - `DnsProxy__HttpPort`
  - `DnsProxy__BindAddresses__0`
  - `DnsProxy__AllowedClientNetworks__0`
  - `DnsProxy__FilterRefreshInterval`
  - `DnsProxy__FilterDownloadTimeout`
  - `DnsProxy__MaxFilterListSizeInBytes`
  - `DnsProxy__BlockListCacheFolderPath`
  - `DnsProxy__PositiveCacheDuration`
  - `DnsProxy__NegativeCacheDuration`
  - `DnsProxy__MaximumCacheDuration`
  - `DnsProxy__MaxCacheEntries`
  - `DnsProxy__MaxDnsQueriesPerClientPerMinute`
  - `DnsProxy__DnssecValidationMode`
  - `DnsProxy__CustomRecords__0__Domain`
  - `DnsProxy__CustomRecords__0__Type`
  - `DnsProxy__CustomRecords__0__Value`
  - `DnsProxy__BootstrapDnsServers__0`
  - `DnsProxy__Upstreams__0__Url`

Custom records:

- Custom records are answered before filtering, cache, and upstream forwarding.
- Block lists and temporary filtering pauses do not affect custom records.
- Use `Value` for one answer or `Values` for multiple answers of the same type.
- Supported formats:
  - `A`: `192.168.1.11`
  - `AAAA`: `::1`
  - `CNAME`, `PTR`, `NS`: domain name
  - `TXT`: text
  - `MX`: `10 mail.sample.local`
  - `SRV`: `10 5 443 service.sample.local`
  - `CAA`: `0 issue letsencrypt.org`
