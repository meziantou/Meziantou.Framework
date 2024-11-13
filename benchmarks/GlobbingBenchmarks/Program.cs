using BenchmarkDotNet.Running;
using GlobbingBenchmarks;

BenchmarkRunner.Run<GlobIsMatchBenchmark>();
BenchmarkRunner.Run<EnumerateFilesBenchmark>();
