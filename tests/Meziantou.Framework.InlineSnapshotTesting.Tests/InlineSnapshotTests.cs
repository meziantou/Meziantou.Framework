using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Meziantou.Framework.HumanReadable;
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
    public async Task UpdateSnapshotSupportIfDirective()
    {
        await AssertSnapshot(preprocessorSymbols: ["SampleDirective"],
            source: $$"""
            #if SampleDirective
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), /*start*/expected: /* middle */ "" /* after */);
            #endif
            """,
            expected: $$"""
            #if SampleDirective
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), /*start*/expected: /* middle */ "{}" /* after */);
            #endif
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
    public async Task UpdateSnapshotUsingRawString_Indentation()
    {
        await AssertSnapshot($$""""
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            {{nameof(InlineSnapshot)}}.
                {{nameof(InlineSnapshot.Validate)}}(data, "");
            """", $$""""
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            {{nameof(InlineSnapshot)}}.
                {{nameof(InlineSnapshot.Validate)}}(data, """
                    FirstName: Gérald
                    LastName: Barré
                    NickName: meziantou
                    """);
            """");
    }

    [Fact]
    public async Task UpdateSnapshotUsingVerbatimWhenCSharpLanguageIs10()
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
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(data, @"FirstName: Gérald
            LastName: Barré
            NickName: meziantou");
            """",
            languageVersion: "10", forceUpdateSnapshots: true);
    }

    [Fact]
    public async Task SupportHelperMethods()
    {
        await AssertSnapshot($$""""
            Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected, filePath, lineNumber);
            }
            """", $$""""
            Helper("{}");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task SupportAsyncHelperMethods()
    {
        await AssertSnapshot($$""""
            await Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(data, expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """", $$""""
            await Helper("""
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """);

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(data, expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """");
    }

    [Fact]
    public async Task SupportAsyncGenericHelperMethods()
    {
        await AssertSnapshot($$""""
            await Helper<int>("");

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper<T>(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """", $$""""
            await Helper<int>("{}");

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper<T>(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """");
    }

    [Fact]
    public async Task SupportMultiLevelsHelperMethods()
    {
        await AssertSnapshot($$""""
            Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                Helper2(expected, filePath, lineNumber);
            }

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper2(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected, filePath, lineNumber);
            }
            """", $$""""
            Helper("{}");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                Helper2(expected, filePath, lineNumber);
            }

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper2(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), expected, filePath, lineNumber);
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

    [Fact]
    public async Task UpdateSnapshotWhenForceUpdateSnapshotsIsEnabled()
    {
        await AssertSnapshot(forceUpdateSnapshots: true, source: $$""""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), """
                {}
                """);
            """", expected: $$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), "{}");
            """);
    }

    [Fact]
    public async Task DoNotUpdateSnapshotWhenForceUpdateSnapshotsIsDisableAndTheValueIsOk()
    {
        await AssertSnapshot(forceUpdateSnapshots: false, source: $$""""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}(new object(), """
                {}
                """);
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_AddParameter()
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}("");
            """, $$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.Validate)}}("", "");
            """, launchDebugger: false);
    }

    [Fact]
    public async Task UpdateSnapshot_MultiLine_AddParameter()
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}
                .{{nameof(InlineSnapshot.Validate)}}("");
            """, $$"""
            {{nameof(InlineSnapshot)}}
                .{{nameof(InlineSnapshot.Validate)}}("", "");
            """);
    }

    [Fact]
    public async Task UpdateSnapshot_Builder_MultiLine_AddParameter()
    {
        await AssertSnapshot($$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.WithSettings)}}(default(InlineSnapshotSettings))
                .{{nameof(InlineSnapshot.Validate)}}("");
            """, $$"""
            {{nameof(InlineSnapshot)}}.{{nameof(InlineSnapshot.WithSettings)}}(default(InlineSnapshotSettings))
                .{{nameof(InlineSnapshot.Validate)}}("", "");
            """);
    }

    [Fact]
    public void ScrubLinesMatching_Regex()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesMatching(new Regex("Line[2]", RegexOptions.None, TimeSpan.FromSeconds(10))))
            .Validate("Line1\nLine2\nLine3", "Line1\nLine3");
    }

    [Fact]
    public void ScrubLinesMatching_Pattern()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesMatching("Line[2]"))
            .Validate("Line1\nLine2\nLine3", "Line1\nLine3");
    }

    [Fact]
    [SuppressMessage("Usage", "MA0074:Avoid implicit culture-sensitive methods", Justification = "Testing")]
    public void ScrubLinesContaining()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesContaining("line2"))
            .Validate("Line1\nLine2\nLine3", "Line1\nLine3");
    }

    [Fact]
    public void ScrubLinesContaining_StringComparison_OrdinalIgnoreCase()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, "line2"))
            .Validate("Line1\nLine2\nLine3", "Line1\nLine3");
    }

    [Fact]
    public void ScrubLinesContaining_StringComparison_Ordinal()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesContaining(StringComparison.Ordinal, "line2"))
            .Validate("Line1\nLine2\nLine3", "Line1\nLine2\nLine3");
    }

    [Fact]
    public void ScrubLinesWithReplace()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesWithReplace(line => line.ToLowerInvariant()))
            .Validate("Line1\nLine2\nLine3", "line1\nline2\nline3");
    }

    [Fact]
    public void ScrubLinesWithReplace_RemoveLine()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesWithReplace(line => line == "Line2" ? null : line))
            .Validate("Line1\nLine2\nLine3", "Line1\nLine3");
    }

    [Fact]
    public void Scrub_Guid()
    {
        var guids = new[]
        {
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("6ff5182f-7644-4bc1-a3a4-38092cb3663a"),
        };

        // Validate parallelism to be sure Guids are not shared between compilation
        Parallel.For(1, 1000, _ =>
        {
            InlineSnapshot
                .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.ScrubGuid()))
                .Validate(guids, """
                    - 00000000-0000-0000-0000-000000000001
                    - 00000000-0000-0000-0000-000000000001
                    - 00000000-0000-0000-0000-000000000002
                    """);
        });
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
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Not supported on .NET Framework")]
    private async Task AssertSnapshot(string source, string expected = null, bool launchDebugger = false, string languageVersion = "11", bool autoDetectCI = false, bool forceUpdateSnapshots = false, IEnumerable<KeyValuePair<string, string>> environmentVariables = null, string[]? preprocessorSymbols = null)
    {
        await using var directory = TemporaryDirectory.Create();
        var projectPath = CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetType>exe</TargetType>
                <TargetFramework>{{GetTargetFramework()}}</TargetFramework>
                <LangVersion>{{languageVersion}}</LangVersion>
                <Nullable>disable</Nullable>
                <DebugType>portable</DebugType>
                <DefineConstants>{{string.Join(";", preprocessorSymbols ?? [])}}</DefineConstants>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="{{typeof(HumanReadableSerializer).Assembly.Location}}" />
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
                    {{(launchDebugger ? "System.Diagnostics.Debugger.Launch();" : "")}}
                    InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
                    {
                        {{nameof(InlineSnapshotSettings.AutoDetectContinuousEnvironment)}} = {{(autoDetectCI ? "true" : "false")}},
                        {{nameof(InlineSnapshotSettings.SnapshotUpdateStrategy)}} = {{nameof(SnapshotUpdateStrategy)}}.{{nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure)}},
                        {{nameof(InlineSnapshotSettings.ForceUpdateSnapshots)}} = {{(forceUpdateSnapshots ? "true" : "false")}},
                    };
                }
            }
            """");

#if NET472 || NET48
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
            if (key == "GITHUB_WORKSPACE")
                continue;

            if (key.StartsWith("GITHUB", StringComparison.Ordinal))
            {
                psi.EnvironmentVariables.Remove(key);
            }
        }

        psi.EnvironmentVariables.Add("DiffEngine_Disabled", "true");
        psi.EnvironmentVariables.Add("MF_CurrentDirectory", Environment.CurrentDirectory);
        if (environmentVariables is not null)
        {
            foreach (var variable in environmentVariables)
            {
                psi.EnvironmentVariables.Add(variable.Key, variable.Value);
            }
        }

        var process = Process.Start(psi);
        await process!.WaitForExitAsync();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        _testOutputHelper.WriteLine(stdout);

        var stderr = await process.StandardError.ReadToEndAsync();
        _testOutputHelper.WriteLine(stderr);

        var actual = File.ReadAllText(mainPath);
        expected ??= source;

        actual = SnapshotComparer.Default.NormalizeValue(actual);
        expected = SnapshotComparer.Default.NormalizeValue(expected);
        if (actual != expected)
        {
            Assert.Fail("Snapshots are different\n" + InlineDiffAssertionMessageFormatter.Instance.FormatMessage(expected, actual));
        }

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
#elif NET6_0
            return "net6.0";
#elif NET7_0
            return "net7.0";
#elif NET8_0
            return "net8.0";
#endif
        }

        static string GetPackageReferences()
        {
            var names = typeof(InlineSnapshotTests).Assembly.GetManifestResourceNames();
            using var stream = typeof(InlineSnapshotTests).Assembly.GetManifestResourceStream("Meziantou.Framework.InlineSnapshotTesting.Tests.Meziantou.Framework.InlineSnapshotTesting.csproj");
            var doc = XDocument.Load(stream);
            var items = doc.Root.Descendants("PackageReference");

            var packages = items.Where(item => item.Parent.Attribute("Condition") is null).ToList();
#if NET472 || NET48
            packages.AddRange(items.Where(item => item.Parent.Attribute("Condition") is not null));
#endif

            return string.Join("\n", packages.Select(item => item.ToString()));
        }
    }
}
