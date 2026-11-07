---
title: Response validation
description: Which NTP replies the client accepts, Kiss-o'-Death refusals, and why the result must not drive a security decision.
---

## What the client checks

The socket of the client is connected to the server, so the operating system drops datagrams from any other address.
With [ValidateResponse](cref:Meziantou.Framework.Ntp.NtpClientOptions.ValidateResponse) set to `true`, the default, the client also requires that a reply:

- echoes the transmit timestamp of the request in its originate timestamp, byte for byte (RFC 5905 TEST2), which ties
  the reply to this request;
- has version 3 or 4, and the server mode;
- is not a Kiss-o'-Death packet (stratum 0);
- does not report an alarm condition (`LeapIndicator.AlarmCondition`), which means the clock of the server is not
  synchronized.

A reply that fails is ignored, and the client keeps waiting for a valid one until [Timeout](cref:Meziantou.Framework.Ntp.NtpClientOptions.Timeout): a single stray or forged
datagram cannot make the query fail. Even with [ValidateResponse](cref:Meziantou.Framework.Ntp.NtpClientOptions.ValidateResponse) set to `false`, the client ignores packets that are
too short, and replies without receive and transmit timestamps.

## Kiss-o'-Death replies

A server that refuses to answer sends a Kiss-o'-Death packet: stratum 0, and a four-letter code in the reference
identifier, such as `RATE` (the client queries too often), `DENY` or `RSTR` (the client is not allowed). With
validation on, the client ignores it and the query ends with `TimeoutException`.

To see the code, set [ValidateResponse](cref:Meziantou.Framework.Ntp.NtpClientOptions.ValidateResponse) to `false`, then check [IsKissOfDeath](cref:Meziantou.Framework.Ntp.NtpResponse.IsKissOfDeath) and [KissCode](cref:Meziantou.Framework.Ntp.NtpResponse.KissCode) on the response. The
timestamps of such a reply carry no time: do not use its offset. When a server sends `RATE`, query it less often.

This only works with servers whose refusal carries timestamps: the `RATE` reply of [NtpServer](cref:Meziantou.Framework.Ntp.NtpServer) has none, so the client
ignores it even with validation off. See [Rate limiting](05-RunningAServer.md#rate-limiting).

## What the checks do not protect against

The checks tie a reply to the request; they do not authenticate the server. The library implements neither NTS
(RFC 8915) nor symmetric key authentication, so an attacker who can read and inject packets on the path to the server
can still choose the time the client reports.

Do not use the result alone for a security decision, such as the validity period of a certificate or a token. To limit
the risk, query several servers and compare their answers, and check [RootDispersion](cref:Meziantou.Framework.Ntp.NtpResponse.RootDispersion) and [Stratum](cref:Meziantou.Framework.Ntp.NtpResponse.Stratum).
