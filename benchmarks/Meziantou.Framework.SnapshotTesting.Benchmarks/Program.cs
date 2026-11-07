using BenchmarkDotNet.Running;
using Meziantou.Framework.SnapshotTesting.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(SnapshotValidateBenchmark).Assembly).Run(args);
