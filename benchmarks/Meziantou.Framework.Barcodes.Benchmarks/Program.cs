using BenchmarkDotNet.Running;
using Meziantou.Framework.Barcodes.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RenderingBenchmarks).Assembly).Run(args);
