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
- Default filter lists:
  - AdGuard DNS filter (`https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt`)
  - StevenBlack hosts (`https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts`)
- Remote DNS servers (DoQ): Cloudflare (`cloudflare-dns.com`), Quad9 (`dns.quad9.net`), and NextDNS (`dns.nextdns.io`)
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
