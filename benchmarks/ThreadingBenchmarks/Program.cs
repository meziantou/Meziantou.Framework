using BenchmarkDotNet.Running;
using ThreadingBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(KeyedLockBenchmark).Assembly).Run(args);
