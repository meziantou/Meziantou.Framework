#pragma warning disable MA0101
using System.Text;
using FluentAssertions;
using LibGit2Sharp;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;
using TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Meziantou.Framework.DependencyScanning.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly ITestOutputHelper _testOutputHelper;

    public ScannerTests(ITestOutputHelper testOutputHelper)
    {
        _directory = TemporaryDirectory.Create();
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task NpmPackageJsonDependencies()
    {
        const string Original = /*lang=json,strict*/ @"{
  ""name"": ""sample"",
  ""version"": ""0.1.0"",
  ""dependencies"": {
    ""a"": ""1.0.0""
  },
  ""devDependencies"": {
    ""b"": ""1.2.3"",
    ""c"": null
  }
}";

        const string Expected = /*lang=json,strict*/ @"{
  ""name"": ""sample"",
  ""version"": ""0.1.0"",
  ""dependencies"": {
    ""a"": ""2.0.0""
  },
  ""devDependencies"": {
    ""b"": ""2.0.0"",
    ""c"": null
  }
}";

        AddFile("package.json", Original);
        var result = await GetDependencies(new NpmPackageJsonDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.Npm, "a", "1.0.0", 5, 8),
            (DependencyType.Npm, "b", "1.2.3", 8, 8));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("package.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task NuSpecDependencies()
    {
        const string Original = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <id>sample</id>
        <version>1.0.0</version>
        <authors>Microsoft</authors>
        <dependencies>
            <dependency id=""another-package"" version=""3.0.0"" />
            <dependency id=""yet-another-package"" version=""1.0.0"" />
        </dependencies>
    </metadata>
</package>";

        const string Expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <id>sample</id>
        <version>1.0.0</version>
        <authors>Microsoft</authors>
        <dependencies>
            <dependency id=""another-package"" version=""2.0.0"" />
            <dependency id=""yet-another-package"" version=""2.0.0"" />
        </dependencies>
    </metadata>
</package>";

        AddFile("test.nuspec", Original);
        var result = await GetDependencies(new NuSpecDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.NuGet, "another-package", "3.0.0", 8, 14),
            (DependencyType.NuGet, "yet-another-package", "1.0.0", 9, 14));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("test.nuspec", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildReferencesDependencies()
    {
        const string Original = @"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

    <!-- Comment -->
  <ItemGroup>
    <PackageReference Include=""TestPackage"" Version=""4.2.1"" />
  </ItemGroup>

</Project>
";
        const string Expected = @"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

    <!-- Comment -->
  <ItemGroup>
    <PackageReference Include=""TestPackage"" Version=""2.0.0"" />
  </ItemGroup>

</Project>
";

        AddFile("test.csproj", Original);
        var result = await GetDependencies(new MsBuildReferencesDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.NuGet, "TestPackage", "4.2.1", 10, 6));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildSdkReferencesDependencies()
    {
        const string Original = @"<Project Sdk=""MSBuild.Sdk.Extras/2.0.54"">
    <Sdk Name=""My.Custom.Sdk1"" Version=""1.0.0"" />
    <Import Sdk=""My.Custom.Sdk2/2.0.55"" />
</Project>
";
        const string Expected = @"<Project Sdk=""MSBuild.Sdk.Extras/1.2.3"">
    <Sdk Name=""My.Custom.Sdk1"" Version=""1.2.3"" />
    <Import Sdk=""My.Custom.Sdk2/1.2.3"" />
</Project>
";

        AddFile("test.csproj", Original);
        var result = await GetDependencies(new MsBuildReferencesDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.NuGet, "MSBuild.Sdk.Extras", "2.0.54", 1, 2),
            (DependencyType.NuGet, "My.Custom.Sdk1", "1.0.0", 2, 6),
            (DependencyType.NuGet, "My.Custom.Sdk2", "2.0.55", 3, 6));

        await UpdateDependencies(result, "1.2.3");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PackagesReferencesWithNamespaceDependencies()
    {
        const string Original = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project Sdk=""Microsoft.NET.Sdk"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

    <!-- Comment -->
  <ItemGroup>
    <PackageReference Include=""TestPackage"" Version=""4.2.1"" />
  </ItemGroup>

</Project>
";
        const string Expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project Sdk=""Microsoft.NET.Sdk"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

    <!-- Comment -->
  <ItemGroup>
    <PackageReference Include=""TestPackage"" Version=""2.0.0"" />
  </ItemGroup>

</Project>
";

        AddFile("test.csproj", Original);
        var result = await GetDependencies(new MsBuildReferencesDependencyScanner());
        AssertContainDependency(result, (DependencyType.NuGet, "TestPackage", "4.2.1", 11, 6));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task ProjectJsonDependencies()
    {
        const string Original = /*lang=json,strict*/ @"{
  ""dependencies"": {
    ""a"": {
      ""version"": ""1.0.1"",
      ""type"": ""platform""
    },
    ""b"": {
      ""target"": ""project""
    },
    ""c"": ""1.0.2""
  },
  ""tools"": {
    ""d"": ""1.0.3""
  }
}";

        const string Expected = /*lang=json,strict*/ @"{
  ""dependencies"": {
    ""a"": {
      ""version"": ""2.0.0"",
      ""type"": ""platform""
    },
    ""b"": {
      ""target"": ""project""
    },
    ""c"": ""2.0.0""
  },
  ""tools"": {
    ""d"": ""2.0.0""
  }
}";

        AddFile("project.json", Original);
        var result = await GetDependencies(new ProjectJsonDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.NuGet, "a", "1.0.1", 3, 8),
            (DependencyType.NuGet, "c", "1.0.2", 10, 8),
            (DependencyType.NuGet, "d", "1.0.3", 13, 8));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("project.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PythonRequirementsDependencies()
    {
        const string Original = "A==1.1.0\nB==1.2.0\r\nC==1.3.0";
        const string Expected = "A==2.0.0\nB==2.0.0\r\nC==2.0.0";

        AddFile("requirements.txt", Original);
        var result = await GetDependencies(new PythonRequirementsDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.PyPi, "A", "1.1.0", 1, 4),
            (DependencyType.PyPi, "B", "1.2.0", 2, 4),
            (DependencyType.PyPi, "C", "1.3.0", 3, 4));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("requirements.txt", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task PackagesConfigDependencies()
    {
        const string Original = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<packages>
  <package id=""A"" version=""4.2.1"" targetFramework=""net461"" />
</packages>";

        const string Expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
  <package id=""A"" version=""2.0.0"" targetFramework=""net461"" />
</packages>";


        AddFile("packages.config", Original);
        var result = await GetDependencies(new PackagesConfigDependencyScanner());
        AssertContainDependency(result, (DependencyType.NuGet, "A", "4.2.1", 3, 4));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("packages.config", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PackagesConfigWithCsprojDependencies()
    {
        const string Original = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<packages>
  <package id=""NUnit"" version=""3.11.0"" targetFramework=""net461"" />
</packages>";

        const string OriginalCsproj = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""..\packages\NUnit.3.11.0\build\NUnit.props"" Condition=""Exists('..\packages\NUnit.3.11.0\build\NUnit.props')"" />
  <Import Project=""$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"" Condition=""Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')"" />
  <ItemGroup>
    <Reference Include=""nunit.framework, Version=3.11.0.0, Culture=neutral, PublicKeyToken=2638cd05610744eb, processorArchitecture=MSIL"">
      <HintPath>..\packages\NUnit.3.11.0\lib\net45\nunit.framework.dll</HintPath>
    </Reference>
  </ItemGroup>
  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />
  <Target Name=""EnsureNuGetPackageBuildImports"" BeforeTargets=""PrepareForBuild"">
    <PropertyGroup>
      <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
    </PropertyGroup>
    <Error Condition=""!Exists('..\packages\NUnit.3.11.0\build\NUnit.props')"" Text=""$([System.String]::Format('$(ErrorText)', '..\packages\NUnit.3.11.0\build\NUnit.props'))"" />
  </Target>
</Project>";

        const string Expected = @"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
  <package id=""NUnit"" version=""3.12.0-beta00"" targetFramework=""net461"" />
</packages>";

        const string ExpectedCsproj = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""..\packages\NUnit.3.12.0-beta00\build\NUnit.props"" Condition=""Exists('..\packages\NUnit.3.12.0-beta00\build\NUnit.props')"" />
  <Import Project=""$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"" Condition=""Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')"" />
  <ItemGroup>
    <Reference Include=""nunit.framework, Version=3.12.0.0, Culture=neutral, PublicKeyToken=2638cd05610744eb, processorArchitecture=MSIL"">
      <HintPath>..\packages\NUnit.3.12.0-beta00\lib\net45\nunit.framework.dll</HintPath>
    </Reference>
  </ItemGroup>
  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />
  <Target Name=""EnsureNuGetPackageBuildImports"" BeforeTargets=""PrepareForBuild"">
    <PropertyGroup>
      <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
    </PropertyGroup>
    <Error Condition=""!Exists('..\packages\NUnit.3.12.0-beta00\build\NUnit.props')"" Text=""$([System.String]::Format('$(ErrorText)', '..\packages\NUnit.3.12.0-beta00\build\NUnit.props'))"" />
  </Target>
</Project>";


        AddFile("packages.config", Original);
        AddFile("file.csproj", OriginalCsproj);
        var result = await GetDependencies(new PackagesConfigDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.NuGet, "NUnit", "3.11.0", 3, 4),
            (DependencyType.NuGet, "NUnit", "3.11.0", 7, 8),
            (DependencyType.NuGet, "NUnit", "3.11.0", 6, 6),
            (DependencyType.NuGet, "NUnit", "3.11.0", 15, 6));

        await UpdateDependencies(result, "3.12.0-beta00");
        AssertFileContentEqual("packages.config", Expected, ignoreNewLines: true);
        AssertFileContentEqual("file.csproj", ExpectedCsproj, ignoreNewLines: true);
    }

    [Fact]
    public async Task DockerfileFromDependencies()
    {
        const string Original = @"
FROM a.com/b:1.2.2
FROM a.com/c:1.2.3 AS base
CMD  /code/run-app
";
        const string Expected = @"
FROM a.com/b:2.0.0
FROM a.com/c:2.0.0 AS base
CMD  /code/run-app
";

        AddFile("Dockerfile", Original);
        var result = await GetDependencies(new DockerfileDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.DockerImage, "a.com/b", "1.2.2", 2, 14),
            (DependencyType.DockerImage, "a.com/c", "1.2.3", 3, 14));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("Dockerfile", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task GlobalJsonFromDependencies()
    {
        const string Original = /*lang=json,strict*/ @"{
  ""sdk"": {
    ""version"": ""3.1.100"",
    ""rollForward"": ""disable""
  },
  ""msbuild-sdks"": {
    ""My.Custom.Sdk"": ""5.0.0"",
    ""My.Other.Sdk"": ""1.0.0-beta""
  }
}";
        const string Expected = /*lang=json,strict*/ @"{
  ""sdk"": {
    ""version"": ""3.1.400"",
    ""rollForward"": ""disable""
  },
  ""msbuild-sdks"": {
    ""My.Custom.Sdk"": ""3.1.400"",
    ""My.Other.Sdk"": ""3.1.400""
  }
}";

        AddFile("global.json", Original);
        var result = await GetDependencies(new DotNetGlobalJsonDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.DotNetSdk, ".NET SDK", "3.1.100", 3, 24),
            (DependencyType.NuGet, "My.Custom.Sdk", "5.0.0", 7, 28),
            (DependencyType.NuGet, "My.Other.Sdk", "1.0.0-beta", 8, 32));

        await UpdateDependencies(result, "3.1.400");
        AssertFileContentEqual("global.json", Expected, ignoreNewLines: true);
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public async Task GitSubmodulesFromDependencies()
    {
        // Initialize remote repository
        await using var remote = TemporaryDirectory.Create();
        await ExecuteProcess("git", "init", remote.FullPath);
        await ExecuteProcess("git", "config user.name test", remote.FullPath);
        await ExecuteProcess("git", "config user.email test@example.com", remote.FullPath);
        await File.WriteAllTextAsync(remote.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", "add .", remote.FullPath);
        await ExecuteProcess("git", "commit -m commit-message", remote.FullPath);

        // Get remote head
        string head;
        using (var repository = new Repository(remote.FullPath))
        {
            head = repository.Head.Tip.Sha;
            _testOutputHelper.WriteLine("Head: " + head);
        }

        // Initialize current directory
        await ExecuteProcess("git", "init", _directory.FullPath);
        await ExecuteProcess("git", "config user.name test", _directory.FullPath);
        await ExecuteProcess("git", "config user.email test@example.com", _directory.FullPath);
        await File.WriteAllTextAsync(_directory.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", "add .", _directory.FullPath);
        await ExecuteProcess("git", "commit -m commit-message", _directory.FullPath);

        // Add submodule
        await ExecuteProcess2("git", new string[] { "submodule", "add", remote.FullPath, "submodule_path" }, _directory.FullPath);

        // List files
        var files = Directory.GetFiles(_directory.FullPath, "*", SearchOption.AllDirectories);
        _testOutputHelper.WriteLine("Content of " + _directory.FullPath);
        foreach (var file in files)
        {
            var attr = File.GetAttributes(file);
            _testOutputHelper.WriteLine($"{file} ({attr})");
        }

        // Assert
        var result = await GetDependencies(new GitSubmoduleDependencyScanner());
        AssertContainDependency(result, (DependencyType.GitSubmodule, remote.FullPath, head, 0, 0));

        result.Should().OnlyContain(item => !item.Location.IsUpdatable);

        async Task ExecuteProcess(string process, string args, string workingDirectory)
        {
            _testOutputHelper.WriteLine($"Executing: '{process}' {args} ({workingDirectory})");
            AssertProcessResult(await ProcessExtensions.RunAsTaskAsync(process, args, workingDirectory));
        }

        async Task ExecuteProcess2(string process, string[] args, string workingDirectory)
        {
            _testOutputHelper.WriteLine($"Executing: '{process}' {string.Join(" ", args)} ({workingDirectory})");
            AssertProcessResult(await ProcessExtensions.RunAsTaskAsync(process, args, workingDirectory));
        }

        void AssertProcessResult(ProcessResult result)
        {
            result.ExitCode.Should().Be(0, "git command should return 0. Logs:\n" + string.Join("\n", result.Output));
            _testOutputHelper.WriteLine("git command succeeds\n" + string.Join("\n", result.Output));
        }
    }

    [Fact]
    public async Task GitHubActions()
    {
        const string Path = ".github/workflows/sample.yml";
        const string Original = @"name: demo
on: [push]
jobs:
  check-bats-version:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-node@v1
      - uses: docker://test/setup:v3
      - run: npm install -g bats
      - run: bats -v
    container:
      image: node:10.16-jessie
    services:
      nginx:
        image: nginx:latest
      redis:
        image: redis:1.0
";
        const string Expected = @"name: demo
on: [push]
jobs:
  check-bats-version:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3.0.0
      - uses: actions/setup-node@v3.0.0
      - uses: docker://test/setup:v3.0.0
      - run: npm install -g bats
      - run: bats -v
    container:
      image: node:v3.0.0
    services:
      nginx:
        image: nginx:v3.0.0
      redis:
        image: redis:v3.0.0
";

        AddFile(Path, Original);
        var scanner = new GitHubActionsScanner();
        scanner.ShouldScanFile(Path).Should().BeTrue();
        var result = await GetDependencies(scanner);
        AssertContainDependency(result,
            (DependencyType.GitHubActions, "actions/checkout", "v2", 7, 32),
            (DependencyType.GitHubActions, "actions/setup-node", "v1", 8, 34),
            (DependencyType.DockerImage, "test/setup", "v3", 9, 35),
            (DependencyType.DockerImage, "node", "10.16-jessie", 13, 19),
            (DependencyType.DockerImage, "nginx", "latest", 16, 22),
            (DependencyType.DockerImage, "redis", "1.0", 18, 22));

        await UpdateDependencies(result, "v3.0.0");
        AssertFileContentEqual(Path, Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task Regex()
    {
        const string Original = @"
container:
  image: node:10
services:
  abc
";
        const string Expected = @"
container:
  image: node:v3.0.0
services:
  abc
";

        AddFile("custom/sample.yml", Original);
        var result = await GetDependencies(new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobOptions.None)),
            DependencyType = DependencyType.DockerImage,
            RegexPattern = "image: (?<name>[a-z]+):(?<version>[0-9]+)",
        });
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", "10", 3, 15));

        await UpdateDependencies(result, "v3.0.0");
        AssertFileContentEqual("custom/sample.yml", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DotNetToolsDependencies()
    {
        const string Original = /*lang=json,strict*/ @"{
  ""version"": 1,
  ""isRoot"": true,
  ""tools"": {
    ""dotnet-validate"": {
      ""version"": ""0.0.1-preview.130"",
      ""commands"": [
        ""dotnet-validate""
      ]
    },
    ""dotnet-format"": {
      ""version"": ""5.0.211103"",
      ""commands"": [
        ""dotnet-format""
      ]
    }
  }
}";

        const string Expected = /*lang=json,strict*/ @"{
  ""version"": 1,
  ""isRoot"": true,
  ""tools"": {
    ""dotnet-validate"": {
      ""version"": ""2.0.0"",
      ""commands"": [
        ""dotnet-validate""
      ]
    },
    ""dotnet-format"": {
      ""version"": ""2.0.0"",
      ""commands"": [
        ""dotnet-format""
      ]
    }
  }
}";

        AddFile("dotnet-tools.json", Original);
        var result = await GetDependencies(new DotNetToolManifestDependencyScanner());
        AssertContainDependency(result,
            (DependencyType.NuGet, "dotnet-validate", "0.0.1-preview.130", 5, 22),
            (DependencyType.NuGet, "dotnet-format", "5.0.211103", 11, 20));

        await UpdateDependencies(result, "2.0.0");
        AssertFileContentEqual("dotnet-tools.json", Expected, ignoreNewLines: true);
    }

    private async Task<List<Dependency>> GetDependencies(DependencyScanner scanner)
    {
        var dependencies = new List<Dependency>();
        await foreach (var dep in DependencyScanner.ScanDirectoryAsync(_directory.FullPath, new ScannerOptions { DegreeOfParallelism = 1, Scanners = new[] { scanner } }))
        {
            dependencies.Add(dep);
        }

        // Execute in parallel and compare the number of found dependencies
        var dependencies2 = new List<Dependency>();
        await foreach (var dep in DependencyScanner.ScanDirectoryAsync(_directory.FullPath, new ScannerOptions { DegreeOfParallelism = 16, Scanners = new[] { scanner } }))
        {
            dependencies2.Add(dep);
        }

        dependencies2.Should().HaveCount(dependencies.Count);
        return dependencies;
    }

    private static async Task UpdateDependencies(IEnumerable<Dependency> dependencies, string newVersion)
    {
        await DependencyScanner.UpdateAllAsync(dependencies, newVersion, CancellationToken.None);
    }

    private void AddFile(string path, string content) => AddFile(path, content, Encoding.UTF8);

    private void AddFile(string path, string content, Encoding encoding) => AddFile(path, encoding.GetBytes(content));

    private void AddFile(string path, byte[] content)
    {
        var fullPath = _directory.GetFullPath(path);
        Directory.CreateDirectory(fullPath.Parent);
        File.WriteAllBytes(fullPath, content);
    }

    private static void AssertContainDependency(IEnumerable<Dependency> dependencies, params (DependencyType Type, string Name, string Version, int Line, int Column)[] expectedDepedencies)
    {
        foreach (var expected in expectedDepedencies)
        {
            dependencies.Should().Contain(d =>
                d.Type == expected.Type &&
                d.Name == expected.Name &&
                d.Version == expected.Version &&
                (expected.Line == 0 || ((ILocationLineInfo)d.Location).LineNumber == expected.Line) &&
                (expected.Column == 0 || ((ILocationLineInfo)d.Location).LinePosition == expected.Column), $"'{expected}' should be detected. Dependencies ({dependencies.Count()}):\n{string.Join("\n", dependencies)}");
        }
    }

    private void AssertFileContentEqual(string path, string expected, bool ignoreNewLines)
    {
        var fullPath = _directory.GetFullPath(path);
        var actual = File.ReadAllText(fullPath);
        if (ignoreNewLines)
        {
            actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
            expected = expected.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        actual.Should().Be(expected);
    }

    public void Dispose()
    {
        _directory.Dispose();
    }
}
