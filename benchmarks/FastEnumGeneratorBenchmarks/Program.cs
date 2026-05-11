using BenchmarkDotNet.Running;
using FastEnumGeneratorBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(FastEnumGeneratedMethodsBenchmark).Assembly).Run(args);
