using BenchmarkDotNet.Running;
using Meziantou.Framework.Sanitizers.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(HtmlSanitizerBenchmark).Assembly).Run(args);
