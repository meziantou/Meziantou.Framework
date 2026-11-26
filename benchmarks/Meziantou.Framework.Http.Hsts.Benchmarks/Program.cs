using BenchmarkDotNet.Running;
using Meziantou.Framework.Http.Hsts.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(MustUpgradeRequestBenchmark).Assembly).Run(args);
