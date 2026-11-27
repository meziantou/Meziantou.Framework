using BenchmarkDotNet.Running;
using Meziantou.Framework.SyntaxHighlighting.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(HighlightBenchmarks).Assembly).Run(args);
