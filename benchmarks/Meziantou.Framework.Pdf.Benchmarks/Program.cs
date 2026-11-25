using BenchmarkDotNet.Running;
using Meziantou.Framework.Pdf.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(PdfBenchmark).Assembly).Run(args);
