using BenchmarkDotNet.Running;
using YamlBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RepeatedSerializationBenchmark).Assembly).Run(args);
