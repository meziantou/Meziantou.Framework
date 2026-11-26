using BenchmarkDotNet.Running;
using Meziantou.Framework.PublicApiGenerator.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(GenerateBenchmarks).Assembly).Run(args);
