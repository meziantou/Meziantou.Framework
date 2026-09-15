using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;
using Meziantou.Framework;

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
            await File.WriteAllTextAsync(directory.GetFullPath($"text{i.ToStringInvariant()}.txt"), "", XunitCancellationToken);
        }

        _testOutputHelper.WriteLine("File generated in " + stopwatch.GetElapsedTime());
        stopwatch = ValueStopwatch.StartNew();

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = [new DummyScanner()] }, XunitCancellationToken);
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
            await File.WriteAllTextAsync(directory.GetFullPath($"text{i.ToStringInvariant()}.txt"), "", XunitCancellationToken);
        }

        _testOutputHelper.WriteLine("File generated in " + stopwatch.GetElapsedTime());
        stopwatch = ValueStopwatch.StartNew();

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = [new DummyScannerNeverMatch()] }, XunitCancellationToken);
        _testOutputHelper.WriteLine("File scanned in " + stopwatch.GetElapsedTime());
        Assert.Empty(items);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReportScanException(int degreeOfParallelism)
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = [new ShouldScanThrowScanner()] }, onDependencyFound: _ => { }, XunitCancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = [new ScanThrowScanner()] }, onDependencyFound: _ => { }, XunitCancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReportScanException_IAsyncEnumerable(int degreeOfParallelism)
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var item in await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = [new ShouldScanThrowScanner()] }, XunitCancellationToken))
            {
            }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var item in await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = [new ScanThrowScanner()] }, XunitCancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task ReportScanException_LargeDirectory_FailsFast()
    {
        await using var directory = TemporaryDirectory.Create();
        const int FileCount = 10_050;
        for (var i = 0; i < FileCount; i++)
        {
            await File.WriteAllTextAsync(directory.GetFullPath($"text{i.ToStringInvariant()}.txt"), "", XunitCancellationToken);
        }

        var scanner = new CountingScanThrowScanner();
        var options = new ScannerOptions { DegreeOfParallelism = 2, Scanners = [scanner] };
        var scanTask = DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, onDependencyFound: _ => { }, XunitCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scanTask.WaitAsync(TimeSpan.FromMinutes(2), XunitCancellationToken));
        Assert.True(scanner.ScanCount < FileCount, $"The scan should stop after the first failure, but {scanner.ScanCount.ToStringInvariant()} files were scanned");
    }

    [Fact]
    public async Task ScanDirectory_MissingDirectory_ReturnsFaultedTask()
    {
        await using var directory = TemporaryDirectory.Create();
        var missingDirectory = directory.GetFullPath("missing");

        var task = DependencyScanner.ScanDirectoryAsync(missingDirectory, new ScannerOptions { Scanners = [new DummyScanner()] }, onDependencyFound: _ => { }, XunitCancellationToken);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => task);
    }

    [Theory]
    [InlineData("/root", "/root", "")]
    [InlineData("/root/", "/root", "")]
    [InlineData("/root", "/root/", "")]
    [InlineData("/root", "/root/sub", "sub")]
    [InlineData("/root/", "/root/sub", "sub")]
    [InlineData("/root", "/root/sub/nested", "sub/nested")]
    [InlineData("/root", "/rootother", "")]
    [InlineData("/root", "/other", "")]
    public void CandidateFileContext_RelativeDirectory(string rootDirectory, string directory, string expected)
    {
        var context = new CandidateFileContext(rootDirectory, directory, "file.txt");

        Assert.Equal(expected, context.RelativeDirectory.ToString());
    }

    [Fact]
    public async Task ScanDirectory_ScansFilesConcurrently()
    {
        await using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("file1.txt");
        directory.CreateEmptyFile("file2.txt");

        var scanner = new ConcurrencyProbeScanner(expectedConcurrency: 2);
        var options = new ScannerOptions { DegreeOfParallelism = 2, Scanners = [scanner] };
        await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, onDependencyFound: _ => { }, XunitCancellationToken);

        Assert.True(scanner.ReachedExpectedConcurrency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void DegreeOfParallelism_InvalidValue_Throws(int degreeOfParallelism)
    {
        var options = new ScannerOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.DegreeOfParallelism = degreeOfParallelism);
    }

    [Fact]
    public async Task DegreeOfParallelism_MinusOne_UsesProcessorCount()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath1 = directory.CreateEmptyFile("file1.txt");
        var filePath2 = directory.CreateEmptyFile("file2.txt");
        var options = new ScannerOptions { DegreeOfParallelism = -1, Scanners = [new DummyScanner()] };

        var directoryItems = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, XunitCancellationToken);
        var fileItems = await DependencyScanner.ScanFilesAsync(directory.FullPath, [filePath1, filePath2], options, XunitCancellationToken);

        Assert.Equal(2, directoryItems.Count);
        Assert.Equal(2, fileItems.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ScanDirectory_PropagatesCancellationFromXmlParsing(int degreeOfParallelism)
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath("test.csproj"), """
            <Project><ItemGroup><PackageReference Include="A" Version="1.0.0" /></ItemGroup></Project>
            """, XunitCancellationToken);

        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var options = new ScannerOptions { DegreeOfParallelism = degreeOfParallelism, Scanners = [new MsBuildReferencesDependencyScanner()] };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, onDependencyFound: _ => { }, cancellationTokenSource.Token));
    }

    [Fact]
    public void DefaultScannersIncludeAllScanners()
    {
        var scanners = new ScannerOptions().Scanners.Select(t => t.GetType()).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();

        var allScanners = typeof(ScannerOptions).Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(DependencyScanner)) && type != typeof(RegexScanner))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(scanners);
        Assert.Equal(allScanners, scanners);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UsingGlobs(int degreeOfParallelism)
    {
        await using var directory = TemporaryDirectory.Create();
        var file1 = directory.CreateEmptyFile($"packages.json");
        var file2 = directory.CreateEmptyFile($"node_modules/packages.json");

        var globs = new GlobCollection(Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!**/node_modules/**/*", GlobDialect.Standard));
        var options = new ScannerOptions()
        {
            RecurseSubdirectories = true,
            DegreeOfParallelism = degreeOfParallelism,
            ShouldScanFilePredicate = globs.IsMatch,
            ShouldRecursePredicate = globs.IsPartialMatch,
            Scanners =
            [
                new DummyScanner(),
            ],
        };
        var result = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, XunitCancellationToken);
        Assert.Collection(result, dep =>
        {
            Assert.NotNull(dep.VersionLocation);
            Assert.Equal(file1, dep.VersionLocation.FilePath);
        });
    }

    [Fact]
    public async Task ScanFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath($"text.txt");
        await File.WriteAllTextAsync(filePath, "", XunitCancellationToken);

        var items = await DependencyScanner.ScanFileAsync(directory.FullPath, filePath, new ScannerOptions { Scanners = [new DummyScanner()] }, XunitCancellationToken);
        Assert.Single(items);
    }

    [Fact]
    public async Task ScanFiles()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath1 = directory.GetFullPath($"text0.txt");
        var filePath2 = directory.GetFullPath($"text1.txt");
        await File.WriteAllTextAsync(filePath1, "", XunitCancellationToken);
        await File.WriteAllTextAsync(filePath2, "", XunitCancellationToken);

        var items = await DependencyScanner.ScanFilesAsync(directory.FullPath, [filePath1, filePath2], new ScannerOptions { Scanners = [new DummyScanner()] }, XunitCancellationToken);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ScanFiles_InMemory()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("test.txt", "");

        var items = await DependencyScanner.ScanFilesAsync("/", ["/dir/test.txt"], new ScannerOptions { FileSystem = fs, Scanners = [new DummyScanner()] }, XunitCancellationToken);
        Assert.Single(items);
    }

    [Fact]
    public async Task ScanFile_InMemory()
    {
        var items = await DependencyScanner.ScanFileAsync("/", "/test.txt", [], [new DummyScanner()], XunitCancellationToken);
        Assert.Single(items);
    }

    [Fact]
    public async Task ScanFile_InMemory_PackagesConfig()
    {
        var content = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
              <package id="Serilog" version="3.1.1" targetFramework="net48" />
            </packages>
            """);

        var items = await DependencyScanner.ScanFileAsync("/repo", "/repo/src/packages.config", content, XunitCancellationToken);

        Assert.Equal(["Newtonsoft.Json", "Serilog"], items.Select(item => item.Name).Order(StringComparer.Ordinal));
        var versionLocation = items.First().VersionLocation;
        Assert.NotNull(versionLocation);
        await Assert.ThrowsAsync<NotSupportedException>(() => versionLocation.UpdateAsync("1.0.0", XunitCancellationToken));
    }

    [Fact]
    public async Task DetectChangedLocation()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath($"text0.txt");
        await File.WriteAllTextAsync(filePath, "test", XunitCancellationToken);

        var location = new TextLocation(FileSystem.Instance, filePath, 1, 2, 1);
        await location.UpdateAsync("e", "a", XunitCancellationToken);

        await Assert.ThrowsAsync<DependencyScannerException>(() => location.UpdateAsync("e", "b", XunitCancellationToken));
    }

    [Fact]
    public async Task XmlLocation_UpdateAttribute_PreservesFormatting()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.xml");
        const string Original = """
            <root>
              <package   id = 'A'    version =  '1.2.3'  />
            </root>
            """;
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var document = XDocument.Parse(Original, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        var package = Assert.Single(document.Root!.Elements("package"));
        var versionAttribute = package.Attribute("version");
        Assert.NotNull(versionAttribute);

        var location = new XmlLocation(FileSystem.Instance, filePath, package, versionAttribute);
        await location.UpdateAsync("1.2.3", "2.0.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal(Original.Replace("1.2.3", "2.0.0", StringComparison.Ordinal), updatedContent);
    }

    [Fact]
    public async Task JsonLocation_UpdateString_PreservesFormattingAndStringEscaping()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("package.json");
        const string Original = """
            {
              // keep comments and spacing
              "dependencies"  : {
                "dummy":   "1.2.3",
                "condition": "a && b",
                "escaped": "a \u0026\u0026 b",
              },
            }
            """;
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var location = new JsonLocation(FileSystem.Instance, filePath, "$['dependencies']['dummy']", -1, -1);
        await location.UpdateAsync("1.2.3", "2.0.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal(Original.Replace("\"1.2.3\"", "\"2.0.0\"", StringComparison.Ordinal), updatedContent);
    }

    [Fact]
    public async Task JsonLocation_UpdateString_DoesNotEscapeAmpersands()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("package.json");
        const string Original = """
            {
              "dependencies": {
                "dummy": "1.2.3"
              }
            }
            """;
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var location = new JsonLocation(FileSystem.Instance, filePath, "$['dependencies']['dummy']", -1, -1);
        await location.UpdateAsync("1.2.3", "a && b", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal(Original.Replace("\"1.2.3\"", "\"a && b\"", StringComparison.Ordinal), updatedContent);
    }

    [Fact]
    public async Task AssemblyVersionXmlLocation_UpdateAttribute_PreservesFormatting()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        const string Original = """
            <Project>
              <ItemGroup>
                <Reference Include = 'nunit.framework, Version = 3.11.0.0, Culture = neutral, PublicKeyToken = 2638cd05610744eb' />
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var document = XDocument.Parse(Original, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        var reference = Assert.Single(document.Root!.Descendants("Reference"));
        var includeAttribute = reference.Attribute("Include");
        Assert.NotNull(includeAttribute);

        const string Version = "3.11.0.0";
        var index = includeAttribute.Value.IndexOf(Version, StringComparison.Ordinal);
        Assert.NotEqual(-1, index);

        var location = new AssemblyVersionXmlLocation(FileSystem.Instance, filePath, reference, includeAttribute, index, Version.Length);
        await location.UpdateAsync(Version, "3.12.0-beta00", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal(Original.Replace(Version, "3.12.0.0", StringComparison.Ordinal), updatedContent);
    }

    [Fact]
    public async Task XmlLocation_UpdateElementPart_MapsNormalizedLineEndingsOntoTheFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        const string Original = "<Project>\r\n  <PropertyGroup>\r\n    <TargetFrameworks>\r\n      net8.0;\r\n      net9.0\r\n    </TargetFrameworks>\r\n  </PropertyGroup>\r\n</Project>\r\n";
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("TargetFrameworks"));
        var index = element.Value.IndexOf("net9.0", StringComparison.Ordinal);
        Assert.NotEqual(-1, index);

        var location = new XmlLocation(FileSystem.Instance, filePath, element, index, "net9.0".Length);
        await location.UpdateAsync("net9.0", "net10.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal(Original.Replace("net9.0", "net10.0", StringComparison.Ordinal), updatedContent);
    }

    [Fact]
    public async Task XmlLocation_UpdateElementPart_MapsReferencesOntoTheFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        const string Original = "<Project><Value>a&amp;&#x62;;1.0.0;c</Value></Project>";
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("Value"));
        Assert.Equal("a&b;1.0.0;c", element.Value);

        var location = new XmlLocation(FileSystem.Instance, filePath, element, column: 4, length: 5);
        await location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal("<Project><Value>a&amp;&#x62;;2.0.0;c</Value></Project>", updatedContent);
    }

    [Fact]
    public async Task XmlLocation_UpdateAttributePart_MapsNormalizedWhitespaceAndReferencesOntoTheFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        const string Original = "<Project><Reference Include=\"a&amp;b,\r\n\tVersion=1.0.0\" /></Project>";
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("Reference"));
        var attribute = element.Attribute("Include");
        Assert.NotNull(attribute);
        Assert.Equal("a&b,  Version=1.0.0", attribute.Value);

        var location = new XmlLocation(FileSystem.Instance, filePath, element, attribute, column: 14, length: 5);
        await location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal("<Project><Reference Include=\"a&amp;b,\r\n\tVersion=2.0.0\" /></Project>", updatedContent);
    }

    [Fact]
    public async Task XmlLocation_UpdateElement_ComparesTheDecodedValue()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        await File.WriteAllTextAsync(filePath, "<Project><Version>1.0.0&#x2D;beta</Version></Project>", XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("Version"));
        var location = new XmlLocation(FileSystem.Instance, filePath, element);
        await location.UpdateAsync("1.0.0-beta", "2.0.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal("<Project><Version>2.0.0</Version></Project>", updatedContent);
    }

    [Fact]
    public async Task XmlLocation_Update_EscapesTheNewValue()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        await File.WriteAllTextAsync(filePath, "<Project><Version A=\"1.0.0\">1.0.0</Version></Project>", XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("Version"));
        var attribute = element.Attribute("A");
        Assert.NotNull(attribute);
        await new XmlLocation(FileSystem.Instance, filePath, element).UpdateAsync("1.0.0", "a&<b>\"", XunitCancellationToken);
        await new XmlLocation(FileSystem.Instance, filePath, element, attribute).UpdateAsync("1.0.0", "a&<b>\"", XunitCancellationToken);

        var updatedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
        Assert.Equal("<Project><Version A=\"a&amp;&lt;b&gt;&quot;\">a&amp;&lt;b&gt;\"</Version></Project>", updatedContent);

        var updatedElement = Assert.Single((await LoadXmlDocument(filePath)).Descendants("Version"));
        Assert.Equal("a&<b>\"", updatedElement.Value);
        Assert.Equal("a&<b>\"", updatedElement.Attribute("A")?.Value);
    }

    [Theory]
    [InlineData("<!-- comment -->1.0.0")]
    [InlineData("<![CDATA[1.0.0]]>")]
    [InlineData("<?pi data?>1.0.0")]
    public async Task XmlLocation_UpdateElementWithNonTextContent_Throws(string content)
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.csproj");
        var original = "<Project><Version>" + content + "</Version></Project>";
        await File.WriteAllTextAsync(filePath, original, XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("Version"));
        Assert.Equal("1.0.0", element.Value);

        var location = new XmlLocation(FileSystem.Instance, filePath, element, column: 0, length: 5);
        await Assert.ThrowsAsync<DependencyScannerException>(() => location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken));
        await Assert.ThrowsAsync<DependencyScannerException>(() => location.UpdateAsync("2.0.0", XunitCancellationToken));
        Assert.Equal(original, await File.ReadAllTextAsync(filePath, XunitCancellationToken));
    }

    [Fact]
    public async Task XmlLocation_LinePosition_AddsTheColumnOnce()
    {
        const string Content = "<Project Sdk=\"My.Sdk/1.2.3\" />";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Content));
        var document = await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(stream, XunitCancellationToken);
        var element = document.Root!;
        var attribute = element.Attribute("Sdk");
        Assert.NotNull(attribute);

        ILocationLineInfo location = new XmlLocation(FileSystem.Instance, "test.csproj", element, attribute, column: 7, length: 5);

        Assert.Equal(1, location.LineNumber);
        Assert.Equal(((IXmlLineInfo)attribute).LinePosition + 7, location.LinePosition);
        Assert.EndsWith(":1," + location.LinePosition.ToString(CultureInfo.InvariantCulture), location.ToString());
    }

    [Fact]
    public async Task XmlLocation_Update_HonorsTheEncodingOfTheXmlDeclaration()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("test.nuspec");
        const string Original = "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>\n<package><description>café</description><dependency version=\"1.0.0\" /></package>";
        await File.WriteAllBytesAsync(filePath, Encoding.Latin1.GetBytes(Original), XunitCancellationToken);

        var element = Assert.Single((await LoadXmlDocument(filePath)).Descendants("dependency"));
        Assert.Equal("café", element.Parent!.Element("description")!.Value);
        var attribute = element.Attribute("version");
        Assert.NotNull(attribute);

        var location = new XmlLocation(FileSystem.Instance, filePath, element, attribute);
        await location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken);

        var updatedContent = await File.ReadAllBytesAsync(filePath, XunitCancellationToken);
        Assert.Equal(Encoding.Latin1.GetBytes(Original.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), updatedContent);
    }

    [Fact]
    public async Task TextLocation_Update_FileThatIsNotValidUtf8_ThrowsWithoutModifyingTheFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("requirements.txt");
        byte[] original = [.. "# caf"u8, 0xE9, .. "\nrequests==1.0.0\n"u8];
        await File.WriteAllBytesAsync(filePath, original, XunitCancellationToken);

        var location = new TextLocation(FileSystem.Instance, filePath, line: 2, column: 11, length: 5);
        await Assert.ThrowsAsync<DependencyScannerException>(() => location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken));

        Assert.Equal(original, await File.ReadAllBytesAsync(filePath, XunitCancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextLocation_Update_PreservesTheByteOrderMark(bool withBom)
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("requirements.txt");
        byte[] bom = withBom ? [0xEF, 0xBB, 0xBF] : [];
        await File.WriteAllBytesAsync(filePath, [.. bom, .. "# café\nrequests==1.0.0\n"u8], XunitCancellationToken);

        var location = new TextLocation(FileSystem.Instance, filePath, line: 2, column: 11, length: 5);
        await location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken);

        Assert.Equal([.. bom, .. "# café\nrequests==2.0.0\n"u8], await File.ReadAllBytesAsync(filePath, XunitCancellationToken));
    }

    [Theory]
    [InlineData("a\rFROM b:1", 2)]
    [InlineData("a\r\nFROM b:1", 2)]
    [InlineData("a\r\r\nFROM b:1", 3)]
    [InlineData("a\n\rFROM b:1", 3)]
    public void TextLocation_FromIndex_EndsLinesOnCarriageReturnsAndLineFeeds(string text, int expectedLine)
    {
        var index = text.IndexOf("b:1", StringComparison.Ordinal);

        var location = TextLocation.FromIndex(FileSystem.Instance, "file.txt", text, index, length: 3);

        Assert.Equal(expectedLine, location.LineNumber);
        Assert.Equal(6, location.LinePosition);
    }

    [Theory]
    [InlineData("\r", 3)]
    [InlineData("\r\r\n", 5)]
    public async Task TextLocation_Update_EndsLinesOnCarriageReturns(string newLine, int line)
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("file.txt");
        var original = "a" + newLine + "b" + newLine + "value: 1.0.0" + newLine;
        await File.WriteAllTextAsync(filePath, original, XunitCancellationToken);

        // The line number as TextReader.ReadLine counts it
        var location = new TextLocation(FileSystem.Instance, filePath, line, column: 8, length: 5);
        await location.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken);

        Assert.Equal(original.Replace("1.0.0", "2.0.0", StringComparison.Ordinal), await File.ReadAllTextAsync(filePath, XunitCancellationToken));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("xml")]
    [InlineData("json")]
    public async Task Location_Update_CancellationAfterReadingTheFile_DoesNotModifyTheFile(string kind)
    {
        const string XmlContent = "<Project><Version>1.0.0</Version></Project>";
        var (content, location) = kind switch
        {
            "text" => ("version: 1.0.0", (Func<IFileSystem, Location>)(fileSystem => new TextLocation(fileSystem, "file", line: 1, column: 10, length: 5))),
            "xml" => (XmlContent, fileSystem => new XmlLocation(fileSystem, "file", XDocument.Parse(XmlContent).Root!.Element("Version")!)),
            _ => ("{\"version\":\"1.0.0\"}", fileSystem => new JsonLocation(fileSystem, "file", "$['version']", -1, -1)),
        };

        using var cancellationTokenSource = new CancellationTokenSource();
        await using var stream = CancelAtEndOfStreamMemoryStream.Create(Encoding.UTF8.GetBytes(content), cancellationTokenSource);
        var fileSystem = new SingleStreamFileSystem(stream);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => location(fileSystem).UpdateAsync("1.0.0", "2.0.0", cancellationTokenSource.Token));

        Assert.Equal(content, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task JsonLocation_UpdatePart_ValueOnlyFoundInAnotherPart_Throws()
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("renovate.json");

        // The location was recorded for "github>org/cfg#1.0.0", then the file changed
        const string Original = """{"extends":["github>org/1.0.0#2.0.0"]}""";
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var location = new JsonLocation(FileSystem.Instance, filePath, "$['extends'][0]", 15, 5);
        await Assert.ThrowsAsync<DependencyScannerException>(() => location.UpdateAsync("1.0.0", "3.0.0", XunitCancellationToken));

        Assert.Equal(Original, await File.ReadAllTextAsync(filePath, XunitCancellationToken));
    }

    [Theory]
    [InlineData("github>org/configuration", """{"extends":["github>org/configuration#2.0.0"]}""")]
    [InlineData("github>o/c", """{"extends":["github>o/c#2.0.0"]}""")]
    [InlineData("github>org/configuration-1.0.0", null)]
    public async Task JsonLocation_UpdatePart_AfterRenamingThePrecedingPart(string newName, string? expected)
    {
        await using var directory = TemporaryDirectory.Create();
        var filePath = directory.GetFullPath("renovate.json");
        const string Original = """{"extends":["github>org/cfg#1.0.0"]}""";
        await File.WriteAllTextAsync(filePath, Original, XunitCancellationToken);

        var nameLocation = new JsonLocation(FileSystem.Instance, filePath, "$['extends'][0]", 0, "github>org/cfg".Length);
        var versionLocation = new JsonLocation(FileSystem.Instance, filePath, "$['extends'][0]", "github>org/cfg#".Length, "1.0.0".Length);
        await nameLocation.UpdateAsync("github>org/cfg", newName, XunitCancellationToken);

        if (expected is null)
        {
            // The old version occurs twice after the recorded start, so the one to replace is ambiguous
            var renamedContent = await File.ReadAllTextAsync(filePath, XunitCancellationToken);
            await Assert.ThrowsAsync<DependencyScannerException>(() => versionLocation.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken));
            Assert.Equal(renamedContent, await File.ReadAllTextAsync(filePath, XunitCancellationToken));
        }
        else
        {
            await versionLocation.UpdateAsync("1.0.0", "2.0.0", XunitCancellationToken);
            Assert.Equal(expected, await File.ReadAllTextAsync(filePath, XunitCancellationToken));
        }
    }

    private static async Task<XDocument> LoadXmlDocument(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        return await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(stream, XunitCancellationToken);
    }

    [Fact]
    public async Task GetEncodingAsync_ReadUntilCountOrEndAsync_ReadsBufferUsingSlices()
    {
        await using var stream = new RestrictedStream(new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'a']), new RestrictedStreamOptions
        {
            AllowAsynchronousCalls = true,
            AllowReading = true,
            MaxReadLength = 1,
        });

        var encoding = await StreamUtilities.GetEncodingAsync(stream, XunitCancellationToken);

        Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' }, 65001)] // UTF-8
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0x00 }, 1200)] // UTF-16LE
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, (byte)'a' }, 1201)] // UTF-16BE
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, 12000)] // UTF-32LE
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF }, 12001)] // UTF-32BE
    public async Task GetEncodingAsync_DetectsBom(byte[] content, int expectedCodePage)
    {
        await using var stream = new MemoryStream(content);

        var encoding = await StreamUtilities.GetEncodingAsync(stream, XunitCancellationToken);

        Assert.Equal(expectedCodePage, encoding.CodePage);
    }

    [Fact]
    public async Task GetEncodingAsync_DoesNotDetectUtf7()
    {
        await using var stream = new MemoryStream("+/v8"u8.ToArray());

        var encoding = await StreamUtilities.GetEncodingAsync(stream, XunitCancellationToken);

        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [Fact]
    public async Task CreateReaderAsync_DetectsUtf8Bom_WhenStreamReadsOneByteAtATime()
    {
        await using var stream = new RestrictedStream(new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'a']), new RestrictedStreamOptions
        {
            AllowAsynchronousCalls = true,
            AllowReading = true,
            AllowSeeking = true,
            MaxReadLength = 1,
        });

        using var reader = await StreamUtilities.CreateReaderAsync(stream, XunitCancellationToken);
        var text = await reader.ReadToEndAsync(XunitCancellationToken);

        Assert.Equal("a", text);
    }

    [Fact]
    public async Task DetectUnsupportedType()
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var item in await DependencyScanner.ScanDirectoryAsync(directory.FullPath, new ScannerOptions { Scanners = [new ReportUnsupportedDependencyType()] }, XunitCancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task FilterIncludedTypes()
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);
        var options = new ScannerOptions
        {
            Scanners = [new ScannerWithTypes([DependencyType.NuGet, DependencyType.Npm])],
            IncludedDependencyTypes = [DependencyType.NuGet],
        };

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, XunitCancellationToken);
        var item = Assert.Single(items);
        Assert.Equal(DependencyType.NuGet, item.Type);
    }

    [Fact]
    public async Task FilterExcludedTypes()
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);
        var options = new ScannerOptions
        {
            Scanners = [new ScannerWithTypes([DependencyType.NuGet, DependencyType.Npm])],
            ExcludedDependencyTypes = [DependencyType.NuGet],
        };

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, XunitCancellationToken);
        var item = Assert.Single(items);
        Assert.Equal(DependencyType.Npm, item.Type);
    }

    [Fact]
    public async Task FilterIncludedExcludedTypes()
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);
        var options = new ScannerOptions
        {
            Scanners = [new ScannerWithTypes([DependencyType.NuGet, DependencyType.Npm, DependencyType.PyPi])],
            IncludedDependencyTypes = [DependencyType.Npm],
            ExcludedDependencyTypes = [DependencyType.NuGet],
        };

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, XunitCancellationToken);
        var item = Assert.Single(items);
        Assert.Equal(DependencyType.Npm, item.Type);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(64)]
    public async Task ReportDependency_SupportedTypeWithLargeValue(int typeValue)
    {
        await using var directory = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(directory.GetFullPath($"text.txt"), "", XunitCancellationToken);
        var type = (DependencyType)typeValue;
        var options = new ScannerOptions { Scanners = [new ScannerWithTypes([DependencyType.NuGet, type])] };

        var items = await DependencyScanner.ScanDirectoryAsync(directory.FullPath, options, XunitCancellationToken);

        Assert.Equal([DependencyType.NuGet, type], items.Select(item => item.Type).Order());
    }

    private sealed class ScannerWithTypes(DependencyType[] types) : DependencyScanner
    {
        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = types;

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            foreach (var type in types)
            {
                context.ReportDependency(this, "", "", type, nameLocation: null, new TextLocation(FileSystem.Instance, context.FullPath, 1, 1, 1));
            }

            return ValueTask.CompletedTask;
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class DummyScanner : DependencyScanner
    {
        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Unknown];

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            context.ReportDependency(this, "", "", DependencyType.Unknown, nameLocation: null, new TextLocation(FileSystem.Instance, context.FullPath, 1, 1, 1));
            return ValueTask.CompletedTask;
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class DummyScannerNeverMatch : DependencyScanner
    {
        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.Unknown];

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            context.ReportDependency(this, "", "", DependencyType.Unknown, nameLocation: null, new TextLocation(FileSystem.Instance, context.FullPath, 1, 1, 1));
            return ValueTask.CompletedTask;
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => false;
    }

    private sealed class ConcurrencyProbeScanner(int expectedConcurrency) : DependencyScanner
    {
        private readonly TaskCompletionSource _reachedExpectedConcurrency = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _currentConcurrency;

        public bool ReachedExpectedConcurrency => _reachedExpectedConcurrency.Task.IsCompletedSuccessfully;

        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [];

        public override async ValueTask ScanAsync(ScanFileContext context)
        {
            if (Interlocked.Increment(ref _currentConcurrency) >= expectedConcurrency)
            {
                _reachedExpectedConcurrency.TrySetResult();
            }

            // Wait for the other workers to pick up a file, without blocking the scan forever when they never do
            await Task.WhenAny(_reachedExpectedConcurrency.Task, Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken)).ConfigureAwait(false);
            Interlocked.Decrement(ref _currentConcurrency);
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class ScanThrowScanner : DependencyScanner
    {
        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [];

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            throw new InvalidOperationException();
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class CountingScanThrowScanner : DependencyScanner
    {
        private int _scanCount;

        public int ScanCount => Volatile.Read(ref _scanCount);

        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [];

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            Interlocked.Increment(ref _scanCount);
            throw new InvalidOperationException();
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class ShouldScanThrowScanner : DependencyScanner
    {
        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [];

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            return ValueTask.CompletedTask;
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => throw new InvalidOperationException();
    }

    private sealed class ReportUnsupportedDependencyType : DependencyScanner
    {
        protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

        public override ValueTask ScanAsync(ScanFileContext context)
        {
            context.ReportDependency(this, "", "", DependencyType.Unknown, nameLocation: null, new TextLocation(FileSystem.Instance, context.FullPath, 1, 1, 1));
            return ValueTask.CompletedTask;
        }

        protected override bool ShouldScanFileCore(CandidateFileContext file) => true;
    }

    private sealed class CancelAtEndOfStreamMemoryStream(CancellationTokenSource cancellationTokenSource) : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => CancelAtEnd(base.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => CancelAtEnd(base.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => CancelAtEnd(await base.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => CancelAtEnd(await base.ReadAsync(buffer, cancellationToken));

        public static CancelAtEndOfStreamMemoryStream Create(byte[] content, CancellationTokenSource cancellationTokenSource)
        {
            var stream = new CancelAtEndOfStreamMemoryStream(cancellationTokenSource);
            stream.Write(content);
            stream.Position = 0;
            return stream;
        }

        private int CancelAtEnd(int read)
        {
            if (read == 0)
            {
                cancellationTokenSource.Cancel();
            }

            return read;
        }
    }

    private sealed class SingleStreamFileSystem(Stream stream) : IFileSystem
    {
        public Stream OpenRead(string path) => stream;
        public Stream OpenReadWrite(string path) => stream;
        public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions) => throw new NotSupportedException();
    }

    private sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly List<(string Path, byte[] Content)> _files = [];

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
