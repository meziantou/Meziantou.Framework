using System.Diagnostics;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;

public sealed class InlineSnapshotTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public InlineSnapshotTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task UpdateSnapshotUsingQuotedString()
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), "");
            """, $$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), "{}");
            """);
    }
    
    [Fact]
    public async Task UpdateSnapshotPreserveComments()
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), /*start*/expected: /* middle */ "" /* after */);
            """, $$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), /*start*/expected: /* middle */ "{}" /* after */);
            """);
    }
    
    [Fact]
    public async Task UpdateSnapshotWhenExpectedIsNull()
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected: null);
            """, $$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected: "{}");
            """);
    }

    [Fact]
    public async Task UpdateSnapshotUsingRawString()
    {
        await AssertSnapshot($$""""
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(data, "");
            """", $$""""
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(data, """
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """);
            """");
    }

    [Fact]
    public async Task SupportHelperMethods()
    {
        await AssertSnapshot($$""""
            Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), null, expected, filePath, lineNumber);
            }
            """", $$""""
            Helper("{}");
            
            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), null, expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task UpdateMultipleSnapshots()
    {
        await AssertSnapshot($$""""
            Console.WriteLine("first");
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new { A = 1, B = 2 }, "");
            Console.WriteLine("Second");
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new { A = 3, B = 4 }, "");
            """", $$""""
            Console.WriteLine("first");
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new { A = 1, B = 2 }, """
                A: 1
                B: 2
                """);
            Console.WriteLine("Second");
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new { A = 3, B = 4 }, """
                A: 3
                B: 4
                """);
            """");
    }

    [Theory]
    [InlineData("CI", "true")]
    [InlineData("CI", "TRUE")]
    [InlineData("CI", "TruE")]
    [InlineData("GITLAB_CI", "true")]
    public async Task DoNotUpdateOnCI(string key, string value)
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), "");
            """,
            autoDetectCI: true,
            environmentVariables: new[] { new KeyValuePair<string, string>(key, value) });
    }

    [SuppressMessage("Design", "MA0042:Do not use blocking calls in an async method", Justification = "Not supported on .NET Framework")]
    private async Task AssertSnapshot(string source, string expected = null, bool autoDetectCI = false, IEnumerable<KeyValuePair<string, string>> environmentVariables = null)
    {
        await using var directory = TemporaryDirectory.Create();
        var projectPath = CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetType>exe</TargetType>
                <TargetFramework>{{GetTargetFramework()}}</TargetFramework>
                <LangVersion>11</LangVersion>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="{{typeof(InlineSnapshot).Assembly.Location}}" />
              </ItemGroup>
              <ItemGroup>
                {{GetPackageReferences()}}
              </ItemGroup>
            </Project>            
            """);

        CreateTextFile("globals.cs", """
            global using System;
            global using System.Runtime.CompilerServices;
            global using Meziantou.Framework.InlineSnapshotTesting;
            """);

        CreateTextFile("settings.cs", $$""""
            static class Sample
            {
                [ModuleInitializer]
                public static void Initialize()
                {
                    InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with 
                    {
                        {{nameof(InlineSnapshotSettings.AutoDetectContinuousEnvironment)}} = {{(autoDetectCI ? "true" : "false")}},
                        {{nameof(InlineSnapshotSettings.SnapshotUpdateStrategy)}} = {{nameof(SnapshotUpdateStrategy)}}.{{nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure)}},
                    };
                }
            }
            """");

#if NET472
        CreateTextFile("ModuleInitializerAttribute.cs", $$""""
            namespace System.Runtime.CompilerServices
            {
                [AttributeUsage(AttributeTargets.Method, Inherited = false)]
                public sealed class ModuleInitializerAttribute : Attribute
                {
                    public ModuleInitializerAttribute()
                    {
                    }
                }
            }
            """");
#endif

        var mainPath = CreateTextFile("Program.cs", source);

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{projectPath}\"")
        {
            WorkingDirectory = directory.FullPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.EnvironmentVariables.Remove("CI");
        foreach (var key in psi.EnvironmentVariables.Keys.Cast<string>().ToArray())
        {
            if (key.StartsWith("GITHUB", StringComparison.Ordinal))
            {
                psi.EnvironmentVariables.Remove(key);
            }
        }

        psi.EnvironmentVariables.Add("DiffEngine_Disabled", "true");
        if (environmentVariables != null)
        {
            foreach (var variable in environmentVariables)
            {
                psi.EnvironmentVariables.Add(variable.Key, variable.Value);
            }
        }

        var process = Process.Start(psi);
        process!.WaitForExit();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        _testOutputHelper.WriteLine(stdout);

        var stderr = await process.StandardError.ReadToEndAsync();
        _testOutputHelper.WriteLine(stderr);

        var actual = File.ReadAllText(mainPath);
        actual.Should().Be(expected ?? source, InlineDiffAssertionMessageFormatter.Instance.FormatMessage(expected, actual));

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

        static string GetPackageReferences()
        {
            var names = typeof(InlineSnapshotTests).Assembly.GetManifestResourceNames();
            using var stream = typeof(InlineSnapshotTests).Assembly.GetManifestResourceStream("Meziantou.Framework.InlineSnapshotTesting.Tests.Meziantou.Framework.InlineSnapshotTesting.csproj");
            var doc = XDocument.Load(stream);
            var items = doc.Root.Descendants("PackageReference");

            var packages = items.Where(item => item.Parent.Attribute("Condition") == null).ToList();
#if NET472
            packages.AddRange(items.Where(item => item.Parent.Attribute("Condition") != null));
#endif

            return string.Join("\n", packages.Select(item => item.ToString()));
        }
    }
}
