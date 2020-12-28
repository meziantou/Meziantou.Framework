using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Scanners;
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
            await using var directory = TemporaryDirectory.Create();
            const int FileCount = 10_000;
            for (var i = 0; i < FileCount; i++)
            {
                await File.WriteAllTextAsync(directory.GetFullPath($"text{i.ToStringInvariant()}.txt"), "");
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
            await using var directory = TemporaryDirectory.Create();
            const int FileCount = 10_000;
            for (var i = 0; i < FileCount; i++)
            {
                await File.WriteAllTextAsync(directory.GetFullPath($"text{i.ToStringInvariant()}.txt"), "");
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

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ReportScanException(int degreeOfParallelism)
        {
            await using var directory = TemporaryDirectory.Create();
            await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "");

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ShouldScanThrowScanner() } }, _ => ValueTask.CompletedTask);
            });

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ScanThrowScanner() } }, _ => ValueTask.CompletedTask);
            });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ReportScanException_IAsyncEnumerablt(int degreeOfParallelism)
        {
            await using var directory = TemporaryDirectory.Create();
            await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "");

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var item in DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ShouldScanThrowScanner() } }))
                {
                }
            });

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var item in DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ScanThrowScanner() } }))
                {
                }
            });
        }

        [Fact]
        public void DefaultScannersIncludeAllScanners()
        {
            var scanners = new ScannerOptions().Scanners.Select(t => t.GetType()).OrderBy(t => t.FullName).ToArray();

            var allScanners = typeof(ScannerOptions).Assembly.GetExportedTypes()
                .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(DependencyScanner)) && type != typeof(RegexScanner))
                .OrderBy(t => t.FullName)
                .ToArray();

            Assert.NotEmpty(scanners);
            Assert.Equal(allScanners, scanners);
        }

        private sealed class DummyScanner : DependencyScanner
        {
            public override ValueTask ScanAsync(ScanFileContext context)
            {
                return context.ReportDependency(new Dependency("", "", DependencyType.Unknown, new TextLocation(context.FullPath, 1, 1, 1)));
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

        private sealed class ScanThrowScanner : DependencyScanner
        {
            public override ValueTask ScanAsync(ScanFileContext context)
            {
                throw new InvalidOperationException();
            }

            protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
        }

        private sealed class ShouldScanThrowScanner : DependencyScanner
        {
            public override ValueTask ScanAsync(ScanFileContext context)
            {
                return ValueTask.CompletedTask;
            }

            protected override bool ShouldScanFileCore(CandidateFileContext file) => throw new InvalidOperationException();
        }

    }
}
