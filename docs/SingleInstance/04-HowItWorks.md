---
title: How it works
description: The scope of an instance on each operating system, the lifetime of the SingleInstance object, and how malformed messages are handled.
---

## Detecting the first instance

`StartApplication` tries to acquire a named mutex derived from the application id, without waiting. The process that
owns the mutex is the first instance; the others get `false`. When the first instance exits, the operating system
releases the mutex, and the next process that calls `StartApplication` becomes the first instance. A mutex left
abandoned by a process that crashed is acquired as well.

Calling `StartApplication` again after it returned `true` returns `true` and does nothing else. Calling it after
`Dispose` throws an `ObjectDisposedException`.

## Scope of an instance

| Operating system | Scope |
| --- | --- |
| Windows | The logon session of the user: the mutex and the pipe are created in the `Local\` namespace, and the pipe name includes the session id |
| Linux, macOS | The user: the names include a hash of the user name, so other users of the machine get their own instance |

On every operating system, the pipe only accepts connections from processes of the same user.

## Notifications

When `StartServer` is `true` (the default), the first instance listens on a named pipe derived from the application
id. On Linux and macOS, .NET implements named pipes with Unix domain sockets, whose file lives in the temporary folder.

`NotifyFirstInstance` connects to the pipe, sends the process id and the arguments in one message, and closes the
connection. The first instance raises `NewInstance` for each message. A message that is truncated or malformed is
ignored, and the first instance keeps accepting the next notifications.

## Lifetime

`SingleInstance` implements `IDisposable`. `Dispose` stops the notification server and closes the mutex, so keep
the object alive for as long as the application runs: store it in a field of the application, or declare it with
`using var` at the top of `Main`, as in [Getting started](02-GettingStarted.md).
