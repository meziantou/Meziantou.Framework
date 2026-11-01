using BenchmarkDotNet.Running;
using Meziantou.Framework.JsonPath.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(EvaluationBenchmark).Assembly).Run(args);
