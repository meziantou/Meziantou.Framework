---
title: Introduction
description: A stopwatch that is a struct, to measure elapsed time without allocating.
---

`Meziantou.Framework.ValueStopwatch` provides `ValueStopwatch`, a stopwatch that is a `readonly struct`. It records
the timestamp at which it starts, and computes the elapsed time from it when you ask. Starting one allocates nothing,
which makes it suitable for code that measures many short operations: middleware, request handlers, loops.

`ValueStopwatch` uses the same high-resolution timestamps as `Stopwatch`: `Stopwatch.GetTimestamp` and
`Stopwatch.GetElapsedTime`. It is based on the `ValueStopwatch` that the .NET runtime uses internally.

## What you can do with it

- **Measure the duration** of an operation with `StartNew` and `GetElapsedTime`. See
  [Getting started](02-GettingStarted.md).
- **Detect a stopwatch that was never started**, such as a default value or an uninitialized field. See
  [Getting started](02-GettingStarted.md#a-stopwatch-that-was-never-started).
- **Work with raw timestamps** that you store or compare yourself. See [Timestamps](03-Timestamps.md).

## When to use it

Use `ValueStopwatch` to measure a duration from a starting point. It has no `Stop`, `Reset` or `Restart`: it always
measures the time since `StartNew`. To pause and resume a measure, or to accumulate several intervals, use
`Stopwatch`.

`ValueStopwatch` is a regular struct, not a `ref struct`: you can store it in a field, capture it in a lambda, or keep
it across an `await`. It is immutable, so copying it is safe: the copy measures from the same starting point.

## Namespace

`ValueStopwatch` is in the `Meziantou.Framework` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and measure your first operation.
