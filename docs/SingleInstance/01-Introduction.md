---
title: Introduction
description: Allow a single running instance of an application, and forward the arguments of the next launches to it.
---

`Meziantou.Framework.SingleInstance` provides the `SingleInstance` class: it tells an application whether it is the
first running instance, and lets the next instances send their command-line arguments to the first one before they
exit.

A typical use is a document editor: when the user opens a second file from the file explorer, the operating system
starts a new process. That process notices that the editor is already running, sends the file name to it, and exits.
The running editor opens the file in a new tab.

## What you can do with it

- **Detect the first instance** with `StartApplication`, which returns `true` in the first process and `false` in the
  others. See [Getting started](02-GettingStarted.md).
- **Send the arguments to the first instance** with `NotifyFirstInstance`, and handle them in the `NewInstance` event.
  See [Receiving notifications](03-ReceivingNotifications.md).
- **Only detect a running instance**, without listening for notifications. See
  [Getting started](02-GettingStarted.md#detect-a-running-instance-only).

## Principles

- **One instance per user.** Two users of the same machine can each run their own instance. See
  [How it works](04-HowItWorks.md) for the exact scope on each operating system.
- **Cross-platform.** It works on Windows, Linux and macOS. The notification uses a named pipe, which is backed by a
  Unix domain socket on Linux and macOS.
- **Identified by a GUID.** Every process of the application creates its `SingleInstance` with the same application
  id. Generate a new GUID once for your application and keep it in the code.

## Namespace

The types are in the [Meziantou.Framework](cref:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| `SingleInstance` | Detects the first instance, starts the notification server and sends notifications |
| `SingleInstanceEventArgs` | The process id and the arguments of the instance that sent a notification |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and make a console application single-instance.
- [Receiving notifications](03-ReceivingNotifications.md): handle the arguments of the next instances.
