using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;
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
            items.Should().HaveCount(FileCount);
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
            items.Should().BeEmpty();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ReportScanException(int degreeOfParallelism)
        {
            await using var directory = TemporaryDirectory.Create();
            await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "");

            await new Func<Task>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ShouldScanThrowScanner() } }, _ => ValueTask.CompletedTask))
                .Should().ThrowExactlyAsync<InvalidOperationException>();

            await new Func<Task>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ScanThrowScanner() } }, _ => ValueTask.CompletedTask))
                .Should().ThrowExactlyAsync<InvalidOperationException>();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task ReportScanException_IAsyncEnumerable(int degreeOfParallelism)
        {
            await using var directory = TemporaryDirectory.Create();
            await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "");

            await new Func<Task>(async () =>
            {
                await foreach (var item in DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ShouldScanThrowScanner() } }))
                {
                }
            }).Should().ThrowExactlyAsync<InvalidOperationException>();

            await new Func<Task>(async () =>
            {
                await foreach (var item in DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ScanThrowScanner() } }))
                {
                }
            }).Should().ThrowExactlyAsync<InvalidOperationException>();
        }

        [Fact]
        public void DefaultScannersIncludeAllScanners()
        {
            var scanners = new ScannerOptions().Scanners.Select(t => t.GetType()).OrderBy(t => t.FullName).ToArray();

            var allScanners = typeof(ScannerOptions).Assembly.GetExportedTypes()
                .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(DependencyScanner)) && type != typeof(RegexScanner))
                .OrderBy(t => t.FullName)
                .ToArray();

            scanners.Should().NotBeEmpty();
            scanners.Should().BeEquivalentTo(allScanners);
        }

        [Fact]
        public async Task UsingGlobs()
        {
            await using var directory = TemporaryDirectory.Create();
            var file1 = directory.CreateEmptyFile($"packages.json");
            var file2 = directory.CreateEmptyFile($"node_modules/packages.json");

            var globs = new GlobCollection(Glob.Parse("**/*", GlobOptions.None), Glob.Parse("!**/node_modules/**/*", GlobOptions.None));
            var options = new ScannerOptions()
            {
                RecurseSubdirectories = true,
                ShouldScanFilePredicate = (ref FileSystemEntry entry) => globs.IsMatch(ref entry),
                ShouldRecursePredicate = (ref FileSystemEntry entry) => globs.IsPartialMatch(ref entry),
                Scanners = new DependencyScanner[]
                {
                    new DummyScanner(),
                },
            };
            var result = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options).ToListAsync();

            result.Should().SatisfyRespectively(dep => dep.Location.FilePath.Should().Be(file1));
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
