using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Meziantou.Framework.DependencyScanning.Tests
{
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
            const string Original = @"{
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

            const string Expected = @"{
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
            AssertFileContentEqual("package.json", Expected);
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
            AssertFileContentEqual("test.nuspec", Expected);
        }

        [Fact]
        public async Task PackagesReferencesDependencies()
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
            var result = await GetDependencies(new PackageReferencesDependencyScanner());
            AssertContainDependency(result,
                (DependencyType.NuGet, "TestPackage", "4.2.1", 10, 6));

            await UpdateDependencies(result, "2.0.0");
            AssertFileContentEqual("test.csproj", Expected);
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
            var result = await GetDependencies(new PackageReferencesDependencyScanner());
            AssertContainDependency(result, (DependencyType.NuGet, "TestPackage", "4.2.1", 11, 6));

            await UpdateDependencies(result, "2.0.0");
            AssertFileContentEqual("test.csproj", Expected);
        }

        [Fact]
        public async Task ProjectJsonDependencies()
        {
            const string Original = @"{
  ""dependencies"": {
    ""a"": {
      ""version"": ""1.0.1"",
      ""type"": ""platform""
    },
    ""b"": {
      ""target"": ""project""
    },
    ""c"": ""1.0.2"",
  },
  ""tools"": {
    ""d"": ""1.0.3""
  }
}";

            const string Expected = @"{
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
            AssertFileContentEqual("project.json", Expected);
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
            AssertFileContentEqual("requirements.txt", Expected);
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
            AssertFileContentEqual("packages.config", Expected);
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
            AssertFileContentEqual("packages.config", Expected);
            AssertFileContentEqual("file.csproj", ExpectedCsproj);
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
            AssertFileContentEqual("Dockerfile", Expected);
        }

        [RunIfWindowsFact]
        public async Task GitSubmodulesFromDependencies()
        {
            // Initialize remote repository
            using var remote = TemporaryDirectory.Create();
            await ExecuteProcess("git", "init", remote.FullPath);
            await ExecuteProcess("git", "config user.name test", remote.FullPath);
            await ExecuteProcess("git", "config user.email test@example.com", remote.FullPath);
            File.WriteAllText(remote.GetFullPath("test.txt"), "content");
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
            File.WriteAllText(_directory.GetFullPath("test.txt"), "content");
            await ExecuteProcess("git", "add .", _directory.FullPath);
            await ExecuteProcess("git", "commit -m commit-message", _directory.FullPath);

            // Add submodule
            await ExecuteProcess2("git", new string[] { "submodule", "add", remote.FullPath, "submodule_path" }, _directory.FullPath);

            // List files
            // TODO log file attribute
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

            Assert.All(result, result => Assert.False(result.Location.IsUpdatable));

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
                if (result.ExitCode == 0)
                {
                    _testOutputHelper.WriteLine("git command succeeds\n" + string.Join("\n", result.Output));
                    return;
                }

                Assert.False(true, "git command failed. Logs:\n" + string.Join("\n", result.Output));
            }
        }

        private async Task<List<Dependency>> GetDependencies(DependencyScanner scanner)
        {
            var dependencies = new List<Dependency>();
            await foreach (var dep in DependencyScanner.ScanDirectoryAsync(_directory.FullPath, new ScannerOptions { DegreeOfParallelism = 1, Scanners = new[] { scanner } }))
            {
                dependencies.Add(dep);
            }

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
                Assert.True(dependencies.Any(d =>
                    d.Type == expected.Type &&
                    d.Name == expected.Name &&
                    d.Version == expected.Version &&
                    (expected.Line == 0 || ((ILocationLineInfo)d.Location).LineNumber == expected.Line) &&
                    (expected.Column == 0 || ((ILocationLineInfo)d.Location).LinePosition == expected.Column)), $"Dependency '{expected}' not found. Dependencies ({dependencies.Count()}):\n{string.Join("\n", dependencies)}");
            }
        }

        private void AssertFileContentEqual(string path, string expected)
        {
            var fullPath = _directory.GetFullPath(path);
            Assert.Equal(expected, File.ReadAllText(fullPath));
        }

        public void Dispose()
        {
            _directory.Dispose();
        }
    }
}
