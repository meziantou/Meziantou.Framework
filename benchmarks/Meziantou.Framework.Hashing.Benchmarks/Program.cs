using BenchmarkDotNet.Running;
using Meziantou.Framework.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Blake2bBenchmarks).Assembly).Run(args);
