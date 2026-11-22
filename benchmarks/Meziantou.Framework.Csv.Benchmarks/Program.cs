using BenchmarkDotNet.Running;
using Meziantou.Framework.Csv.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ReadBenchmark).Assembly).Run(args);
