using BenchmarkDotNet.Running;
using AssertionsBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ValueAssertionBenchmark).Assembly).Run(args);
