using BenchmarkDotNet.Running;
using Meziantou.Framework.SimpleQueryLanguage.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BuildBenchmarks).Assembly).Run(args);
