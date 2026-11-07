using BenchmarkDotNet.Running;
using Meziantou.Framework.Threading.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(KeyedLockBenchmark).Assembly).Run(args);
