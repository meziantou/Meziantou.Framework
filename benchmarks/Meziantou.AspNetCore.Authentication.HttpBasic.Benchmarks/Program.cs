using BenchmarkDotNet.Running;
using Meziantou.AspNetCore.Authentication.HttpBasic.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(AuthenticateBenchmarks).Assembly).Run(args);
