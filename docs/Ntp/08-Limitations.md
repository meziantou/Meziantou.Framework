---
title: Limitations
description: What the NTP client and server do not do, so you can plan around it.
---

## Both packages

- **No NTS** (RFC 8915): it derives its keys from the TLS session (RFC 5705 key export), which `SslStream` does not
  expose. Use a shared key instead: see [Authentication](06-Authentication.md).
- **No Autokey** (RFC 5906), the public key authentication of NTPv4, which is deprecated.
- **Client and server modes only**: no symmetric (peer) mode, broadcast mode, or control messages.
- **NTPv3 and NTPv4 only.**

## Client

- **It does not set the clock** of the machine, and does not keep a disciplined clock: each call measures the offset
  again, from new samples.
- **A simplified filter and selection.** [MeasureAsync](cref:Meziantou.Framework.Ntp.NtpClient.MeasureAsync) keeps the sample with the shortest delay and selects the
  servers with the intersection algorithm of RFC 5905, but it does not age the samples between calls, nor run the
  cluster algorithm that prunes the survivors by jitter. See [Measuring accurately](04-MeasuringAccurately.md).

## Server

- **It does not synchronize its clock.** It answers with its `TimeProvider`, so it is as accurate as the machine. See
  [Describe the quality of the clock](07-RunningAServer.md#describe-the-quality-of-the-clock).
- **One address per server**: start several servers to listen on several addresses, such as IPv4 and IPv6.
- **No leap second announcements**: the leap indicator is always "no warning".
- **No access control by address.** Every client that reaches the server is answered, within the rate limit, unless
  [RequireAuthentication](cref:Meziantou.Framework.Ntp.NtpServerOptions.RequireAuthentication) restricts it to the clients that have a key.
- **The `RATE` reply has no timestamps**, so [NtpClient](cref:Meziantou.Framework.Ntp.NtpClient) ignores it, even with [ValidateResponse](cref:Meziantou.Framework.Ntp.NtpClientOptions.ValidateResponse) off.
