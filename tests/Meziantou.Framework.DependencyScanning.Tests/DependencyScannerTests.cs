using System.Text;
using FluentAssertions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;
using Xunit;
using Xunit.Abstractions;

namespace Meziantou.Framework.DependencyScanning.Tests;

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

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = new[] { new DummyScanner() } });
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

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = new[] { new DummyScannerNeverMatch() } });
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

        await new Func<Task>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ShouldScanThrowScanner() } }, onDependencyFound: _ => { }))
            .Should().ThrowExactlyAsync<InvalidOperationException>();

        await new Func<Task>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ScanThrowScanner() } }, onDependencyFound: _ => { }))
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
            foreach (var item in await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ShouldScanThrowScanner() } }))
            {
            }
        }).Should().ThrowExactlyAsync<InvalidOperationException>();

        await new Func<Task>(async () =>
        {
            foreach (var item in await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = new[] { new ScanThrowScanner() } }))
            {
            }
        }).Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public void DefaultScannersIncludeAllScanners()
    {
        var scanners = new ScannerOptions().Scanners.Select(t => t.GetType()).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();

        var allScanners = typeof(ScannerOptions).Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(DependencyScanner)) && type != typeof(RegexScanner))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        scanners.Should().NotBeEmpty();
        scanners.Should().BeEquivalentTo(allScanners);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UsingGlobs(int degreeOfParallelism)
    {
        await using var directory = TemporaryDirectory.Create();
        var file1 = directory.CreateEmptyFile($"packages.json");
        var file2 = directory.CreateEmptyFile($"node_modules/packages.json");

        var globs = new GlobCollection(Glob.Parse("**/*", GlobOptions.None), Glob.Parse("!**/node_modules/**/*", GlobOptions.None));
        var options = new ScannerOptions()
        {
            RecurseSubdirectories = true,
            DegreeOfParallelism = degreeOfParallelism,
            ShouldScanFilePredicate = globs.IsMatch,
            ShouldRecursePredicate = globs.IsPartialMatch,
            Scanners = new DependencyScanner[]
            {
                new DummyScanner(),
            },
        };
        var result = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options);
        result.Should().SatisfyRespectively(dep => dep.VersionLocation.FilePath.Should().Be(file1));
    }

    [Fact]
    public async Task ScanFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath($"text.txt");
        await File.WriteAllTextAsync(filePath, "");

        var items = await DependencyScanner.ScanFileAsync(directory.FullPath, filePath, new ScannerOptions { Scanners = new[] { new DummyScanner() } });
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ScanFiles()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath1 = directory.GetFullPath($"text0.txt");
        var filePath2 = directory.GetFullPath($"text1.txt");
        await File.WriteAllTextAsync(filePath1, "");
        await File.WriteAllTextAsync(filePath2, "");

        var items = await DependencyScanner.ScanFilesAsync(directory.FullPath, new string[] { filePath1, filePath2 }, new ScannerOptions { Scanners = new[] { new DummyScanner() } });
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScanFiles_InMemory()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("test.txt", "");

        var items = await DependencyScanner.ScanFilesAsync("/", new string[] { "/dir/test.txt" }, new ScannerOptions { FileSystem = fs, Scanners = new[] { new DummyScanner() } });
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ScanFile_InMemory()
    {
        var items = await DependencyScanner.ScanFileAsync("/", "/test.txt", [], new[] { new DummyScanner() });
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task DetectChangedLocation()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath($"text0.txt");
        await File.WriteAllTextAsync(filePath, "test");

        var location = new TextLocation(FileSystem.Instance, filePath, 1, 2, 1);
        await location.UpdateAsync("e", "a");

        await FluentActions.Invoking(() => location.UpdateAsync("e", "b")).Should().ThrowExactlyAsync<DependencyScannerException>();
    }

    private sealed class DummyScanner : DependencyScanner
    {
        public override ValueTask ScanAsync(ScanFileContext context)
        {
            context.ReportDependency(new Dependency("", "", DependencyType.Unknown, nameLocation: null, new TextLocation(FileSystem.Instance, context.FullPath, 1, 1, 1)));
            return ValueTask.CompletedTask;
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class DummyScannerNeverMatch : DependencyScanner
    {
        public override ValueTask ScanAsync(ScanFileContext context)
        {
            context.ReportDependency(new Dependency("", "", DependencyType.Unknown, nameLocation: null, new TextLocation(FileSystem.Instance, context.FullPath, 1, 1, 1)));
            return ValueTask.CompletedTask;
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

    private sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly List<(string Path, byte[] Content)> _files = new();

        public void AddFile(string path, byte[] content)
        {
            _files.Add((path, content));
        }

        public void AddFile(string path, string content)
        {
            _files.Add((path, Encoding.UTF8.GetBytes(content)));
        }

        public Stream OpenRead(string path)
        {
            foreach (var file in _files)
            {
                if (file.Path == path)
                {
                    return new MemoryStream(file.Content);
                }
            }

            throw new FileNotFoundException("File not found", path);
        }

        public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions) => throw new NotSupportedException();
        public Stream OpenReadWrite(string path) => throw new NotSupportedException();
    }
}
