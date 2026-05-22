using System.Collections;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TestUtilities;

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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "expected"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.actual.txt", "sample"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "expected"),
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
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "value_0"u8.ToArray()),
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest_1.verified.txt", "value_1"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "value_0"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTest_1.verified.txt", "value_1"),
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest_0.verified.txt", "value_0"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest_0.verified.txt", "value_0"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTest_1.actual.txt", "value_1"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Succeeds_WhenMultipleExpectedSnapshotsMatch()
    {
        var snapshotFiles = await AssertSnapshot(
            CreateFixedCountSerializerSource(count: 2),
            existingFiles:
            [
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest_0.verified.txt", "value_0"u8.ToArray()),
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest_1.verified.txt", "value_1"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest_0.verified.txt", "value_0"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTest_1.verified.txt", "value_1"),
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
            testFilter: "FullyQualifiedName~GeneratedSnapshotTests.SampleFact");

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleFact.verified.txt", "fact-value"),
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
            ("__snapshots__/GeneratedSnapshotTests_Case_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_Case_beta.verified.txt", "beta"),
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
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_alpha.verified.txt", "alpha"),
            ("__snapshots__/GeneratedSnapshotTests_SampleTheory_beta.verified.txt", "beta"),
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
            """,
            testFramework: SnapshotTestFramework.NUnit);

        AssertSnapshotContent(snapshotFiles,
        [
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
        Assert.StartsWith("__snapshots__/GeneratedSnapshotTests_SampleTest", snapshotFile.RelativePath, StringComparison.Ordinal);
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
        Assert.Matches(new Regex("^__snapshots__/GeneratedSnapshotTests_snapshot\\.verified_[0-9a-f]{8}\\.verified\\.txt$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), snapshotFile.RelativePath);
        Assert.Equal("sample", snapshotFile.ContentAsString);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesTypedExtension_WhenValueIsByteArray_StringSnapshotTypeValue()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var payload = new byte[] { 0x42, 0x00, 0x43 };
                    Snapshot.Validate(payload, "png", SnapshotTestUtilities.CreateSuccessSettings());
                }
            }
            """);

        var snapshotFile = Assert.Single(snapshotFiles);
        Assert.Equal("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.png", snapshotFile.RelativePath);
        Assert.Equal([0x42, 0x00, 0x43], snapshotFile.Content);
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
        Assert.Equal("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.png", snapshotFile.RelativePath);
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
        Assert.Equal("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.png", snapshotFile.RelativePath);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], snapshotFile.Content);
    }

    [Fact]
    public async Task Validate_EndToEnd_SerializesGifFramesAsPng_WhenGifSerializerIsEnabled()
    {
        var payload = CreateTwoFrameGif();
        var sourcePayload = ToByteArraySource(payload);
        var snapshotFiles = await AssertSnapshot(
            $$"""
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var payload = new byte[] { {{sourcePayload}} };
                    var settings = SnapshotTestUtilities.CreateSuccessSettings();
                    settings.Serializers.AddGifSerializer();
                    Snapshot.Validate(payload, SnapshotType.Gif, settings);
                }
            }
            """);

        Assert.Equal(
        [
            "__snapshots__/GeneratedSnapshotTests_SampleTest_0.verified.png",
            "__snapshots__/GeneratedSnapshotTests_SampleTest_1.verified.png",
        ], snapshotFiles.Select(static item => item.RelativePath));
        Assert.All(snapshotFiles, static snapshotFile => Assert.True(PngImageLoader.IsPng(snapshotFile.Content)));
        var firstFrame = Image.Load(snapshotFiles[0].Content);
        var secondFrame = Image.Load(snapshotFiles[1].Content);
        Assert.Equal(firstFrame, secondFrame);
    }

    [Fact]
    public async Task Validate_EndToEnd_SerializesIcoImagesAsPng_WhenIcoSerializerIsEnabled()
    {
        var payload = CreateTwoImageIco();
        var sourcePayload = ToByteArraySource(payload);
        var snapshotFiles = await AssertSnapshot(
            $$"""
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var payload = new byte[] { {{sourcePayload}} };
                    var settings = SnapshotTestUtilities.CreateSuccessSettings();
                    settings.Serializers.AddIcoSerializer();
                    Snapshot.Validate(payload, SnapshotType.Ico, settings);
                }
            }
            """);

        Assert.Equal(
        [
            "__snapshots__/GeneratedSnapshotTests_SampleTest_0.verified.png",
            "__snapshots__/GeneratedSnapshotTests_SampleTest_1.verified.png",
        ], snapshotFiles.Select(static item => item.RelativePath));
        Assert.All(snapshotFiles, static snapshotFile => Assert.True(PngImageLoader.IsPng(snapshotFile.Content)));
        var firstFrame = Image.Load(snapshotFiles[0].Content);
        var secondFrame = Image.Load(snapshotFiles[1].Content);
        Assert.NotEqual(firstFrame, secondFrame);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesBmpPixelComparer_WhenOnlyMetadataDiffers()
    {
        var verifiedPayload = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 2835);
        var actualPayload = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 3780);
        var sourcePayload = ToByteArraySource(actualPayload);

        var snapshotFiles = await AssertSnapshot(
            $$"""
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    var payload = new byte[] { {{sourcePayload}} };
                    var settings = SnapshotTestUtilities.CreateFailureSettings();
                    settings.Comparers.AddImageComparer();
                    Snapshot.Validate(payload, SnapshotType.Bmp, settings);
                }
            }
            """,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.bmp", verifiedPayload),
            ]);

        var snapshotFile = Assert.Single(snapshotFiles);
        Assert.Equal("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.bmp", snapshotFile.RelativePath);
        Assert.Equal(verifiedPayload, snapshotFile.Content);
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
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Theory]
    [InlineData(SnapshotTestFramework.Xunit)]
    [InlineData(SnapshotTestFramework.XunitV3)]
    [InlineData(SnapshotTestFramework.MSTest)]
    [InlineData(SnapshotTestFramework.NUnit)]
    [InlineData(SnapshotTestFramework.TUnit)]
    public async Task Validate_EndToEnd_UsesClassName_WhenTwoClassesShareMethodName(SnapshotTestFramework testFramework)
    {
        var snapshotFiles = await AssertSnapshot(GetDuplicateMethodAcrossClassesSource(testFramework), testFramework: testFramework);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/FirstSnapshotTests_SampleTest.verified.txt", "first"),
            ("__snapshots__/SecondSnapshotTests_SampleTest.verified.txt", "second"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Works_WhenUsingArtifactsOutput()
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"u8.ToArray()),
            ],
            directoryBuildPropsContent:
            """
            <Project>
              <PropertyGroup>
                <UseArtifactsOutput>true</UseArtifactsOutput>
              </PropertyGroup>
            </Project>
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Works_WhenUsingArtifactsOutput_WithoutInitialSnapshot()
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
            directoryBuildPropsContent:
            """
            <Project>
              <PropertyGroup>
                <UseArtifactsOutput>true</UseArtifactsOutput>
              </PropertyGroup>
            </Project>
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Works_WhenUsingArtifactsOutputAndNonDeterministicBuild()
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
                new SnapshotFile("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"u8.ToArray()),
            ],
            directoryBuildPropsContent:
            """
            <Project>
              <PropertyGroup>
                <UseArtifactsOutput>true</UseArtifactsOutput>
                <Deterministic>false</Deterministic>
              </PropertyGroup>
            </Project>
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_Works_WhenUsingArtifactsOutputAndNonDeterministicBuild_WithoutInitialSnapshot()
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
            directoryBuildPropsContent:
            """
            <Project>
              <PropertyGroup>
                <UseArtifactsOutput>true</UseArtifactsOutput>
                <Deterministic>false</Deterministic>
              </PropertyGroup>
            </Project>
            """);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesContainingMethodName_WhenCalledFromLambda()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            using System.Threading.Tasks;

            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public async Task SampleTest()
                {
                    await Task.Run(() => Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings()));
                }
            }
            """,
            testFramework: SnapshotTestFramework.Xunit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
        ]);
    }

    [Fact]
    public async Task Validate_EndToEnd_UsesContainingMethodName_WhenCalledFromAsyncLambda()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            using System.Threading.Tasks;

            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public async Task SampleTest()
                {
                    await Task.Run(async () =>
                    {
                        Snapshot.Validate("sample", SnapshotTestUtilities.CreateSuccessSettings());
                        await Task.CompletedTask;
                    });
                }
            }
            """,
            testFramework: SnapshotTestFramework.Xunit);

        AssertSnapshotContent(snapshotFiles,
        [
            ("__snapshots__/GeneratedSnapshotTests_SampleTest.verified.txt", "sample"),
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

    private static string GetDuplicateMethodAcrossClassesSource(SnapshotTestFramework framework)
    {
        return framework switch
        {
            SnapshotTestFramework.Xunit or SnapshotTestFramework.XunitV3 =>
                """
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
                """,
            SnapshotTestFramework.TUnit =>
                """
                using System.Threading.Tasks;

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
                        SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
                    };

                    settings.Serializers.Add(new FixedCountSerializer({{count}}));
                    Snapshot.Validate("sample", settings);
                }
            }

            file sealed class FixedCountSerializer(int count) : ISnapshotSerializer
            {
                public bool TrySerialize(SnapshotType type, object? value, out SerializedSnapshot? result)
                {
                    if (type != SnapshotType.Default)
                    {
                        result = null;
                        return false;
                    }

                    var data = new List<SnapshotData>(count);
                    for (var i = 0; i < count; i++)
                    {
                        data.Add(new SnapshotData("txt", Encoding.UTF8.GetBytes("value_" + i.ToString(CultureInfo.InvariantCulture))));
                    }

                    result = new SerializedSnapshot(data);
                    return true;
                }
            }
            """;
    }

    private static void AssertSnapshotContent(SnapshotFile[] snapshotFiles, (string RelativePath, string Content)[] expected)
    {
        Assert.Equal(expected, snapshotFiles.Select(f => (f.RelativePath, f.ContentAsString)));
    }

    private static string ToByteArraySource(byte[] data)
    {
        return string.Join(", ", data.Select(static item => "0x" + item.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static byte[] CreateBmp24(int width, int height, IReadOnlyList<uint> pixels, int pixelsPerMeter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Count != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        const int FileHeaderSize = 14;
        const int InfoHeaderSize = 40;
        var rowSizeWithoutPadding = checked(width * 3);
        var rowStride = (rowSizeWithoutPadding + 3) & ~3;
        var pixelDataSize = checked(rowStride * height);
        var data = new byte[FileHeaderSize + InfoHeaderSize + pixelDataSize];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        WriteUInt32LittleEndian(data, 2, (uint)data.Length);
        WriteUInt32LittleEndian(data, 10, FileHeaderSize + InfoHeaderSize);
        WriteUInt32LittleEndian(data, 14, InfoHeaderSize);
        WriteInt32LittleEndian(data, 18, width);
        WriteInt32LittleEndian(data, 22, height);
        WriteUInt16LittleEndian(data, 26, 1);
        WriteUInt16LittleEndian(data, 28, 24);
        WriteUInt32LittleEndian(data, 30, 0);
        WriteUInt32LittleEndian(data, 34, (uint)pixelDataSize);
        WriteInt32LittleEndian(data, 38, pixelsPerMeter);
        WriteInt32LittleEndian(data, 42, pixelsPerMeter);

        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            var sourceOffset = sourceRow * width;
            var destinationOffset = FileHeaderSize + InfoHeaderSize + y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[sourceOffset + x];
                data[destinationOffset + x * 3] = (byte)(pixel & 0xFF);
                data[destinationOffset + x * 3 + 1] = (byte)((pixel >> 8) & 0xFF);
                data[destinationOffset + x * 3 + 2] = (byte)((pixel >> 16) & 0xFF);
            }
        }

        return data;
    }

    private static byte[] CreatePngRgba32(int width, int height, IReadOnlyList<uint> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Count != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        var rowStride = checked(width * 4 + 1);
        var imageData = new byte[checked(rowStride * height)];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            imageData[rowOffset] = 0;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                var pixelOffset = rowOffset + 1 + x * 4;
                imageData[pixelOffset] = (byte)((pixel >> 16) & 0xFF);
                imageData[pixelOffset + 1] = (byte)((pixel >> 8) & 0xFF);
                imageData[pixelOffset + 2] = (byte)(pixel & 0xFF);
                imageData[pixelOffset + 3] = (byte)(pixel >> 24);
            }
        }

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(imageData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8;
        ihdrData[9] = 6;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(stream, "IHDR"u8, ihdrData);
        WritePngChunk(stream, "IDAT"u8, compressedData);
        WritePngChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(type);
        stream.Write(data);

        var crc = ComputePngCrc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdatePngCrc32(crc, type);
        crc = UpdatePngCrc32(crc, data);
        return ~crc;
    }

    private static uint UpdatePngCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
            }
        }

        return crc;
    }

    private static void WriteUInt32LittleEndian(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static void WriteInt32LittleEndian(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static void WriteUInt16LittleEndian(byte[] data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
    }

    private static byte[] CreateSingleFrameGif()
    {
        return
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00,
            0x80, 0x01, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x3B,
        ];
    }

    private static byte[] CreateTwoFrameGif()
    {
        return
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00,
            0x80, 0x01, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x3B,
        ];
    }

    private static byte[] CreateTwoImageIco()
    {
        var firstImage = CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFFFF0000u,
            ]);
        var secondImage = CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF00FF00u,
            ]);

        var firstImageOffset = 6 + 16 + 16;
        var secondImageOffset = firstImageOffset + firstImage.Length;
        var data = new byte[secondImageOffset + secondImage.Length];
        WriteUInt16LittleEndian(data, 0, 0);
        WriteUInt16LittleEndian(data, 2, 1);
        WriteUInt16LittleEndian(data, 4, 2);

        WriteIcoDirectoryEntry(data, 6, width: 1, height: 1, bitsPerPixel: 32, firstImage.Length, firstImageOffset);
        WriteIcoDirectoryEntry(data, 22, width: 1, height: 1, bitsPerPixel: 32, secondImage.Length, secondImageOffset);

        Buffer.BlockCopy(firstImage, 0, data, firstImageOffset, firstImage.Length);
        Buffer.BlockCopy(secondImage, 0, data, secondImageOffset, secondImage.Length);
        return data;
    }

    private static void WriteIcoDirectoryEntry(byte[] data, int offset, byte width, byte height, ushort bitsPerPixel, int imageSize, int imageOffset)
    {
        data[offset] = width;
        data[offset + 1] = height;
        data[offset + 2] = 0;
        data[offset + 3] = 0;
        WriteUInt16LittleEndian(data, offset + 4, 1);
        WriteUInt16LittleEndian(data, offset + 6, bitsPerPixel);
        WriteUInt32LittleEndian(data, offset + 8, (uint)imageSize);
        WriteUInt32LittleEndian(data, offset + 12, (uint)imageOffset);
    }

    private static async Task<SnapshotFile[]> AssertSnapshot(
        [StringSyntax("c#-test")] string source,
        SnapshotTestFramework testFramework = SnapshotTestFramework.XunitV3,
        string? targetFramework = null,
        bool expectFailure = false,
        IReadOnlyList<SnapshotFile>? existingFiles = null,
        string? testFilter = null,
        string? directoryBuildPropsContent = null)
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
                <TargetFramework>{{targetFramework ?? TargetFrameworkHelper.GetTargetFrameworkMoniker()}}</TargetFramework>
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

        if (!string.IsNullOrEmpty(directoryBuildPropsContent))
        {
            CreateTextFile("Directory.Build.props", directoryBuildPropsContent);
        }

        await ExecuteDotNet(directory.FullPath, dotnetPath, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await ExecuteDotNet(directory.FullPath, dotnetPath, ["build", "--no-restore", "--disable-build-servers"], expectedExitCode: 0);
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
