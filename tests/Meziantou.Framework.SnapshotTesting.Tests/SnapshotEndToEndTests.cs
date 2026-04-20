#nullable enable

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotEndToEndTests
{
    public enum SnapshotTestFramework
    {
        Xunit,
        XunitV3,
        MSTest,
        NUnit,
        TUnit,
    }

    [Fact]
    public async Task Validate_EndToEnd_CreatesActualFile_WhenSnapshotFails()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("sample", SnapshotTestUtilities.CreateFailureSettings());
                }
            }
            """,
            expectFailure: true,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleTest.verified.txt", "expected"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest.actual.txt", "sample"),
            ("__snapshots__/SampleTest.verified.txt", "expected"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_CreatesSnapshot_WhenSnapshotDoesNotExist()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_DoesNotCreateActualFile_WhenSnapshotMatches()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("sample", SnapshotTestUtilities.CreateFailureSettings());
                }
            }
            """,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleTest.verified.txt", "sample"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Fails_WhenExpectedHasMoreFilesThanActual()
    {
        var snapshotFiles = await AssertSnapshot(
            CreateFixedCountSerializerSource(count: 1),
            expectFailure: true,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleTest.verified.txt", "value_0"u8.ToArray()),
                new SnapshotFile("__snapshots__/SampleTest_1.verified.txt", "value_1"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest.verified.txt", "value_0"),
            ("__snapshots__/SampleTest_1.verified.txt", "value_1"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Fails_WhenActualHasMoreFilesThanExpected()
    {
        var snapshotFiles = await AssertSnapshot(
            CreateFixedCountSerializerSource(count: 2),
            expectFailure: true,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleTest_0.verified.txt", "value_0"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest_0.verified.txt", "value_0"),
            ("__snapshots__/SampleTest_1.actual.txt", "value_1"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Succeeds_WhenMultipleExpectedSnapshotsMatch()
    {
        var snapshotFiles = await AssertSnapshot(
            CreateFixedCountSerializerSource(count: 2),
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleTest_0.verified.txt", "value_0"u8.ToArray()),
                new SnapshotFile("__snapshots__/SampleTest_1.verified.txt", "value_1"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest_0.verified.txt", "value_0"),
            ("__snapshots__/SampleTest_1.verified.txt", "value_1"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_MultipleXunitTests_Succeeds_WhenAllSnapshotsMatch()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleFact()
                {
                    Snapshot.Validate("fact-value", SnapshotTestUtilities.CreateFailureSettings());
                }

                [Theory]
                [InlineData("alpha")]
                [InlineData("beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateFailureSettings());
                }
            }
            """,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleFact.verified.txt", "fact-value"u8.ToArray()),
                new SnapshotFile("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"u8.ToArray()),
                new SnapshotFile("__snapshots__/SampleTheory_beta.verified.txt", "beta"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleFact.verified.txt", "fact-value"),
            ("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/SampleTheory_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_MultipleXunitTests_Fails_WhenOneSnapshotIsBad()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleFact()
                {
                    Snapshot.Validate("fact-value", SnapshotTestUtilities.CreateFailureSettings());
                }

                [Theory]
                [InlineData("alpha")]
                [InlineData("beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateFailureSettings());
                }
            }
            """,
            expectFailure: true,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleFact.verified.txt", "fact-value"u8.ToArray()),
                new SnapshotFile("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"u8.ToArray()),
                new SnapshotFile("__snapshots__/SampleTheory_beta.verified.txt", "incorrect"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleFact.verified.txt", "fact-value"),
            ("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/SampleTheory_beta.actual.txt", "beta"),
            ("__snapshots__/SampleTheory_beta.verified.txt", "incorrect"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_MultipleXunitTests_RunsSingleFilteredTest()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleFact()
                {
                    Snapshot.Validate("fact-value", SnapshotTestUtilities.CreateSuccessSettings());
                }

                [Theory]
                [InlineData("alpha")]
                [InlineData("beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFilter: "FullyQualifiedName~GeneratedSnapshotTests.SampleFact");

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleFact.verified.txt", "fact-value"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_CreatesDistinctSnapshots_WhenTestContextIsUsed()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Theory]
                [InlineData("alpha")]
                [InlineData("beta")]
                public void SampleTheory(string value)
                {
                    var previous = Snapshot.TestContext.Value;
                    Snapshot.TestContext.Value = new SnapshotTestContext(TestName: "Case_" + value);
                    try
                    {
                        Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                    }
                    finally
                    {
                        Snapshot.TestContext.Value = previous;
                    }
                }
            }
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/Case_alpha.verified.txt", "alpha"),
            ("__snapshots__/Case_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_CreatesDistinctSnapshots_WhenUsingXunitV3Context()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Theory]
                [InlineData("alpha")]
                [InlineData("beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/SampleTheory_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_CreatesDistinctSnapshots_WhenUsingTUnitContext()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Test]
                [Arguments("alpha")]
                [Arguments("beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFramework: SnapshotTestFramework.TUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/SampleTheory_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_CreatesDistinctSnapshots_WhenUsingNUnitContext()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            [TestFixture]
            public sealed class GeneratedSnapshotTests
            {
                [TestCase("alpha")]
                [TestCase("beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFramework: SnapshotTestFramework.NUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/SampleTheory_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_UsesCustomSnapshotNames_WhenUsingNUnitContext()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            [TestFixture]
            public sealed class GeneratedSnapshotTests
            {
                [TestCase("alpha", TestName = "Case_alpha")]
                [TestCase("beta", TestName = "Case_beta")]
                public void SampleTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFramework: SnapshotTestFramework.NUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/Case_alpha.verified.txt", "alpha"),
            ("__snapshots__/Case_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesHashSuffix_WhenSnapshotNameIsTooLong()
    {
        var methodName = "SampleTest" + new string('a', 200);
        var snapshotFiles = await AssertSnapshot(
            $$"""
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void {{methodName}}()
                {
                    Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        var snapshotFile = Assert.Single(snapshotFiles);
        Assert.StartsWith("__snapshots__/SampleTest", snapshotFile.RelativePath, StringComparison.Ordinal);
        Assert.Matches(new Regex("^__snapshots__/[A-Za-z0-9._-]+_[0-9a-f]{8}\\.verified\\.txt$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), snapshotFile.RelativePath);
        Assert.Equal("sample", snapshotFile.ContentAsString);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesHashSuffix_WhenSnapshotNameIsReserved()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var previous = Snapshot.TestContext.Value;
                    Snapshot.TestContext.Value = new SnapshotTestContext(TestName: "snapshot.verified");
                    try
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                    finally
                    {
                        Snapshot.TestContext.Value = previous;
                    }
                }
            }
            """);

        var snapshotFile = Assert.Single(snapshotFiles);
        Assert.Matches(new Regex("^__snapshots__/snapshot\\.verified_[0-9a-f]{8}\\.verified\\.txt$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), snapshotFile.RelativePath);
        Assert.Equal("sample", snapshotFile.ContentAsString);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesTypedExtension_WhenValueIsByteArray()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var payload = new byte[] { 0x42, 0x00, 0x43 };
                    Snapshot.Validate(payload, SnapshotType.Png, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        var snapshotFile = Assert.Single(snapshotFiles);
        Assert.Equal("__snapshots__/SampleTest.verified.png", snapshotFile.RelativePath);
        Assert.Equal([0x42, 0x00, 0x43], snapshotFile.Content);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesTypedExtension_WhenValueIsStream()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    using var stream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03, 0x04 });
                    Snapshot.Validate(stream, SnapshotType.Png, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        var snapshotFile = Assert.Single(snapshotFiles);
        Assert.Equal("__snapshots__/SampleTest.verified.png", snapshotFile.RelativePath);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], snapshotFile.Content);
    }

    [Theory]
    [InlineData(SnapshotTestFramework.Xunit)]
    [InlineData(SnapshotTestFramework.XunitV3)]
    [InlineData(SnapshotTestFramework.MSTest)]
    [InlineData(SnapshotTestFramework.NUnit)]
    [InlineData(SnapshotTestFramework.TUnit)]
    public async Task Validate_EndToEnd_Smoke_WorksAcrossFrameworks(SnapshotTestFramework testFramework)
    {
        var snapshotFiles = await AssertSnapshot(GetFrameworkSmokeSource(testFramework), testFramework);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/SampleTest.verified.txt", "sample"),
        ]);
    }

    private static string GetFrameworkSmokeSource(SnapshotTestFramework framework)
    {
        return framework switch
        {
            SnapshotTestFramework.Xunit or SnapshotTestFramework.XunitV3 =>
                """
                public sealed class GeneratedSnapshotTests
                {
                    [Fact]
                    public void SampleTest()
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }
                """,
            SnapshotTestFramework.MSTest =>
                """
                [TestClass]
                public sealed class GeneratedSnapshotTests
                {
                    [TestMethod]
                    public void SampleTest()
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }
                """,
            SnapshotTestFramework.NUnit =>
                """
                [TestFixture]
                public sealed class GeneratedSnapshotTests
                {
                    [Test]
                    public void SampleTest()
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }
                """,
            SnapshotTestFramework.TUnit =>
                """
                using System.Threading.Tasks;

                public sealed class GeneratedSnapshotTests
                {
                    [Test]
                    public async Task SampleTest()
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                        await Task.CompletedTask;
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null),
        };
    }

    private static string CreateFixedCountSerializerSource(int count)
    {
        return $$"""
            using System.Globalization;

            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var settings = new SnapshotSettings
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
                    };

                    settings.Serializers.Add(new FixedCountSerializer({{count}}));
                    Snapshot.Validate("sample", settings);
                }
            }

            file sealed class FixedCountSerializer(int count) : ISnapshotSerializer
            {
                public bool CanSerialize(SnapshotType type, object? value) => type == SnapshotType.Default;

                public SerializedSnapshot Serialize(SnapshotType type, object? value)
                {
                    var result = new List<SnapshotData>(count);
                    for (var i = 0; i < count; i++)
                    {
                        result.Add(new SnapshotData("txt", Encoding.UTF8.GetBytes("value_" + i.ToString(CultureInfo.InvariantCulture))));
                    }

                    return new SerializedSnapshot(result);
                }
            }
            """;
    }

    private static void AssertSnapshotContent(SnapshotFile[] snapshotFiles, (string RelativePath, string Content)[] expected)
    {
        Assert.Equal(expected, snapshotFiles.Select(f => (f.RelativePath, f.ContentAsString)));
    }

    private static async Task<SnapshotFile[]> AssertSnapshot(
        [StringSyntax("c#-test")] string source,
        SnapshotTestFramework testFramework = SnapshotTestFramework.XunitV3,
        string? targetFramework = null,
        bool expectFailure = false,
        IReadOnlyList<SnapshotFile>? existingFiles = null,
        string? testFilter = null)
    {
        await using var directory = TemporaryDirectory.Create();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        var snapshotProjectPath = GetRepositoryRoot() / "src" / "Meziantou.Framework.SnapshotTesting" / "Meziantou.Framework.SnapshotTesting.csproj";
        var snapshotPropsPath = GetRepositoryRoot() / "src" / "Meziantou.Framework.SnapshotTesting" / "build" / "Meziantou.Framework.SnapshotTesting.props";
        CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{snapshotPropsPath}}" />
              <PropertyGroup>
                <TargetFramework>{{targetFramework ?? GetTargetFramework()}}</TargetFramework>
                <Nullable>disable</Nullable>
                <IsPackable>false</IsPackable>
                {{GetAdditionalProjectProperties(testFramework)}}
              </PropertyGroup>
              <ItemGroup>
                {{GetPackageReferences(testFramework)}}
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{{snapshotProjectPath}}" />
              </ItemGroup>
            </Project>
            """);
        CreateTextFile("GlobalUsings.cs", GetGlobalUsings(testFramework));
        CreateTextFile("SnapshotTestUtilities.cs", GetSnapshotTestUtilitiesSource());
        CreateTextFile("SnapshotIntegrationTests.cs", source);
        if (testFramework == SnapshotTestFramework.TUnit)
        {
            CreateTextFile("global.json", """
                {
                  "test": {
                    "runner": "Microsoft.Testing.Platform"
                  }
                }
                """);
        }

        if (existingFiles is not null)
        {
            foreach (var existingFile in existingFiles)
            {
                CreateBinaryFile(existingFile.RelativePath, existingFile.Content);
            }
        }

        await ExecuteDotNet(directory.FullPath, dotnetPath, ["restore"], expectedExitCode: 0);
        await ExecuteDotNet(directory.FullPath, dotnetPath, ["build", "--no-restore"], expectedExitCode: 0);
        await ExecuteDotNet(directory.FullPath, dotnetPath, GetDotNetTestArguments(testFramework, testFilter), expectedExitCode: expectFailure ? 1 : 0);

        return GetGeneratedSnapshotFiles(directory.FullPath);

        FullPath CreateTextFile(string path, string content)
        {
            var fullPath = directory.GetFullPath(path);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        FullPath CreateBinaryFile(string path, byte[] data)
        {
            var fullPath = directory.GetFullPath(path);
            fullPath.CreateParentDirectory();
            File.WriteAllBytes(fullPath, data);
            return fullPath;
        }
    }

    private static string[] GetDotNetTestArguments(SnapshotTestFramework testFramework, string? testFilter)
    {
        if (testFramework == SnapshotTestFramework.TUnit)
            return ["test", "--no-build"];

        var arguments = new List<string>
        {
            "test",
            "--no-build",
            "--nologo",
            "-v",
            "minimal",
        };

        if (!string.IsNullOrWhiteSpace(testFilter))
        {
            arguments.Add("--filter");
            arguments.Add(testFilter);
        }

        return [.. arguments];
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        return GetRepositoryRoot(filePath, sourceRoots: null);
    }

    private static FullPath GetRepositoryRoot(string? filePath, IEnumerable<string?>? sourceRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var resolvedSourceFilePath = sourceRoots is null
            ? SnapshotCallerContext.ResolveSourceFilePath(filePath)
            : SnapshotCallerContext.ResolveSourceFilePath(filePath, sourceRoots);

        return resolvedSourceFilePath.Parent.Parent.Parent;
    }

    private static SnapshotFile[] GetGeneratedSnapshotFiles(FullPath rootPath)
    {
        return Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
                    .Select(path => path.Replace('\\', '/'))
                    .Where(path => path.Contains("/__snapshots__/", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .Select(file => new SnapshotFile(Path.GetRelativePath(rootPath, file).Replace('\\', '/'), File.ReadAllBytes(file)))
                    .ToArray();
    }

    private static string GetGlobalUsings(SnapshotTestFramework testFramework)
    {
        var globalUsings = new List<string>()
        {
            "global using System;",
            "global using System.Collections.Generic;",
            "global using System.IO;",
            "global using System.Runtime.CompilerServices;",
            "global using System.Text;",
            "global using Meziantou.Framework.SnapshotTesting;",
        };

        var frameworkGlobalUsing = testFramework switch
        {
            SnapshotTestFramework.Xunit or SnapshotTestFramework.XunitV3 => "global using Xunit;",
            SnapshotTestFramework.MSTest => "global using Microsoft.VisualStudio.TestTools.UnitTesting;",
            SnapshotTestFramework.NUnit => "global using NUnit.Framework;",
            SnapshotTestFramework.TUnit => "global using TUnit.Core;",
            _ => null,
        };

        if (frameworkGlobalUsing is not null)
        {
            globalUsings.Add(frameworkGlobalUsing);
        }

        return string.Join(Environment.NewLine, globalUsings);
    }

    private static string GetTargetFramework()
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
#else
        throw new NotSupportedException("Unsupported target framework");
#endif
    }

    private static string GetSnapshotTestUtilitiesSource()
    {
        return
            """
            public static class SnapshotTestUtilities
            {
                public static SnapshotSettings CreateSuccessSettings()
                {
                    return new SnapshotSettings
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
                    };
                }

                public static SnapshotSettings CreateFailureSettings()
                {
                    return new SnapshotSettings
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
                    };
                }
            }
            """;
    }

    private static string GetAdditionalProjectProperties(SnapshotTestFramework testFramework)
    {
        return testFramework switch
        {
            SnapshotTestFramework.TUnit => "<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>",
            _ => "",
        };
    }

    private static string GetPackageReferences(SnapshotTestFramework testFramework)
    {
        string[] references = testFramework switch
        {
            SnapshotTestFramework.Xunit =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="xunit" Version="2.9.3" />""",
                """<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
            ],
            SnapshotTestFramework.XunitV3 =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="xunit.v3" Version="3.2.2" />""",
                """<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
            ],
            SnapshotTestFramework.MSTest =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="MSTest.TestFramework" Version="4.2.1" />""",
                """<PackageReference Include="MSTest.TestAdapter" Version="4.2.1" />""",
            ],
            SnapshotTestFramework.NUnit =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="NUnit" Version="4.5.1" />""",
                """<PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />""",
            ],
            SnapshotTestFramework.TUnit =>
            [
                """<PackageReference Include="TUnit" Version="1.35.2" />""",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(testFramework), testFramework, null),
        };

        return string.Join(Environment.NewLine, references);
    }

    private static async Task<DotNetExecutionResult> ExecuteDotNet(FullPath workingDirectory, string dotnetPath, IReadOnlyList<string> arguments, int? expectedExitCode = null)
    {
        var process = ProcessWrapper.Create(dotnetPath)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(ProcessValidationMode.None)
            .WithEnvironmentVariables(env =>
            {
                env.Remove("CI");
                foreach (var entry in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
                {
                    var key = (string)entry.Key;
                    if (key == "GITHUB_WORKSPACE")
                        continue;

                    if (key.StartsWith("GITHUB", StringComparison.Ordinal))
                    {
                        env.Remove(key);
                    }
                }

                env.Set("DiffEngine_Disabled", "true");
            })
            .ExecuteBufferedAsync();

        var result = await process;
        var output = new StringBuilder();
        foreach (var line in result.Output)
        {
            output.AppendLine(line.Text);
        }

        if (expectedExitCode.HasValue)
        {
            Assert.True(
                expectedExitCode.Value == result.ExitCode,
                $"dotnet {string.Join(' ', arguments)} returned exit code {result.ExitCode} but {expectedExitCode.Value} was expected.{Environment.NewLine}{output}");
        }

        return new DotNetExecutionResult(result.ExitCode, output.ToString());
    }

    private readonly record struct DotNetExecutionResult(int ExitCode, string Output);

    private sealed record SnapshotFile(string RelativePath, byte[] Content)
    {
        public string ContentAsString => Encoding.UTF8.GetString(Content);
    }
}
