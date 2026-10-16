using BenchmarkDotNet.Running;
using Meziantou.Framework.SnapshotTesting.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ImageHashBenchmark).Assembly).Run(args);
