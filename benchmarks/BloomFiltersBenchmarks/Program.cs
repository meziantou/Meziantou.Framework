using BenchmarkDotNet.Running;
using BloomFiltersBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BloomFilterBenchmark).Assembly).Run(args);
