using BenchmarkDotNet.Running;
using Meziantou.Framework.BloomFilters.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BloomFilterBenchmark).Assembly).Run(args);
