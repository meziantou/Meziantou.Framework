using BenchmarkDotNet.Running;
using Meziantou.Framework.UnicodeBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(SegmentationBenchmarks).Assembly).Run(args);
