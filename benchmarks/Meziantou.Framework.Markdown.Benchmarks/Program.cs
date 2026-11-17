using BenchmarkDotNet.Running;
using Meziantou.Framework.Markdown.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(MarkdownBenchmark).Assembly).Run(args);
