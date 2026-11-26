---
title: QUIC connections
description: Connect and listen with MsQuic, and use datagrams, streams, priorities, statistics and close codes.
---

[MsQuicConnection](cref:Meziantou.Framework.MsQuic.MsQuicConnection),
[MsQuicListener](cref:Meziantou.Framework.MsQuic.MsQuicListener) and
[MsQuicStream](cref:Meziantou.Framework.MsQuic.MsQuicStream) follow the shape of `QuicConnection`, `QuicListener` and
`QuicStream` of `System.Net.Quic`. See [Getting started](02-GettingStarted.md) for a complete example.

## Connect and listen

- [MsQuicConnection.ConnectAsync](cref:Meziantou.Framework.MsQuic.MsQuicConnection.ConnectAsync) takes
  [MsQuicClientConnectionOptions](cref:Meziantou.Framework.MsQuic.MsQuicClientConnectionOptions): the address (an
  `IPEndPoint`, or a `DnsEndPoint` whose addresses are tried in order) and the TLS options. The application protocols
  (ALPN) are required.
- The certificate of the server is validated like `SslStream` does. `RemoteCertificateValidationCallback` can decide
  instead, for instance to accept a certificate by its hash.
- [MsQuicListener.ListenAsync](cref:Meziantou.Framework.MsQuic.MsQuicListener.ListenAsync) takes the address and the
  application protocols, and a callback that returns the options of each connection, from its server name. Return the
  same [MsQuicServerConnectionOptions](cref:Meziantou.Framework.MsQuic.MsQuicServerConnectionOptions) instance for the
  same options: the listener caches the configuration of each instance.

## Datagrams

Datagrams are unreliable and unordered. Both sides must set
[EnableDatagrams](cref:Meziantou.Framework.MsQuic.MsQuicConnectionOptions.EnableDatagrams) before either can send one.

- [IsDatagramSendEnabled](cref:Meziantou.Framework.MsQuic.MsQuicConnection.IsDatagramSendEnabled) tells whether
  datagrams can be sent now.
- [MaxDatagramSendSize](cref:Meziantou.Framework.MsQuic.MsQuicConnection.MaxDatagramSendSize) can grow during the
  connection, as path MTU discovery finds a larger MTU: read it before each send.
- [SendDatagramAsync](cref:Meziantou.Framework.MsQuic.MsQuicConnection.SendDatagramAsync) completes when the datagram is
  sent on the network, and throws an [MsQuicException](cref:Meziantou.Framework.MsQuic.MsQuicException) for a datagram
  that is too large.
- [ReceiveDatagramAsync](cref:Meziantou.Framework.MsQuic.MsQuicConnection.ReceiveDatagramAsync) returns the next
  datagram. When the application does not read them fast enough, the oldest are dropped, beyond
  [ReceivedDatagramQueueCapacity](cref:Meziantou.Framework.MsQuic.MsQuicConnectionOptions.ReceivedDatagramQueueCapacity).

## Streams

- [OpenOutboundStreamAsync](cref:Meziantou.Framework.MsQuic.MsQuicConnection.OpenOutboundStreamAsync) waits until the
  peer allows one more stream, or fails with `StreamLimitReached` when `waitUntilAvailable` is `false`.
- [AcceptInboundStreamAsync](cref:Meziantou.Framework.MsQuic.MsQuicConnection.AcceptInboundStreamAsync) returns the
  streams the peer opens.
- `WriteAsync` with `completeWrites: true` sends the data and the end of the stream in the same packet.
  [Abort](cref:Meziantou.Framework.MsQuic.MsQuicStream.Abort) sends RESET_STREAM or STOP_SENDING with an application
  error code, which the peer reads in [MsQuicException.ApplicationErrorCode](cref:Meziantou.Framework.MsQuic.MsQuicException.ApplicationErrorCode).
- [Priority](cref:Meziantou.Framework.MsQuic.MsQuicStream.Priority) goes from 0 to 65535: the data of the streams with
  a higher priority is sent first.
- Canceling a read or a write aborts that side of the stream.

## Statistics and congestion control

[GetStatistics](cref:Meziantou.Framework.MsQuic.MsQuicConnection.GetStatistics) returns the round-trip time, the bytes
and packets sent and received, the lost packets and the congestion window.
[CongestionControlAlgorithm](cref:Meziantou.Framework.MsQuic.MsQuicConnectionOptions.CongestionControlAlgorithm) picks
CUBIC or BBR.

## Close

[CloseAsync](cref:Meziantou.Framework.MsQuic.MsQuicConnection.CloseAsync) closes the connection with an application
error code. [Closed](cref:Meziantou.Framework.MsQuic.MsQuicConnection.Closed) completes with the reason the connection
closed: the error code of the peer, the idle timeout... Dispose the connections, the streams and the listeners: MsQuic
keeps a connection alive until it is closed.
