using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Meziantou.Framework.DependencyScanning.Tests
{
    public sealed class DependencyScannerTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public DependencyScannerTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task LargeDirectory()
        {
            var stopwatch = ValueStopwatch.StartNew();
            using var directory = TemporaryDirectory.Create();
            const int FileCount = 10_000;
            for (var i = 0; i < FileCount; i++)
            {
                File.WriteAllText(directory.GetFullPath($"text{i}.txt"), "");
            }

            _testOutputHelper.WriteLine("File generated in " + stopwatch.GetElapsedTime());
            stopwatch = ValueStopwatch.StartNew();

            var items = new List<Dependency>(FileCount);
            await foreach (var item in DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = new[] { new DummyScanner() } }))
            {
                items.Add(item);
            }

            _testOutputHelper.WriteLine("File scanned in " + stopwatch.GetElapsedTime());
            Assert.Equal(FileCount, items.Count);
        }

        [Fact]
        public async Task LargeDirectory_NoScannerMatch()
        {
            var stopwatch = ValueStopwatch.StartNew();
            using var directory = TemporaryDirectory.Create();
            const int FileCount = 10_000;
            for (var i = 0; i < FileCount; i++)
            {
                File.WriteAllText(directory.GetFullPath($"text{i}.txt"), "");
            }

            _testOutputHelper.WriteLine("File generated in " + stopwatch.GetElapsedTime());
            stopwatch = ValueStopwatch.StartNew();

            var items = new List<Dependency>(FileCount);
            await foreach (var item in DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = new[] { new DummyScannerNeverMatch() } }))
            {
                items.Add(item);
            }

            _testOutputHelper.WriteLine("File scanned in " + stopwatch.GetElapsedTime());
            Assert.Empty(items);
        }

        private sealed class DummyScanner : DependencyScanner
        {
            public override ValueTask ScanAsync(ScanFileContext context)
            {
                return context.ReportDependency(new Dependency("", "", DependencyType.Unknown, new TextLocation(context.FullPath, 1, 1, 1)));
            }

            public override bool ShouldScanFile(CandidateFileContext file) => true;
        }

        private sealed class DummyScannerNeverMatch : DependencyScanner
        {
            public override ValueTask ScanAsync(ScanFileContext context)
            {
                return context.ReportDependency(new Dependency("", "", DependencyType.Unknown, new TextLocation(context.FullPath, 1, 1, 1)));
            }

            public override bool ShouldScanFile(CandidateFileContext file) => false;
        }
    }
}
