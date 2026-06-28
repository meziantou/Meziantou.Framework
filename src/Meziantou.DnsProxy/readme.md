# Meziantou.DnsProxy

This project provides a DNS proxy service built with:

- `Meziantou.Framework.DnsServer` to accept queries from clients
- `Meziantou.Framework.DnsFilter` to apply filter lists and rewrites
- `Meziantou.Framework.DnsClient` to forward queries to multiple upstream DNS servers

Pipeline:

`client -> filter -> forward to remotes (fastest) -> response to client`

Default configuration:

- DNS listeners: UDP/TCP on port `5053`
- Web diagnostics UI on port `5080`
- DNS over HTTPS listener: disabled by default (`DnsOverHttpsPort=0`; endpoint path `/dns-query`)
- DNS over TLS listener: disabled by default (`DnsOverTlsPort=0`)
- DNS over QUIC listener: disabled by default (`DnsOverQuicPort=0`)
- Filter list refresh interval: `00:30:00`
- DNS cache durations: positive `00:05:00`, negative `00:05:00`, maximum `01:00:00`
- DNSSEC validation: disabled by default (`DnssecValidationMode=None`; use `Local` to enable local validation)
- Bootstrap DNS servers: Quad9 (`9.9.9.9`, `149.112.112.112`, `2620:fe::fe`, `2620:fe::9`) and Cloudflare (`1.1.1.1`, `1.0.0.1`, `2606:4700:4700::1111`, `2606:4700:4700::1001`)
- Default filter lists:
  - AdGuard DNS filter (`https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt`)
  - StevenBlack hosts (`https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts`)
- Remote DNS servers: Cloudflare H3, NextDNS DoQ, Quad9 DoQ, Cloudflare DoH, NextDNS DoH, and Quad9 DoH
- In-memory diagnostics history size: `10000` entries

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

Diagnostics:

- Open `/` in the browser to inspect recent DNS requests/responses and client information.

Parallel instances:

- Override settings using environment variables such as:
  - `DnsProxy__DnsPort`
  - `DnsProxy__HttpPort`
  - `DnsProxy__FilterRefreshInterval`
  - `DnsProxy__PositiveCacheDuration`
  - `DnsProxy__NegativeCacheDuration`
  - `DnsProxy__MaximumCacheDuration`
  - `DnsProxy__DnssecValidationMode`
  - `DnsProxy__BootstrapDnsServers__0`
  - `DnsProxy__Upstreams__0__Url`
