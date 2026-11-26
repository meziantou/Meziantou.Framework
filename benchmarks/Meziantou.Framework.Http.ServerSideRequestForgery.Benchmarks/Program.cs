using BenchmarkDotNet.Running;
using Meziantou.Framework.Http.ServerSideRequestForgery.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ConnectPipelineBenchmark).Assembly).Run(args);
