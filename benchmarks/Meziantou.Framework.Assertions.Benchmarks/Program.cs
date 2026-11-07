using BenchmarkDotNet.Running;
using Meziantou.Framework.Assertions.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ValueAssertionBenchmark).Assembly).Run(args);
