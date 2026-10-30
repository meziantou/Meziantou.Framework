---
title: Getting started
description: Install the package, start a stopwatch, read the elapsed time, and detect a stopwatch that was never started.
---

## Install the package

Add the `Meziantou.Framework.ValueStopwatch` package to a project:

```bash
dotnet add package Meziantou.Framework.ValueStopwatch
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Measure an operation

Start the stopwatch before the operation, and read the elapsed time after it:

<!-- ref:../../tests/Meziantou.Framework.ValueStopwatch.Tests/ValueStopwatchTest.cs;region=ValueStopwatch_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Start a new stopwatch
ValueStopwatch stopwatch = ValueStopwatch.StartNew();

// Get elapsed time
TimeSpan elapsedTime = stopwatch.GetElapsedTime();
```
<!-- endref -->

[StartNew](cref:Meziantou.Framework.ValueStopwatch.StartNew) records the current timestamp. [GetElapsedTime](cref:Meziantou.Framework.ValueStopwatch.GetElapsedTime) returns the time elapsed since then, as a `TimeSpan`.

## Read the elapsed time several times

The stopwatch never stops. Each call to [GetElapsedTime](cref:Meziantou.Framework.ValueStopwatch.GetElapsedTime) measures the time since [StartNew](cref:Meziantou.Framework.ValueStopwatch.StartNew), so you can read
intermediate durations, for instance the time to the first byte and the total time of a request:

<!-- ref:../../tests/Meziantou.Framework.ValueStopwatch.Tests/ValueStopwatchTest.cs;region=ValueStopwatch_ElapsedTimeKeepsGrowing;format=md-fenced-code-block;dedent=true -->
```csharp
var stopwatch = ValueStopwatch.StartNew();

TimeSpan first = stopwatch.GetElapsedTime();
TimeSpan second = stopwatch.GetElapsedTime();

// The stopwatch never stops: each call measures the time since StartNew
Assert.True(second >= first);
```
<!-- endref -->

To measure a new interval, assign a new stopwatch: `stopwatch = ValueStopwatch.StartNew();`.

## A stopwatch that was never started

`default(ValueStopwatch)`, which is also the value of a field that was never assigned, was never started. [IsActive](cref:Meziantou.Framework.ValueStopwatch.IsActive)
tells whether a stopwatch was created by [StartNew](cref:Meziantou.Framework.ValueStopwatch.StartNew), and [GetElapsedTime](cref:Meziantou.Framework.ValueStopwatch.GetElapsedTime) throws an `InvalidOperationException` on a
stopwatch that was not:

<!-- ref:../../tests/Meziantou.Framework.ValueStopwatch.Tests/ValueStopwatchTest.cs;region=ValueStopwatch_DefaultValue;format=md-fenced-code-block;dedent=true -->
```csharp
// A default ValueStopwatch was never started
ValueStopwatch stopwatch = default;
Assert.False(stopwatch.IsActive);
Assert.Throws<InvalidOperationException>(() => stopwatch.GetElapsedTime());

stopwatch = ValueStopwatch.StartNew();
Assert.True(stopwatch.IsActive);
Assert.True(stopwatch.GetElapsedTime() >= TimeSpan.Zero);
```
<!-- endref -->

Check [IsActive](cref:Meziantou.Framework.ValueStopwatch.IsActive) before reading a stopwatch that may not have been started, for instance a field set when an
operation begins and read when it ends.

## Next steps

- [Timestamps](03-Timestamps.md): measure between two timestamps that you store yourself.
