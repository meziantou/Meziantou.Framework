using BenchmarkDotNet.Running;
using Meziantou.Framework.Yaml.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RepeatedSerializationBenchmark).Assembly).Run(args);
