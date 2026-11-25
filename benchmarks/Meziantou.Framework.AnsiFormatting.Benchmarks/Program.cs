using BenchmarkDotNet.Running;
using Meziantou.Framework.AnsiFormatting.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(AnsiTextProcessorBenchmarks).Assembly).Run(args);
