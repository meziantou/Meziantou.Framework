---
title: WebTransport sessions
description: Open WebTransport sessions as a client, accept them as a server, and use their streams and datagrams.
---

A WebTransport session is an extended CONNECT request on an HTTP/3 connection. See
[Getting started](02-GettingStarted.md) for a complete example.

## Client

[WebTransportSession.ConnectAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.ConnectAsync) opens a
QUIC connection to the host of the `https` URL, checks that the server supports WebTransport, and sends the CONNECT
request. [WebTransportClientOptions](cref:Meziantou.Framework.WebTransport.WebTransportClientOptions) sets:

- `Headers`: headers added to the request, such as `origin`.
- `Protocols`: the application protocols the client offers. The server picks one, in
  [Protocol](cref:Meziantou.Framework.WebTransport.WebTransportSession.Protocol).
- `ServerCertificateHashes`: the SHA-256 hashes of the certificates to accept instead of validating the certificate
  against the trusted roots. As in the browsers, the certificate must also be valid now, for at most 14 days.

A rejected request throws a [WebTransportException](cref:Meziantou.Framework.WebTransport.WebTransportException) whose
`StatusCode` is the status of the response.

## Server

[WebTransportListener.AcceptAsync](cref:Meziantou.Framework.WebTransport.WebTransportListener.AcceptAsync) returns the
next [WebTransportSessionRequest](cref:Meziantou.Framework.WebTransport.WebTransportSessionRequest): its path, its
headers and the protocols the client offers. Accept it with
[AcceptAsync](cref:Meziantou.Framework.WebTransport.WebTransportSessionRequest.AcceptAsync), with the protocol you pick
and response headers, or reject it with
[RejectAsync](cref:Meziantou.Framework.WebTransport.WebTransportSessionRequest.RejectAsync) and a status code. A
connection can carry several sessions.

## Streams and datagrams

- [OpenBidirectionalStreamAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.OpenBidirectionalStreamAsync),
  [OpenUnidirectionalStreamAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.OpenUnidirectionalStreamAsync)
  and [AcceptStreamAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.AcceptStreamAsync) work in both
  directions.
- [WebTransportStream.Abort](cref:Meziantou.Framework.WebTransport.WebTransportStream.Abort) takes a 32-bit application
  error code, which the peer reads in
  [StreamErrorCode](cref:Meziantou.Framework.WebTransport.WebTransportException.StreamErrorCode).
- [SendDatagramAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.SendDatagramAsync) sends at most
  [MaxDatagramSize](cref:Meziantou.Framework.WebTransport.WebTransportSession.MaxDatagramSize) bytes; the size can grow
  during the session.

## Close and drain

- [CloseAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.CloseAsync) closes the session with an error
  code and a reason of at most 1024 bytes. The streams of the session are aborted.
- [Closed](cref:Meziantou.Framework.WebTransport.WebTransportSession.Closed) completes with the code and the reason of
  the peer, or `0` and an empty reason when the peer ends the session without one. It faults when the session ends
  abruptly: the connection is lost, or the CONNECT stream is reset.
- [DrainAsync](cref:Meziantou.Framework.WebTransport.WebTransportSession.DrainAsync) asks the peer to end the session,
  and [Draining](cref:Meziantou.Framework.WebTransport.WebTransportSession.Draining) completes when the peer asks.
