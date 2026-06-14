using BenchmarkDotNet.Running;
using SnapshotTestingBenchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ImageHashBenchmark).Assembly).Run(args);
