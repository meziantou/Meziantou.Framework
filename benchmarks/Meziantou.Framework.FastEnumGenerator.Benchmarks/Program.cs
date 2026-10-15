using BenchmarkDotNet.Running;
using Meziantou.Framework.FastEnumGenerator.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(FastEnumGeneratedMethodsBenchmark).Assembly).Run(args);
