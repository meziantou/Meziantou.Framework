---
title: Serving the network
description: Listen on the network, restrict the clients that may query the proxy, and enable DNS over TLS, HTTPS and QUIC.
---

## Listen on the network

The DNS listeners bind to the loopback addresses by default, so only the machine itself can query the proxy. To serve
other machines, set the addresses to listen on, and restrict the clients:

```bash
Meziantou.DnsProxy --DnsProxy:DnsPort=53 --DnsProxy:BindAddresses:0=0.0.0.0 --DnsProxy:BindAddresses:1=:: --DnsProxy:AllowedClientNetworks:0=192.168.1.0/24 --DnsProxy:AllowedClientNetworks:1=fd00::/8
```

| Setting | Default | Use |
| --- | --- | --- |
| `DnsPort` | 5053 | The port of the UDP and TCP listeners. |
| `BindAddresses` | `127.0.0.1`, `::1` | The addresses of the DNS listeners. `0.0.0.0` and `::` listen on every interface. An invalid address stops the proxy at startup. |
| `AllowedClientNetworks` | (empty) | The networks (`192.168.1.0/24`) or addresses of the clients that may query the proxy. |

- When `AllowedClientNetworks` is empty, every client that can reach a listener is allowed: always set it when the
  proxy listens on more than the loopback addresses, or the proxy can be used by anyone who reaches it, for instance
  to amplify attacks.
- Loopback clients are always allowed, so the diagnostics page and the local DNS over HTTPS endpoint keep working.
- A client outside the allowed networks gets `Refused`.
- An invalid entry of `AllowedClientNetworks` is skipped with a warning. Check the log: when every entry is invalid,
  the list is empty, and every client is allowed.

## Enable DNS over TLS, HTTPS and QUIC

Secure listeners are disabled by default (port 0). Set their ports, and the certificate they present:

```bash
Meziantou.DnsProxy --DnsProxy:DnsOverTlsPort=853 --DnsProxy:DnsOverQuicPort=853 --DnsProxy:DnsOverHttpsPort=443 --DnsProxy:CertificatePath=/etc/dnsproxy/dns.pfx --DnsProxy:CertificatePassword=...
```

| Setting | Default | Use |
| --- | --- | --- |
| `DnsOverTlsPort` | 0 (disabled) | DNS over TLS, on each of `BindAddresses`. |
| `DnsOverQuicPort` | 0 (disabled) | DNS over QUIC, on each of `BindAddresses`. |
| `DnsOverHttpsPort` | 0 (disabled) | DNS over HTTPS, on the loopback address only. |
| `DnsOverHttpsPath` | `/dns-query` | The path of the DNS over HTTPS endpoint. |
| `CertificatePath`, `CertificatePassword` | (none) | The certificate of the secure listeners, as a PFX (PKCS#12) file, and its password. |

- One certificate serves the three listeners. Without `CertificatePath`, enabling a secure listener stops the proxy at
  startup.
- The DNS over HTTPS listener is bound to the loopback address, even when `BindAddresses` lists other addresses: to
  serve it to the network, put a reverse proxy in front of it.
- When `DnsOverHttpsPort` is 0, the DNS over HTTPS path is still served over plain HTTP on `HttpPort`, the port of the
  diagnostics page, on the loopback address, for tests.
- DNS over QUIC needs QUIC support from the operating system; without it, the proxy logs a warning and the other
  listeners run normally.
- Secret values such as `CertificatePassword` are better set through environment variables or a protected
  configuration file than on the command line, where other users of the machine can see them.
