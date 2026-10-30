---
title: Introduction
description: OTLP receiver endpoints for ASP.NET Core, over HTTP and gRPC, with handlers, samplers, tail sampling and an in-memory store for tests.
---

The `Meziantou.Framework.OpenTelemetryCollector` packages receive the telemetry that applications export with the
OpenTelemetry protocol (OTLP): logs, traces and metrics. They add the OTLP endpoints to an ASP.NET Core application, over
HTTP and gRPC, and pass each export request to handlers that you write, or to an in-memory store.

Use them to collect telemetry in your own service, or in tests, to check what an application exports.

## What you can do with them

- **Host the OTLP endpoints** and receive telemetry in memory. See [Getting started](02-GettingStarted.md).
- **Assert on the exported telemetry in tests**, with the in-memory receiver. See
  [Testing with the in-memory receiver](03-InMemoryReceiver.md).
- **Handle the telemetry in code**: store it, forward it, or reject part of it. See
  [Custom handlers](04-CustomHandlers.md).
- **Configure the endpoints**: paths, HTTP and gRPC, Protobuf and JSON, compression and security. See
  [Endpoints](05-Endpoints.md).
- **Drop telemetry** before it reaches the handlers, request by request or trace by trace once the whole trace is
  received. See [Sampling](06-Sampling.md) and [Tail sampling](07-TailSampling.md).

## Packages

| Package | Main types |
| --- | --- |
| `Meziantou.Framework.OpenTelemetryCollector` | `OpenTelemetryHandler`, `OpenTelemetrySampler`, `OpenTelemetryTailSampler`, `OpenTelemetryReceiverOptions` |
| `Meziantou.Framework.OpenTelemetryCollector.InMemory` | `InMemoryOpenTelemetryHandler`, `InMemoryOpenTelemetryHandlerOptions` |

The in-memory package references the main one.

## Namespaces

- `Meziantou.Framework.OpenTelemetryCollector`: the handlers, samplers, options, and the `AddOpenTelemetryReceiver` and
  `MapOpenTelemetryReceiverEndpoints` extension methods.
- `Meziantou.Framework.OpenTelemetryCollector.InMemory`: the in-memory receiver and `AddInMemoryOpenTelemetryReceiver`.
- `OpenTelemetry.Proto.*`: the OTLP messages that the handlers receive, such as `ExportLogsServiceRequest` in
  `OpenTelemetry.Proto.Collector.Logs.V1` and `LogRecord` in `OpenTelemetry.Proto.Logs.V1`. See
  [the available fields](04-CustomHandlers.md#available-fields).

## Next steps

- [Getting started](02-GettingStarted.md): install the packages and receive your first export request.
- [Testing with the in-memory receiver](03-InMemoryReceiver.md): check what an application exports. To check the logs
  an application writes through `ILogger`, without OTLP, see [Logging](../Logging/01-Introduction.md).
