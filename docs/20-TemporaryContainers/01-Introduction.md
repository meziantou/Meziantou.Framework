---
title: Introduction
description: Start throwaway containers from .NET integration tests, on Docker, Podman, Apple container or wslc.
---

`Meziantou.Framework.TemporaryContainers` starts containers for integration tests: a real PostgreSQL, Redis or SQL Server
for the duration of a test, or your own image built from a Dockerfile. The container is removed when you dispose it.

The library drives the container runtime that is installed on the machine, through its command line or the Docker
Engine API. It needs no daemon SDK, and the same code runs on Docker, Podman, Apple's `container` on macOS, and `wslc` on
Windows.

## What you can do with it

- **Start a database in one line** with the ready-made definitions for Redis, PostgreSQL, MongoDB and SQL Server, which
  generate a strong password and give you the connection string. See [Databases](03-Databases.md).
- **Run any image**: from a registry, built from a Dockerfile, loaded from an archive, or already on the machine, with
  the command, environment variables, ports, volumes and resource limits you choose. See
  [Configuring a container](04-Configuration/01-Overview.md).
- **Know when the container is ready**: wait for a log message, an open port, or a check of your own before your test
  runs. See [Wait strategies](04-Configuration/06-WaitStrategies.md).
- **Work with the running container**: run commands in it, copy files in and out, read its logs, stop and restart it.
  See [Working with a container](05-WorkingWithContainers.md).
- **Use the runtime you have**: the runtime is detected automatically, or you choose it. See
  [Container runtimes](06-Runtimes.md).
- **Leave nothing behind**: disposing removes the container, a cleanup removes what an interrupted run left, and a
  watchdog removes the containers of a process that was killed. See [Cleaning up](07-Cleanup.md).
- **Share one container between test processes** to start a slow database once. See
  [Reusing a container](08-ReusingContainers.md).

## Principles

- **Safe by default.** Ports are published on the loopback address only, and the database definitions generate a random
  password, so a test database is not reachable from the network.
- **Ready means ready.** `StartAsync` returns once the wait strategies agree that the service accepts connections, not
  when the process starts.
- **Explicit failures.** A runtime that refuses an operation throws a `ContainerRuntimeException` that tells what it ran
  and what it answered; a container that is not ready in time throws a `TimeoutException` with its state and its last
  log lines. See [Troubleshooting](10-Troubleshooting.md).
- **Everything is labeled.** Every container, volume and built image records the process that created it, so a later
  run can remove the leftovers of a run that was killed without touching anything else.

## Namespace

Every type is in the `Meziantou.Framework.TemporaryContainers` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and start your first container.
- [Using containers in tests and CI](09-TestFrameworksAndCI.md): share containers between tests and run them on a build
  agent.
