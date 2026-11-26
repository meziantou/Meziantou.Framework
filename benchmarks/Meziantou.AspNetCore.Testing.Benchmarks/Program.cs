using BenchmarkDotNet.Running;
using Meziantou.AspNetCore.Testing.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(LocatorBenchmarks).Assembly).Run(args);
