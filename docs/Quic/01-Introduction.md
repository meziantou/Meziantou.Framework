---
title: Introduction
description: Two packages to use the QUIC features System.Net.Quic does not expose, and WebTransport over HTTP/3.
---

| Package | Use it to |
| --- | --- |
| `Meziantou.Framework.MsQuic` | Open QUIC connections with datagrams (RFC 9221), stream priorities, connection statistics and the choice of the congestion control algorithm. See [QUIC connections](03-QuicConnections.md). |
| `Meziantou.Framework.WebTransport` | Open WebTransport sessions over HTTP/3 as a client, or accept them as a server: streams, datagrams, close codes and reasons. See [WebTransport sessions](04-WebTransportSessions.md). |

`System.Net.Quic` and `Meziantou.Framework.MsQuic` both run on [MsQuic](https://github.com/microsoft/msquic), and can
run in the same process. The second one exposes the features of MsQuic that `System.Net.Quic` keeps internal, which
WebTransport needs.

## Requirements

- MsQuic: it ships with .NET on Windows. On Linux, install the `libmsquic` package; on macOS, run
  `brew install libmsquic`. See [Getting started](02-GettingStarted.md).
- A 64-bit process.
- The packages do not run in the browser.

## Next steps

- [Getting started](02-GettingStarted.md): install MsQuic, open a connection and a session.
- [Limitations](05-Limitations.md): what the packages do not do.
