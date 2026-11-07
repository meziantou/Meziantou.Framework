# Meziantou.Framework.ValueStopwatch

`Meziantou.Framework.ValueStopwatch` provides `ValueStopwatch`, a simple equivalent of `Stopwatch`, but as a struct. It helps reduce allocations.

<!-- ref:../../tests/Meziantou.Framework.ValueStopwatch.Tests/ValueStopwatchTest.cs;region=ValueStopwatch_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Start a new stopwatch
ValueStopwatch stopwatch = ValueStopwatch.StartNew();

// Get elapsed time
TimeSpan elapsedTime = stopwatch.GetElapsedTime();
```
<!-- endref -->