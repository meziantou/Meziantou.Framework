using BenchmarkDotNet.Running;
using Meziantou.Framework.TaggedValues.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(AnalyzerBenchmarks).Assembly).Run(args);
