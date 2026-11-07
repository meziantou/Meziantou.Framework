using BenchmarkDotNet.Running;
using Meziantou.Framework.Barcodes.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(PngBenchmarks).Assembly).Run(args);
