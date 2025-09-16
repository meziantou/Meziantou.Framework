using Microsoft.CodeAnalysis;
using Xunit;
using System.Diagnostics;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests.SnapshotUpdateStrategies;
public sealed class PromptContextTests(ITestOutputHelper testOutputHelper)
{
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

        var name = await GetTestName(source, new[] { ("xunit", "2.5.1"), ("xunit.runner.visualstudio", "2.5.1") });
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

        var name = await GetTestName(source, new[] { ("NUnit", "3.13.3"), ("NUnit3TestAdapter", "4.5.0") });
        Assert.Equal("Dummy.MyTest", name);
    }

    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Not supported in .NET Framework")]
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
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
                {{string.Join("\n", packages.Select(p => $"""<PackageReference Include="{p.PackageName}" Version="{p.Version}" />"""))}}
              </ItemGroup>
            </Project>
            """);

        var outputFilePath = directory.GetFullPath("output.txt");
        outputFilePath.CreateParentDirectory();

        source = source.Replace("#TESTCONTENT#", $$""""
            // System.Diagnostics.Debugger.Launch();
            var name = Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies.PromptContext.Get("dummy.cs").TestName;
            System.IO.File.WriteAllText("""{{outputFilePath}}""", name);
            """", StringComparison.Ordinal);

        var mainPath = CreateTextFile("Program.cs", source);

        var psi = new ProcessStartInfo("dotnet", $"test \"{projectPath}\" -p:UseSharedCompilation=false")
        {
            WorkingDirectory = directory.FullPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var process = Process.Start(psi);
        process.OutputDataReceived += (sender, e) => testOutputHelper.WriteLine(e.Data ?? "");
        process.ErrorDataReceived += (sender, e) => testOutputHelper.WriteLine(e.Data ?? "");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process!.WaitForExitAsync();

        var actual = File.ReadAllText(outputFilePath);
        Assert.Equal(0, process.ExitCode);
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
#elif NET48
            return "net48";
#elif NET8_0
            return "net8.0";
#elif NET9_0
            return "net9.0";
#elif NET10_0
            return "net10.0";
#endif
        }
    }
}
