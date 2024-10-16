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

public sealed class ScannerTests(ITestOutputHelper testOutputHelper) : IDisposable
{
    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    [Fact]
    public async Task NpmPackageJsonDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "name": "sample",
  "version": "0.1.0",
  "dependencies": {
    "a": "1.0.0"
  },
  "devDependencies": {
    "b": "1.2.3",
    "c": null
  }
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "name": "sample",
  "version": "0.1.0",
  "dependencies": {
    "a": "2.0.0"
  },
  "devDependencies": {
    "b": "2.0.0",
    "c": null
  }
}
""";

        AddFile("package.json", Original);
        var result = await GetDependencies<NpmPackageJsonDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.Npm, "a", "1.0.0", 5, 11),
            (DependencyType.Npm, "b", "1.2.3", 8, 11));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("package.json", Expected, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd")]
    [InlineData("http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")]
    public async Task NuSpecDependencies(string xmlns)
    {
        var original = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="{xmlns}">
                <metadata>
                    <id>sample</id>
                    <version>1.0.0</version>
                    <authors>Microsoft</authors>
                    <dependencies>
                        <dependency id="another-package" version="3.0.0" />
                        <dependency id="yet-another-package" version="1.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        var expected = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="{xmlns}">
                <metadata>
                    <id>sample</id>
                    <version>1.0.0</version>
                    <authors>Microsoft</authors>
                    <dependencies>
                        <dependency id="dummy1" version="2.0.0" />
                        <dependency id="dummy2" version="2.0.0" />
                    </dependencies>
                </metadata>
            </package>
            """;

        AddFile("test.nuspec", original);
        var result = await GetDependencies<NuSpecDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "another-package", "3.0.0", 8, 46),
            (DependencyType.NuGet, "yet-another-package", "1.0.0", 9, 50));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.nuspec", expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildReferencesDependencies()
    {
        const string Original = """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
      <PackageVersion Include="PackageA" Version="1.0.0" />
  </ItemGroup>

  <!-- Comment -->
  <ItemGroup>
    <PackageReference Include="TestPackage" Version="4.2.1" />
    <PackageReference Include="PackageA" VersionOverride="1.2.1" />
    <PackageDownload Include="PackageC" Version="1.2.2" />
    <GlobalPackageReference Include="PackageD" Version="1.2.3" />
  </ItemGroup>

</Project>
""";
        const string Expected = """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Sample</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
      <PackageVersion Include="dummy1" Version="2.0.0" />
  </ItemGroup>

  <!-- Comment -->
  <ItemGroup>
    <PackageReference Include="dummy2" Version="2.0.0" />
    <PackageReference Include="dummy3" VersionOverride="2.0.0" />
    <PackageDownload Include="dummy4" Version="2.0.0" />
    <GlobalPackageReference Include="dummy5" Version="2.0.0" />
  </ItemGroup>

</Project>
""";

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "PackageA", "1.0.0", 8, 42),
            (DependencyType.NuGet, "PackageA", "1.2.1", 14, 42),
            (DependencyType.NuGet, "TestPackage", "4.2.1", 13, 45),
            (DependencyType.NuGet, "PackageC", "1.2.2", 15, 41),
            (DependencyType.NuGet, "PackageD", "1.2.3", 16, 48));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task MsBuildSdkReferencesDependencies()
    {
        const string Original = """
            <Project Sdk="MSBuild.Sdk.Extras/2.0.54">
                <Sdk Name="My.Custom.Sdk1" Version="1.0.0" />
                <Import Sdk="My.Custom.Sdk2/2.0.55" />
            </Project>
            """;
        const string Expected = """
            <Project Sdk="dummy1/1.2.3">
                <Sdk Name="dummy2" Version="1.2.3" />
                <Import Sdk="dummy3/1.2.3" />
            </Project>
            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        await UpdateDependencies(result, "dummy", "1.2.3");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PackagesReferencesWithNamespaceDependencies()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Sample</RootNamespace>
              </PropertyGroup>

                <!-- Comment -->
              <ItemGroup>
                <PackageReference Include="TestPackage" Version="4.2.1" />
              </ItemGroup>

            </Project>

            """;
        const string Expected = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <RootNamespace>Sample</RootNamespace>
              </PropertyGroup>

                <!-- Comment -->
              <ItemGroup>
                <PackageReference Include="dummy1" Version="2.0.0" />
              </ItemGroup>

            </Project>

            """;

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        AssertContainDependency(result, (DependencyType.NuGet, "TestPackage", "4.2.1", 11, 45));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task DotNetTargetFramework()
    {
        const string Original = """
<Project>
    <PropertyGroup>
        <TargetFramework>net472</TargetFramework>
        <TargetFrameworks>net48</TargetFrameworks>
        <TargetFrameworks>net5.0;net6.0</TargetFrameworks>
        <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    </PropertyGroup>
</Project>
""";
        const string Expected = """
<Project>
    <PropertyGroup>
        <TargetFramework>net0.0</TargetFramework>
        <TargetFrameworks>net0.0</TargetFrameworks>
        <TargetFrameworks>net0.0;net0.0</TargetFrameworks>
        <TargetFrameworkVersion>net0.0</TargetFrameworkVersion>
    </PropertyGroup>
</Project>
""";

        AddFile("test.csproj", Original);
        var result = await GetDependencies<MsBuildReferencesDependencyScanner>();
        await UpdateDependencies(result, "dummy", "net0.0");
        AssertFileContentEqual("test.csproj", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task ProjectJsonDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": {
      "version": "1.0.1",
      "type": "platform"
    },
    "b": {
      "target": "project"
    },
    "c": "1.0.2"
  },
  "tools": {
    "d": "1.0.3"
  }
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "dependencies": {
    "a": {
      "version": "2.0.0",
      "type": "platform"
    },
    "b": {
      "target": "project"
    },
    "c": "2.0.0"
  },
  "tools": {
    "d": "2.0.0"
  }
}
""";

        AddFile("project.json", Original);
        var result = await GetDependencies<ProjectJsonDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "a", "1.0.1", 4, 19),
            (DependencyType.NuGet, "c", "1.0.2", 10, 11),
            (DependencyType.NuGet, "d", "1.0.3", 13, 11));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("project.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PythonRequirementsDependencies()
    {
        const string Original = "A==1.1.0\nB==1.2.0\r\nC==1.3.0";
        const string Expected = "dummy1==2.0.0\ndummy2==2.0.0\r\ndummy3==2.0.0";

        AddFile("requirements.txt", Original);
        var result = await GetDependencies<PythonRequirementsDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.PyPi, "A", "1.1.0", 1, 4),
            (DependencyType.PyPi, "B", "1.2.0", 2, 4),
            (DependencyType.PyPi, "C", "1.3.0", 3, 4));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("requirements.txt", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task PackagesConfigDependencies()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="A" version="4.2.1" targetFramework="net461" />
            </packages>
            """;

        const string Expected = """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="dummy1" version="2.0.0" targetFramework="net461" />
            </packages>
            """;

        AddFile("packages.config", Original);
        var result = await GetDependencies<PackagesConfigDependencyScanner>();
        AssertContainDependency(result, (DependencyType.NuGet, "A", "4.2.1", 3, 19));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("packages.config", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task PackagesConfigWithCsprojDependencies()
    {
        const string Original = """
            <?xml version="1.0" encoding="utf-8" ?>
            <packages>
              <package id="NUnit" version="3.11.0" targetFramework="net461" />
            </packages>
            """;

        const string OriginalCsproj = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="..\packages\NUnit.3.11.0\build\NUnit.props" Condition="Exists('..\packages\NUnit.3.11.0\build\NUnit.props')" />
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
              <ItemGroup>
                <Reference Include="nunit.framework, Version=3.11.0.0, Culture=neutral, PublicKeyToken=2638cd05610744eb, processorArchitecture=MSIL">
                  <HintPath>..\packages\NUnit.3.11.0\lib\net45\nunit.framework.dll</HintPath>
                </Reference>
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
                <PropertyGroup>
                  <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
                </PropertyGroup>
                <Error Condition="!Exists('..\packages\NUnit.3.11.0\build\NUnit.props')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\NUnit.3.11.0\build\NUnit.props'))" />
              </Target>
            </Project>
            """;

        const string Expected = """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="dummy1" version="3.12.0-beta00" targetFramework="net461" />
            </packages>
            """;

        const string ExpectedCsproj = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="..\packages\NUnit.3.12.0-beta00\build\NUnit.props" Condition="Exists('..\packages\NUnit.3.12.0-beta00\build\NUnit.props')" />
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
              <ItemGroup>
                <Reference Include="nunit.framework, Version=3.12.0.0, Culture=neutral, PublicKeyToken=2638cd05610744eb, processorArchitecture=MSIL">
                  <HintPath>..\packages\NUnit.3.12.0-beta00\lib\net45\nunit.framework.dll</HintPath>
                </Reference>
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
                <PropertyGroup>
                  <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
                </PropertyGroup>
                <Error Condition="!Exists('..\packages\NUnit.3.12.0-beta00\build\NUnit.props')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\NUnit.3.12.0-beta00\build\NUnit.props'))" />
              </Target>
            </Project>
            """;


        AddFile("packages.config", Original);
        AddFile("file.csproj", OriginalCsproj);
        var result = await GetDependencies<PackagesConfigDependencyScanner>();
        await UpdateDependencies(result, "dummy", "3.12.0-beta00");
        AssertFileContentEqual("packages.config", Expected, ignoreNewLines: true);
        AssertFileContentEqual("file.csproj", ExpectedCsproj, ignoreNewLines: true);
    }

    [Fact]
    public async Task DockerfileFromDependencies()
    {
        const string Original = """
            FROM a.com/b:1.2.2
            FROM a.com/c:1.2.3 AS base
            CMD  /code/run-app
            """;
        const string Expected = """
            FROM dummy1:2.0.0
            FROM dummy2:2.0.0 AS base
            CMD  /code/run-app
            """;

        AddFile("Dockerfile", Original);
        var result = await GetDependencies<DockerfileDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.DockerImage, "a.com/b", "1.2.2", 1, 14),
            (DependencyType.DockerImage, "a.com/c", "1.2.3", 2, 14));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Dockerfile", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task GlobalJsonFromDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "sdk": {
    "version": "3.1.100",
    "rollForward": "disable"
  },
  "msbuild-sdks": {
    "My.Custom.Sdk" : "5.0.0",
    "My.Other.Sdk": "1.0.0-beta"
  }
}
""";
        const string Expected = /*lang=json,strict*/ """
{
  "sdk": {
    "version": "3.1.400",
    "rollForward": "disable"
  },
  "msbuild-sdks": {
    "My.Custom.Sdk": "3.1.400",
    "My.Other.Sdk": "3.1.400"
  }
}
""";

        AddFile("global.json", Original);
        var result = await GetDependencies<DotNetGlobalJsonDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.DotNetSdk, Name: null, "3.1.100", 3, 17),
            (DependencyType.NuGet, "My.Custom.Sdk", "5.0.0", 7, 24),
            (DependencyType.NuGet, "My.Other.Sdk", "1.0.0-beta", 8, 22));

        await UpdateDependencies(result, "dummy", "3.1.400");
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
        await ExecuteProcess("git", "config commit.gpgsign false", remote.FullPath);
        await File.WriteAllTextAsync(remote.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", "add .", remote.FullPath);
        await ExecuteProcess("git", "commit -m commit-message", remote.FullPath);

        // Get remote head
        string head;
        using (var repository = new Repository(remote.FullPath))
        {
            head = repository.Head.Tip.Sha;
            testOutputHelper.WriteLine("Head: " + head);
        }

        // Initialize current directory
        await ExecuteProcess("git", "init", _directory.FullPath);
        await ExecuteProcess("git", "config user.name test", _directory.FullPath);
        await ExecuteProcess("git", "config user.email test@example.com", _directory.FullPath);
        await ExecuteProcess("git", "config commit.gpgsign false", _directory.FullPath);
        await File.WriteAllTextAsync(_directory.GetFullPath("test.txt"), "content");
        await ExecuteProcess("git", "add .", _directory.FullPath);
        await ExecuteProcess("git", "commit -m commit-message", _directory.FullPath);

        // Add submodule
        await ExecuteProcess2("git", ["-c", "protocol.file.allow=always", "submodule", "add", remote.FullPath, "submodule_path"], _directory.FullPath);

        // List files
        var files = Directory.GetFiles(_directory.FullPath, "*", SearchOption.AllDirectories);
        testOutputHelper.WriteLine("Content of " + _directory.FullPath);
        foreach (var file in files)
        {
            var attr = File.GetAttributes(file);
            testOutputHelper.WriteLine($"{file} ({attr})");
        }

        // Assert
        var result = await GetDependencies<GitSubmoduleDependencyScanner>();
        AssertContainDependency(result, (DependencyType.GitSubmodule, remote.FullPath, head, 0, 0));

        result.Should().OnlyContain(item => !item.VersionLocation.IsUpdatable);

        async Task ExecuteProcess(string process, string args, string workingDirectory)
        {
            testOutputHelper.WriteLine($"Executing: '{process}' {args} ({workingDirectory})");
            AssertProcessResult(await ProcessExtensions.RunAsTaskAsync(process, args, workingDirectory));
        }

        async Task ExecuteProcess2(string process, string[] args, string workingDirectory)
        {
            testOutputHelper.WriteLine($"Executing: '{process}' {string.Join(' ', args)} ({workingDirectory})");
            AssertProcessResult(await ProcessExtensions.RunAsTaskAsync(process, args, workingDirectory));
        }

        void AssertProcessResult(ProcessResult result)
        {
            result.ExitCode.Should().Be(0, "git command should return 0. Logs:\n" + string.Join('\n', result.Output));
            testOutputHelper.WriteLine("git command succeeds\n" + string.Join('\n', result.Output));
        }
    }

    [Fact]
    public async Task GitHubActions()
    {
        const string Path = ".github/workflows/sample.yml";
        const string Original = """
name: demo
on: [push]
jobs:
    check-bats-version:
        runs-on: ubuntu-latest
        steps:
            - uses: actions/checkout@v2
            - uses: actions/setup-node@v1
            - uses: docker://test/setup:v3
            - uses: "docker://image/without/version"
            - uses: "sample-org/project/.github/workflows/test@main"
            - run: npm install -g bats
            - run: bats -v
        container:
            image: node:10.16-jessie
        services:
            nginx:
                image: nginx:latest
            redis:
                image: redis:1.0
            service3:
                image: alpine
    job_template:
        uses: sample/template.yml@v1
""";
        const string Expected = """
name: demo
on: [push]
jobs:
    check-bats-version:
        runs-on: ubuntu-latest
        steps:
            - uses: dummy1@v3.0.0
            - uses: dummy2@v3.0.0
            - uses: docker://dummy3:v3.0.0
            - uses: "docker://dummy4"
            - uses: "dummy5@v3.0.0"
            - run: npm install -g bats
            - run: bats -v
        container:
            image: dummy6:v3.0.0
        services:
            nginx:
                image: dummy7:v3.0.0
            redis:
                image: dummy8:v3.0.0
            service3:
                image: dummy9
    job_template:
        uses: dummy10@v3.0.0
""";

        AddFile(Path, Original);
        var result = await GetDependencies<GitHubActionsScanner>();
        AssertContainDependency(result,
            (DependencyType.GitHubActions, "actions/checkout", "v2", 7, 38),
            (DependencyType.GitHubActions, "actions/setup-node", "v1", 8, 40),
            (DependencyType.DockerImage, "test/setup", "v3", 9, 41),
            (DependencyType.DockerImage, "image/without/version", null, 0, 0),
            (DependencyType.GitHubActions, "sample-org/project/.github/workflows/test", "main", 11, 64),
            (DependencyType.DockerImage, "node", "10.16-jessie", 15, 25),
            (DependencyType.DockerImage, "nginx", "latest", 18, 30),
            (DependencyType.DockerImage, "redis", "1.0", 20, 30),
            (DependencyType.DockerImage, "alpine", null, 0, 0));

        await UpdateDependencies(result, "dummy", "v3.0.0");
        AssertFileContentEqual(Path, Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task GitHubActions_WrongPath_NoDependency()
    {
        const string Path = "sample/.github/workflows/sample.yml";
        const string Original = """
name: demo
on: [push]
jobs:
    check-bats-version:
        runs-on: ubuntu-latest
        steps:
            - uses: actions/checkout@v2
            - uses: actions/setup-node@v1
""";
        AddFile(Path, Original);
        var result = await GetDependencies<GitHubActionsScanner>();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Regex()
    {
        const string Original = """
            container:
              image: node:10
            services:
              abc
            """;
        const string Expected = """
            container:
              image: dummy1:v3.0.0
            services:
              abc
            """;

        AddFile("custom/sample.yml", Original);
        var result = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobOptions.None)),
            DependencyType = DependencyType.DockerImage,
            RegexPattern = "image: (?<name>[a-z]+):(?<version>[0-9]+)",
        }]);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", "10", 2, 15));

        await UpdateDependencies(result, "dummy", "v3.0.0");
        AssertFileContentEqual("custom/sample.yml", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task Regex_OptionalVersion()
    {
        const string Original = """
            container:
              image: node
            services:
              abc
            """;
        const string Expected = """
            container:
              image: dummy1
            services:
              abc
            """;

        AddFile("custom/sample.yml", Original);
        var result = await GetDependencies<RegexScanner>([new RegexScanner()
        {
            FilePatterns = new GlobCollection(Glob.Parse("**/*", GlobOptions.None)),
            DependencyType = DependencyType.DockerImage,
            RegexPattern = "image: (?<name>[a-z]+)(:(?<version>[0-9]+))?",
        }]);
        AssertContainDependency(result,
            (DependencyType.DockerImage, "node", null, 0, 0));

        await UpdateDependencies(result, "dummy", "v3.0.0");
        AssertFileContentEqual("custom/sample.yml", Expected, ignoreNewLines: false);
    }

    [Fact]
    public async Task DotNetToolsDependencies()
    {
        const string Original = /*lang=json,strict*/ """
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-validate": {
      "version": "0.0.1-preview.130",
      "commands": [
        "dotnet-validate"
      ]
    },
    "dotnet-format": {
      "version": "5.0.211103",
      "commands": [
        "dotnet-format"
      ]
    }
  }
}
""";

        const string Expected = /*lang=json,strict*/ """
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-validate": {
      "version": "2.0.0",
      "commands": [
        "dotnet-validate"
      ]
    },
    "dotnet-format": {
      "version": "2.0.0",
      "commands": [
        "dotnet-format"
      ]
    }
  }
}
""";

        AddFile("dotnet-tools.json", Original);
        var result = await GetDependencies<DotNetToolManifestDependencyScanner>();
        AssertContainDependency(result,
            (DependencyType.NuGet, "dotnet-validate", "0.0.1-preview.130", 6, 19),
            (DependencyType.NuGet, "dotnet-format", "5.0.211103", 12, 19));

        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("dotnet-tools.json", Expected, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsContainerDependencies()
    {
        AddFile("sample.yml", """
            container: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            container: 'dummy1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsContainerWithoutVersionDependencies()
    {
        AddFile("sample.yml", """
            container: 'image'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            container: 'dummy1'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsJobContainerDependencies()
    {
        AddFile("sample.yml", """
            jobs:
            - job: dummy
              container: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "image", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            jobs:
            - job: dummy
              container: 'image1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsDeploymentContainerDependencies()
    {
        AddFile("sample.yml", """
            jobs:
            - deployment: dummy
              container: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            jobs:
            - deployment: dummy
              container: 'dummy1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsContainerExpandedDependencies()
    {
        AddFile("sample.yml", """
            container:
                image: 'image:1.2.3'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            container:
                image: 'dummy1:2.3.4'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsVmImageDependencies()
    {
        AddFile("sample.yml", """
            pool:
              vmImage: 'ubuntu-18.04'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "", "windows-latest");
        AssertFileContentEqual("sample.yml", """
            pool:
              vmImage: 'windows-latest'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsJobsVmImageDependencies()
    {
        AddFile("sample.yml", """
            jobs:
            - job: dummy
              pool:
                vmImage: 'ubuntu-18.04'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "", "windows-latest");
        AssertFileContentEqual("sample.yml", """
            jobs:
            - job: dummy
              pool:
                vmImage: 'windows-latest'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsStageVmImageDependencies()
    {
        AddFile("sample.yml", """
            stages:
            - stage: dummy
              pool:
                vmImage: 'ubuntu-18.04'
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "", "windows-latest");
        AssertFileContentEqual("sample.yml", """
            stages:
            - stage: dummy
              pool:
                vmImage: 'windows-latest'
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesContainerDependencies()
    {
        AddFile("sample.yml", """
            resources:
              containers:
              - container: dummy
                image: image:1.2.3
                registry: 'registry'
                type: ACR
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.3.4");
        AssertFileContentEqual("sample.yml", """
            resources:
              containers:
              - container: dummy
                image: dummy1:2.3.4
                registry: 'registry'
                type: ACR
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesRepositoryDependencies()
    {
        AddFile("sample.yml", """
            resources:
              repositories:
              - repository: dummy
                name: repo
                ref: 'main'
                type: git
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "1.2.3");
        AssertFileContentEqual("sample.yml", """
            resources:
              repositories:
              - repository: dummy
                name: dummy1
                ref: '1.2.3'
                type: git
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesStepsTask()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            steps:
            - task: UseDotNet@2
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2'
            steps:
            - task: dummy1@2
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesJobsStepsTask()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            jobs:
            - job: B
              steps:
              - task: UseDotNet@2
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2'
            jobs:
            - job: B
              steps:
              - task: dummy1@2
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsResourcesStagesJobsStepsTask()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            stages:
            - stage: A
              jobs:
              - job: B
                steps:
                - task: UseDotNet@2
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2'
            stages:
            - stage: A
              jobs:
              - job: B
                steps:
                - task: dummy1@2
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task AzureDevOpsTemplateInSteps()
    {
        AddFile("sample.yml", """
            pool:
                vmImage: 'ubuntu-18.04'
            steps:
              - template: file.yml@templates
            """);
        var result = await GetDependencies<AzureDevOpsScanner>();
        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("sample.yml", """
            pool:
                vmImage: '2.0.0'
            steps:
              - template: dummy1@2.0.0
            """, ignoreNewLines: true);
    }

    [Fact]
    public async Task HelmChartsDependencies()
    {
        AddFile("Chart.yaml", """
            dependencies:
            - name: nginx
              version: "1.2.3"
              repository: "https://example.com/charts"
            - name: memcached
              version: "3.2.1"
              repository: https://another.example.com/charts
            """);
        var result = await GetDependencies<HelmChartDependencyScanner>();
        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual("Chart.yaml", """
            dependencies:
            - name: nginx
              version: "2.0.0"
              repository: "dummy1"
            - name: memcached
              version: "2.0.0"
              repository: dummy2
            """, ignoreNewLines: true);
    }

    [Theory]
    [InlineData("renovate.json")]
    [InlineData("renovate.json5")]
    [InlineData("renovaterc")]
    [InlineData("renovaterc.json")]
    [InlineData("renovaterc.json5")]
    [InlineData(".github/renovate.json")]
    [InlineData(".github/renovate.json5")]
    [InlineData(".gitlab/renovate.json")]
    [InlineData(".gitlab/renovate.json5")]
    public async Task RenovateExtendsDependencies(string filePath)
    {
        AddFile(filePath, """
            {
              "extends": [
                "config:recommended",
                "github>owner/repo",
                "github>owner/repo#1.2.3",
                "github>owner/repo:file",
                "github>owner/repo:file#1.2.3",
                "github>owner/repo//file",
                "github>owner/repo//file#1.2.3",
              ],
              "packageRules": [
                {
                  "extends": [
                    "github>owner/repo//file#1.2.3"
                  ]
                }
              ]
            }
            """);
        var result = await GetDependencies<RenovateExtendsDependencyScanner>();
        await UpdateDependencies(result, "dummy", "2.0.0");
        AssertFileContentEqual(filePath, """
            {
              "extends": [
                "dummy1",
                "dummy2",
                "dummy3#2.0.0",
                "dummy4",
                "dummy5#2.0.0",
                "dummy6",
                "dummy7#2.0.0"
              ],
              "packageRules": [
                {
                  "extends": [
                    "dummy8#2.0.0"
                  ]
                }
              ]
            }
            """, ignoreNewLines: true);
    }

    private async Task<Dependency[]> GetDependencies<T>(DependencyScanner[]? scanners = null) where T : DependencyScanner
    {
        var options = new ScannerOptions { DegreeOfParallelism = 1 };
        if (scanners is not null)
        {
            options.Scanners = scanners;
        }

        var dependencies = await Scan(options);
        foreach (var dep in dependencies)
        {
            testOutputHelper.WriteLine($"- {dep}");
        }

        // Validate dependencies
        foreach (var dep in dependencies)
        {
            if (dep.Name is not null)
            {
                dep.NameLocation.Should().NotBeNull();
            }

            if (dep.Version is not null)
            {
                dep.VersionLocation.Should().NotBeNull();
            }
        }

        // Ensure getting scanning in parallel gives the same result
        options.DegreeOfParallelism = 16;
        var dependencies2 = await Scan(options);
        dependencies2.Should().HaveCount(dependencies.Length);
        return dependencies;

        async Task<Dependency[]> Scan(ScannerOptions options)
        {
            var items = await DependencyScanner.ScanDirectoryAsync(_directory.FullPath, options);
            return items.Where(d => d.Tags.Contains(typeof(T).FullName)).ToArray();
        }
    }

    private sealed record DetectedDependency(Dependency Dependency, Location Location, Func<Task> UpdateText);

    private static async Task UpdateDependencies(IEnumerable<Dependency> dependencies, string newName, string newVersion)
    {
        // dep name often have unique names (json, yaml, etc.)
        var i = dependencies.Count(d => d.NameLocation is not null && d.NameLocation.IsUpdatable);

        var allLocations = dependencies
            .SelectMany(d => new DetectedDependency[]
            {
                new(d, d.NameLocation, () => d.UpdateNameAsync(newName + i--.ToStringInvariant())),
                new(d, d.VersionLocation, () => d.UpdateVersionAsync(newVersion)),
            })
            .Where(item => item.Location is not null);

        // Group by file location and order by position desc
        foreach (var locationsByFile in allLocations.Where(item => item.Location.IsUpdatable).GroupBy(item => item.Location.FilePath, StringComparer.Ordinal))
        {
            var locationsWithLineInfo = locationsByFile
                .Where(item => item.Location is ILocationLineInfo)
                .OrderByDescending(item => (((ILocationLineInfo)item.Location).LineNumber, ((ILocationLineInfo)item.Location).LinePosition))
                .ToArray();

            var locationWithoutLineInfo = locationsByFile.Where(item => item.Location is not ILocationLineInfo).ToArray();

            foreach (var item in locationsWithLineInfo)
            {

                await item.UpdateText().ConfigureAwait(false);
            }

            foreach (var item in locationWithoutLineInfo)
            {
                await item.UpdateText().ConfigureAwait(false);
            }
        }
    }

    private void AddFile(string path, string content) => AddFile(path, content, Encoding.UTF8);

    private void AddFile(string path, string content, Encoding encoding) => AddFile(path, encoding.GetBytes(content));

    private void AddFile(string path, byte[] content)
    {
        var fullPath = _directory.GetFullPath(path);
        Directory.CreateDirectory(fullPath.Parent);
        File.WriteAllBytes(fullPath, content);
    }

    private static void AssertContainDependency(IEnumerable<Dependency> dependencies, params (DependencyType Type, string Name, string Version, int VersionLine, int VersionColumn)[] expectedDependencies)
    {
        foreach (var expected in expectedDependencies)
        {
            dependencies.Should().Contain(d =>
                d.Type == expected.Type &&
                d.Name == expected.Name &&
                d.Version == expected.Version &&
                (expected.VersionLine == 0 || ((ILocationLineInfo)d.VersionLocation).LineNumber == expected.VersionLine) &&
                (expected.VersionColumn == 0 || ((ILocationLineInfo)d.VersionLocation).LinePosition == expected.VersionColumn), $"\n'{expected}' should be detected. Dependencies ({dependencies.Count()}):\n{string.Join('\n', dependencies)}");
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
