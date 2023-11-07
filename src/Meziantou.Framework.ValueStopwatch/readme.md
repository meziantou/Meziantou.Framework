# Meziantou.Framework.ValueStopwatch

`Meziantou.Framework.ValueStopwatch` provides `ValueStopwatch`, a simple equivalent of `Stopwatch`, but as a struct. It helps reduce allocations.

````c#
// Start a new stopwatch
ValueStopwatch stopwatch = ValueStopwatch.StartNew();

// Get elapsed time
TimeSpan elapsedTime = stopwatch.GetElapsedTime();
````