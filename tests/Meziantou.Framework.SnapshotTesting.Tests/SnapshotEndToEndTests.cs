using System.Collections;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json;
using TestUtilities;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed partial class SnapshotEndToEndTests
{
    private const string InlineSnapshotTestingLibraryName = "Meziantou.Framework.InlineSnapshotTesting";

    public enum SnapshotTestFramework
    {
        Xunit,
        XunitV3,
        MSTest,
        NUnit,
        TUnit,
    }

    [Fact]
    public async Task Validate_EndToEnd_EmbedsGeneratedSourceRootFileInBinlog()
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
            """,
            assertGeneratedSourceRootFile: true);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_EmbedsGeneratedSourceRootFileInBinlog_WhenImportingBuildTransitiveTargets()
    {
        // NuGet imports the 'buildTransitive' targets for both direct and transitive package references,
        // so everything the 'build' targets provide must also be available there
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
            """,
            assertGeneratedSourceRootFile: true,
            importBuildTransitiveTargets: true);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_RegistersSourceRootForBothPackages_WhenInlineSnapshotTestingIsReferenced()
    {
        // The project directory is registered as a source root, so the compiler maps the test file path to a '/_N/' path.
        // Each package compiles its own copy of the mapping table, so both packages must register the mapping.
        var snapshotFiles = await AssertSnapshot(
            """
            using System.Collections.Concurrent;
            using System.Linq;
            using System.Reflection;

            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    AssertSourceFilePathIsMapped(typeof(Snapshot).Assembly);
                    AssertSourceFilePathIsMapped(typeof(Meziantou.Framework.InlineSnapshotTesting.InlineSnapshot).Assembly);
                    Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                }

                private static void AssertSourceFilePathIsMapped(Assembly assembly, [CallerFilePath] string filePath = null)
                {
                    Assert.False(File.Exists(filePath), $"The path '{filePath}' is not mapped by the compiler.");

                    var type = assembly.GetType("Meziantou.Framework.SnapshotTesting.CallerContextUtilities", throwOnError: true);
                    var field = type.GetField("SourceRootMappings", BindingFlags.NonPublic | BindingFlags.Static);
                    var mappings = (ConcurrentDictionary<string, string>)field.GetValue(null);
                    Assert.True(
                        mappings.Any(mapping => filePath.StartsWith(mapping.Key, StringComparison.Ordinal) && File.Exists(Path.Combine(mapping.Value, filePath[mapping.Key.Length..]))),
                        $"'{filePath}' is not mapped by {assembly.GetName().Name}: {string.Join(", ", mappings)}");
                }
            }
            """,
            directoryBuildPropsContent: """
                <Project>
                  <ItemGroup>
                    <SourceRoot Include="$(MSBuildProjectDirectory)/" />
                  </ItemGroup>
                </Project>
                """,
            assertGeneratedSourceRootFile: true,
            additionalLibraries: [InlineSnapshotTestingLibraryName]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleFact.verified.txt", "fact-value"u8.ToArray()),
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"u8.ToArray()),
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "beta"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleFact.verified.txt", "fact-value"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "beta"),
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleFact.verified.txt", "fact-value"u8.ToArray()),
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"u8.ToArray()),
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "incorrect"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleFact.verified.txt", "fact-value"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.actual.txt", "beta"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "incorrect"),
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
            testFilter: "GeneratedSnapshotTests.SampleFact");

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleFact.verified.txt", "fact-value"),
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

            // Each instance of the class gets its own snapshot.
            [Arguments("en-US")]
            [Arguments("fr-FR")]
            public sealed class ClassArgumentTests
            {
                private readonly string _culture;

                public ClassArgumentTests(string culture) => _culture = culture;

                [Test]
                public async Task Format()
                {
                    Snapshot.Validate(_culture, SnapshotTestUtilities.CreateSuccessSettings());
                    await Task.CompletedTask;
                }
            }
            """,
            testFramework: SnapshotTestFramework.TUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/ClassArgumentTests_Format_en-US.verified.txt", "en-US"),
            ("__snapshots__/ClassArgumentTests_Format_fr-FR.verified.txt", "fr-FR"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "beta"),
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

            // Each instance of a parameterized fixture gets its own snapshot.
            [TestFixture("en-US")]
            [TestFixture("fr-FR")]
            public sealed class FixtureTests
            {
                private readonly string _culture;

                public FixtureTests(string culture) => _culture = culture;

                [Test]
                public void Format()
                {
                    Snapshot.Validate(_culture, SnapshotTestUtilities.CreateSuccessSettings());
                }

                [TestCase(1)]
                public void Theory(int value)
                {
                    Snapshot.Validate(_culture + value, SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFramework: SnapshotTestFramework.NUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/FixtureTests_Format_en-US.verified.txt", "en-US"),
            ("__snapshots__/FixtureTests_Format_fr-FR.verified.txt", "fr-FR"),
            ("__snapshots__/FixtureTests_Theory_1_en-US.verified.txt", "en-US1"),
            ("__snapshots__/FixtureTests_Theory_1_fr-FR.verified.txt", "fr-FR1"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "beta"),
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
            ("__snapshots__/GeneratedSnapshotTests_Case_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_Case_beta.verified.txt", "beta"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_NamesDecimalArgumentsAfterTheMethod_WhenUsingXunitV3Context()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Theory]
                [InlineData(1.5)]
                [InlineData(2.5)]
                public void SampleTheory(double value)
                {
                    Snapshot.Validate(value.ToString(System.Globalization.CultureInfo.InvariantCulture), SnapshotTestUtilities.CreateSuccessSettings());
                }

                [Theory]
                [InlineData("alpha")]
                public void StringTheory(string value)
                {
                    Snapshot.Validate(value, SnapshotTestUtilities.CreateSuccessSettings());
                }

                [Fact]
                public void TwoCalls()
                {
                    Snapshot.Validate("first", SnapshotTestUtilities.CreateSuccessSettings());
                    Snapshot.Validate("second", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_1.5.verified.txt", "1.5"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_2.5.verified.txt", "2.5"),
            ("__snapshots__/GeneratedSnapshotTests_StringTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_TwoCalls.verified.txt", "first"),
            ("__snapshots__/GeneratedSnapshotTests_TwoCalls~2.verified.txt", "second"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_UsesTheSnapshotNamedByAnEarlierVersion_WhenUsingXunitV3Context()
    {
        // Earlier versions named '[InlineData(1.5)]' after the text following the last '.' of the display name.
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Theory]
                [InlineData(1.5)]
                public void SampleTheory(double value)
                {
                    Snapshot.Validate(value.ToString(System.Globalization.CultureInfo.InvariantCulture), SnapshotTestUtilities.CreateFailureSettings());
                }
            }
            """,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_5__1.5_31bc2a9f.verified.txt", "1.5"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_5__1.5_31bc2a9f.verified.txt", "1.5"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Fails_WhenTwoTestsShareADisplayName()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact(DisplayName = "Works")]
                public void First()
                {
                    Snapshot.Validate("value", SnapshotTestUtilities.CreateSuccessSettings());
                }

                [Fact(DisplayName = "Works")]
                public void Second()
                {
                    Snapshot.Validate("value", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            expectFailure: true);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_Works.verified.txt", "value"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_Fails_WhenArgumentsOnlyDifferByType()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Theory]
                [InlineData(1)]
                [InlineData(1L)]
                public void SampleTheory(object value)
                {
                    Snapshot.Validate("value", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            expectFailure: true);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_1.verified.txt", "value"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_CreatesDistinctSnapshotsForArgumentsContainingADot_WhenUsingNUnitContext()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            [TestFixture]
            public sealed class GeneratedSnapshotTests
            {
                [TestCase(1.5)]
                [TestCase(2.5)]
                public void SampleTheory(double value)
                {
                    Snapshot.Validate(value.ToString(System.Globalization.CultureInfo.InvariantCulture), SnapshotTestUtilities.CreateSuccessSettings());
                }

                [TestCase(1, TestName = "Case 1.0")]
                [TestCase(2, TestName = "Case 2.0")]
                public void Named(int value)
                {
                    Snapshot.Validate(value.ToString(System.Globalization.CultureInfo.InvariantCulture), SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFramework: SnapshotTestFramework.NUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_Case_1.0_dd400af3.verified.txt", "1"),
            ("__snapshots__/GeneratedSnapshotTests_Case_2.0_77a8171f.verified.txt", "2"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_1.5.verified.txt", "1.5"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_2.5.verified.txt", "2.5"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Theory_CreatesDistinctSnapshotsForArrayArguments_WhenUsingTUnitContext()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Test]
                [Arguments(new[] { 1, 2 })]
                [Arguments(new[] { 3 })]
                public void SampleTheory(int[] value)
                {
                    Snapshot.Validate(string.Join(",", value), SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            testFramework: SnapshotTestFramework.TUnit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_1_2_29248347.verified.txt", "1,2"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_3_0de94de7.verified.txt", "3"),
        ]);
    }

    [Theory]
    [InlineData(SnapshotTestFramework.Xunit)]
    [InlineData(SnapshotTestFramework.XunitV3)]
    [InlineData(SnapshotTestFramework.MSTest)]
    [InlineData(SnapshotTestFramework.NUnit)]
    [InlineData(SnapshotTestFramework.TUnit)]
    public async Task Validate_EndToEnd_NamesSnapshotsAfterTheRunningTest_AcrossFrameworks(SnapshotTestFramework testFramework)
    {
        // Each class of the generated project covers one case, so a single build and test run per framework covers them all:
        // - GeneratedSnapshotTests: a test calling Validate directly
        // - FirstSnapshotTests and SecondSnapshotTests: two classes sharing a method name
        // - AsyncHelperSnapshotTests: a helper that awaits before asserting, so the test method is no longer on the call stack and
        //   only the test framework context can tell which test is running (not supported by xunit v2 and MSTest)
        var snapshotFiles = await AssertSnapshot(GetNamingAcrossFrameworksSource(testFramework), testFramework);

        List<(string RelativePath, string Content)> expected =
        [
            ("__snapshots__/FirstSnapshotTests_SampleTest.verified.txt", "first"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
            ("__snapshots__/SecondSnapshotTests_SampleTest.verified.txt", "second"),
        ];

        if (SupportsAsyncHelper(testFramework))
        {
            expected.Insert(0, ("__snapshots__/AsyncHelperSnapshotTests_SampleTest.verified.txt", "async-helper"));
        }

        AssertSnapshotContent(snapshotFiles, [.. expected]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validate_EndToEnd_Works_WhenUsingArtifactsOutput(bool deterministic)
    {
        // ExistingSnapshotTests compares with a snapshot that already exists, NewSnapshotTests creates its snapshot
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class ExistingSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("existing", SnapshotTestUtilities.CreateFailureSettings());
                }
            }

            public sealed class NewSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("new", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/ExistingSnapshotTests_SampleTest.verified.txt", "existing"u8.ToArray()),
            ],
            directoryBuildPropsContent:
            $"""
            <Project>
              <PropertyGroup>
                <UseArtifactsOutput>true</UseArtifactsOutput>
                <Deterministic>{(deterministic ? "true" : "false")}</Deterministic>
              </PropertyGroup>
            </Project>
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/ExistingSnapshotTests_SampleTest.verified.txt", "existing"),
            ("__snapshots__/NewSnapshotTests_SampleTest.verified.txt", "new"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesContainingMethodName_WhenCalledFromLambda()
    {
        // xunit v2 does not expose the running test, so the names come from the call stack
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class LambdaSnapshotTests
            {
                [Fact]
                public async Task SampleTest()
                {
                    await Task.Run(() => Snapshot.Validate("lambda", SnapshotTestUtilities.CreateSuccessSettings()));
                }
            }

            public sealed class AsyncLambdaSnapshotTests
            {
                [Fact]
                public async Task SampleTest()
                {
                    await Task.Run(async () =>
                    {
                        Snapshot.Validate("async-lambda", SnapshotTestUtilities.CreateSuccessSettings());
                        await Task.CompletedTask;
                    });
                }
            }
            """,
            testFramework: SnapshotTestFramework.Xunit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/AsyncLambdaSnapshotTests_SampleTest.verified.txt", "async-lambda"),
            ("__snapshots__/LambdaSnapshotTests_SampleTest.verified.txt", "lambda"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_ExcludesCSharpSnapshotFilesFromCompilation()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("class GeneratedSnapshotTests { }", SnapshotType.Create("cs"), SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """,
            existingFiles:
            [
                // Compiling those files would report CS0101 as GeneratedSnapshotTests is already defined
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.cs", "class GeneratedSnapshotTests { }"u8.ToArray()),
                new SnapshotFile("Nested/__snapshots__/NestedSnapshot.verified.cs", "class GeneratedSnapshotTests { }"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.cs", "class GeneratedSnapshotTests { }"),
        ]);
    }

    [Fact]
    public async Task Build_EndToEnd_ExcludesVisualBasicSnapshotFilesFromCompilation()
    {
        await using var directory = TemporaryDirectory.Create();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        var snapshotTargetsPath = GetRepositoryRoot() / "src" / "Meziantou.Framework.SnapshotTesting" / "build" / "Meziantou.Framework.SnapshotTesting.targets";
        var inlineSnapshotTargetsPath = GetRepositoryRoot() / "src" / InlineSnapshotTestingLibraryName / "build" / "Meziantou.Framework.InlineSnapshotTesting.targets";

        // The source root files are C# files, so they must not be generated for a Visual Basic project (BC30035)
        File.WriteAllText(directory.GetFullPath("Project.vbproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{snapshotTargetsPath}" />
              <Import Project="{inlineSnapshotTargetsPath}" />
              <PropertyGroup>
                <TargetFramework>{TargetFrameworkHelper.GetTargetFrameworkMoniker()}</TargetFramework>
                <IsPackable>false</IsPackable>
                <DeterministicSourcePaths>true</DeterministicSourcePaths>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(directory.GetFullPath("Sample.vb"), """
            Public Class Sample
            End Class
            """);

        // Compiling this file would report BC30203 as it is not valid Visual Basic
        var snapshotPath = directory.GetFullPath("Nested/__snapshots__/Sample_SampleTest.verified.vb");
        snapshotPath.CreateParentDirectory();
        File.WriteAllText(snapshotPath, "-- not valid Visual Basic --");

        await ExecuteDotNet(directory.FullPath, dotnetPath, ["build", "--disable-build-servers"], expectedExitCode: 0);
    }

    [Fact]
    public async Task Build_EndToEnd_EscapesSourceRootPath_WhenPathContainsQuotes()
    {
        await using var directory = TemporaryDirectory.Create();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        // Only the generation targets are run: the SDK itself cannot compute the compiler path map when a source root
        // contains an apostrophe (CS8101), and the compiler rejects file paths containing a double quote.
        var projectDirectoryName = OperatingSystem.IsWindows() ? "o'brien" : "o'brien \"quoted\"";
        var projectDirectory = directory.CreateDirectory(projectDirectoryName);
        var snapshotTargetsPath = GetRepositoryRoot() / "src" / "Meziantou.Framework.SnapshotTesting" / "build" / "Meziantou.Framework.SnapshotTesting.targets";
        var inlineSnapshotTargetsPath = GetRepositoryRoot() / "src" / InlineSnapshotTestingLibraryName / "build" / "Meziantou.Framework.InlineSnapshotTesting.targets";
        File.WriteAllText(projectDirectory / "Project.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{snapshotTargetsPath}" />
              <Import Project="{inlineSnapshotTargetsPath}" />
              <PropertyGroup>
                <TargetFramework>{TargetFrameworkHelper.GetTargetFrameworkMoniker()}</TargetFramework>
                <IsPackable>false</IsPackable>
                <DeterministicSourcePaths>true</DeterministicSourcePaths>
              </PropertyGroup>
              <ItemGroup>
                <SourceRoot Include="$(MSBuildProjectDirectory)/" />
              </ItemGroup>
            </Project>
            """);

        await ExecuteDotNet(projectDirectory, dotnetPath, ["msbuild", "-restore", "-nologo", "-t:GenerateSnapshotTestingSourceRoots;GenerateInlineSnapshotTestingSourceRoots"], expectedExitCode: 0);

        var expectedLiteralEnd = "/" + projectDirectoryName.Replace("\"", "\"\"", StringComparison.Ordinal) + "/\");";
        foreach (var fileName in new[] { "SnapshotTestingSourceRoot.g.cs", "InlineSnapshotTestingSourceRoot.g.cs" })
        {
            var files = Directory.GetFiles(projectDirectory / "obj", fileName, SearchOption.AllDirectories);
            var file = Assert.Single(files);
            Assert.Contains(expectedLiteralEnd, File.ReadAllText(file), message: $"'{fileName}' does not contain the escaped source root path.");
        }
    }

    private static bool SupportsAsyncHelper(SnapshotTestFramework framework) => framework is SnapshotTestFramework.XunitV3 or SnapshotTestFramework.NUnit or SnapshotTestFramework.TUnit;

    private static string GetNamingAcrossFrameworksSource(SnapshotTestFramework framework)
    {
        var source = framework switch
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

                public sealed class FirstSnapshotTests
                {
                    [Fact]
                    public void SampleTest()
                    {
                        Snapshot.Validate("first", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }

                public sealed class SecondSnapshotTests
                {
                    [Fact]
                    public void SampleTest()
                    {
                        Snapshot.Validate("second", SnapshotTestUtilities.CreateSuccessSettings());
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

                [TestClass]
                public sealed class FirstSnapshotTests
                {
                    [TestMethod]
                    public void SampleTest()
                    {
                        Snapshot.Validate("first", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }

                [TestClass]
                public sealed class SecondSnapshotTests
                {
                    [TestMethod]
                    public void SampleTest()
                    {
                        Snapshot.Validate("second", SnapshotTestUtilities.CreateSuccessSettings());
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

                [TestFixture]
                public sealed class FirstSnapshotTests
                {
                    [Test]
                    public void SampleTest()
                    {
                        Snapshot.Validate("first", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }

                [TestFixture]
                public sealed class SecondSnapshotTests
                {
                    [Test]
                    public void SampleTest()
                    {
                        Snapshot.Validate("second", SnapshotTestUtilities.CreateSuccessSettings());
                    }
                }

                [TestFixture]
                public sealed class AsyncHelperSnapshotTests
                {
                    [Test]
                    public async Task SampleTest()
                    {
                        await SnapshotHelpers.ValidateAsync("async-helper");
                    }
                }
                """,
            SnapshotTestFramework.TUnit =>
                """
                public sealed class GeneratedSnapshotTests
                {
                    [Test]
                    public async Task SampleTest()
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                        await Task.CompletedTask;
                    }
                }

                public sealed class FirstSnapshotTests
                {
                    [Test]
                    public async Task SampleTest()
                    {
                        Snapshot.Validate("first", SnapshotTestUtilities.CreateSuccessSettings());
                        await Task.CompletedTask;
                    }
                }

                public sealed class SecondSnapshotTests
                {
                    [Test]
                    public async Task SampleTest()
                    {
                        Snapshot.Validate("second", SnapshotTestUtilities.CreateSuccessSettings());
                        await Task.CompletedTask;
                    }
                }

                public sealed class AsyncHelperSnapshotTests
                {
                    [Test]
                    public async Task SampleTest()
                    {
                        await SnapshotHelpers.ValidateAsync("async-helper");
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null),
        };

        if (framework is SnapshotTestFramework.XunitV3)
        {
            source += Environment.NewLine + Environment.NewLine +
                """
                public sealed class AsyncHelperSnapshotTests
                {
                    [Fact]
                    public async Task SampleTest()
                    {
                        await SnapshotHelpers.ValidateAsync("async-helper");
                    }
                }
                """;
        }

        if (SupportsAsyncHelper(framework))
        {
            source += Environment.NewLine + Environment.NewLine +
                """
                public static class SnapshotHelpers
                {
                    public static async Task ValidateAsync(object value, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    {
                        await Task.Yield();
                        Snapshot.Validate(value, null, SnapshotTestUtilities.CreateSuccessSettings(), filePath, lineNumber);
                    }
                }
                """;
        }

        return source;
    }

    private static void AssertSnapshotContent(SnapshotFile[] snapshotFiles, (string RelativePath, string Content)[] expected)
    {
        Assert.Equal(expected, snapshotFiles.Select(f => (f.RelativePath, f.ContentAsString)));
    }

    /// <summary>
    /// Writes a test project containing <paramref name="source"/>, builds it, runs its tests with <paramref name="testFramework"/>,
    /// and returns the files of its <c>__snapshots__</c> folder ordered by relative path.
    /// </summary>
    /// <remarks>
    /// The generated project references the Meziantou.Framework.SnapshotTesting assembly this test project was built with, and
    /// the assemblies it depends on, instead of the project itself. So no generated project builds the repository's projects,
    /// which is slow and made the test processes of each target framework race on the same build outputs.
    /// <paramref name="additionalLibraries"/> adds other libraries this test project references, with their dependencies.
    /// </remarks>
    private static async Task<SnapshotFile[]> AssertSnapshot(
        [StringSyntax("c#-test")] string source,
        SnapshotTestFramework testFramework = SnapshotTestFramework.XunitV3,
        bool expectFailure = false,
        IReadOnlyList<SnapshotFile>? existingFiles = null,
        string? testFilter = null,
        string? directoryBuildPropsContent = null,
        bool assertGeneratedSourceRootFile = false,
        bool importBuildTransitiveTargets = false,
        IReadOnlyList<string>? additionalLibraries = null)
    {
        await using var directory = TemporaryDirectory.Create();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        var targetsFolder = importBuildTransitiveTargets ? "buildTransitive" : "build";
        var snapshotTargetsPath = GetRepositoryRoot() / "src" / "Meziantou.Framework.SnapshotTesting" / targetsFolder / "Meziantou.Framework.SnapshotTesting.targets";

        // Like its package, referencing InlineSnapshotTesting imports its targets
        var referenceInlineSnapshotTesting = additionalLibraries?.Contains(InlineSnapshotTestingLibraryName, StringComparer.Ordinal) is true;
        var inlineSnapshotTargetsPath = GetRepositoryRoot() / "src" / InlineSnapshotTestingLibraryName / targetsFolder / "Meziantou.Framework.InlineSnapshotTesting.targets";
        CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{snapshotTargetsPath}}" />
              {{(referenceInlineSnapshotTesting ? $"<Import Project=\"{inlineSnapshotTargetsPath}\" />" : "")}}
              <PropertyGroup>
                <TargetFramework>{{TargetFrameworkHelper.GetTargetFrameworkMoniker()}}</TargetFramework>
                <Nullable>disable</Nullable>
                <IsPackable>false</IsPackable>
                {{(assertGeneratedSourceRootFile ? "<DeterministicSourcePaths>true</DeterministicSourcePaths>" : "")}}
                {{GetAdditionalProjectProperties(testFramework)}}
              </PropertyGroup>
              <ItemGroup>
                {{GetPackageReferences(testFramework)}}
              </ItemGroup>
              <ItemGroup>
                {{GetLibraryReferences(["Meziantou.Framework.SnapshotTesting", .. additionalLibraries ?? []])}}
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

        if (!string.IsNullOrEmpty(directoryBuildPropsContent))
        {
            CreateTextFile("Directory.Build.props", directoryBuildPropsContent);
        }

        // Restoring downloads the packages of the test framework, so it is the only step that can fail for a transient reason
        await ExecuteDotNetWithRetry(directory.FullPath, dotnetPath, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var buildArguments = new List<string>
        {
            "build",
            "--no-restore",
            "--disable-build-servers",
        };

        var binlogPath = directory.GetFullPath("build.binlog");
        if (assertGeneratedSourceRootFile)
        {
            buildArguments.Add("/bl:" + binlogPath);
        }

        await ExecuteDotNet(directory.FullPath, dotnetPath, buildArguments, expectedExitCode: 0);
        if (assertGeneratedSourceRootFile)
        {
            AssertGeneratedSourceRootFileExists(directory.FullPath, "SnapshotTestingSourceRoot.g.cs");
            AssertBinlogContains(binlogPath, "SnapshotTestingSourceRoot.g.cs");
            if (referenceInlineSnapshotTesting)
            {
                AssertGeneratedSourceRootFileExists(directory.FullPath, "InlineSnapshotTestingSourceRoot.g.cs");
                AssertBinlogContains(binlogPath, "InlineSnapshotTestingSourceRoot.g.cs");
            }
        }

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

    private static string GetLibraryReferences(IEnumerable<string> libraryNames)
    {
        var references = new List<string>();
        foreach (var assemblyPath in GetLibraryAssemblyPaths(libraryNames))
        {
            references.Add($"""<Reference Include="{SecurityElement.Escape(assemblyPath)}" />""");
        }

        return string.Join(Environment.NewLine, references);
    }

    // The assemblies of the libraries and of their dependencies, as copied next to this test assembly for the target framework it runs on
    private static List<string> GetLibraryAssemblyPaths(IEnumerable<string> libraryNames)
    {
        var baseDirectory = FullPath.FromPath(AppContext.BaseDirectory);
        var depsFilePath = baseDirectory / (typeof(SnapshotEndToEndTests).Assembly.GetName().Name + ".deps.json");
        using var document = JsonDocument.Parse(File.ReadAllText(depsFilePath));
        var runtimeTargetName = document.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString();
        Assert.NotNull(runtimeTargetName);

        // Each library is named "<name>/<version>"
        var libraries = document.RootElement.GetProperty("targets").GetProperty(runtimeTargetName)
            .EnumerateObject()
            .ToDictionary(library => library.Name[..library.Name.IndexOf('/', StringComparison.Ordinal)], library => library.Value, StringComparer.OrdinalIgnoreCase);

        var assemblyPaths = new List<string>();
        var visitedLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingLibraries = new Stack<string>();
        foreach (var libraryName in libraryNames)
        {
            Assert.Contains(libraryName, libraries, message: $"'{libraryName}' is not referenced by the test project, see '{depsFilePath}'.");
            pendingLibraries.Push(libraryName);
        }

        while (pendingLibraries.TryPop(out var libraryName))
        {
            if (!visitedLibraries.Add(libraryName) || !libraries.TryGetValue(libraryName, out var library))
                continue;

            if (library.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    pendingLibraries.Push(dependency.Name);
                }
            }

            if (library.TryGetProperty("runtime", out var runtimeAssets))
            {
                foreach (var runtimeAsset in runtimeAssets.EnumerateObject())
                {
                    var fileName = runtimeAsset.Value.TryGetProperty("localPath", out var localPath) ? localPath.GetString() : Path.GetFileName(runtimeAsset.Name);
                    Assert.NotNull(fileName);

                    var assemblyPath = baseDirectory / fileName;
                    Assert.True(File.Exists(assemblyPath), $"The assembly '{assemblyPath}' of '{libraryName}' does not exist.");
                    assemblyPaths.Add(assemblyPath);
                }
            }
        }

        return assemblyPaths;
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
            if (testFramework == SnapshotTestFramework.XunitV3)
            {
                // xunit.v3 uses Microsoft.Testing.Platform; VSTest-style --filter is ignored.
                // Use xunit's MTP extension option --filter-method passed after the -- separator.
                arguments.Add("--");
                arguments.Add("--filter-method");
                arguments.Add(testFilter);
            }
            else
            {
                arguments.Add("--filter");
                arguments.Add(testFilter);
            }
        }

        return [.. arguments];
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var resolvedSourceFilePath = SnapshotCallerContext.ResolveSourceFilePath(filePath);

        return resolvedSourceFilePath.Parent.Parent.Parent;
    }

    private static SnapshotFile[] GetGeneratedSnapshotFiles(FullPath rootPath)
    {
        var snapshotDirectory = Path.Combine(rootPath, "__snapshots__");
        if (!Directory.Exists(snapshotDirectory))
            return [];

        return Directory.GetFiles(snapshotDirectory, "*", SearchOption.AllDirectories)
            .Select(path => (AbsolutePath: path, RelativePath: Path.GetRelativePath(rootPath, path).Replace('\\', '/')))
            .OrderBy(path => path.RelativePath, StringComparer.Ordinal)
            .Select(file => new SnapshotFile(file.RelativePath, File.ReadAllBytes(file.AbsolutePath)))
            .ToArray();
    }

    private static void AssertGeneratedSourceRootFileExists(FullPath rootPath, string fileName)
    {
        var intermediateDirectory = Path.Combine(rootPath, "obj");
        var files = Directory.Exists(intermediateDirectory)
            ? Directory.GetFiles(intermediateDirectory, fileName, SearchOption.AllDirectories)
            : [];

        Assert.NotEmpty(files, $"No source root file was generated in '{intermediateDirectory}'.");
    }

    private static void AssertBinlogContains(FullPath binlogPath, string value)
    {
        using var fileStream = File.OpenRead(binlogPath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var memoryStream = new MemoryStream();
        gzipStream.CopyTo(memoryStream);

        var bytes = memoryStream.ToArray();
        Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value)) >= 0, $"The binlog does not contain '{value}'.");
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
            "global using System.Threading.Tasks;",
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
                        SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
                    };
                }

                public static SnapshotSettings CreateFailureSettings()
                {
                    return new SnapshotSettings
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
                        SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
                    };
                }
            }
            """;
    }

    private static string GetAdditionalProjectProperties(SnapshotTestFramework testFramework)
    {
        return testFramework switch
        {
            SnapshotTestFramework.TUnit or SnapshotTestFramework.XunitV3 => "<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>",
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

    private static async Task ExecuteDotNetWithRetry(FullPath workingDirectory, string dotnetPath, IReadOnlyList<string> arguments, int expectedExitCode, int retryCount = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);

        var maxAttempts = retryCount + 1;
        var output = new StringBuilder();
        var result = default(DotNetExecutionResult);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = await ExecuteDotNet(workingDirectory, dotnetPath, arguments);
            if (result.ExitCode == expectedExitCode)
            {
                return;
            }

            output.AppendLine(CultureInfo.InvariantCulture, $"Attempt {attempt} of {maxAttempts} returned exit code {result.ExitCode}.");
            output.AppendLine(result.Output);

            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), XunitCancellationToken);
            }
        }

        Assert.Equal(expectedExitCode, result.ExitCode, message: $"dotnet {string.Join(' ', arguments)} returned exit code {result.ExitCode} after {maxAttempts} attempts but {expectedExitCode} was expected.{Environment.NewLine}{output}");
    }

    private static async Task<DotNetExecutionResult> ExecuteDotNet(FullPath workingDirectory, string dotnetPath, IReadOnlyList<string> arguments, int? expectedExitCode = null)
    {
        var process = ProcessWrapper.Create(dotnetPath)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(ProcessValidationMode.None)
            .WithInputStream(InputSource.FromStream(Stream.Null))
            .WithEnvironmentVariables(env =>
            {
                env.Remove("CI");
                foreach (var entry in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
                {
                    var key = (string)entry.Key;
                    if (key.StartsWith("GITHUB_", StringComparison.Ordinal))
                    {
                        env.Remove(key);
                    }
                }

                env.Set("DiffEngine_Disabled", "true");
                env.Set("MSBUILDDISABLENODEREUSE", "1");
                env.Set("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
                env.Set("TUNIT_DISABLE_LOGO", "true");
                env.Set("TUNIT_DISABLE_HTML_REPORTER", "true");
                env.Set("TUNIT_DISABLE_GITHUB_REPORTER", "true");
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
