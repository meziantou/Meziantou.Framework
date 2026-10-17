using BenchmarkDotNet.Running;
using Meziantou.Framework.Globbing.Benchmarks;

BenchmarkRunner.Run<GlobIsMatchBenchmark>();
BenchmarkRunner.Run<EnumerateFilesBenchmark>();
