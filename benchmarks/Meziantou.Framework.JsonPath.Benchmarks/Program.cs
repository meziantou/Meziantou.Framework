using BenchmarkDotNet.Running;
using Meziantou.Framework.JsonPath.Benchmarks;

BenchmarkRunner.Run<JsonPathSliceBenchmark>();
BenchmarkRunner.Run<JsonPathObjectEqualityBenchmark>();
