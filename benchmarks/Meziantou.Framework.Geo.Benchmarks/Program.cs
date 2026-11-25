using BenchmarkDotNet.Running;
using Meziantou.Framework.Geo.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(FormatBenchmarks).Assembly).Run(args);
