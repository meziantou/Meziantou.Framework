#pragma warning disable MA0032 // Use an overload with a CancellationToken argument

using System.Globalization;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

namespace DependencyScanningBenchmarks;

[MemoryDiagnoser]
public class DependencyScannerBenchmark
{
    private const int N = 100_000;
    private static readonly FullPath Directory10000 = FullPath.GetTempPath() / "meziantou.framework" / "benchmarks" / "dependency_scanner_10_000";

    [Params(1, 2, 4, 8, 16, 32)]
    public int DegreeOfParallelism { get; set; }

    [GlobalSetup]
    public static void Initialize()
    {
        Directory.CreateDirectory(Directory10000);
        var existingFiles = Directory.GetFiles(Directory10000);
        if (existingFiles.Length == N)
            return;

        foreach (var file in existingFiles)
        {
            File.Delete(file);
        }

        for (var i = 0; i < N; i++)
        {
            var extension = i switch
            {
                < 1000 => ".cs",
                < 2000 => ".md",
                < 3000 => ".vb",
                < 4000 => ".js",
                < 5000 => ".ts",
                < 6000 => ".json",
                < 7000 => ".csproj",
                < 8000 => ".bin",
                < 9000 => ".sln",
                _ => ".txt",
            };

            using var stream = File.Create(Directory10000 / ("file" + i.ToString("00000", CultureInfo.InvariantCulture) + extension));
        }
    }

    [Benchmark]
    public void MatchNoneChannel()
    {
        var options = new ScannerOptions
        {
            Scanners = new[] { new DummyScannerNeverMatch() },
            RecurseSubdirectories = true,
        };
        GetDependenciesChannel(options).Wait();
    }

    [Benchmark]
    public void MatchAllChannel()
    {
        var options = new ScannerOptions
        {
            Scanners = new[] { new DummyScanner() },
            RecurseSubdirectories = true,
        };
        GetDependenciesChannel(options).Wait();
    }

    [Benchmark]
    public void DefaultScannersChannel()
    {
        var options = new ScannerOptions
        {
            RecurseSubdirectories = true,
        };
        GetDependenciesChannel(options).Wait();
    }

    [Benchmark]
    public void MatchNoneForEach()
    {
        var options = new ScannerOptions
        {
            Scanners = new[] { new DummyScannerNeverMatch() },
            RecurseSubdirectories = true,
        };
        GetDependenciesForEach(options).Wait();
    }

    [Benchmark]
    public void MatchAllForEach()
    {
        var options = new ScannerOptions
        {
            Scanners = new[] { new DummyScanner() },
            RecurseSubdirectories = true,
        };
        GetDependenciesForEach(options).Wait();
    }

    [Benchmark]
    public void DefaultScannersForEach()
    {
        var options = new ScannerOptions
        {
            RecurseSubdirectories = true,
        };
        GetDependenciesForEach(options).Wait();
    }

    private async Task GetDependenciesChannel(ScannerOptions options)
    {
        options.DegreeOfParallelism = DegreeOfParallelism;
        await DependencyScanner.ScanDirectoryAsync(Directory10000, options, _ => new ValueTask()).ConfigureAwait(false);
    }

    private async Task GetDependenciesForEach(ScannerOptions options)
    {
        options.DegreeOfParallelism = DegreeOfParallelism;
        await foreach (var _ in DependencyScanner.ScanDirectoryAsync(Directory10000, options).ConfigureAwait(false))
        {
        }
    }

    private sealed class DummyScanner : DependencyScanner
    {
        internal static readonly Dependency Dependency = new("", "", DependencyType.Unknown, new TextLocation("", 1, 1, 1));

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            return context.ReportDependency(Dependency);
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class DummyScannerNeverMatch : DependencyScanner
    {
        public override ValueTask ScanAsync(ScanFileContext context)
        {
            return context.ReportDependency(new Dependency("", "", DependencyType.Unknown, new TextLocation(context.FullPath, 1, 1, 1)));
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => false;
    }
}
