---
title: Limitations
description: What the MsQuic and WebTransport packages do not do.
---

- Only 64-bit processes are supported, and the packages do not run in the browser.
- Client certificates are not supported.
- On Linux and macOS, the private key of the server certificate must be exportable (`X509KeyStorageFlags.Exportable`):
  MsQuic loads the certificate from a PKCS #12 export, as `System.Net.Quic` does.
- MsQuic does not expose the TLS exporter, so the keying material of a session cannot be exported.
- The HTTP/3 layer of `Meziantou.Framework.WebTransport` only serves WebTransport: the QPACK dynamic table is disabled,
  and the other requests get a 501 response.
- A client session has its own QUIC connection: sessions are not pooled.
- The flow control of WebTransport (the `WT_MAX_DATA` and `WT_MAX_STREAMS` capsules of draft 09 and later) is not
  implemented: QUIC flow control applies.
