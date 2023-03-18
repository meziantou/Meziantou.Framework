using Microsoft.CodeAnalysis;
using Xunit;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests.SnapshotUpdateStrategies;
public sealed class PromptContextTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public PromptContextTests(ITestOutputHelper testOutputHelper) => _testOutputHelper = testOutputHelper;

    [Fact]
    public async Task TestNameFromXUnitFact()
    {
        var source = """
            public class Dummy
            {
                [Xunit.Fact]
                public void MyTest()
                {
                    #TESTCONTENT#
                }
            }
            """;

        var name = await GetTestName(source, new[] { ("xunit", "2.4.2"), ("xunit.runner.visualstudio", "2.4.5") });
        Assert.Equal("MyTest", name);
    }
    
    [Fact]
    public async Task TestNameFromXUnitTheory()
    {
        var source = """
            public class Dummy
            {
                [Xunit.Theory]
                [Xunit.InlineData(1)]
                public void MyTest(int i)
                {
                    _ = i;

                    #TESTCONTENT#
                }
            }
            """;

        var name = await GetTestName(source, new[] { ("xunit", "2.4.2"), ("xunit.runner.visualstudio", "2.4.5") });
        Assert.Equal("MyTest", name);
    }

    [Fact]
    public async Task NUnit()
    {
        var source = """
            public class Dummy
            {
                [NUnit.Framework.Test]
                public void MyTest()
                {
                    #TESTCONTENT#
                }
            }
            """;

        var name = await GetTestName(source, new[] { ("NUnit", "3.13.3"), ("NUnit3TestAdapter", "4.3.1") });
        Assert.Equal("Dummy.MyTest", name);
    }

    private async Task<string> GetTestName(string source, (string PackageName, string Version)[] packages)
    {
        await using var directory = TemporaryDirectory.Create();
        var projectPath = CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{GetTargetFramework()}}</TargetFramework>
                <LangVersion>11</LangVersion>
                <AssemblyName>{{typeof(PromptContextTests).Assembly.GetName().Name}}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="{{typeof(InlineSnapshot).Assembly.Location}}" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.4.1" />
                {{string.Join("\n", packages.Select(p => $"""<PackageReference Include="{p.PackageName}" Version="{p.Version}" />"""))}}
              </ItemGroup>
            </Project>            
            """);

        var outputFilePath = directory.GetFullPath("output.txt");

        source = source.Replace("#TESTCONTENT#", $$""""
            var name = Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies.PromptContext.Get("dummy.cs").TestName;
            System.IO.File.WriteAllText("""{{outputFilePath}}""", name);
            """", StringComparison.Ordinal);

        var mainPath = CreateTextFile("Program.cs", source);

        var psi = new ProcessStartInfo("dotnet", $"test \"{projectPath}\"")
        {
            WorkingDirectory = directory.FullPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var process = Process.Start(psi);
        process!.WaitForExit();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        _testOutputHelper.WriteLine(stdout);

        var stderr = await process.StandardError.ReadToEndAsync();
        _testOutputHelper.WriteLine(stderr);

        Assert.Equal(0, process.ExitCode);
        var actual = File.ReadAllText(outputFilePath);
        return actual;

        FullPath CreateTextFile(string path, string content)
        {
            var fullPath = directory.GetFullPath(path);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        static string GetTargetFramework()
        {
#if NET472
            return "net472";
#elif NET6_0
            return "net6.0";
#elif NET7_0
            return "net7.0";
#endif
        }
    }
}
