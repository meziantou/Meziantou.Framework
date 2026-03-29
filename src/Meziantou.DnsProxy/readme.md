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
- Filter list refresh interval: `00:30:00`
- Default filter lists:
  - AdGuard DNS filter (`https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt`)
  - StevenBlack hosts (`https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts`)
- Remote DNS servers (DoQ): Cloudflare (`cloudflare-dns.com`), Quad9 (`dns.quad9.net`), and NextDNS (`dns.nextdns.io`)
- In-memory diagnostics history size: `10000` entries

Diagnostics:

- Open `/` in the browser to inspect recent DNS requests/responses and client information.

Parallel instances:

- Override settings using environment variables such as:
  - `DnsProxy__DnsPort`
  - `DnsProxy__HttpPort`
  - `DnsProxy__FilterRefreshInterval`
