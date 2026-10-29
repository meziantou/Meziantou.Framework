---
title: Limitations
description: What the NTP client and server do not do, so you can plan around it.
---

## Both packages

- **No authentication.** Neither NTS (RFC 8915) nor symmetric key authentication is implemented. See
  [What the checks do not protect against](04-ResponseValidation.md#what-the-checks-do-not-protect-against).
- **Client and server modes only**: no symmetric (peer) mode, broadcast mode, or control messages.
- **NTPv3 and NTPv4 only.**

## Client

- **It does not set the clock** of the machine, and does not keep a disciplined clock: each call is one measurement.
- **One measurement per call**, without the filtering of several samples that a full NTP implementation does. Query
  several times, or several servers, and combine the results when you need accuracy.
- **One server per call**: the client does not choose between servers.
- **No retry.** A lost reply ends in `TimeoutException`.

## Server

- **It does not synchronize its clock.** It answers with its `TimeProvider`, so it is as accurate as the machine. See
  [Describe the quality of the clock](05-RunningAServer.md#describe-the-quality-of-the-clock).
- **One address per server**: start several servers to listen on several addresses, such as IPv4 and IPv6.
- **No leap second announcements**: the leap indicator is always "no warning".
- **No access control.** Every client that reaches the server is answered, within the rate limit.
- **The `RATE` reply has no timestamps**, so `NtpClient` ignores it, even with `ValidateResponse` off.
