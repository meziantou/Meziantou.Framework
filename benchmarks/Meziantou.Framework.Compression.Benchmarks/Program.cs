using BenchmarkDotNet.Running;
using Meziantou.Framework.Compression.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(XzBenchmark).Assembly).Run(args);
