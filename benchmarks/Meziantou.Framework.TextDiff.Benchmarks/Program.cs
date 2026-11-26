using BenchmarkDotNet.Running;
using Meziantou.Framework.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(TextDiffBenchmarks).Assembly).Run(args);
