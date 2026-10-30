---
title: Introduction
description: An NTP client to measure the offset of the local clock, and an NTP server that answers with the time of the machine.
---

NTP (Network Time Protocol) is how computers get the time from a server. Two packages implement it, versions 3 and 4:

| Package | Use it to |
| --- | --- |
| `Meziantou.Framework.NtpClient` | Ask an NTP server for the time, and compute the offset of the local clock and the round-trip delay. See [Querying a server](03-QueryingAServer.md). |
| `Meziantou.Framework.NtpServer` | Answer NTP queries with the time of the machine, or of any `TimeProvider`, for instance in tests. See [Running a server](05-RunningAServer.md). |

Both are in the [Meziantou.Framework.Ntp](cref:Meziantou.Framework.Ntp) namespace, target .NET 10 and later, and are compatible with trimming.

## What you can do with them

- **Check the clock of a machine**: compare it with a time server, and alert when the offset is too large.
- **Get a reliable time** when the local clock cannot be trusted, by adding the offset to it.
- **Test code that reads the time over NTP**: run a server on the loopback interface whose clock is a fake
  `TimeProvider`, at any date.
- **Serve the time on an isolated network**, from a machine whose clock is accurate.

Neither package sets the clock of the machine: the client measures, and the server answers. Neither authenticates the
other side. See [Limitations](06-Limitations.md).

## Next steps

- [Getting started](02-GettingStarted.md): query a server.
- [Response validation](04-ResponseValidation.md): what the client accepts, and what it does not protect against.
