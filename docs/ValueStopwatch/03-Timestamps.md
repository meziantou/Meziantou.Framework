---
title: Timestamps
description: Read the high-resolution timestamp and compute the time elapsed between two timestamps.
---

`ValueStopwatch` also exposes the timestamps it is built on. A timestamp is a `long`, so you can store it anywhere,
for instance in an array or in a field updated with `Interlocked`, and compute durations later:

<!-- ref:../../tests/Meziantou.Framework.ValueStopwatch.Tests/ValueStopwatchTest.cs;region=ValueStopwatch_Timestamps;format=md-fenced-code-block;dedent=true -->
```csharp
long start = ValueStopwatch.GetTimestamp();
// The operation to measure
long end = ValueStopwatch.GetTimestamp();

TimeSpan elapsed = ValueStopwatch.GetElapsedTime(start, end);
Assert.True(elapsed >= TimeSpan.Zero);

// The timestamps are ticks of Stopwatch.Frequency, so they can be stored and compared as numbers
Assert.Equal(TimeSpan.FromSeconds(1), ValueStopwatch.GetElapsedTime(0, Stopwatch.Frequency));
```
<!-- endref -->

- `ValueStopwatch.GetTimestamp()` returns the current value of the high-resolution counter, like
  `Stopwatch.GetTimestamp()`.
- `ValueStopwatch.GetElapsedTime(startTimestamp, endTimestamp)` converts the difference between two timestamps to a
  `TimeSpan`, using `Stopwatch.Frequency`, like `Stopwatch.GetElapsedTime(startTimestamp, endTimestamp)`.

A timestamp counts ticks of `Stopwatch.Frequency` from an arbitrary origin. It only makes sense compared with another
timestamp of the same machine: do not convert it to a date, and do not compare timestamps that come from different
machines, or from before a restart of the machine.
