using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Meziantou.Framework.DiffEngine;
using Meziantou.Framework.SnapshotTesting.MergeTools;
using Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;
using Meziantou.Xunit;
using GifFrameData = Meziantou.Framework.SnapshotTesting.Tests.ImageTestData.GifFrameData;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed partial class SnapshotTests
{
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "SNAPSHOTTESTING_STRATEGY";

    [Fact]
    public void Validate_CreateSnapshotFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + (context.Extension ?? "txt")),
        };

        Snapshot.Validate(new { A = 1 }, settings);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Contains("A: 1", File.ReadAllText(files[0]));
    }

    [Fact]
    public void Validate_FailsWhenSnapshotFileSetChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var baseSettings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotPathStrategy = context => directory / ("snapshot_fixed_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + (context.Extension ?? "txt")),
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
        };

        ValidateWithSerializerCount(baseSettings, 3);

        var validateSettings = baseSettings with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        var exception = Assert.Throws<SnapshotAssertionException>(() => ValidateWithSerializerCount(validateSettings, 2));
        Assert.Contains("Unexpected snapshot files:", exception.Message);
    }

    [Fact]
    public void Settings_WithDeepClone()
    {
        var original = new SnapshotSettings();
        original.Serializers.Add(new FixedCountSerializer(count: 1));
        original.Comparers.Set(SnapshotType.Create("dummy"), ByteArraySnapshotComparer.Instance);
        original.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, "line2");
        var originalSerializerCount = original.Serializers.Count;

        var clone = original.Clone();
        clone.Serializers.Add(new FixedCountSerializer(count: 2));

        Assert.NotSame(original.Serializers, clone.Serializers);
        Assert.NotSame(original.Comparers, clone.Comparers);
        Assert.NotSame(original.Scrubbers, clone.Scrubbers);
        Assert.Equal(original.Scrubbers, clone.Scrubbers);
        Assert.Equal(originalSerializerCount + 1, clone.Serializers.Count);
        Assert.Equal(originalSerializerCount, original.Serializers.Count);
    }

    [Theory]
    [InlineData("a\nb", "a\r\nb")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\r\nb\n", "a\nb\r\n")]
    [InlineData("\r\n\r\n", "\n\r")]
    [InlineData("é\r\n€", "é\n€")]
    public void TextComparer_IgnoresLineEndingDifferences(string expected, string actual)
    {
        var comparer = new SnapshotSettings().Comparers.Get(SnapshotType.Default);

        Assert.True(comparer.Equals(new SnapshotData("txt", Encoding.UTF8.GetBytes(expected)), new SnapshotData("txt", Encoding.UTF8.GetBytes(actual))));
    }

    [Theory]
    [InlineData("a\nb", "a\n\nb")]
    [InlineData("a\r\nb", "a\r\rb")]
    [InlineData("a\nb", "ab")]
    [InlineData("a\n", "a")]
    [InlineData("a", "a\r\n")]
    [InlineData("a\nb", "a\nc")]
    [InlineData("ab\n", "a\nb")]
    public void TextComparer_DetectsDifferences(string expected, string actual)
    {
        var comparer = new SnapshotSettings().Comparers.Get(SnapshotType.Svg);

        Assert.False(comparer.Equals(new SnapshotData("svg", Encoding.UTF8.GetBytes(expected)), new SnapshotData("svg", Encoding.UTF8.GetBytes(actual))));
        Assert.False(comparer.Equals(new SnapshotData("svg", Encoding.UTF8.GetBytes(actual)), new SnapshotData("svg", Encoding.UTF8.GetBytes(expected))));
    }

    [Fact]
    public void Validate_WritesLineFeedsAndAcceptsVerifiedFileWithCarriageReturns()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.txt";
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = _ => path,
        };
        var value = new { A = 1, B = "line1\r\nline2" };

        Snapshot.Validate(value, settings);
        Assert.Equal("A: 1\nB:\n  line1\n  line2", File.ReadAllText(path));

        File.WriteAllText(path, "A: 1\r\nB:\r\n  line1\r\n  line2");
        Snapshot.Validate(value, settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow });
        Assert.False(File.Exists(directory / "snapshot.actual.txt"));
    }

    [Fact]
    public void ResolveSourceFilePath_ThrowsWhenSourceFilePathIsNotFound()
    {
        var sourceFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "file.cs");

        var exception = Assert.Throws<SnapshotException>(() => SnapshotCallerContext.ResolveSourceFilePath(sourceFilePath));
        Assert.Contains(sourceFilePath, exception.Message);
    }

    [Fact]
    public void ResolveSourceFilePath_UsesRegisteredSourceRootMapping()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFilePath = directory.GetFullPath("sub/file.cs");
        sourceFilePath.CreateParentDirectory();
        File.WriteAllText(sourceFilePath, "class C {}");

        var sourceRoot = directory.FullPath.Value.Replace('\\', '/');
        Snapshot.RegisterSourceRootMapping("/_snapshot_tests_/", sourceRoot + "/");

        var resolvedPath = SnapshotCallerContext.ResolveSourceFilePath("/_snapshot_tests_/sub/file.cs");

        Assert.Equal(sourceFilePath, resolvedPath);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesIndexPatternForShortName()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Image_snapshot"),
            Settings: settings,
            SnapshotCount: 3);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(SnapshotNameWithIndexRegex(), path.Name);
        Assert.DoesNotMatch(SnapshotNameHashWithIndexFragmentRegex(), path.Name);
        Assert.HasCountLessThanOrEqual(settings.MaxSnapshotFileNameLength, path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_OmitsIndex_WhenSingleSnapshot()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Image_snapshot"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(SnapshotNameRegex(), path.Name);
        Assert.DoesNotContain("_0", path.Name);
        Assert.HasCountLessThanOrEqual(settings.MaxSnapshotFileNameLength, path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_PrefixesClassName_WhenAvailable()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "MethodName"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("UnitTestClass1_MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_DoesNotDuplicateClassName_WhenNameIsClassQualified()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "MyNamespace.UnitTestClass1.MethodName"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("MyNamespace.UnitTestClass1.MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_DefaultsToClassNameTestNameStrategy()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Case_alpha"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("UnitTestClass1_Case_alpha.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesTestNameStrategy()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.TestName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Case_alpha"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("Case_alpha.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesFullNameStrategy()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.FullName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: null,
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("UnitTestClass1.MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_FullNameStrategy_DoesNotDuplicateClassName_WhenNameIsClassQualified()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.FullName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "MyNamespace.UnitTestClass1.MethodName"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("MyNamespace.UnitTestClass1.MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesHashAndIndexPatternForLongName()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: new string('a', 120),
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: null,
            Settings: settings,
            SnapshotCount: 3);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(SnapshotNameWithHashAndIndexRegex(), path.Name);
        Assert.HasCountLessThanOrEqual(settings.MaxSnapshotFileNameLength, path.Name);
    }

    [Theory]
    [InlineData("snapshot.verified")]
    [InlineData("snapshot.actual")]
    [InlineData("Parse.actual.value")]
    [InlineData("Parse.verified.value")]
    [InlineData("Parse.ACTUAL.value")]
    [InlineData("a.actual.verified.actual.b")]
    public void SnapshotPathStrategy_UsesHashAndIndexPatternForReservedNames(string testName)
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: testName),
            Settings: settings,
            SnapshotCount: 3);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(SnapshotNameWithHashAndIndexRegex(), path.Name);
        Assert.HasCountLessThanOrEqual(settings.MaxSnapshotFileNameLength, path.Name);

        // The name part never contains a marker, so the approval tool can never take the verified file for an actual file.
        var nameWithoutMarker = path.Name[..^".verified.png".Length].ToUpperInvariant();
        Assert.DoesNotContain(".ACTUAL.", nameWithoutMarker);
        Assert.DoesNotContain(".VERIFIED.", nameWithoutMarker);
    }

    [Theory]
    [InlineData("NUL", true)]
    [InlineData("nul", true)]
    [InlineData("Con.Parse", true)]
    [InlineData("aux", true)]
    [InlineData("PRN", true)]
    [InlineData("COM1", true)]
    [InlineData("com0", true)]
    [InlineData("LPT9", true)]
    [InlineData("Console", false)]
    [InlineData("COM10", false)]
    [InlineData("Null", false)]
    public void SnapshotPathStrategy_AddsAHashToWindowsDeviceNames(string testName, bool isDeviceName)
    {
        var settings = new SnapshotSettings { SnapshotNamingStrategy = SnapshotNamingStrategies.TestName };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath(Path.Combine(Path.GetTempPath(), "SampleTests.cs")),
            ClassName: "SampleTests",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "txt",
            TestContext: new SnapshotTestContext(TestName: testName),
            Settings: settings);

        var path = settings.SnapshotPathStrategy(context);

        if (isDeviceName)
        {
            Assert.Matches(SnapshotNameWithHashSuffixRegex(), path.Name);
        }
        else
        {
            Assert.Equal(testName + ".verified.txt", path.Name);
        }
    }

    [Fact]
    public void Validate_SupportsExtensionsContainingADot()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "Generate", "Generate");
        var type = SnapshotType.Create("g.cs");

        // A file left behind when the snapshot was stored as text.
        var snapshotDirectory = directory / "__snapshots__";
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(snapshotDirectory / "C_Generate.verified.txt", "text");

        ValidateWithDefaultNaming("class A;", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure, type);
        Assert.Equal(["C_Generate.verified.g.cs"], GetSnapshotFileNames(directory));

        var exception = Assert.Throws<SnapshotAssertionException>(() => ValidateWithDefaultNaming("class B;", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.Disallow, type));
        Assert.Contains((snapshotDirectory / "C_Generate.actual.g.cs").Value, exception.Message);
        Assert.Equal(["C_Generate.actual.g.cs", "C_Generate.verified.g.cs"], GetSnapshotFileNames(directory));

        // Another test using the same name is reported.
        Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("class A;", CreateDetectedTestContext("C", "Other", "Generate"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.Disallow, type));
    }

    [Fact]
    public void Validate_ResolvesTestMethod_WhenCalledFromHelperMethod()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / (SnapshotSettings.Default.SnapshotPathStrategy(context).Name);
            },
        };

        ValidateThroughHelper("sample", settings);

        Assert.NotNull(capturedContext);
        Assert.Equal(nameof(SnapshotTests), capturedContext.ClassName);
        Assert.Equal(nameof(Validate_ResolvesTestMethod_WhenCalledFromHelperMethod), capturedContext.MethodName);
        Assert.Equal(nameof(Validate_ResolvesTestMethod_WhenCalledFromHelperMethod), capturedContext.TestContext?.TestName);
        Assert.Equal(nameof(ValidateThroughHelper), capturedContext.MemberName);
        Assert.Equal("SnapshotTests.cs", capturedContext.SourceFilePath.Name);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Equal("SnapshotTests_Validate_ResolvesTestMethod_WhenCalledFromHelperMethod.verified.txt", Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task Validate_ResolvesTestMethod_WhenCalledFromAsyncHelperMethodInAnotherClass()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / (SnapshotSettings.Default.SnapshotPathStrategy(context).Name);
            },
        };

        await SnapshotHelpers.ValidateThroughAsyncHelper("sample", settings);

        Assert.NotNull(capturedContext);
        Assert.Equal(nameof(SnapshotTests), capturedContext.ClassName);
        Assert.Equal(nameof(Validate_ResolvesTestMethod_WhenCalledFromAsyncHelperMethodInAnotherClass), capturedContext.MethodName);
        Assert.Equal("SnapshotTests.cs", capturedContext.SourceFilePath.Name);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Equal("SnapshotTests_Validate_ResolvesTestMethod_WhenCalledFromAsyncHelperMethodInAnotherClass.verified.txt", Path.GetFileName(files[0]));
    }

    [Fact]
    public void Validate_DoesNotWalkTheStack_WhenTheStrategyDoesNotReadTheTestNames()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / "snapshot.verified.txt";
            },
        };

        Snapshot.Validate("sample", settings);

        Assert.NotNull(capturedContext);
        Assert.False(capturedContext.StackWalkPerformed);
        Assert.Equal(nameof(Validate_DoesNotWalkTheStack_WhenTheStrategyDoesNotReadTheTestNames), capturedContext.MemberName);

        // The frames of the assertion are gone by now, so the names are read from the test framework instead
        // of describing an unrelated call stack.
        Assert.Equal(nameof(Validate_DoesNotWalkTheStack_WhenTheStrategyDoesNotReadTheTestNames), capturedContext.MethodName);
        Assert.Equal(nameof(SnapshotTests), capturedContext.ClassName);
        Assert.False(capturedContext.StackWalkPerformed);
    }

    [Fact]
    public void Validate_DoesNotWalkTheStack_WhenTheTestFrameworkProvidesTheNames()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / (context.ClassName + "_" + context.MethodName + ".verified.txt");
            },
        };

        Snapshot.Validate("sample", settings);

        // Xunit v3 exposes both names, so the strategy read them without paying for the walk.
        Assert.NotNull(capturedContext);
        Assert.False(capturedContext.StackWalkPerformed);
        Assert.Equal(nameof(Validate_DoesNotWalkTheStack_WhenTheTestFrameworkProvidesTheNames), capturedContext.MethodName);
        Assert.Equal(nameof(SnapshotTests), capturedContext.ClassName);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Equal(nameof(SnapshotTests) + "_" + nameof(Validate_DoesNotWalkTheStack_WhenTheTestFrameworkProvidesTheNames) + ".verified.txt", Path.GetFileName(files[0]));
    }

    [Fact]
    public void Validate_WalksTheStack_WhenTheTestContextDoesNotProvideTheNames()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / (context.ClassName + "_" + context.MethodName + ".verified.txt");
            },
        };

        // An explicit context replaces the one detected from the test framework. This one carries no names,
        // so the call stack is the only remaining source.
        using (new SnapshotTestContextScope(new SnapshotTestContext(TestName: "custom")))
        {
            Snapshot.Validate("sample", settings);
        }

        Assert.NotNull(capturedContext);
        Assert.True(capturedContext.StackWalkPerformed);
        Assert.Equal(nameof(Validate_WalksTheStack_WhenTheTestContextDoesNotProvideTheNames), capturedContext.MethodName);
        Assert.Equal(nameof(SnapshotTests), capturedContext.ClassName);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Equal(nameof(SnapshotTests) + "_" + nameof(Validate_WalksTheStack_WhenTheTestContextDoesNotProvideTheNames) + ".verified.txt", Path.GetFileName(files[0]));
    }

    [Fact]
    public void Validate_WalksTheStack_RecognizesAttributesDerivedFromATestAttribute()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / (context.ClassName + "_" + context.MethodName + ".verified.txt");
            },
        };

        // The explicit context carries no names, so the call stack is walked. The walk must stop at the method marked
        // with an attribute deriving from a test attribute ([DataTestMethod], [SkippableFact]...) rather than at the
        // helper it calls, or at this test method further up the stack.
        using (new SnapshotTestContextScope(new SnapshotTestContext(TestName: "custom")))
        {
            MethodWithDerivedTestAttribute(settings);
        }

        Assert.NotNull(capturedContext);
        Assert.True(capturedContext.StackWalkPerformed);
        Assert.Equal(nameof(MethodWithDerivedTestAttribute), capturedContext.MethodName);
        Assert.Equal(nameof(SnapshotTests), capturedContext.ClassName);
    }

    [DerivedTestMethod]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MethodWithDerivedTestAttribute(SnapshotSettings settings)
    {
        ValidateThroughHelper("sample", settings);
    }

    private static class FakeTestFramework
    {
        // Only the name of the attribute is known to the stack walk, as it does not reference any test framework.
        [AttributeUsage(AttributeTargets.Method)]
        public abstract class TestMethodAttribute : Attribute;
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class DerivedTestMethodAttribute : FakeTestFramework.TestMethodAttribute;

    [Fact]
    public void Validate_UsesTheNamesOfAnExplicitTestContext_OverTheCallStack()
    {
        using var directory = TemporaryDirectory.Create();
        SnapshotPathContext? capturedContext = null;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context =>
            {
                capturedContext = context;
                return directory / (context.ClassName + "_" + context.MethodName + ".verified.txt");
            },
        };

        using (new SnapshotTestContextScope(new SnapshotTestContext { ClassName = "CustomClass", MethodName = "CustomMethod" }))
        {
            Snapshot.Validate("sample", settings);
        }

        Assert.NotNull(capturedContext);
        Assert.False(capturedContext.StackWalkPerformed);
        Assert.Equal("CustomClass", capturedContext.ClassName);
        Assert.Equal("CustomMethod", capturedContext.MethodName);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Equal("CustomClass_CustomMethod.verified.txt", Path.GetFileName(files[0]));
    }

    private sealed class SnapshotTestContextScope : IDisposable
    {
        private readonly SnapshotTestContext? _previous;

        public SnapshotTestContextScope(SnapshotTestContext? context)
        {
            _previous = Snapshot.TestContext.Value;
            Snapshot.TestContext.Value = context;
        }

        public void Dispose() => Snapshot.TestContext.Value = _previous;
    }

    private static class SnapshotHelpers
    {
        public static async Task ValidateThroughAsyncHelper(object? value, SnapshotSettings settings, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
        {
            // Yielding drops the test method from the call stack, so only the test framework still knows
            // which test is running.
            await Task.Yield();
            Snapshot.Validate(value, type: null, settings, filePath, lineNumber);
        }
    }

    private static void ValidateThroughHelper(object? value, SnapshotSettings settings, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        Snapshot.Validate(value, type: null, settings, filePath, lineNumber);
    }

    [Fact]
    public void Validate_UsesSnapshotTypeExtensionWhenSerializerDoesNotProvideOne()
    {
        using var directory = TemporaryDirectory.Create();
        var snapshotType = SnapshotType.Png;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / (context.Type.Type + "_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension),
        };

        settings.Serializers.Add(new SnapshotTypeSerializer(extension: null));
        Snapshot.Validate("sample", snapshotType, settings);

        var filePath = directory / "png_0.verified.png";
        Assert.True(File.Exists(filePath));
        Assert.Equal("png", File.ReadAllText(filePath));
    }

    [Fact]
    public void Validate_UsesSerializerExtensionWhenProvided()
    {
        using var directory = TemporaryDirectory.Create();
        var snapshotType = SnapshotType.Png;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / (context.Type.Type + "_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension),
        };

        settings.Serializers.Add(new SnapshotTypeSerializer(extension: "cs"));
        Snapshot.Validate("sample", snapshotType, settings);

        var filePath = directory / "png_0.verified.cs";
        Assert.True(File.Exists(filePath));
        Assert.Equal("png", File.ReadAllText(filePath));
    }

    [Fact]
    public void SnapshotType_DefaultsExposeOptionalMetadata()
    {
        Assert.Equal("text/plain", SnapshotType.Default.MimeType);
        Assert.Equal("Text", SnapshotType.Default.DisplayName);
        Assert.Equal("image/png", SnapshotType.Png.MimeType);
        Assert.Equal("PNG image", SnapshotType.Png.DisplayName);
        Assert.Equal("image/svg+xml", SnapshotType.Svg.MimeType);
        Assert.Equal("SVG image", SnapshotType.Svg.DisplayName);
        Assert.Equal("image/gif", SnapshotType.Gif.MimeType);
        Assert.Equal("GIF image", SnapshotType.Gif.DisplayName);
        Assert.Equal("image/tiff", SnapshotType.Tiff.MimeType);
        Assert.Equal("TIFF image", SnapshotType.Tiff.DisplayName);
        Assert.Equal("image/x-icon", SnapshotType.Ico.MimeType);
        Assert.Equal("ICO image", SnapshotType.Ico.DisplayName);
    }

    [Fact]
    public void SnapshotType_EqualityUsesTypeOnly()
    {
        var pngA = SnapshotType.Create("png", mimeType: "image/png", displayName: "Portable Network Graphics");
        var pngB = SnapshotType.Create("png", mimeType: "application/octet-stream", displayName: "Png");

        Assert.Equal(pngA, pngB);
        Assert.Equal(pngA.GetHashCode(), pngB.GetHashCode());
    }

    [Fact]
    public void SnapshotType_CreateReturnsCachedInstance()
    {
        var pngA = SnapshotType.Create("png");
        var pngB = SnapshotType.Create("png");
        var svgA = SnapshotType.Create("svg");
        var svgB = SnapshotType.Create("svg");

        Assert.Same(pngA, pngB);
        Assert.Same(svgA, svgB);
        Assert.Same(SnapshotType.Svg, svgA);
    }

    [Fact]
    public void DefaultSerializer_HandlesByteArrayAsBinary()
    {
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "binary-data"u8.ToArray();
        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, expectedBytes);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
    }

    [Fact]
    public void DefaultSerializer_HandlesStreamAsBinary()
    {
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "stream-binary-data"u8.ToArray();
        using var stream = new MemoryStream(expectedBytes);
        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, stream);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void DefaultSerializer_ReadsTheStreamFromItsCurrentPosition()
    {
        var snapshotType = SnapshotType.Png;
        using var stream = new MemoryStream("stream-binary-data"u8.ToArray());
        stream.Position = "stream-".Length;

        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, stream);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal("binary-data"u8.ToArray(), snapshot.Data);
    }

    [Fact]
    public void DefaultSerializer_ProducesAnEmptySnapshotForAStreamPositionedAtTheEnd()
    {
        // The stream is not rewound, so a caller that has just written to it snapshots nothing. Pinned here
        // because it is a surprising consequence of reading from the current position.
        var snapshotType = SnapshotType.Png;
        using var stream = new MemoryStream();
        stream.Write("stream-binary-data"u8);

        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, stream);

        var snapshot = Assert.Single(data.Data);
        Assert.Empty(snapshot.Data);
    }

    [Fact]
    public void DefaultSerializer_HandlesNonSeekableStream()
    {
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "stream-binary-data"u8.ToArray();
        using var stream = new NonSeekableStream(expectedBytes);

        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, stream);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(expectedBytes, snapshot.Data);
    }

    [Fact]
    public void DefaultSerializer_HandlesGifByteArrayAsSingleBinarySnapshot()
    {
        var snapshotType = SnapshotType.Gif;
        var expectedBytes = CreateTwoFrameGif();
        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, expectedBytes);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
    }

    [Fact]
    public void AddGifSerializer_SerializesGifByteArrayAsOneSnapshotPerFrame()
    {
        var snapshotType = SnapshotType.Gif;
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(snapshotType, CreateTwoFrameGif());

        Assert.Equal(2, data.Data.Count);
        Assert.All(data.Data, snapshot => Assert.Equal(SnapshotType.Png.FileExtension, snapshot.Extension));
        Assert.Equal(CreateSingleFramePng(), data.Data[0].Data);
        Assert.Equal(CreateSingleFramePng(), data.Data[1].Data);
    }

    [Fact]
    public void AddGifSerializer_CompositesDeltaEncodedFramesOverThePreviousFrame()
    {
        var gif = ImageTestData.CreateGif(
            width: 2,
            height: 2,
            colorTable: [0xFFFFFFu, 0x000000u],
            backgroundColorIndex: 1,
            new GifFrameData(Left: 0, Top: 0, Width: 2, Height: 2, PixelIndexes: [0, 0, 0, 0]),
            new GifFrameData(Left: 1, Top: 0, Width: 1, Height: 1, PixelIndexes: [1]));

        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(SnapshotType.Gif, gif);

        Assert.Equal(2, data.Data.Count);
        Assert.Equal(CreatePng2x2(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu), data.Data[0].Data);
        Assert.Equal(CreatePng2x2(0xFFFFFFFFu, 0xFF000000u, 0xFFFFFFFFu, 0xFFFFFFFFu), data.Data[1].Data);
    }

    [Fact]
    public void AddGifSerializer_RestoresTheBackgroundColorBetweenFramesWhenDisposalMethodIsRestoreToBackground()
    {
        var gif = ImageTestData.CreateGif(
            width: 2,
            height: 2,
            colorTable: [0xFFFFFFu, 0x000000u, 0xFF0000u],
            backgroundColorIndex: 2,
            new GifFrameData(Left: 0, Top: 0, Width: 2, Height: 2, PixelIndexes: [0, 0, 0, 0]),
            new GifFrameData(Left: 0, Top: 0, Width: 1, Height: 1, PixelIndexes: [1]) { DisposalMethod = 2 },
            new GifFrameData(Left: 1, Top: 1, Width: 1, Height: 1, PixelIndexes: [1]));

        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(SnapshotType.Gif, gif);

        Assert.Equal(3, data.Data.Count);
        Assert.Equal(CreatePng2x2(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu), data.Data[0].Data);
        Assert.Equal(CreatePng2x2(0xFF000000u, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu), data.Data[1].Data);

        // The rectangle covered by the second frame went back to the background color, the rest of the canvas
        // still holds the pixels of the first frame.
        Assert.Equal(CreatePng2x2(0xFFFF0000u, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFF000000u), data.Data[2].Data);
    }

    [Fact]
    public void AddGifSerializer_RestoresThePreviousFrameWhenDisposalMethodIsRestoreToPrevious()
    {
        var gif = ImageTestData.CreateGif(
            width: 2,
            height: 2,
            colorTable: [0xFFFFFFu, 0x000000u],
            backgroundColorIndex: 1,
            new GifFrameData(Left: 0, Top: 0, Width: 2, Height: 2, PixelIndexes: [0, 0, 0, 0]),
            new GifFrameData(Left: 0, Top: 0, Width: 1, Height: 1, PixelIndexes: [1]) { DisposalMethod = 3 },
            new GifFrameData(Left: 1, Top: 1, Width: 1, Height: 1, PixelIndexes: [1]));

        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(SnapshotType.Gif, gif);

        Assert.Equal(3, data.Data.Count);
        Assert.Equal(CreatePng2x2(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu), data.Data[0].Data);
        Assert.Equal(CreatePng2x2(0xFF000000u, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu), data.Data[1].Data);
        Assert.Equal(CreatePng2x2(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFF000000u), data.Data[2].Data);
    }

    [Fact]
    public void AddGifSerializer_KeepsThePixelsOfThePreviousFrameWhereTheFrameIsTransparent()
    {
        var gif = ImageTestData.CreateGif(
            width: 2,
            height: 2,
            colorTable: [0xFFFFFFu, 0x000000u],
            backgroundColorIndex: 0,
            new GifFrameData(Left: 0, Top: 0, Width: 2, Height: 2, PixelIndexes: [1, 1, 1, 1]),
            new GifFrameData(Left: 0, Top: 0, Width: 2, Height: 2, PixelIndexes: [0, 1, 1, 1]) { TransparentColorIndex = 1 });

        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(SnapshotType.Gif, gif);

        Assert.Equal(2, data.Data.Count);
        Assert.Equal(CreatePng2x2(0xFF000000u, 0xFF000000u, 0xFF000000u, 0xFF000000u), data.Data[0].Data);
        Assert.Equal(CreatePng2x2(0xFFFFFFFFu, 0xFF000000u, 0xFF000000u, 0xFF000000u), data.Data[1].Data);
    }

    [Fact]
    public void AddGifSerializer_FallsBackToBinarySerializerWhenPayloadIsNotGif()
    {
        var snapshotType = SnapshotType.Gif;
        var payload = "not-a-gif"u8.ToArray();
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(snapshotType, payload);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(payload, snapshot.Data);
    }

    [Fact]
    public void AddIcoSerializer_SerializesIcoByteArrayAsOneSnapshotPerEntry()
    {
        var snapshotType = SnapshotType.Ico;
        var settings = new SnapshotSettings();
        settings.Serializers.AddIcoSerializer();
        var data = settings.Serializers.Serialize(snapshotType, CreateTwoEntryIco());

        Assert.Equal(2, data.Data.Count);
        Assert.All(data.Data, snapshot => Assert.Equal(SnapshotType.Png.FileExtension, snapshot.Extension));
        Assert.Equal(CreateSingleFramePng(), data.Data[0].Data);
        Assert.Equal(CreateSingleFramePng(color: 0xFF000000u), data.Data[1].Data);
    }

    [Fact]
    public void AddIcoSerializer_RegistersTheSerializerOnlyOnce()
    {
        var settings = new SnapshotSettings();
        settings.Serializers.AddIcoSerializer();
        var count = settings.Serializers.Count;

        settings.Serializers.AddIcoSerializer();

        Assert.HasCount(count, settings.Serializers);
        Assert.Single(settings.Serializers, static serializer => serializer is IcoSnapshotSerializer);
    }

    [Fact]
    public void AddIcoSerializer_FallsBackToBinarySerializerWhenPayloadIsNotIco()
    {
        var snapshotType = SnapshotType.Ico;
        var payload = "not-an-ico"u8.ToArray();
        var settings = new SnapshotSettings();
        settings.Serializers.AddIcoSerializer();
        var data = settings.Serializers.Serialize(snapshotType, payload);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(payload, snapshot.Data);
    }

    [Fact]
    public void AddGifSerializer_StaticFixture_Snapshot()
    {
        var payload = ImageTestData.ReadImageFixture("serializer-two-frame.gif");
        var settings = SnapshotSettings.Default with { };
        settings.Serializers.AddGifSerializer();

        Snapshot.Validate(payload, SnapshotType.Gif, settings);
    }

    [Fact]
    public void AddIcoSerializer_StaticFixture_Snapshot()
    {
        var payload = ImageTestData.ReadImageFixture("serializer-multi-entry.ico");
        var settings = SnapshotSettings.Default with { };
        settings.Serializers.AddIcoSerializer();

        Snapshot.Validate(payload, SnapshotType.Ico, settings);
    }

    [Fact]
    public void AddImageComparer_ComparesBmpSnapshotsByPixels()
    {
        var expectedData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 2835);
        var actualData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 3780);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Bmp);
        Assert.True(comparer.Equals(new SnapshotData("bmp", expectedData), new SnapshotData("bmp", actualData)));
    }

    [Fact]
    public void AddImageComparer_DetectsBmpPixelDifferences()
    {
        var expectedData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 2835);
        var actualData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF040506u,
            ],
            pixelsPerMeter: 2835);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Bmp);
        Assert.False(comparer.Equals(new SnapshotData("bmp", expectedData), new SnapshotData("bmp", actualData)));
    }

    [Fact]
    public void AddImageComparer_ComparesPngSnapshotsByPixels()
    {
        var expectedData = ImageTestData.CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            gamma: 0.45455f);
        var actualData = ImageTestData.CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            gamma: 1.0f);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Png);
        Assert.True(comparer.Equals(new SnapshotData("png", expectedData), new SnapshotData("png", actualData)));
    }

    [Fact]
    public void AddImageComparer_DetectsPngPixelDifferences()
    {
        var expectedData = ImageTestData.CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ]);
        var actualData = ImageTestData.CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF040506u,
            ]);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Png);
        Assert.False(comparer.Equals(new SnapshotData("png", expectedData), new SnapshotData("png", actualData)));
    }

    [Fact]
    public void AddImageComparer_ComparesJpegSnapshotsByPixels()
    {
        var expectedData = ImageTestData.ReadImageFixture("ycbcr-420-baseline.jpg");
        var actualData = ImageTestData.AddJpegCommentSegment(expectedData, "metadata-only-difference");

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Jpeg);
        Assert.True(comparer.Equals(new SnapshotData("jpeg", expectedData), new SnapshotData("jpeg", actualData)));
    }

    [Fact]
    public void AddImageComparer_RegistersJpgAlias()
    {
        var expectedData = ImageTestData.ReadImageFixture("ycbcr-420-baseline.jpg");
        var actualData = ImageTestData.AddJpegCommentSegment(expectedData, "jpg-alias");

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Create("jpg"));
        Assert.True(comparer.Equals(new SnapshotData("jpg", expectedData), new SnapshotData("jpg", actualData)));
    }

    [Fact]
    public void AddImageComparer_DetectsJpegPixelDifferences()
    {
        var expectedData = ImageTestData.ReadImageFixture("ycbcr-420-baseline.jpg");
        var actualData = ImageTestData.ReadImageFixture("ycbcr-420-different.jpg");

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Jpeg);
        Assert.False(comparer.Equals(new SnapshotData("jpeg", expectedData), new SnapshotData("jpeg", actualData)));
    }

    [Fact]
    public void AddImageComparer_ComparesTiffSnapshotsByPixels()
    {
        var expectedData = ImageTestData.ReadImageFixture("tiff-rgb24-none.tiff");
        var actualData = ImageTestData.ReadImageFixture("tiff-rgb24-lzw.tiff");

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Tiff);
        Assert.True(comparer.Equals(new SnapshotData("tiff", expectedData), new SnapshotData("tiff", actualData)));
    }

    [Fact]
    public void AddImageComparer_RegistersTifAlias()
    {
        var expectedData = ImageTestData.ReadImageFixture("tiff-rgb24-none.tiff");
        var actualData = ImageTestData.ReadImageFixture("tiff-rgb24-packbits.tiff");

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Create("tif"));
        Assert.True(comparer.Equals(new SnapshotData("tif", expectedData), new SnapshotData("tif", actualData)));
    }

    [Fact]
    public void AddImageComparer_DetectsTiffPixelDifferences()
    {
        var expectedData = ImageTestData.ReadImageFixture("tiff-rgb24-none.tiff");
        var actualData = ImageTestData.ReadImageFixture("tiff-rgb24-different.tiff");

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Tiff);
        Assert.False(comparer.Equals(new SnapshotData("tiff", expectedData), new SnapshotData("tiff", actualData)));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_AllowsSmallDifferences()
    {
        var expectedData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF000000u,
            ],
            pixelsPerMeter: 2835);
        var actualData = ImageTestData.CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010000u,
            ],
            pixelsPerMeter: 2835);

        var comparer = new ImageComparer(new ImageComparisonSettings
        {
            SimilarityThreshold = 0.95f,
        });

        Assert.True(comparer.Equals(new SnapshotData("bmp", expectedData), new SnapshotData("bmp", actualData)));
    }

    [Fact]
    public void ImageComparer_WithDHashThreshold_UsesInclusiveHammingDistance()
    {
        var expectedImage = CreatePatternImage(width: 32, height: 32, inverted: false);
        var actualImage = CreatePatternImage(width: 32, height: 32, inverted: true);
        var distance = ImageHash.ComputeDHashDistance(expectedImage, actualImage);
        Assert.True(distance > 0);

        var expected = CreateSnapshotData(expectedImage);
        var actual = CreateSnapshotData(actualImage);
        Assert.False(new ImageComparer(new ImageComparisonSettings { DHashThreshold = distance - 1 }).Equals(expected, actual));
        Assert.True(new ImageComparer(new ImageComparisonSettings { DHashThreshold = distance }).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithPHashThreshold_UsesInclusiveHammingDistance()
    {
        var expectedImage = CreatePatternImage(width: 32, height: 32, inverted: false);
        var actualImage = CreatePatternImage(width: 32, height: 32, inverted: true);
        var distance = ImageHash.ComputePHashDistance(expectedImage, actualImage);
        Assert.True(distance > 0);

        var expected = CreateSnapshotData(expectedImage);
        var actual = CreateSnapshotData(actualImage);
        Assert.False(new ImageComparer(new ImageComparisonSettings { PHashThreshold = distance - 1 }).Equals(expected, actual));
        Assert.True(new ImageComparer(new ImageComparisonSettings { PHashThreshold = distance }).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithHashThresholdZero_AllowsIdenticalHashes()
    {
        var image = CreatePatternImage(width: 32, height: 32, inverted: false);
        var snapshot = CreateSnapshotData(image);
        var comparer = new ImageComparer(new ImageComparisonSettings
        {
            DHashThreshold = 0,
            PHashThreshold = 0,
        });

        Assert.True(comparer.Equals(snapshot, snapshot));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithOnlyHashThresholds_RejectsDifferentDimensions(bool useDHash)
    {
        // Both images reduce to the same thumbnail, so only the dimensions tell them apart
        var expected = CreateSnapshotData(CreateSolidImage(64, 64, 0xFF000000u));
        var actual = CreateSnapshotData(CreateSolidImage(128, 32, 0xFF000000u));

        Assert.False(CreateHashComparer(useDHash, threshold: 64).Equals(expected, actual));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_DetectsSmallFeatures(bool useDHash)
    {
        // Sampling only a few source pixels per thumbnail cell missed features that fall between the samples
        var expectedImage = CreateSolidImage(1024, 1024, 0xFFFFFFFFu);
        var squareImage = CreateSolidImage(1024, 1024, 0xFFFFFFFFu);
        squareImage = FillRectangle(squareImage, x: 500, y: 500, width: 31, height: 31, 0xFF000000u);
        var bandImage = CreateSolidImage(1024, 1024, 0xFFFFFFFFu);
        bandImage = FillRectangle(bandImage, x: 700, y: 0, width: 11, height: 1024, 0xFF000000u);

        var comparer = CreateHashComparer(useDHash, threshold: 0);
        var expected = CreateSnapshotData(expectedImage);
        Assert.False(comparer.Equals(expected, CreateSnapshotData(squareImage)));
        Assert.False(comparer.Equals(expected, CreateSnapshotData(bandImage)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_DetectsUniformColorDifferences(bool useDHash)
    {
        // Uniform images have no structure, so their hashes are equal whatever their color
        var whiteImage = CreateSolidImage(16, 16, 0xFFFFFFFFu);
        var blackImage = CreateSolidImage(16, 16, 0xFF000000u);
        var white = CreateSnapshotData(whiteImage);
        var black = CreateSnapshotData(blackImage);
        var gray = CreateSnapshotData(CreateSolidImage(16, 16, 0xFF808080u));

        var distance = useDHash ? ImageHash.ComputeDHashDistance(whiteImage, blackImage) : ImageHash.ComputePHashDistance(whiteImage, blackImage);
        Assert.Equal(64, distance);
        Assert.False(CreateHashComparer(useDHash, threshold: 63).Equals(white, black));
        Assert.False(CreateHashComparer(useDHash, threshold: 5).Equals(white, gray));
        Assert.True(CreateHashComparer(useDHash, threshold: 64).Equals(white, black));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThresholdZero_IgnoresImperceptibleDifferencesInFlatImages(bool useDHash)
    {
        // The DCT of a flat image is rounding noise, which used to decide most pHash bits
        var expected = CreateSnapshotData(CreateSolidImage(100, 60, 0xFF808080u));
        var actual = CreateSnapshotData(CreateSolidImage(100, 60, 0xFF818080u));

        Assert.True(CreateHashComparer(useDHash, threshold: 0).Equals(expected, actual));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_DetectsAlphaDifferences(bool useDHash)
    {
        var comparer = CreateHashComparer(useDHash, threshold: 0);

        Assert.False(comparer.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFFFF0000u)), CreateSnapshotData(CreateSolidImage(16, 16, 0x00FF0000u))));
        Assert.False(comparer.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFF000000u)), CreateSnapshotData(CreateSolidImage(16, 16, 0x00000000u))));
        Assert.False(comparer.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFFFFFFFFu)), CreateSnapshotData(CreateSolidImage(16, 16, 0x00FFFFFFu))));

        // Only the alpha channel draws the square
        var squareImage = CreateSolidImage(64, 64, 0xFF000000u);
        squareImage = FillRectangle(squareImage, x: 16, y: 16, width: 24, height: 24, 0x00000000u);
        Assert.False(comparer.Equals(CreateSnapshotData(CreateSolidImage(64, 64, 0xFF000000u)), CreateSnapshotData(squareImage)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_TransparentPixelsAreEqualWhateverColorTheyHide(bool useDHash)
    {
        var expected = CreateSnapshotData(CreateSolidImage(16, 16, 0x00FF0000u));
        var actual = CreateSnapshotData(CreateSolidImage(16, 16, 0x0000FF00u));

        Assert.True(CreateHashComparer(useDHash, threshold: 0).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_UsesWindowedSsim()
    {
        // A single SSIM over the whole image scored this pair 0.087 although the square covers less than 1% of it
        var (width, height, expectedPixels, actualPixels) = ImageLoaderTests.CreateReferenceImages("WhiteWithBlackSquare");
        var expected = CreateSnapshotData(Image.Create(width, height, Array.ConvertAll(expectedPixels, pixel => new Argb(pixel))));
        var actual = CreateSnapshotData(Image.Create(width, height, Array.ConvertAll(actualPixels, pixel => new Argb(pixel))));

        Assert.True(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.98f }).Equals(expected, actual));
        Assert.False(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.99f }).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_DetectsAlphaDifferences()
    {
        var comparer = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.5f });

        Assert.False(comparer.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFFFF0000u)), CreateSnapshotData(CreateSolidImage(16, 16, 0x00FF0000u))));
        Assert.False(comparer.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFF000000u)), CreateSnapshotData(CreateSolidImage(16, 16, 0x00000000u))));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_TransparentPixelsAreEqualWhateverColorTheyHide()
    {
        var expected = CreateSnapshotData(CreateSolidImage(16, 16, 0x00FF0000u));
        var actual = CreateSnapshotData(CreateSolidImage(16, 16, 0x0000FF00u));

        Assert.True(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 1f }).Equals(expected, actual));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ImageComparisonSettings_RejectsInvalidSimilarityThresholds(float threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageComparisonSettings { SimilarityThreshold = threshold });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageSharp.ImageComparisonSettings { SimilarityThreshold = threshold });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkiaSharp.ImageComparisonSettings { SimilarityThreshold = threshold });
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(null)]
    public void ImageComparisonSettings_AcceptsValidSimilarityThresholds(float? threshold)
    {
        Assert.Equal(threshold, new ImageComparisonSettings { SimilarityThreshold = threshold }.SimilarityThreshold);
        Assert.Equal(threshold, new ImageSharp.ImageComparisonSettings { SimilarityThreshold = threshold }.SimilarityThreshold);
        Assert.Equal(threshold, new SkiaSharp.ImageComparisonSettings { SimilarityThreshold = threshold }.SimilarityThreshold);
    }

    [Fact]
    public void ImageComparer_ExactComparison_TransparentPixelsAreEqualWhateverColorTheyHide()
    {
        var expectedImage = Image.Create(2, 1, [new Argb(0, 10, 20, 30), new Argb(255, 1, 2, 3)]);
        var actualImage = Image.Create(2, 1, [new Argb(0, 40, 50, 60), new Argb(255, 1, 2, 3)]);
        Assert.Equal(expectedImage, actualImage);
        Assert.Equal(expectedImage.GetHashCode(), actualImage.GetHashCode());
        Assert.True(ImageComparer.Instance.Equals(CreateSnapshotData(expectedImage), CreateSnapshotData(actualImage)));

        // A nearly transparent pixel still shows its color
        var translucentImage = Image.Create(2, 1, [new Argb(1, 40, 50, 60), new Argb(255, 1, 2, 3)]);
        Assert.NotEqual(Image.Create(2, 1, [new Argb(1, 10, 20, 30), new Argb(255, 1, 2, 3)]), translucentImage);
    }

    [Fact]
    public void ImageComparer_ReturnsFalseWhenTheHeaderAnnouncesAnImageTooLargeToAllocate()
    {
        var expectedData = ImageTestData.ReadImageFixture("ycbcr-444-baseline.jpg");
        var actualData = CreateJpegAnnouncingAnImageTooLargeToAllocate(expectedData);

        var exception = Record.Exception(() => Image.Load(actualData));
        Assert.True(exception is InvalidDataException or NotSupportedException, exception?.ToString());
        Assert.False(ImageComparer.Instance.Equals(new SnapshotData("jpg", expectedData), new SnapshotData("jpg", actualData)));
        Assert.False(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0, DHashThreshold = 64 }).Equals(new SnapshotData("jpg", expectedData), new SnapshotData("jpg", actualData)));
    }

    [Fact]
    public void ImageSharpComparer_ImplementsTheSameSimilarityRules()
    {
        var exactSettings = new SnapshotSettings();
        ImageSharp.SnapsthotSettingsImageSharpExtensions.AddImageSharp(exactSettings);
        var exact = exactSettings.Comparers.Get(SnapshotType.Png);

        var similaritySettings = new SnapshotSettings();
        ImageSharp.SnapsthotSettingsImageSharpExtensions.AddImageSharp(similaritySettings, new ImageSharp.ImageComparisonSettings { SimilarityThreshold = 0.5f });
        var similarity = similaritySettings.Comparers.Get(SnapshotType.Png);

        // Transparent pixels are equal whatever color they hide, with both comparisons
        var transparentRed = CreateSnapshotData(CreateSolidImage(16, 16, 0x00FF0000u));
        var transparentGreen = CreateSnapshotData(CreateSolidImage(16, 16, 0x0000FF00u));
        Assert.True(exact.Equals(transparentRed, transparentGreen));
        Assert.True(similarity.Equals(transparentRed, transparentGreen));

        // Opacity is not ignored
        Assert.False(similarity.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFFFF0000u)), transparentRed));
        Assert.False(similarity.Equals(CreateSnapshotData(CreateSolidImage(16, 16, 0xFF000000u)), CreateSnapshotData(CreateSolidImage(16, 16, 0x00000000u))));

        // The SSIM is computed over local windows
        var (width, height, expectedPixels, actualPixels) = ImageLoaderTests.CreateReferenceImages("TexturedWithRedBlock");
        var expected = CreateSnapshotData(Image.Create(width, height, Array.ConvertAll(expectedPixels, pixel => new Argb(pixel))));
        var actual = CreateSnapshotData(Image.Create(width, height, Array.ConvertAll(actualPixels, pixel => new Argb(pixel))));
        Assert.True(similarity.Equals(expected, actual));
        Assert.False(exact.Equals(expected, actual));

        // A snapshot that cannot be decoded does not match
        Assert.False(similarity.Equals(expected, new SnapshotData("png", [.. expected.Data.AsSpan(0, 40)])));
        var jpeg = ImageTestData.ReadImageFixture("ycbcr-444-baseline.jpg");
        var tooLargeJpeg = CreateJpegAnnouncingAnImageTooLargeToAllocate(jpeg);
        Assert.False(exact.Equals(new SnapshotData("jpg", jpeg), new SnapshotData("jpg", tooLargeJpeg)));
        Assert.False(similarity.Equals(new SnapshotData("jpg", jpeg), new SnapshotData("jpg", tooLargeJpeg)));
    }

    [Theory]
    [InlineData(ImageBackend.BuiltIn)]
    [InlineData(ImageBackend.ImageSharp)]
    [InlineData(ImageBackend.SkiaSharp)]
    public void ImageBackends_ExactComparisonSeesEveryBitOf16BitSamples(ImageBackend backend)
    {
        var comparer = CreateImageBackendComparer(backend, similarityThreshold: null);
        var expected = CreatePngSnapshot(bitDepth: 16, colorType: 6, [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xFF, 0xFF]);

        // Only the low byte of the red sample differs
        Assert.False(comparer.Equals(expected, CreatePngSnapshot(bitDepth: 16, colorType: 6, [0x12, 0x35, 0x56, 0x78, 0x9A, 0xBC, 0xFF, 0xFF])));

        // The same pixel, stored without the alpha channel
        Assert.True(comparer.Equals(expected, CreatePngSnapshot(bitDepth: 16, colorType: 2, [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC])));

        // The 8-bit sample v is the 16-bit sample v × 257
        var exact16Bit = CreatePngSnapshot(bitDepth: 16, colorType: 2, [0x12, 0x12, 0x56, 0x56, 0x9A, 0x9A]);
        Assert.True(comparer.Equals(exact16Bit, CreatePngSnapshot(bitDepth: 8, colorType: 2, [0x12, 0x56, 0x9A])));
        Assert.False(comparer.Equals(expected, CreatePngSnapshot(bitDepth: 8, colorType: 2, [0x12, 0x56, 0x9A])));
    }

    [Theory]
    [InlineData(ImageBackend.BuiltIn)]
    [InlineData(ImageBackend.ImageSharp)]
    [InlineData(ImageBackend.SkiaSharp)]
    public void ImageBackends_SimilarityKeepsTheHighByteOf16BitSamples(ImageBackend backend)
    {
        // A checkerboard of 0x00FF and 0x0000 samples is flat once reduced to 8 bits by keeping the high byte, but
        // not when the samples are rounded (0x00FF becomes 1)
        var comparer = CreateImageBackendComparer(backend, similarityThreshold: 1f);
        var flat = CreateGray16Checkerboard(0x0000, 0x0000);

        Assert.True(comparer.Equals(flat, CreateGray16Checkerboard(0x0000, 0x00FF)));
        Assert.False(comparer.Equals(flat, CreateGray16Checkerboard(0x0000, 0x0100)));
    }

    [Theory]
    [InlineData(ImageBackend.BuiltIn)]
    [InlineData(ImageBackend.ImageSharp)]
    [InlineData(ImageBackend.SkiaSharp)]
    public void ImageBackends_ExplainWhyImagesDoNotMatch(ImageBackend backend)
    {
        var white = CreateSnapshotData(CreateSolidImage(16, 16, 0xFFFFFFFFu));
        var black = CreateSnapshotData(CreateSolidImage(16, 16, 0xFF000000u));
        var exact = CreateImageBackendComparer(backend, similarityThreshold: null);
        var similarity = CreateImageBackendComparer(backend, similarityThreshold: 0.95f);

        Assert.True(exact.Equals(white, white, out var reason));
        Assert.Null(reason);

        Assert.False(exact.Equals(white, black, out reason));
        Assert.Equal("The images have different pixels.", reason);

        Assert.False(similarity.Equals(white, black, out reason));
        Assert.StartsWith("The SSIM score 0", reason);
        Assert.EndsWith(" is below the threshold 0.95.", reason);

        Assert.False(similarity.Equals(white, CreateSnapshotData(CreateSolidImage(16, 17, 0xFFFFFFFFu)), out reason));
        Assert.Equal("The images have different sizes: expected 16x16, actual 16x17.", reason);

        Assert.False(exact.Equals(white, new SnapshotData("png", [.. white.Data.AsSpan(0, 20)]), out reason));
        Assert.StartsWith("The actual snapshot cannot be decoded as an image", reason);
        Assert.False(exact.Equals(new SnapshotData("png", [1, 2, 3]), white, out reason));
        Assert.StartsWith("The expected snapshot cannot be decoded as an image", reason);
    }

    [Fact]
    public void ImageComparer_ExplainsWhichHashCheckFailed()
    {
        var expected = CreateSnapshotData(CreatePatternImage(width: 32, height: 32, inverted: false));
        var actual = CreateSnapshotData(CreatePatternImage(width: 32, height: 32, inverted: true));
        var dHashDistance = ImageHash.ComputeDHashDistance(CreatePatternImage(32, 32, inverted: false), CreatePatternImage(32, 32, inverted: true));
        var pHashDistance = ImageHash.ComputePHashDistance(CreatePatternImage(32, 32, inverted: false), CreatePatternImage(32, 32, inverted: true));
        var comparer = new ImageComparer(new ImageComparisonSettings { DHashThreshold = 0, PHashThreshold = 0 });

        Assert.False(comparer.Equals(expected, actual, out var reason));
        Assert.Equal($"The dHash distance {dHashDistance} is above the threshold 0. The pHash distance {pHashDistance} is above the threshold 0.", reason);

        Assert.False(new ImageComparer(new ImageComparisonSettings { DHashThreshold = 64, PHashThreshold = 0 }).Equals(expected, actual, out reason));
        Assert.Equal($"The pHash distance {pHashDistance} is above the threshold 0.", reason);
    }

    [Fact]
    public void Validate_ReportsWhyImagesDoNotMatch()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = _ => directory / "snapshot.verified.png",
        };
        settings.Comparers.AddImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.99f });
        var verifiedPath = directory.GetFullPath("snapshot.verified.png");
        File.WriteAllBytes(verifiedPath, PngImageEncoder.Encode(CreateSolidImage(16, 16, 0xFFFFFFFFu)));

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate(PngImageEncoder.Encode(CreateSolidImage(16, 16, 0xFF000000u)), SnapshotType.Png, settings));

        Assert.Matches(@"Changed snapshot files:\r?\n  \* " + Regex.Escape(verifiedPath.Value) + @"\r?\n    The SSIM score -?[0-9.]+ is below the threshold 0\.99\.", exception.Message);
    }

    [Fact]
    public void Validate_ReportsTheMismatchReasonOfEachChangedFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        settings.Comparers.Set(SnapshotType.Default, new ExplainingSnapshotComparer());
        File.WriteAllText(directory.GetFullPath("snapshot_0.verified.txt"), "value_0");
        var changedPath = directory.GetFullPath("snapshot_1.verified.txt");
        File.WriteAllText(changedPath, "other");

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.Matches(@"Changed snapshot files:\r?\n  \* " + Regex.Escape(changedPath.Value) + @"\r?\n    'other' is not 'value_1'\r?\n", exception.Message);
        Assert.DoesNotContain("value_0'", exception.Message);
    }

    private sealed class ExplainingSnapshotComparer : ISnapshotComparer
    {
        public bool Equals(SnapshotData expected, SnapshotData actual) => Equals(expected, actual, out _);

        public bool Equals(SnapshotData expected, SnapshotData actual, out string? mismatchReason)
        {
            if (expected.Data.AsSpan().SequenceEqual(actual.Data))
            {
                mismatchReason = null;
                return true;
            }

            mismatchReason = $"'{Encoding.UTF8.GetString(expected.Data)}' is not '{Encoding.UTF8.GetString(actual.Data)}'";
            return false;
        }
    }

    public enum ImageBackend
    {
        BuiltIn,
        ImageSharp,
        SkiaSharp,
    }

    private static ISnapshotComparer CreateImageBackendComparer(ImageBackend backend, float? similarityThreshold)
    {
        var settings = new SnapshotSettings();
        switch (backend)
        {
            case ImageBackend.BuiltIn:
                settings.Comparers.AddImageComparer(new ImageComparisonSettings { SimilarityThreshold = similarityThreshold });
                break;

            case ImageBackend.ImageSharp:
                ImageSharp.SnapsthotSettingsImageSharpExtensions.AddImageSharp(settings, new ImageSharp.ImageComparisonSettings { SimilarityThreshold = similarityThreshold });
                break;

            case ImageBackend.SkiaSharp:
                SkiaSharp.SnapshotSettingsSkiaSharpExtensions.AddSkiaSharp(settings, new SkiaSharp.ImageComparisonSettings { SimilarityThreshold = similarityThreshold });
                break;
        }

        return settings.Comparers.Get(SnapshotType.Png);
    }

    private static SnapshotData CreatePngSnapshot(byte bitDepth, byte colorType, byte[] samples)
    {
        return new SnapshotData("png", ImageTestData.CreatePng(width: 1, height: 1, bitDepth, colorType, samples));
    }

    private static SnapshotData CreateGray16Checkerboard(ushort first, ushort second)
    {
        const int Size = 16;
        var samples = new byte[Size * Size * 2];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(samples.AsSpan((y * Size + x) * 2), (x + y) % 2 is 0 ? first : second);
            }
        }

        return new SnapshotData("png", ImageTestData.CreatePng(Size, Size, bitDepth: 16, colorType: 0, samples));
    }

    private static byte[] CreateJpegAnnouncingAnImageTooLargeToAllocate(byte[] jpeg)
    {
        // 37182 × 57756 pixels is a valid int, but more elements than an array can hold
        var result = (byte[])jpeg.Clone();
        var startOfFrame = result.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        Assert.True(startOfFrame >= 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(startOfFrame + 5), 57756);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(startOfFrame + 7), 37182);
        return result;
    }

    private static ImageComparer CreateHashComparer(bool useDHash, int threshold)
    {
        return new ImageComparer(useDHash ? new ImageComparisonSettings { DHashThreshold = threshold } : new ImageComparisonSettings { PHashThreshold = threshold });
    }

    private static Image CreateSolidImage(int width, int height, uint color)
    {
        var pixels = new Argb[width * height];
        pixels.AsSpan().Fill(new Argb(color));
        return Image.Create(width, height, pixels);
    }

    private static Image FillRectangle(Image image, int x, int y, int width, int height, uint color)
    {
        var pixels = image.Pixels.ToArray();
        for (var row = y; row < y + height; row++)
        {
            pixels.AsSpan(row * image.Width + x, width).Fill(new Argb(color));
        }

        return Image.Create(image.Width, image.Height, pixels);
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_RejectsDifferentDimensions()
    {
        var expected = CreateSnapshotData(Image.Create(1, 1, [new Argb(0xFFFFFFFFu)]));
        var actual = CreateSnapshotData(Image.Create(2, 2, [new Argb(0xFFFFFFFFu), new Argb(0xFFFFFFFFu), new Argb(0xFFFFFFFFu), new Argb(0xFFFFFFFFu)]));
        var comparer = new ImageComparer(new ImageComparisonSettings
        {
            SimilarityThreshold = 0,
            DHashThreshold = 0,
        });

        Assert.False(comparer.Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithMultipleThresholds_RequiresAllToPass()
    {
        var expected = CreateSnapshotData(CreatePatternImage(width: 32, height: 32, inverted: false));
        var actual = CreateSnapshotData(CreatePatternImage(width: 32, height: 32, inverted: true));
        var comparer = new ImageComparer(new ImageComparisonSettings
        {
            DHashThreshold = 64,
            PHashThreshold = 0,
        });

        Assert.False(comparer.Equals(expected, actual));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void ImageComparisonSettings_RejectsInvalidHashThresholds(int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageComparisonSettings { DHashThreshold = threshold });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageComparisonSettings { PHashThreshold = threshold });
    }

    [Fact]
    public void ImageHash_ComputesKnownDHash()
    {
        var ascendingPixels = Enumerable.Range(0, 9 * 8).Select(index => new Argb(0xFF000000u | (uint)(index % 9 * 16) * 0x00010101u)).ToArray();
        var descendingPixels = Enumerable.Range(0, 9 * 8).Select(index => new Argb(0xFF000000u | (uint)((8 - index % 9) * 16) * 0x00010101u)).ToArray();

        Assert.Equal(0UL, ImageHash.ComputeDHash(Image.Create(9, 8, ascendingPixels)));
        Assert.Equal(ulong.MaxValue, ImageHash.ComputeDHash(Image.Create(9, 8, descendingPixels)));
    }

    [Fact]
    public void ImageHash_ComputesKnownPHash()
    {
        var image = CreatePatternImage(width: 32, height: 32, inverted: false);

        Assert.Equal(11580269642849678823UL, ImageHash.ComputePHash(image));
    }

    // The expected hashes come from an independent Python implementation that computes the area-weighted thumbnail
    // with arbitrary-precision integers. 13×5 is smaller than the pHash thumbnail, so each pixel spans several cells.
    [Theory]
    [InlineData(1000, 700, 1125899973951492UL, 48225311559189755UL)]
    [InlineData(13, 5, 872590157973323776UL, 14782499366000441473UL)]
    public void ImageHash_MatchesAReferenceImplementation(int width, int height, ulong expectedDHash, ulong expectedPHash)
    {
        var pixels = new Argb[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = new Argb(255, (byte)((x * 17 + y * 31 + x * y * 3) % 256), (byte)((x * 7 + y * 13) % 256), (byte)((x * x + y * 5) % 256));
            }
        }

        var image = Image.Create(width, height, pixels);
        Assert.Equal(expectedDHash, ImageHash.ComputeDHash(image));
        Assert.Equal(expectedPHash, ImageHash.ComputePHash(image));
    }

    [Fact]
    public void ImageHash_FlatImagesHaveNoPHashNoise()
    {
        // Only the DC coefficient is above the median; every AC coefficient is zero up to rounding
        Assert.Equal(1UL, ImageHash.ComputePHash(CreateSolidImage(37, 23, 0xFF818080u)));
        Assert.Equal(0UL, ImageHash.ComputeDHash(CreateSolidImage(1000, 7, 0xFF818080u)));
    }

    [Fact]
    public void ImageHash_UsesEverySourcePixel()
    {
        // With area averaging, darkening a single pixel darkens its thumbnail cell. Bilinear sampling only read
        // the pixels around the center of each cell, and never this one.
        var expected = CreateSolidImage(90, 80, 0xFFFFFFFFu);
        var actual = CreateSolidImage(90, 80, 0xFFFFFFFFu);
        actual = FillRectangle(actual, x: 12, y: 42, width: 1, height: 1, 0xFF000000u);

        Assert.Equal(1, ImageHash.ComputeHammingDistance(ImageHash.ComputeDHash(expected), ImageHash.ComputeDHash(actual)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ImageHash_IgnoresAOneLevelChangeInASinglePixel(bool useDHash, bool gradient)
    {
        // Such a change is invisible (SSIM 1.000000), but it used to flip about half the pHash bits of flat images
        var expected = gradient ? CreateGradientImage(400, 300) : CreateSolidImage(400, 300, 0xFF808080u);
        var pixels = expected.Pixels.ToArray();
        var pixel = pixels[150 * 400 + 200];
        pixels[150 * 400 + 200] = new Argb(pixel.A, (byte)(pixel.R + 1), pixel.G, pixel.B);
        var actual = Image.Create(400, 300, pixels);

        Assert.Equal(0, ComputeHashDistance(useDHash, expected, actual));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ImageHash_IgnoresScatteredOneLevelNoise(bool useDHash, bool gradient)
    {
        // 109 pixels changed by ±1 used to give a dHash distance of 25
        var expected = gradient ? CreateGradientImage(400, 300) : CreateSolidImage(400, 300, 0xFF808080u);
        var pixels = expected.Pixels.ToArray();
        for (var i = 0; i < 109; i++)
        {
            // Scattered over the whole image, a few pixels per thumbnail cell
            var index = (int)((i * 1_103_515_245L + 12_345) % pixels.Length);
            var pixel = pixels[index];
            var delta = i % 2 is 0 ? 1 : -1;

            // The images are gray, and clamping keeps black and white pixels from wrapping around
            var value = (byte)Math.Clamp(pixel.R + delta, 0, 255);
            pixels[index] = new Argb(pixel.A, value, value, value);
        }

        var actual = Image.Create(400, 300, pixels);

        Assert.InRange(ComputeHashDistance(useDHash, expected, actual), 0, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageHash_DifferentStructuresAreFarApart(bool useDHash)
    {
        // Same mean luminance, so the distance comes from the hashes only
        var blackLeftHalf = FillRectangle(CreateSolidImage(400, 300, 0xFFFFFFFFu), x: 0, y: 0, width: 200, height: 300, 0xFF000000u);
        var blackRightHalf = FillRectangle(CreateSolidImage(400, 300, 0xFFFFFFFFu), x: 200, y: 0, width: 200, height: 300, 0xFF000000u);
        var pattern = CreatePatternImage(400, 300, inverted: false);
        var invertedPattern = CreatePatternImage(400, 300, inverted: true);
        var gradient = CreateGradientImage(400, 300);

        Assert.True(ComputeHashDistance(useDHash, blackLeftHalf, blackRightHalf) >= 4);
        Assert.True(ComputeHashDistance(useDHash, pattern, invertedPattern) >= 16);
        Assert.True(ComputeHashDistance(useDHash, pattern, gradient) >= 16);
    }

    private static int ComputeHashDistance(bool useDHash, Image expected, Image actual)
    {
        return useDHash ? ImageHash.ComputeDHashDistance(expected, actual) : ImageHash.ComputePHashDistance(expected, actual);
    }

    private static Image CreateGradientImage(int width, int height)
    {
        var pixels = new Argb[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)(x * 255 / (width - 1));
                pixels[y * width + x] = new Argb(255, value, value, value);
            }
        }

        return Image.Create(width, height, pixels);
    }

    [Fact]
    public void Validate_CreatesActualFileWhenSnapshotChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        var expectedPath = directory.GetFullPath("snapshot.verified.txt");
        var actualPath = directory.GetFullPath("snapshot.actual.txt");

        File.WriteAllText(expectedPath, "expected");
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.True(File.Exists(actualPath));
        Assert.Equal("actual", File.ReadAllText(actualPath));
    }

    // Replaces the process-wide retry notification, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void Validate_RetriesWhenActualFileIsLocked()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        var expectedPath = directory.GetFullPath("snapshot.verified.txt");
        var actualPath = directory.GetFullPath("snapshot.actual.txt");
        File.WriteAllText(expectedPath, "expected");
        File.WriteAllText(actualPath, "locked");

        var retryCount = 0;
        using var lockStream = new FileStream(actualPath, FileMode.Open, FileAccess.Read, FileShare.None);

        // The lock is released when the first attempt fails, so the retry succeeds whatever the load of the machine.
        SnapshotUpdateStrategy.RetryingFileOperation = _ =>
        {
            retryCount++;
            lockStream.Dispose();
        };
        try
        {
            Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
        }
        finally
        {
            SnapshotUpdateStrategy.RetryingFileOperation = null;
        }

        Assert.Equal(1, retryCount);
        Assert.Equal("actual", File.ReadAllText(actualPath));
    }

    [Fact]
    public void Validate_ErrorMessageIncludesVerifiedAndActualPaths_ForAllChangedFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.txt"),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));

        var verifiedPath0 = directory.GetFullPath("snapshot_0.verified.txt");
        var verifiedPath1 = directory.GetFullPath("snapshot_1.verified.txt");
        File.WriteAllText(verifiedPath0, "old_0");
        File.WriteAllText(verifiedPath1, "old_1");

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
        var actualPath0 = directory.GetFullPath("snapshot_0.actual.txt");
        var actualPath1 = directory.GetFullPath("snapshot_1.actual.txt");

        Assert.Contains("Snapshots do not match.", exception.Message);
        Assert.Contains("Verified: " + verifiedPath0.Value, exception.Message);
        Assert.Contains("Actual:   " + actualPath0.Value, exception.Message);
        Assert.Contains("Verified: " + verifiedPath1.Value, exception.Message);
        Assert.Contains("Actual:   " + actualPath1.Value, exception.Message);
        Assert.Contains("Resolution guidance:", exception.Message);
        Assert.Contains("If the new behavior is correct, copy each .actual file to its .verified file.", exception.Message);
        Assert.Contains("To update snapshots automatically, re-run the test with SNAPSHOTTESTING_STRATEGY=Overwrite (or OverwriteWithoutFailure).", exception.Message);
        Assert.True(File.Exists(actualPath0));
        Assert.True(File.Exists(actualPath1));
    }

    // Sets the process-wide SNAPSHOTTESTING_STRATEGY variable, which every 'new SnapshotSettings()' reads, so it must not run beside any other test
    [Theory(DisableParallelization = true)]
    [InlineData("DISALLOW", nameof(SnapshotUpdateStrategy.Disallow))]
    [InlineData("overwrite", nameof(SnapshotUpdateStrategy.Overwrite))]
    [InlineData("mErGeToOlSyNc", nameof(SnapshotUpdateStrategy.MergeToolSync))]
    [InlineData("OverwriteWithoutFailure", nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void SnapshotUpdateStrategy_Default_CanBeConfiguredUsingEnvironmentVariable(string value, string expectedStrategyName)
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, value);

        var settings = new SnapshotSettings();

        Assert.Same(GetSnapshotUpdateStrategy(expectedStrategyName), settings.SnapshotUpdateStrategy);
    }

    // Sets the process-wide SNAPSHOTTESTING_STRATEGY variable, which every 'new SnapshotSettings()' reads, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void SnapshotUpdateStrategy_Default_InvalidEnvironmentVariableValue_UsesDisallow()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, "invalid");

        var settings = new SnapshotSettings();

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    // Sets the process-wide SNAPSHOTTESTING_STRATEGY variable, which every 'new SnapshotSettings()' reads, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void SnapshotUpdateStrategy_ExplicitSetting_HasPriorityOverEnvironmentVariable()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, nameof(SnapshotUpdateStrategy.Overwrite));

        var settings = new SnapshotSettings()
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void Validate_SucceedsWhenMultipleSnapshotFilesMatch()
    {
        using var directory = TemporaryDirectory.Create();
        var baseSettings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotPathStrategy = context => directory / ("snapshot_fixed_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + (context.Extension ?? "txt")),
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
        };

        ValidateWithSerializerCount(baseSettings, count: 2);

        var validateSettings = baseSettings with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        ValidateWithSerializerCount(validateSettings, count: 2);
    }

    [Fact]
    public void ScrubLinesMatching_Regex()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesMatching(Line2Regex());
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesMatching_Pattern()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesMatching("Line[2]");
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesContaining_StringComparison_OrdinalIgnoreCase()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, "line2");
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesContaining_StringComparison_Ordinal()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesContaining(StringComparison.Ordinal, "line2");
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line2", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesWithReplace()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesWithReplace(line => line.ToLowerInvariant());
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["line1", "line2", "line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesWithReplace_RemoveLine()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesWithReplace(line => line == "Line2" ? null : line);
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Theory]
    [InlineData("a\nb\n", "// a\n// b\n")]
    [InlineData("a\r\nb\r\n", "// a\r\n// b\r\n")]
    [InlineData("a\nb", "// a\n// b")]
    [InlineData("a\r\rb", "// a\r// \r// b")]
    [InlineData("\n", "// \n")]
    [InlineData("", "")]
    [InlineData("a\u2028b\fc\u0085d\u2029e", "// a\u2028b\fc\u0085d\u2029e")]
    public void ScrubLinesWithReplace_SplitsOnlyOnLineBreaks(string text, string expected)
    {
        var settings = new SnapshotSettings();
        settings.ScrubLinesWithReplace(line => "// " + line);

        Assert.Equal(expected, Assert.Single(settings.Scrubbers).Scrub(text));
    }

    [Theory]
    [InlineData("Name: x\u2028Password: y\nOther", "Other")]
    [InlineData("Line1\nName: x\n", "Line1\n")]
    [InlineData("Line1\nName: x", "Line1\n")]
    public void ScrubLinesContaining_RemovesWholeLines(string text, string expected)
    {
        var settings = new SnapshotSettings();
        settings.ScrubLinesContaining(StringComparison.Ordinal, "Name");

        Assert.Equal(expected, Assert.Single(settings.Scrubbers).Scrub(text));
    }

    [Theory]
    [InlineData("/home/runner/work", "/home/TheUserName/work")]
    [InlineData(@"C:\Users\Runner\.nuget", @"C:\Users\TheUserName\.nuget")]
    [InlineData("runner@host runner", "TheUserName@host TheUserName")]
    [InlineData("Built by runner.", "Built by TheUserName.")]
    [InlineData("xunit.runner.visualstudio", "xunit.runner.visualstudio")]
    [InlineData("runners test-runner runner_1 runner-2 myrunner", "runners test-runner runner_1 runner-2 myrunner")]
    [InlineData("", "")]
    public void ScrubUserName_ReplacesOnlyWholeWords(string text, string expected)
    {
        var settings = new SnapshotSettings();
        SnapshotSettingsScrubberExtensions.AddWholeWordScrubber(settings, "runner", "TheUserName");

        Assert.Equal(expected, Assert.Single(settings.Scrubbers).Scrub(text));
    }

    [Fact]
    public void ScrubUserName_ReplacesTheCurrentUserName()
    {
        var userNameSettings = new SnapshotSettings();
        userNameSettings.ScrubUserName();
        Assert.Equal("user TheUserName end", Assert.Single(userNameSettings.Scrubbers).Scrub($"user {Environment.UserName} end"));

        var machineNameSettings = new SnapshotSettings();
        machineNameSettings.ScrubMachineName();
        Assert.Equal("host TheMachineName end", Assert.Single(machineNameSettings.Scrubbers).Scrub($"host {Environment.MachineName} end"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ScrubUserName_DoesNothingWhenTheNameIsEmpty(string? name)
    {
        var settings = new SnapshotSettings();
        SnapshotSettingsScrubberExtensions.AddWholeWordScrubber(settings, name, "TheUserName");

        Assert.Empty(settings.Scrubbers);
    }

    [Fact]
    public void Scrub_Guid()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ConfigureHumanReadableSerializer(options => options.ScrubGuid());
        var guids = new[]
        {
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("6ff5182f-7644-4bc1-a3a4-38092cb3663a"),
            Guid.Empty,
        };

        Snapshot.Validate(guids, settings);

        Assert.Equal(
        [
            "- 00000000-0000-0000-0000-000000000001",
            "- 00000000-0000-0000-0000-000000000001",
            "- 00000000-0000-0000-0000-000000000002",
            "- 00000000-0000-0000-0000-000000000000",
        ], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void Scrub_UseRelativeTimeSpan()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        var origin = TimeSpan.FromSeconds(1);
        settings.ConfigureHumanReadableSerializer(options => options.UseRelativeTimeSpan(origin));
        var values = new[]
        {
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

        Snapshot.Validate(values, settings);

        Assert.Equal(
        [
            "- -00:00:01",
            "- 00:00:00",
            "- 00:00:01",
        ], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void Scrub_UseRelativeDateTime()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        var origin = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        settings.ConfigureHumanReadableSerializer(options => options.UseRelativeDateTime(origin));
        var values = new[]
        {
            origin,
            origin.AddSeconds(1),
            origin.AddSeconds(2),
        };

        Snapshot.Validate(values, settings);

        Assert.Equal(
        [
            "- 00:00:00",
            "- 00:00:01",
            "- 00:00:02",
        ], ReadVerifiedSnapshotLines(directory));
    }

    [Theory]
    [InlineData("line 1\r\nline\t2", "line 1␍␊\nline␉2")]
    [InlineData("a b\tc ", "a b␉c␠")]
    [InlineData("a\u00A0b", "a<U+00A0>b")]
    public void ShowInvisibleCharactersInValues(string value, string expected)
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ConfigureHumanReadableSerializer(options => options.ShowInvisibleCharactersInValues = true);

        Snapshot.Validate(value, settings);

        Assert.Equal(expected, File.ReadAllText(directory / "snapshot.verified.txt"));
    }

    [Theory]
    [InlineData("ping", "ping", "")]
    [InlineData("ping ", "ping", "")]
    [InlineData("ping a b", "ping", "a b")]
    [InlineData("\"ping\"", "ping", "")]
    [InlineData("\"ping\" ", "ping", "")]
    [InlineData("\"ping\" a b", "ping", "a b")]
    public void GitTool_ParseCommand(string value, string command, string arguments)
    {
        Assert.Equal((command, arguments), GitTool.ParseCommandFromConfiguration(value));
    }

    private static void ValidateWithSerializerCount(SnapshotSettings settings, int count)
    {
        settings.Serializers.Add(new FixedCountSerializer(count));
        Snapshot.Validate("sample", settings);
    }

    private sealed class FixedCountSerializer(int count) : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
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

    private static SnapshotSettings CreateDeterministicSnapshotSettings(TemporaryDirectory directory, string serializedValue)
    {
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = _ => directory / "snapshot.verified.txt",
        };

        settings.Serializers.Add(new FixedValueSerializer(serializedValue));
        return settings;
    }

    private static SnapshotSettings CreateScrubberSnapshotSettings(TemporaryDirectory directory)
    {
        return new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = _ => directory / "snapshot.verified.txt",
        };
    }

    private static string[] ReadVerifiedSnapshotLines(TemporaryDirectory directory)
    {
        var path = directory / "snapshot.verified.txt";
        return File.ReadAllLines(path);
    }

    private static SnapshotUpdateStrategy GetSnapshotUpdateStrategy(string name)
    {
        return name switch
        {
            nameof(SnapshotUpdateStrategy.Disallow) => SnapshotUpdateStrategy.Disallow,
            nameof(SnapshotUpdateStrategy.MergeTool) => SnapshotUpdateStrategy.MergeTool,
            nameof(SnapshotUpdateStrategy.MergeToolSync) => SnapshotUpdateStrategy.MergeToolSync,
            nameof(SnapshotUpdateStrategy.Overwrite) => SnapshotUpdateStrategy.Overwrite,
            nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure) => SnapshotUpdateStrategy.OverwriteWithoutFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
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

    private static byte[] CreatePng2x2(uint topLeft, uint topRight, uint bottomLeft, uint bottomRight)
    {
        return ImageTestData.CreatePngRgba32(width: 2, height: 2, pixels: [topLeft, topRight, bottomLeft, bottomRight]);
    }

    private static byte[] CreateSingleFramePng(uint color = 0xFFFFFFFFu)
    {
        return ImageTestData.CreatePngRgba32(width: 1, height: 1, pixels: [color]);
    }

    private static Image CreatePatternImage(int width, int height, bool inverted)
    {
        var pixels = new Argb[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)((x * 17 + y * 31 + x * y * 3) % 256);
                if (inverted)
                    value = (byte)(255 - value);

                pixels[y * width + x] = new Argb(255, value, value, value);
            }
        }

        return Image.Create(width, height, pixels);
    }

    private static SnapshotData CreateSnapshotData(Image image)
    {
        return new SnapshotData("png", PngImageEncoder.Encode(image));
    }

    private static byte[] CreateTwoEntryIco()
    {
        return ImageTestData.CreateIcoWithPngEntries(CreateSingleFramePng(), CreateSingleFramePng(color: 0xFF000000u));
    }

    private sealed class SnapshotTypeSerializer(string? extension) : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            if (type != SnapshotType.Png)
            {
                result = null;
                return false;
            }

            result = new SerializedSnapshot([new SnapshotData(extension, Encoding.UTF8.GetBytes(type.Type))]);
            return true;
        }
    }

    private sealed class FixedValueSerializer(string value) : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value_, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            if (type != SnapshotType.Default)
            {
                result = null;
                return false;
            }

            result = new SerializedSnapshot([new SnapshotData("txt", Encoding.UTF8.GetBytes(value))]);
            return true;
        }
    }

    [Fact]
    public void DefaultSnapshotPath_IsStableWhenTheAssertionMoves()
    {
        var settings = new SnapshotSettings();
        var sourceFilePath = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "SampleTests.cs"));
        var methodName = "SampleTest" + new string('a', 200);

        SnapshotPathContext CreateContext(int lineNumber) => new(
            sourceFilePath,
            "SampleTests",
            methodName,
            lineNumber,
            SnapshotType.Default,
            Index: 0,
            Extension: "txt",
            TestContext: null,
            settings);

        var beforeTheEdit = settings.SnapshotPathStrategy(CreateContext(12));
        var afterTheEdit = settings.SnapshotPathStrategy(CreateContext(4711));

        Assert.Equal(beforeTheEdit, afterTheEdit);
        Assert.Matches(SnapshotNameWithHashSuffixRegex(), beforeTheEdit.Name);
    }

    [GeneratedRegex("_[0-9a-f]{8}\\.verified\\.txt$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnapshotNameWithHashSuffixRegex();

    [Fact]
    public void GitTool_GetGitConfiguration_ReadsTheValueWithoutDeadlocking()
    {
        global::Xunit.Assert.SkipWhen(ExecutableFinder.GetFullExecutablePath("git") is null, "git is not installed.");

        using var directory = TemporaryDirectory.Create();
        RunGit(directory.FullPath, "init");
        RunGit(directory.FullPath, "config", "difftool.sample.cmd", "sample $LOCAL $REMOTE");

        Assert.Equal("sample $LOCAL $REMOTE", TestGitTool.Read(directory.FullPath, "difftool.sample.cmd"));
        Assert.Null(TestGitTool.Read(directory.FullPath, "difftool.missing.cmd"));

        static void RunGit(string workingDirectory, params string[] arguments)
        {
            var psi = new ProcessStartInfo(ExecutableFinder.GetFullExecutablePath("git")!)
            {
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
    }

    private sealed class TestGitTool : GitTool
    {
        public override MergeToolResult? Start(string currentFilePath, string newFilePath) => null;

        public static string? Read(string? workingDirectory, string key) => GetGitConfiguration(workingDirectory, key);

        public static ProcessStartInfo CreateStartInfo(string command, string? workingDirectory, IReadOnlyList<KeyValuePair<string, string>> variables) => CreateCommandStartInfo(command, workingDirectory, variables);

        public static ProcessStartInfo CreateStartInfoWithoutShell(string command, IReadOnlyList<KeyValuePair<string, string>> variables) => CreateCommandStartInfoWithoutShell(command, variables);

        public static (string Command, string Arguments) Expand(string command, IReadOnlyList<KeyValuePair<string, string>> variables) => ExpandCommandWithoutShell(command, variables);
    }

    [Fact]
    public void Validate_UsesTheComparerRegisteredForTheStoredFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var gif = CreateTwoFrameGif();

        // The GIF serializer stores PNG frames, so the comparison is about PNG bytes even though the
        // assertion asked for SnapshotType.Gif.
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.png"),
        };
        settings.Serializers.AddGifSerializer();

        Snapshot.Validate(gif, SnapshotType.Gif, settings);

        var comparer = new RecordingSnapshotComparer();
        var compareSettings = settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        compareSettings.Comparers.Set(SnapshotType.Png, comparer);

        Snapshot.Validate(gif, SnapshotType.Gif, compareSettings);

        Assert.True(comparer.InvocationCount > 0, "The comparer registered for PNG was not used.");
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("ico")]
    public void Validate_ComparesGifAndIcoFramesByPixelsByDefault(string format)
    {
        using var directory = TemporaryDirectory.Create();
        var snapshotType = SnapshotType.Create(format);
        var payload = format is "gif" ? CreateTwoFrameGif() : CreateTwoEntryIco();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.png"),
        };
        settings.Serializers.AddGifSerializer();
        settings.Serializers.AddIcoSerializer();
        Snapshot.Validate(payload, snapshotType, settings);

        // Rewrite each frame with the same pixels but stored deflate blocks, as another zlib could compress them
        var framePaths = Directory.GetFiles(directory.FullPath, "*.verified.png");
        Assert.HasCount(2, framePaths);
        foreach (var framePath in framePaths)
        {
            var encodedFrame = File.ReadAllBytes(framePath);
            var frame = Image.Load(encodedFrame);
            var storedFrame = ImageTestData.CreatePngRgba32(frame.Width, frame.Height, [.. frame.Pixels.ToArray().Select(pixel => pixel.PackedValue)], compressionLevel: CompressionLevel.NoCompression);
            Assert.NotEqual(encodedFrame, storedFrame);
            File.WriteAllBytes(framePath, storedFrame);
        }

        Snapshot.Validate(payload, snapshotType, settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow });

        var byteSettings = settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        byteSettings.Comparers.Set(snapshotType, new RecordingSnapshotComparer());
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate(payload, snapshotType, byteSettings));
    }

    [Fact]
    public void Validate_FallsBackToTheComparerForTheRequestedType()
    {
        using var directory = TemporaryDirectory.Create();
        var comparer = new RecordingSnapshotComparer();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        settings.Comparers.Set(SnapshotType.Default, comparer);
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "actual");

        Snapshot.Validate("sample", settings);

        Assert.True(comparer.InvocationCount > 0, "The comparer registered for the requested type was not used.");
    }

    private sealed class RecordingSnapshotComparer : ISnapshotComparer
    {
        public int InvocationCount { get; private set; }

        public bool Equals(SnapshotData expected, SnapshotData actual)
        {
            InvocationCount++;
            return expected.Data.AsSpan().SequenceEqual(actual.Data);
        }
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }

    [Fact]
    public void Serializers_TheLastRegisteredSerializerWins()
    {
        var settings = new SnapshotSettings();
        settings.Serializers.Add(new FixedSnapshotSerializer("first"));
        settings.Serializers.Add(new FixedSnapshotSerializer("second"));

        var data = Assert.Single(settings.Serializers.Serialize(SnapshotType.None, new SerializerProbe()).Data);
        Assert.Equal("second", Encoding.UTF8.GetString(data.Data));
    }

    [Fact]
    public void ConfigureHumanReadableSerializer_ReplacesTheSerializerInPlace()
    {
        var settings = new SnapshotSettings();
        var initialCount = settings.Serializers.Count;
        var initialOrder = settings.Serializers.Select(serializer => serializer.GetType()).ToList();

        settings.ConfigureHumanReadableSerializer(options => options.MaxDepth = 3);

        Assert.Equal(initialCount, settings.Serializers.Count);
        Assert.Equal(initialOrder, settings.Serializers.Select(serializer => serializer.GetType()).ToList());
        Assert.Equal(3, settings.Serializers.OfType<HumanReadableSnapshotSerializer>().Single().Options.MaxDepth);
    }

    [Fact]
    public async Task AddConverter_ConcurrentCallsAllApplyAndKeepTheSerializerOrder()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var settings = new SnapshotSettings();
            var initialOrder = settings.Serializers.Select(serializer => serializer.GetType()).ToList();
            var converters = Enumerable.Range(0, 64).Select(_ => new ProbeConverter()).ToArray();

            using (var barrier = new Barrier(Math.Max(4, Environment.ProcessorCount)))
            {
                var tasks = Enumerable.Range(0, barrier.ParticipantCount).Select(participant => Task.Factory.StartNew(() =>
                {
                    barrier.SignalAndWait(XunitCancellationToken);
                    for (var i = participant; i < converters.Length; i += barrier.ParticipantCount)
                    {
                        settings.AddConverter(converters[i]);
                    }
                }, XunitCancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

                await Task.WhenAll(tasks);
            }

            Assert.Equal(initialOrder, settings.Serializers.Select(serializer => serializer.GetType()).ToList());
            var options = Assert.Single(settings.Serializers.OfType<HumanReadableSnapshotSerializer>()).Options;
            Assert.HasCount(converters.Length, options.Converters.OfType<ProbeConverter>());
            foreach (var converter in converters)
            {
                Assert.Contains(converter, options.Converters);
            }

            // A HumanReadable serializer appended after the binary serializers would serialize byte arrays as text.
            var bytes = "binary-data"u8.ToArray();
            var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Png, bytes).Data);
            Assert.Equal(bytes, snapshot.Data);
        }
    }

    private sealed class ProbeConverter : Meziantou.Framework.HumanReadable.HumanReadableConverter<SerializerProbe>
    {
        protected override void WriteValue(Meziantou.Framework.HumanReadable.HumanReadableTextWriter writer, SerializerProbe? value, Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions options)
        {
            writer.WriteValue("probe");
        }
    }

    [Fact]
    public void Serializers_SerializeIsNotBrokenByAConcurrentRegistration()
    {
        var settings = new SnapshotSettings();
        RunConcurrently(
            writer: () =>
            {
                var serializer = new NeverMatchingSnapshotSerializer();
                settings.Serializers.Add(serializer);
                settings.Serializers.Remove(serializer);
                settings.ConfigureHumanReadableSerializer(options => options.MaxDepth = 32);
            },
            reader: () => settings.Serializers.Serialize(SnapshotType.None, new { A = 1 }));
    }

    [Fact]
    public void Comparers_GetIsNotBrokenByAConcurrentRegistration()
    {
        var settings = new SnapshotSettings();
        RunConcurrently(
            writer: () =>
            {
                settings.Comparers.Set(SnapshotType.Png, ByteArraySnapshotComparer.Instance);
                settings.Comparers.Remove(SnapshotType.Png);
            },
            reader: () =>
            {
                _ = settings.Comparers.Get(SnapshotType.Png);
                _ = settings.Comparers.ToList();
            });
    }

    [Fact]
    public void Scrubbers_EnumerationIsNotBrokenByAConcurrentRegistration()
    {
        var settings = new SnapshotSettings();
        var scrubber = new LineFilterScrubber(_ => false);
        RunConcurrently(
            writer: () =>
            {
                settings.Scrubbers.Add(scrubber);
                settings.Scrubbers.Remove(scrubber);
            },
            reader: () =>
            {
                foreach (var item in settings.Scrubbers)
                {
                    _ = item;
                }
            });
    }

    [Fact]
    public void Scrubbers_SupportTheFullListContract()
    {
        var settings = new SnapshotSettings();
        var first = new LineFilterScrubber(_ => false);
        var second = new LineFilterScrubber(_ => true);

        settings.Scrubbers.Add(first);
        settings.Scrubbers.Insert(0, second);
        Assert.Equal([second, first], settings.Scrubbers);
        Assert.Equal(0, settings.Scrubbers.IndexOf(second));
        Assert.True(settings.Scrubbers.Contains(first));

        settings.Scrubbers[0] = first;
        Assert.Equal([first, first], settings.Scrubbers);

        settings.Scrubbers.RemoveAt(0);
        Assert.Equal([first], settings.Scrubbers);

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Scrubbers.RemoveAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Scrubbers.Insert(2, first));

        settings.Scrubbers.Clear();
        Assert.Empty(settings.Scrubbers);
    }

    /// <summary>Runs <paramref name="reader"/> on the current thread while <paramref name="writer"/> keeps mutating the same settings.</summary>
    private static void RunConcurrently(Action writer, Action reader)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var writerTask = Task.Run(() =>
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                writer();
            }
        });

        try
        {
            for (var i = 0; i < 20_000; i++)
            {
                reader();
            }
        }
        finally
        {
            cancellationTokenSource.Cancel();
        }

        writerTask.GetAwaiter().GetResult();
    }

    private sealed class SerializerProbe;

    private sealed class NeverMatchingSnapshotSerializer : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            result = null;
            return false;
        }
    }

    private sealed class FixedSnapshotSerializer(string content) : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            if (value is not SerializerProbe)
            {
                result = null;
                return false;
            }

            result = new SerializedSnapshot([new SnapshotData("txt", Encoding.UTF8.GetBytes(content))]);
            return true;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnapshotNameWithIndexRegex();

    [GeneratedRegex("_[0-9a-f]{8}_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnapshotNameHashWithIndexFragmentRegex();

    [GeneratedRegex("^[A-Za-z0-9._-]+\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnapshotNameRegex();

    [GeneratedRegex("^[A-Za-z0-9._-]+_[0-9a-f]{8}_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SnapshotNameWithHashAndIndexRegex();

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool))]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync))]
    public void Validate_MergeToolStrategy_DoesNotLeaveAnEmptyVerifiedFileWhenTheMergeToolCannotStart(string strategyName)
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = GetSnapshotUpdateStrategy(strategyName),
            MergeTools = [new UnavailableMergeTool()],
        };

        Assert.Throws<SnapshotException>(() => Snapshot.Validate("value", settings));

        // An empty verified file would become the expectation of the test on the next run.
        Assert.False(File.Exists(directory / "snapshot.verified.txt"));
        Assert.Equal("sample", File.ReadAllText(directory / "snapshot.actual.txt"));
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool))]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync))]
    public void Validate_MergeToolStrategy_KeepsTheVerifiedFileWhenTheMergeToolCannotStart(string strategyName)
    {
        using var directory = TemporaryDirectory.Create();
        var verifiedPath = directory / "snapshot.verified.txt";
        File.WriteAllText(verifiedPath, "recorded");

        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = GetSnapshotUpdateStrategy(strategyName),
            MergeTools = [new UnavailableMergeTool()],
        };

        Assert.Throws<SnapshotException>(() => Snapshot.Validate("value", settings));

        Assert.Equal("recorded", File.ReadAllText(verifiedPath));
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool), false)]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool), true)]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync), false)]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync), true)]
    public void Validate_MergeToolStrategy_WithoutMergeTools_ReportsTheSnapshotDifference(string strategyName, bool emptyList)
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = GetSnapshotUpdateStrategy(strategyName),
            MergeTools = emptyList ? [] : null,
        };

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("value", settings));

        Assert.StartsWith("Snapshots do not match.", exception.Message);
        Assert.False(File.Exists(directory / "snapshot.verified.txt"));
        Assert.Equal("sample", File.ReadAllText(directory / "snapshot.actual.txt"));
    }

    [Fact]
    public void VerifiedFilePlaceholder_DeletesTheFileWhenTheMergeToolDidNotWriteIt()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.txt";

        var placeholder = VerifiedFilePlaceholder.TryCreate(path);

        Assert.NotNull(placeholder);
        Assert.Empty(File.ReadAllBytes(path));

        placeholder.DeleteIfUnused();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void VerifiedFilePlaceholder_KeepsTheFileWhenTheMergeToolWroteIt()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.txt";

        var placeholder = VerifiedFilePlaceholder.TryCreate(path);
        Assert.NotNull(placeholder);
        File.WriteAllText(path, "merged");

        placeholder.DeleteIfUnused();

        Assert.Equal("merged", File.ReadAllText(path));
    }

    [Fact]
    public void VerifiedFilePlaceholder_KeepsTheFileWhenTheMergeToolSavedAnEmptySnapshot()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.txt";

        var placeholder = VerifiedFilePlaceholder.TryCreate(path);
        Assert.NotNull(placeholder);

        // An empty snapshot is a meaningful expectation, so saving one must not be mistaken for an
        // untouched placeholder.
        File.WriteAllBytes(path, []);

        placeholder.DeleteIfUnused();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void VerifiedFilePlaceholder_DoesNotTouchAnExistingVerifiedFile()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.txt";
        File.WriteAllText(path, "recorded");

        Assert.Null(VerifiedFilePlaceholder.TryCreate(path));
        Assert.Equal("recorded", File.ReadAllText(path));
    }

    [Fact]
    public void Validate_MergeToolStrategy_DoesNotDeleteTheVerifiedFilesTheAssertionNoLongerProduces()
    {
        using var directory = TemporaryDirectory.Create();
        var mergeTool = new RecordingMergeTool();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
            MergeTools = [mergeTool],
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        var obsoletePath = directory.GetFullPath("snapshot.verified.txt");
        File.WriteAllText(obsoletePath, "value_0");

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        // The developer may reject the merge, so the change must not be applied behind their back.
        Assert.Contains(obsoletePath.Value, exception.Message);
        Assert.Equal("value_0", File.ReadAllText(obsoletePath));
        Assert.Equal(2, mergeTool.StartCount);
    }

    [Fact]
    public void Validate_MergeToolStrategy_TriesTheNextMergeToolWhenOneFailsToStart()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(directory / "snapshot.verified.txt", "recorded");
        var mergeTool = new RecordingMergeTool();
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
            MergeTools = [new ThrowingMergeTool(), mergeTool],
        };

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("value", settings));

        Assert.StartsWith("Snapshots do not match.", exception.Message);
        Assert.Equal(1, mergeTool.StartCount);
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool))]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync))]
    public void Validate_MergeToolStrategy_ReportsTheSnapshotAndTheFailuresWhenNoMergeToolStarts(string strategyName)
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(directory / "snapshot.verified.txt", "recorded");
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = GetSnapshotUpdateStrategy(strategyName),
            MergeTools = [new ThrowingMergeTool()],
        };

        var exception = Assert.Throws<SnapshotException>(() => Snapshot.Validate("value", settings));

        Assert.Contains("  * Verified: " + (directory / "snapshot.verified.txt").Value, exception.Message);
        Assert.Contains("    Actual:   " + (directory / "snapshot.actual.txt").Value, exception.Message);
        Assert.Contains("  - BrokenMergeTool: The merge tool is broken.", exception.Message);
        Assert.Contains("SnapshotSettings.MergeTools", exception.Message);
        Assert.Contains("DiffEngine_Tool", exception.Message);
        Assert.Contains("DiffEngine_Disabled", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    // Sets the process-wide DiffEngine_Disabled variable, so it must not run beside any other test
    [Theory(DisableParallelization = true)]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool), "true")]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool), "1")]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync), "TRUE")]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync), "1")]
    public void Validate_MergeToolStrategy_WhenDiffToolsAreDisabled_ReportsTheSnapshotDifference(string strategyName, string value)
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value);
        using var directory = TemporaryDirectory.Create();
        var mergeTool = new RecordingMergeTool();
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = GetSnapshotUpdateStrategy(strategyName),
            MergeTools = [mergeTool],
        };

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("value", settings));

        Assert.StartsWith("Snapshots do not match.", exception.Message);
        Assert.Contains("Resolution guidance:", exception.Message);
        Assert.Equal(0, mergeTool.StartCount);
        Assert.False(File.Exists(directory / "snapshot.verified.txt"));
    }

    // Sets the process-wide DiffEngine_Disabled variable, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void MergeTool_IsDisabled_IgnoresTheDetectedEnvironmentsWhenAutoDetectionIsOff()
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value: null);

        Assert.False(MergeTool.IsDisabled(autoDetectContinuousEnvironment: false));
    }

    // Sets the process-wide DiffEngine_Disabled variable and replaces the environment detection, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void MergeTool_IsDisabled_UsesTheContinuousEnvironmentDetection()
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value: null);
        try
        {
            ContinuousEnvironmentDetector.DescriptionOverride = () => null;
            Assert.False(MergeTool.IsDisabled(autoDetectContinuousEnvironment: true));

            ContinuousEnvironmentDetector.DescriptionOverride = () => "an LLM agent (ClaudeCode)";
            Assert.True(MergeTool.IsDisabled(autoDetectContinuousEnvironment: true));
            Assert.False(MergeTool.IsDisabled(autoDetectContinuousEnvironment: false));
        }
        finally
        {
            ContinuousEnvironmentDetector.DescriptionOverride = null;
        }
    }

    // Sets the process-wide DiffEngine_Disabled variable and replaces the environment detection, so it must not run beside any other test
    [Theory(DisableParallelization = true)]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeTool))]
    [InlineData(nameof(SnapshotUpdateStrategy.MergeToolSync))]
    public void MergeToolStrategy_DoesNotStartTheMergeToolWhenAnEnvironmentIsDetected(string strategyName)
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value: null);
        using var directory = TemporaryDirectory.Create();
        var verifiedPath = directory / "snapshot.verified.txt";
        var actualPath = directory / "snapshot.actual.txt";
        File.WriteAllText(verifiedPath, "recorded");
        File.WriteAllText(actualPath, "sample");
        var mergeTool = new RecordingMergeTool();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = true,
            MergeTools = [mergeTool],
        };

        ContinuousEnvironmentDetector.DescriptionOverride = () => "an LLM agent (ClaudeCode)";
        try
        {
            GetSnapshotUpdateStrategy(strategyName).UpdateFile(settings, verifiedPath, actualPath);
        }
        finally
        {
            ContinuousEnvironmentDetector.DescriptionOverride = null;
        }

        Assert.Equal(0, mergeTool.StartCount);
        Assert.Equal("recorded", File.ReadAllText(verifiedPath));
        Assert.Equal("sample", File.ReadAllText(actualPath));
    }

    [Fact]
    public void Validate_MergeToolSync_KeepsThePlaceholderWhenTheLauncherDoesNotWaitForTheMerge()
    {
        using var directory = TemporaryDirectory.Create();
        var mergeTool = new RecordingMergeTool { WaitsForMerge = false };
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeToolSync,
            MergeTools = [mergeTool],
        };

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("value", settings));

        // The IDE that received the files may still be showing the diff, and saving writes to the verified file.
        Assert.Equal(1, mergeTool.StartCount);
        Assert.True(File.Exists(directory / "snapshot.verified.txt"));
    }

    [Fact]
    public void Validate_MergeToolSync_DeletesTheActualFileWhenTheMergeAcceptedIt()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(directory / "snapshot.verified.txt", "recorded");
        var mergeTool = new RecordingMergeTool { OnStart = (verifiedPath, actualPath) => File.Copy(actualPath, verifiedPath, overwrite: true) };
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeToolSync,
            MergeTools = [mergeTool],
        };

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("value", settings));

        Assert.Equal("sample", File.ReadAllText(directory / "snapshot.verified.txt"));
        Assert.False(File.Exists(directory / "snapshot.actual.txt"));
    }

    [Fact]
    public void Validate_MergeToolSync_KeepsTheActualFileWhenTheMergeRejectedIt()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(directory / "snapshot.verified.txt", "recorded");
        var settings = CreateDeterministicSnapshotSettings(directory, "sample") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeToolSync,
            MergeTools = [new RecordingMergeTool()],
        };

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("value", settings));

        Assert.Equal("recorded", File.ReadAllText(directory / "snapshot.verified.txt"));
        Assert.Equal("sample", File.ReadAllText(directory / "snapshot.actual.txt"));
    }

    // Sets the process-wide DiffEngine_Tool variable, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void DiffToolFromEnvironmentVariable_NamingItself_DoesNotRecurse()
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Tool", nameof(MergeTool.DiffToolFromEnvironmentVariable));

        Assert.Null(MergeToolFromEnvironment.GetTool());
        Assert.Null(MergeTool.DiffToolFromEnvironmentVariable.Start("current.txt", "new.txt"));
    }

    // Sets the process-wide DiffEngine_Tool variable, so it must not run beside any other test
    [Theory(DisableParallelization = true)]
    [InlineData("rider")]
    [InlineData("RIDER")]
    [InlineData(" Rider ")]
    public void DiffToolFromEnvironmentVariable_IgnoresTheCase(string value)
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Tool", value);

        Assert.Same(MergeTool.Rider, MergeToolFromEnvironment.GetTool());
    }

    [Fact]
    public async Task ProcessMergeToolResult_RunsTheCleanupWhenReleasedBeforeTheProcessExits()
    {
        var cleanedUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = ProcessMergeToolResult.Start(CreateShellStartInfo(OperatingSystem.IsWindows() ? "ping -n 2 127.0.0.1 > NUL" : "sleep 1"), onExited: cleanedUp.SetResult);

        // The non-blocking merge tool strategy releases the result right after the tool starts.
        result.Dispose();

        await cleanedUp.Task.WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMergeToolResult_WaitForExit_WorksWhenTheProcessAlreadyExited()
    {
        var cleanedUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var result = ProcessMergeToolResult.Start(CreateShellStartInfo("exit 0"), onExited: cleanedUp.SetResult);
        await cleanedUp.Task.WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        // The exit notification used to dispose the process, and waiting for it then threw.
        result.WaitForExit();
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GitTool_CreateCommandStartInfo_PassesThePathsVerbatim()
    {
        using var directory = TemporaryDirectory.Create();
        var local = directory.GetFullPath("dir with spaces/local 'quoted' \"file\".txt");
        var remote = directory.GetFullPath("dir $HOME `pwd`/remote.txt");
        var output = directory.GetFullPath("output dir/merged.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var startInfo = TestGitTool.CreateStartInfo("""VAR=x printf '%s\n' "$LOCAL" "${REMOTE}" "$BASE" > "$MERGED" && test "$VAR" = "" """, directory.FullPath,
        [
            new("LOCAL", local),
            new("REMOTE", remote),
            new("BASE", local),
            new("MERGED", output),
        ]);

        using (var process = Process.Start(startInfo)!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        string[] expectedLines = [local.Value, remote.Value, local.Value];
        Assert.Equal(expectedLines, File.ReadAllLines(output));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GitTool_CreateCommandStartInfo_UsesTheShellOfGitForWindows()
    {
        global::Xunit.Assert.SkipWhen(ExecutableFinder.GetFullExecutablePath("git") is null, "git is not installed.");

        using var directory = TemporaryDirectory.Create();
        var local = directory.GetFullPath("dir with spaces/local 'quoted' file.txt");
        var remote = directory.GetFullPath("dir $HOME `pwd` & %PATH%/remote.txt");
        var output = directory.GetFullPath("output dir/merged.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var startInfo = TestGitTool.CreateStartInfo("""printf '%s\n' "$LOCAL" "${REMOTE}" "$BASE" > "$MERGED" """, directory.FullPath,
        [
            new("LOCAL", local),
            new("REMOTE", remote),
            new("BASE", local),
            new("MERGED", output),
        ]);

        global::Xunit.Assert.SkipUnless(string.Equals(Path.GetFileName(startInfo.FileName), "sh.exe", StringComparison.OrdinalIgnoreCase), "The shell of Git for Windows cannot be found.");
        using (var process = Process.Start(startInfo)!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        string[] expectedLines = [local.Value, remote.Value, local.Value];
        Assert.Equal(expectedLines, File.ReadAllLines(output));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GitTool_CreateCommandStartInfoWithoutShell_PassesEachPathAsASingleArgument()
    {
        using var directory = TemporaryDirectory.Create();
        var remote = directory.GetFullPath("dir with spaces/remote file.txt");
        var output = directory.GetFullPath("output dir/merged file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(remote)!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(remote, "sample");

        var command = "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /d /c copy /y \"$REMOTE\" $MERGED";
        var startInfo = TestGitTool.CreateStartInfoWithoutShell(command,
        [
            new("REMOTE", remote),
            new("MERGED", output),
        ]);
        startInfo.RedirectStandardOutput = true;

        using (var process = Process.Start(startInfo)!)
        {
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        Assert.Equal("sample", File.ReadAllText(output));
    }

    [Fact]
    public void GitTool_ExpandCommandWithoutShell_QuotesEachPlaceholder()
    {
        var (command, arguments) = TestGitTool.Expand("""
            "C:\Program Files\Tool\tool.exe" '$LOCAL' "$REMOTE" ${BASE} $MERGED $LOCALS /flag
            """,
            [
                new("LOCAL", @"C:\dir with spaces\local.txt"),
                new("REMOTE", @"C:\dir\remote.txt"),
                new("BASE", @"C:\a ""quoted"" dir\base.txt"),
                new("MERGED", @"C:\dir\merged.txt"),
            ]);

        Assert.Equal(@"C:\Program Files\Tool\tool.exe", command);
        Assert.Equal(@"""C:\dir with spaces\local.txt"" C:\dir\remote.txt ""C:\a \""quoted\"" dir\base.txt"" C:\dir\merged.txt $LOCALS /flag", arguments);
    }

    [Fact]
    public void AutoDiffEngineTool_SkipsTerminalTools()
    {
        var selectedTool = AutoDiffEngineTool.SelectTool(".txt",
        [
            (DiffTool.Vim, true, []),
            (DiffTool.Neovim, true, []),
            (DiffTool.Meld, true, []),
            (DiffTool.VisualStudioCode, true, [".svg", ".bin"]),
        ]);

        Assert.Equal(DiffTool.Meld, selectedTool);
    }

    [Fact]
    public void AutoDiffEngineTool_PrefersTheToolDedicatedToTheExtension()
    {
        (DiffTool Tool, bool SupportsText, IEnumerable<string> BinaryExtensions)[] availableTools =
        [
            (DiffTool.Vim, true, [".png"]),
            (DiffTool.Meld, true, []),
            (DiffTool.WinMerge, true, [".png"]),
        ];

        Assert.Equal(DiffTool.WinMerge, AutoDiffEngineTool.SelectTool(".PNG", availableTools));
        Assert.Equal(DiffTool.Meld, AutoDiffEngineTool.SelectTool("", availableTools));
    }

    [Fact]
    public void AutoDiffEngineTool_ReturnsNullWhenOnlyTerminalToolsAreAvailable()
    {
        Assert.Null(AutoDiffEngineTool.SelectTool(".txt", [(DiffTool.Vim, true, []), (DiffTool.Neovim, true, []), (DiffTool.MsWordDiff, false, [".docx"])]));
    }

    [Fact]
    public void DiffEngineTool_CreateStartInfo_StartsAnExecutableWithTheArguments()
    {
        var startInfo = DiffEngineTool.CreateStartInfo(@"C:\Program Files\Tool\tool.exe", @"""C:\R&D\a b.txt"" C:\R&D\c.txt");

        Assert.Equal(@"C:\Program Files\Tool\tool.exe", startInfo.FileName);
        Assert.Equal(@"""C:\R&D\a b.txt"" C:\R&D\c.txt", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }

    [Theory]
    [InlineData(@"C:\Tools\code.cmd")]
    [InlineData(@"C:\Tools\CODE.BAT")]
    public void DiffEngineTool_CreateStartInfo_EscapesTheArgumentsOfABatchFileForCmd(string executablePath)
    {
        var arguments = "--wait --diff " + CommandLineBuilder.WindowsQuotedArguments(@"C:\src\R&D\a b.txt", @"C:\100%\x^y|z.txt", @"C:\dir\""quoted""\", "plain");

        var startInfo = DiffEngineTool.CreateStartInfo(executablePath, arguments);

        Assert.Equal("cmd.exe", Path.GetFileName(startInfo.FileName));
        Assert.False(startInfo.UseShellExecute);
        var expectedCommandLine = CommandLineBuilder.WindowsCmdArguments(executablePath, "--wait", "--diff", @"C:\src\R&D\a b.txt", @"C:\100%\x^y|z.txt", @"C:\dir\""quoted""\", "plain");
        Assert.Equal("/d /e:on /v:off /s /c \"" + expectedCommandLine + "\"", startInfo.Arguments);
        Assert.Contains(@"^""C:\src\R^&D\a b.txt^""", startInfo.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a b")]
    [InlineData("a \"b c\" d")]
    [InlineData(@"C:\dir with spaces\")]
    [InlineData(@"C:\a ""quoted"" dir\b.txt")]
    [InlineData(@"\\server\share\x\\")]
    [InlineData("tab\tseparated")]
    public void DiffEngineTool_SplitWindowsArguments_ReversesWindowsQuoting(string value)
    {
        string[] values = [value, "--diff", value + "suffix"];

        Assert.Equal(values, DiffEngineTool.SplitWindowsArguments(CommandLineBuilder.WindowsQuotedArguments(values)));
    }

    [Fact]
    public void DiffEngineTool_SplitWindowsArguments_HandlesDoubledQuotesAndRepeatedSpaces()
    {
        string[] expected = ["a\"b", "c", @"d\\e"];

        Assert.Equal(expected, DiffEngineTool.SplitWindowsArguments("  \"a\"\"b\"   c\td\\\\e  "));
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GitMergeTool_RunsTheConfiguredCommandAndDeletesTheCopyOfTheVerifiedFile()
    {
        global::Xunit.Assert.SkipWhen(ExecutableFinder.GetFullExecutablePath("git") is null, "git is not installed.");

        using var directory = TemporaryDirectory.Create();
        var repository = directory.GetFullPath("repo with spaces");
        Directory.CreateDirectory(repository);
        RunGitCommand(repository, "init");
        RunGitCommand(repository, "config", "merge.tool", "my tool");
        RunGitCommand(repository, "config", "mergetool.my tool.cmd", """cp "$REMOTE" "$MERGED" && printf '%s' "$LOCAL" > "$MERGED.local" && cmp -s "$LOCAL" "$BASE" """);

        var verifiedPath = repository / "snapshot.verified.txt";
        var actualPath = repository / "snapshot.actual.txt";
        File.WriteAllText(verifiedPath, "recorded");
        File.WriteAllText(actualPath, "sample");

        using (var result = MergeTool.GitMergeTool.Start(verifiedPath, actualPath))
        {
            Assert.NotNull(result);
            result.WaitForExit();
            Assert.Equal(0, ((ProcessMergeToolResult)result).Process.ExitCode);
        }

        Assert.Equal("sample", File.ReadAllText(verifiedPath));
        var copyPath = File.ReadAllText(verifiedPath + ".local");
        Assert.NotEqual(verifiedPath.Value, copyPath);

        // The copy is deleted by the exit notification, which runs on the thread pool.
        var copyDirectory = Path.GetDirectoryName(copyPath)!;
        var stopwatch = Stopwatch.StartNew();
        while (Directory.Exists(copyDirectory) && stopwatch.Elapsed < TimeSpan.FromMinutes(2))
        {
            Thread.Sleep(50);
        }

        Assert.False(Directory.Exists(copyDirectory));
    }

    private static void RunGitCommand(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo(ExecutableFinder.GetFullExecutablePath("git")!)
        {
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static ProcessStartInfo CreateShellStartInfo(string command)
    {
        var startInfo = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe") : new ProcessStartInfo("/bin/sh");
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add(command);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        return startInfo;
    }

    private sealed class RecordingMergeTool : MergeTool
    {
        public int StartCount { get; private set; }

        public bool WaitsForMerge { get; init; } = true;

        public Action<string, string>? OnStart { get; init; }

        public override MergeToolResult? Start(string currentFilePath, string newFilePath)
        {
            StartCount++;
            OnStart?.Invoke(currentFilePath, newFilePath);
            return new CompletedMergeToolResult(WaitsForMerge);
        }
    }

    private sealed class CompletedMergeToolResult(bool waitsForMerge) : MergeToolResult
    {
        public override bool WaitsForMerge => waitsForMerge;

        public override void Dispose()
        {
        }

        public override void WaitForExit()
        {
        }
    }

    private sealed class ThrowingMergeTool : MergeTool
    {
        public override MergeToolResult? Start(string currentFilePath, string newFilePath) => throw new InvalidOperationException("The merge tool is broken.");

        public override string ToString() => "BrokenMergeTool";
    }

    private sealed class UnavailableMergeTool : MergeTool
    {
        public override MergeToolResult? Start(string currentFilePath, string newFilePath) => null;
    }

    [GeneratedRegex("Line[2]", RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex Line2Regex();

    [Fact]
    public void Validate_ForceUpdate_WritesTheCurrentSnapshotOverAStaleActualFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "correct") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            ForceUpdateSnapshots = true,
        };

        var verifiedPath = directory.GetFullPath("snapshot.verified.txt");
        File.WriteAllText(verifiedPath, "correct");
        File.WriteAllText(directory.GetFullPath("snapshot.actual.txt"), "wrong");

        Snapshot.Validate("sample", settings);

        Assert.Equal("correct", File.ReadAllText(verifiedPath));
    }

    [Fact]
    public void Validate_ForceUpdate_SucceedsWhenTheSnapshotMatchesAndNoActualFileExists()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "correct") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            ForceUpdateSnapshots = true,
        };

        var verifiedPath = directory.GetFullPath("snapshot.verified.txt");
        File.WriteAllText(verifiedPath, "correct");

        Snapshot.Validate("sample", settings);

        Assert.Equal("correct", File.ReadAllText(verifiedPath));
    }

    [Fact]
    public void Validate_ForceUpdate_WritesEverySnapshotWhenOnlySomeOfThemChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.txt"),
            ForceUpdateSnapshots = true,
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));

        File.WriteAllText(directory.GetFullPath("snapshot_0.verified.txt"), "value_0");
        File.WriteAllText(directory.GetFullPath("snapshot_0.actual.txt"), "stale");
        File.WriteAllText(directory.GetFullPath("snapshot_1.verified.txt"), "outdated");

        Snapshot.Validate("sample", settings);

        Assert.Equal("value_0", File.ReadAllText(directory.GetFullPath("snapshot_0.verified.txt")));
        Assert.Equal("value_1", File.ReadAllText(directory.GetFullPath("snapshot_1.verified.txt")));
    }

    [Fact]
    public void Validate_KeepsTheSnapshotOfATestWhoseNameEndsWithAnIndex()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
        };
        settings.Serializers.Add(new FixedValueSerializer("value"));

        var siblingPath = directory.GetFullPath("snapshot_1.verified.txt");
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "value");
        File.WriteAllText(siblingPath, "sibling");

        Snapshot.Validate("sample", settings);

        Assert.True(File.Exists(siblingPath));
        Assert.Equal("sibling", File.ReadAllText(siblingPath));
    }

    [Fact]
    public void Validate_DoesNotReportTheSnapshotOfATestWhoseNameEndsWithAnIndex()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
        };
        settings.Serializers.Add(new FixedValueSerializer("value"));

        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "value");
        File.WriteAllText(directory.GetFullPath("snapshot_1.verified.txt"), "sibling");

        Snapshot.Validate("sample", settings);
    }

    [Fact]
    public void Validate_DeletesTheIndexedSnapshotsLeftBehindWhenTheAssertionProducesASingleOne()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
        };
        settings.Serializers.Add(new FixedValueSerializer("value"));

        // 'snapshot.verified.txt' does not exist yet: this is the run where the assertion went from two snapshots to one.
        File.WriteAllText(directory.GetFullPath("snapshot_0.verified.txt"), "value_0");
        File.WriteAllText(directory.GetFullPath("snapshot_1.verified.txt"), "value_1");

        Snapshot.Validate("sample", settings);

        Assert.Equal("value", File.ReadAllText(directory.GetFullPath("snapshot.verified.txt")));
        Assert.False(File.Exists(directory.GetFullPath("snapshot_0.verified.txt")));
        Assert.False(File.Exists(directory.GetFullPath("snapshot_1.verified.txt")));
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.Disallow))]
    [InlineData(nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void Validate_KeepsTheIndexedSnapshotsOfAnotherTest_WhenTheSingleSnapshotExists(string strategyName)
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        // The tests 'Render_0' and 'Render_1' - the cases of '[TestCase(0)] Render(int)' next to 'Render()' - did not run
        // in this process: a filtered run, or the other target framework's process.
        var snapshotDirectory = directory / "__snapshots__";
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(snapshotDirectory / "C_Render.verified.txt", "value");
        File.WriteAllText(snapshotDirectory / "C_Render_0.verified.txt", "other_0");
        File.WriteAllText(snapshotDirectory / "C_Render_1.verified.txt", "other_1");
        File.WriteAllText(snapshotDirectory / "C_Render_1.actual.txt", "pending");
        File.SetLastWriteTimeUtc(snapshotDirectory / "C_Render_1.actual.txt", DateTime.UtcNow.AddDays(-1));

        ValidateWithDefaultNaming("value", CreateDetectedTestContext("C", "Render", "Render"), sourceFile, lineNumber: 10, GetSnapshotUpdateStrategy(strategyName));

        Assert.Equal(["C_Render.verified.txt", "C_Render_0.verified.txt", "C_Render_1.actual.txt", "C_Render_1.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_KeepsTheIndexedSnapshotsOfAnotherTestOfTheProcess_WhenTheSingleSnapshotDoesNotExist()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        ValidateWithDefaultNaming("other_0", CreateDetectedTestContext("C", "Render", "Render_0"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming("other_1", CreateDetectedTestContext("C", "Render", "Render_1"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming("value", CreateDetectedTestContext("C", "Render", "Render"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);

        Assert.Equal(["C_Render.verified.txt", "C_Render_0.verified.txt", "C_Render_1.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_ReportsATestNamedAfterAFileOfAnAssertionThatProducesSeveralSnapshots()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "Frames", "Frames")))
        {
            Snapshot.Validate("sample", type: null, settings, sourceFile.Value, callerLineNumber: 10);
        }

        // 'Frames' stores its snapshots in 'C_Frames_0.verified.txt' and 'C_Frames_1.verified.txt'.
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("other", CreateDetectedTestContext("C", "Frames", "Frames_0"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("'C_Frames_0'", exception.Message);
        Assert.Equal("value_0", File.ReadAllText(directory / "__snapshots__" / "C_Frames_0.verified.txt"));
    }

    [Fact]
    public void Validate_DeletesTheSingleSnapshotLeftBehindWhenTheAssertionProducesSeveral()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));

        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "value_0");

        Snapshot.Validate("sample", settings);

        Assert.False(File.Exists(directory.GetFullPath("snapshot.verified.txt")));
        Assert.Equal("value_0", File.ReadAllText(directory.GetFullPath("snapshot_0.verified.txt")));
        Assert.Equal("value_1", File.ReadAllText(directory.GetFullPath("snapshot_1.verified.txt")));
    }

    [Fact]
    public void Validate_ReportsTheSingleSnapshotLeftBehindWhenTheAssertionProducesSeveral()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = CreateIndexedSnapshotPathStrategy(directory),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));

        var obsoletePath = directory.GetFullPath("snapshot.verified.txt");
        File.WriteAllText(obsoletePath, "value_0");
        File.WriteAllText(directory.GetFullPath("snapshot_0.verified.txt"), "value_0");
        File.WriteAllText(directory.GetFullPath("snapshot_1.verified.txt"), "value_1");

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.Contains("Unexpected snapshot files:", exception.Message);
        Assert.Contains(obsoletePath.Value, exception.Message);
    }

    [Fact]
    public void Validate_ComparesEachSnapshotFileOnce()
    {
        using var directory = TemporaryDirectory.Create();
        var comparer = new RecordingSnapshotComparer();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        settings.Comparers.Set(SnapshotType.Default, comparer);
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "expected");

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.Equal(1, comparer.InvocationCount);
    }

    [Fact]
    public void DefaultSnapshotPath_DoesNotDependOnTheCheckoutDirectory()
    {
        var settings = new SnapshotSettings();
        var methodName = "SampleTest" + new string('a', 200);

        SnapshotPathContext CreateContext(string root) => new(
            FullPath.FromPath(Path.Combine(root, "Tests", "SampleTests.cs")),
            "SampleTests",
            methodName,
            LineNumber: 12,
            SnapshotType.Default,
            Index: 0,
            Extension: "txt",
            TestContext: null,
            settings);

        var firstCheckout = settings.SnapshotPathStrategy(CreateContext(Path.Combine(Path.GetTempPath(), "repo-a")));
        var secondCheckout = settings.SnapshotPathStrategy(CreateContext(Path.Combine(Path.GetTempPath(), "repo-b")));

        Assert.Equal(firstCheckout.Name, secondCheckout.Name);
        Assert.Matches(SnapshotNameWithHashSuffixRegex(), firstCheckout.Name);
    }

    [Fact]
    public void DefaultSnapshotPath_DistinguishesTestNamesThatSanitizeToTheSameFileName()
    {
        var settings = new SnapshotSettings();

        SnapshotPathContext CreateContext(string testName) => new(
            FullPath.FromPath(Path.Combine(Path.GetTempPath(), "SampleTests.cs")),
            "SampleTests",
            "SampleTheory",
            LineNumber: 12,
            SnapshotType.Default,
            Index: 0,
            Extension: "txt",
            new SnapshotTestContext(TestName: testName),
            settings);

        var slash = settings.SnapshotPathStrategy(CreateContext("Case_a/b"));
        var question = settings.SnapshotPathStrategy(CreateContext("Case_a?b"));

        Assert.NotEqual(slash.Name, question.Name);
        Assert.Matches(SnapshotNameWithHashSuffixRegex(), slash.Name);
        Assert.Matches(SnapshotNameWithHashSuffixRegex(), question.Name);
    }

    [Fact]
    public void DefaultSnapshotPath_DoesNotHashANameThatSanitizationLeavesUnchanged()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            FullPath.FromPath(Path.Combine(Path.GetTempPath(), "SampleTests.cs")),
            "SampleTests",
            "SampleTest",
            LineNumber: 12,
            SnapshotType.Default,
            Index: 0,
            Extension: "txt",
            TestContext: null,
            settings);

        var path = settings.SnapshotPathStrategy(context);

        Assert.Equal("SampleTests_SampleTest.verified.txt", path.Name);
    }

    [Fact]
    public void DefaultSnapshotPath_KeepsTheFileNamesOfEarlierVersions()
    {
        var settings = new SnapshotSettings();

        string GetFileName(string? className, string methodName, string? testName, int index = 0, int count = 1, string extension = "txt") => settings.SnapshotPathStrategy(new SnapshotPathContext(
            FullPath.FromPath(Path.Combine(Path.GetTempPath(), "Tests.cs")),
            className,
            methodName,
            LineNumber: 12,
            SnapshotType.Default,
            index,
            extension,
            testName is null ? null : new SnapshotTestContext(TestName: testName),
            settings,
            count)).Name;

        // The expected names were produced by the version that preceded the naming fixes.
        Assert.Equal("C_Normal.verified.txt", GetFileName("C", "Normal", testName: null));
        Assert.Equal("C_Theory_alpha.verified.txt", GetFileName("C", "Theory", "Theory_alpha"));
        Assert.Equal("C_Works.verified.png", GetFileName("C", "WorksA", "Works", extension: "png"));
        Assert.Equal("C_Frames_1.verified.png", GetFileName("C", "Frames", "Frames", index: 1, count: 2, extension: "png"));
        Assert.Equal("C_5__1.5_c40b6b3e.verified.txt", GetFileName("C", "Dbl", "5)_1.5"));
        Assert.Equal("C_Arr_System.Int32_41fdd94b.verified.txt", GetFileName("C", "Arr", "Arr_System.Int32[]"));
        Assert.Equal("C_Empty_a7d400d1.verified.txt", GetFileName("C", "Empty", "Empty_"));
    }

    [Fact]
    public void DefaultSnapshotPath_KeepsTheFileNameWithinTheFileSystemLimit()
    {
        var settings = new SnapshotSettings();

        string GetFileName(string testName) => settings.SnapshotPathStrategy(new SnapshotPathContext(
            FullPath.FromPath(Path.Combine(Path.GetTempPath(), "Tests.cs")),
            "C",
            "M",
            LineNumber: 12,
            SnapshotType.Default,
            Index: 0,
            Extension: "txt",
            new SnapshotTestContext(TestName: testName),
            settings)).Name;

        // 100 CJK characters fit in MaxSnapshotFileNameLength but take 300 bytes, more than ext4 or APFS accept.
        var longName = GetFileName(new string('名', 100));
        Assert.True(Encoding.UTF8.GetByteCount(longName) <= 255, longName);
        Assert.Matches(SnapshotNameWithHashSuffixRegex(), longName);
        Assert.NotEqual(longName, GetFileName(new string('名', 99) + "字"));

        // A name within the limit keeps its name.
        Assert.Equal("C_" + new string('名', 40) + ".verified.txt", GetFileName(new string('名', 40)));
        Assert.Equal("C_" + new string('a', 100) + ".verified.txt", GetFileName(new string('a', 100)));
    }

    [Fact]
    public void TestNames_XunitV3_KeepTheNamesOfEarlierVersions()
    {
        AssertTestNames("Normal", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Normal", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Normal", "Ns.C", "Normal", []));
        AssertTestNames("Works", SnapshotTestContext.GetXunitV3TestNames("Works", testMethodNameOfTest: null, hasTestCase: true, "Works", "Ns.C", "WorksA", []));
        AssertTestNames("InNested", SnapshotTestContext.GetXunitV3TestNames("Ns.C+Nested.InNested", testMethodNameOfTest: null, hasTestCase: true, "Ns.C+Nested.InNested", "Ns.C+Nested", "InNested", []));
        AssertTestNames("Theory_alpha", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Theory(value: \"alpha\")", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Theory", "Ns.C", "Theory", ["alpha"]));
        AssertTestNames("Theory_snake_case", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Theory(value: \"snake_case\")", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Theory", "Ns.C", "Theory", ["snake_case"]));
        AssertTestNames("Theory_null", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Theory(value: null)", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Theory", "Ns.C", "Theory", [null]));
        AssertTestNames("Custom_1", SnapshotTestContext.GetXunitV3TestNames("Custom(value: 1)", testMethodNameOfTest: null, hasTestCase: true, "Custom", "Ns.C", "CustomTheory", [1]));
        AssertTestNames("Kinds_42_True_Monday_x_System.Int32_-1", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Kinds(a: 42, b: True, c: Monday, d: 'x', e: typeof(int), f: -1)", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Kinds", "Ns.C", "Kinds", [42, true, DayOfWeek.Monday, 'x', typeof(int), -1L]));
        AssertTestNames("M_1", SnapshotTestContext.GetXunitV3TestNames("Ns.C.M(value: 1)", testMethodNameOfTest: null, hasTestCase: false, testCaseDisplayName: null, testClassName: null, testMethodName: null, [1]));
    }

    [Fact]
    public void TestNames_XunitV3_DoNotTruncateNamesAtADot()
    {
        AssertTestNames("Dbl_1.5", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Dbl(value: 1.5)", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Dbl", "Ns.C", "Dbl", [1.5]), expectedLegacyTestName: "5)_1.5");
        AssertTestNames("Str_a.b", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Str(value: \"a.b\")", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Str", "Ns.C", "Str", ["a.b"]), expectedLegacyTestName: "b\")_a.b");
        AssertTestNames("Works.Fine", SnapshotTestContext.GetXunitV3TestNames("Works.Fine", testMethodNameOfTest: null, hasTestCase: true, "Works.Fine", "Ns.C", "WorksFine", []), expectedLegacyTestName: "Fine");
        AssertTestNames("Case (1.5)", SnapshotTestContext.GetXunitV3TestNames("Case (1.5)", testMethodNameOfTest: null, hasTestCase: true, "Case (1.5)", "Ns.C", "CaseParen", []), expectedLegacyTestName: "5)");
        AssertTestNames("Custom.Dot_1", SnapshotTestContext.GetXunitV3TestNames("Custom.Dot(value: 1)", testMethodNameOfTest: null, hasTestCase: true, "Custom.Dot", "Ns.C", "M", [1]), expectedLegacyTestName: "Dot_1");

        // Depending on the Xunit version, the display name of the test case contains the arguments too.
        AssertTestNames("Dbl_2.5", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Dbl(value: 2.5)", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Dbl(value: 2.5)", "Ns.C", "Dbl", [2.5]), expectedLegacyTestName: "5)_2.5");
        AssertTestNames("Case (fast)_1", SnapshotTestContext.GetXunitV3TestNames("Case (fast)(value: 1)", testMethodNameOfTest: null, hasTestCase: true, "Case (fast)(value: 1)", "Ns.C", "M", [1]), expectedLegacyTestName: "Case _1");
    }

    [Fact]
    public void TestNames_NUnit_KeepTheNamesOfEarlierVersions()
    {
        AssertTestNames("Normal", SnapshotTestContext.GetNUnitTestNames("Normal", "Normal", "Ns.C.Normal", []));
        AssertTestNames("Alpha_alpha", SnapshotTestContext.GetNUnitTestNames("Alpha(\"alpha\")", "Alpha", "Ns.C.Alpha(\"alpha\")", ["alpha"]));
        AssertTestNames("Case_alpha", SnapshotTestContext.GetNUnitTestNames("Case_alpha", "Named", "Ns.C.Case_alpha", [1]));
        AssertTestNames("M_1_a", SnapshotTestContext.GetNUnitTestNames("M(1,\"a\")", "M", "Ns.C.M(1,\"a\")", [1, "a"]));
        AssertTestNames("Tmpl_1", SnapshotTestContext.GetNUnitTestNames("Tmpl((1))", "Tmpl", "Ns.C.Tmpl((1))", [1]));
        AssertTestNames("Generic<Int32>_1", SnapshotTestContext.GetNUnitTestNames("Generic<Int32>(1)", "Generic", "Ns.C.Generic<Int32>(1)", [1]));
    }

    [Fact]
    public void TestNames_NUnit_DoNotTruncateNamesAtADot()
    {
        AssertTestNames("Dbl_1.5", SnapshotTestContext.GetNUnitTestNames("Dbl(1.5d)", "Dbl", "Ns.C.Dbl(1.5d)", [1.5]), expectedLegacyTestName: "5d)");
        AssertTestNames("Str_a.b", SnapshotTestContext.GetNUnitTestNames("Str(\"a.b\")", "Str", "Ns.C.Str(\"a.b\")", ["a.b"]), expectedLegacyTestName: "b\")");
        AssertTestNames("Case 1.0", SnapshotTestContext.GetNUnitTestNames("Case 1.0", "Named", "Ns.C.Case 1.0", [1]), expectedLegacyTestName: "0");
    }

    [Fact]
    public void TestNames_TUnit_KeepTheNamesOfEarlierVersions()
    {
        AssertTestNames("Normal", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Normal", "Normal", []));
        AssertTestNames("Dbl_1.5", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Dbl(1.5)", "Dbl", [1.5]));
        AssertTestNames("Custom 1", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Custom 1", "Custom", [1]));
    }

    [Fact]
    public void TestNames_NUnit_IncludeTheArgumentsOfAParameterizedFixture()
    {
        // [TestFixture("en-US")] [TestFixture("fr-FR")]: the instances run the same tests under the same names.
        var enUs = SnapshotTestContext.GetNUnitTestNames("Format", "Format", "Ns.C(\"en-US\").Format", [], fixtureArguments: ["en-US"]);
        var frFr = SnapshotTestContext.GetNUnitTestNames("Format", "Format", "Ns.C(\"fr-FR\").Format", [], fixtureArguments: ["fr-FR"]);
        AssertTestNames("Format_en-US", enUs, expectedLegacyTestName: "Format");
        AssertTestNames("Format_fr-FR", frFr, expectedLegacyTestName: "Format");
        Assert.False(enUs.HasAmbiguousArguments);

        AssertTestNames("Alpha_alpha_en-US_2", SnapshotTestContext.GetNUnitTestNames("Alpha(\"alpha\")", "Alpha", "Ns.C(\"en-US\",2).Alpha(\"alpha\")", ["alpha"], fixtureArguments: ["en-US", 2]), expectedLegacyTestName: "Alpha_alpha");
        AssertTestNames("Case_alpha_1.5", SnapshotTestContext.GetNUnitTestNames("Case_alpha", "Named", "Ns.C(1.5d).Case_alpha", [1], fixtureArguments: [1.5]), expectedLegacyTestName: "Case_alpha");

        // A fixture without arguments keeps its name.
        AssertTestNames("Format", SnapshotTestContext.GetNUnitTestNames("Format", "Format", "Ns.C.Format", [], fixtureArguments: []));

        // '_' also separates the fixture arguments from the rest of the name.
        Assert.True(SnapshotTestContext.GetNUnitTestNames("M(\"a_b\")", "M", "Ns.C(\"c\").M(\"a_b\")", ["a_b"], fixtureArguments: ["c"]).HasAmbiguousArguments);
        Assert.True(SnapshotTestContext.GetNUnitTestNames("M(\"a\")", "M", "Ns.C(\"b_c\").M(\"a\")", ["a"], fixtureArguments: ["b_c"]).HasAmbiguousArguments);
    }

    [Fact]
    public void TestNames_TUnit_IncludeTheArgumentsOfTheClass()
    {
        AssertTestNames("Format_en-US", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Format", "Format", [], classArguments: ["en-US"]), expectedLegacyTestName: "Format");
        AssertTestNames("Dbl_1.5_en-US", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Dbl(1.5)", "Dbl", [1.5], classArguments: ["en-US"]), expectedLegacyTestName: "Dbl_1.5");
        AssertTestNames("Custom 1_en-US", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Custom 1", "Custom", [1], classArguments: ["en-US"]), expectedLegacyTestName: "Custom 1");
    }

    [Fact]
    public void TestNames_FormatDatesWithTheRoundTripFormat()
    {
        var first = new DateTime(2024, 1, 2, 3, 4, 5, 100, DateTimeKind.Utc);
        var second = new DateTime(2024, 1, 2, 3, 4, 5, 200, DateTimeKind.Utc);

        AssertTestNames("M_2024-01-02T03:04:05.1000000Z", SnapshotTestContext.GetNUnitTestNames("M(2024-01-02 03:04:05)", "M", "Ns.C.M(2024-01-02 03:04:05)", [first]), expectedLegacyTestName: "M_01/02/2024 03:04:05");
        AssertTestNames("M_2024-01-02T03:04:05.2000000Z", SnapshotTestContext.GetNUnitTestNames("M(2024-01-02 03:04:05)", "M", "Ns.C.M(2024-01-02 03:04:05)", [second]), expectedLegacyTestName: "M_01/02/2024 03:04:05");

        Assert.Equal("2024-01-02T03:04:05.1000000", SnapshotTestContext.FormatArguments([DateTime.SpecifyKind(first, DateTimeKind.Unspecified)], out _));
        Assert.Equal("2024-01-02T03:04:05.1000000+02:00", SnapshotTestContext.FormatArguments([new DateTimeOffset(2024, 1, 2, 3, 4, 5, 100, TimeSpan.FromHours(2))], out _));
        Assert.Equal("03:04:05.1000000", SnapshotTestContext.FormatArguments([new TimeOnly(3, 4, 5, 100)], out _));
    }

    [Fact]
    public void TestNames_FlagTheCasesWhoseArgumentsOnlyDifferByType()
    {
        // An object parameter receiving 1, 1L and "1" names the three cases alike. Unique names keep the test apart
        // from the other tests using the process-wide registry.
        var methodName = "M_" + Guid.NewGuid().ToString("N");
        Assert.False(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_1", [1]));
        Assert.True(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_1", [1L]));
        Assert.True(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_1", ["1"]));
        Assert.False(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_[1]", [new object[] { 1 }]));
        Assert.True(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_[1]", [new object[] { 1L }]));

        // The case that ran first, running again, is not flagged.
        Assert.False(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_1", [1]));

        // Other names and tests without arguments are not affected.
        Assert.False(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M_2", [2L]));
        Assert.False(SnapshotTestContext.HasArgumentsOfAnotherType("C", methodName, "M", []));
    }

    [Fact]
    public void GetContext_CachesATestFrameworkThatIsNotAvailable()
    {
        Func<SnapshotTestContext?>? cachedFactory = null;
        var probeCount = 0;
        Func<Func<SnapshotTestContext?>?> probe = () =>
        {
            probeCount++;
            return null;
        };

        Assert.Null(SnapshotTestContext.GetContext(ref cachedFactory, probe).Invoke());
        Assert.Null(SnapshotTestContext.GetContext(ref cachedFactory, probe).Invoke());

        Assert.Equal(1, probeCount);
    }

    [Fact]
    public void TestNames_FormatListsElementByElement()
    {
        AssertTestNames("Arr_[1, 2]", SnapshotTestContext.GetXunitV3TestNames("Ns.C.Arr(value: [1, 2])", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Arr", "Ns.C", "Arr", [new[] { 1, 2 }]), expectedLegacyTestName: "Arr_System.Int32[]");
        AssertTestNames("Arr_[3]", SnapshotTestContext.GetNUnitTestNames("Arr([3])", "Arr", "Ns.C.Arr([3])", [new[] { 3 }]), expectedLegacyTestName: "Arr_System.Int32[]");
        AssertTestNames("Arr_[\"a\", null]", SnapshotTestContext.GetTUnitTestNames(hasTestDetails: true, "Arr(a, null)", "Arr", [new List<string?> { "a", null }]), expectedLegacyTestName: "Arr_System.Collections.Generic.List`1[System.String]");
    }

    [Fact]
    public void TestNames_DistinguishNullFromTheNullString()
    {
        var nullNames = SnapshotTestContext.GetXunitV3TestNames("Ns.C.Nul(value: null)", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Nul", "Ns.C", "Nul", [null]);
        var stringNames = SnapshotTestContext.GetXunitV3TestNames("Ns.C.Nul(value: \"null\")", testMethodNameOfTest: null, hasTestCase: true, "Ns.C.Nul", "Ns.C", "Nul", ["null"]);

        AssertTestNames("Nul_null", nullNames);
        AssertTestNames("Nul_\"null\"", stringNames, expectedLegacyTestName: "Nul_null");
    }

    [Fact]
    public void FormatArguments_FlagsTheArgumentsThatDoNotTellTheTestCasesApart()
    {
        static bool IsAmbiguous(params object?[] arguments)
        {
            SnapshotTestContext.FormatArguments(arguments, out var hasAmbiguousArguments);
            return hasAmbiguousArguments;
        }

        Assert.False(IsAmbiguous(1, "a", true, DayOfWeek.Monday, null, new Uri("https://example.com"), ("a", 1)));
        Assert.False(IsAmbiguous("snake_case"));
        Assert.True(IsAmbiguous(new object()));
        Assert.True(IsAmbiguous(new[] { new object() }));
        Assert.True(IsAmbiguous(new Dictionary<string, int>(StringComparer.Ordinal)));
        Assert.True(IsAmbiguous("a_b", "c"));
    }

    private static void AssertTestNames(string expectedTestName, SnapshotTestContext.TestNames names, string? expectedLegacyTestName = null)
    {
        Assert.Equal(expectedTestName, names.TestName);
        Assert.Equal(expectedLegacyTestName ?? expectedTestName, names.LegacyTestName);
    }

    [Theory]
    [InlineData("alpha")]
    public void Validate_KeepsTheNameOfATheoryWithAStringArgument(string value)
    {
        Assert.Equal("SnapshotTests_Validate_KeepsTheNameOfATheoryWithAStringArgument_alpha.verified.txt", GetDefaultSnapshotFileName(value));
    }

    [Theory]
    [InlineData(1.5)]
    public void Validate_NamesATheoryWithADecimalArgumentAfterItsMethod(double value)
    {
        Assert.Equal("SnapshotTests_Validate_NamesATheoryWithADecimalArgumentAfterItsMethod_1.5.verified.txt", GetDefaultSnapshotFileName(value));
    }

    private static string GetDefaultSnapshotFileName(object value)
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / SnapshotSettings.Default.SnapshotPathStrategy(context).Name,
        };

        Snapshot.Validate(value, settings);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        return Path.GetFileName(files[0]);
    }

    [Fact]
    public void Validate_GivesEachCallOfATestItsOwnSnapshot()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "M", "M");

        for (var run = 0; run < 2; run++)
        {
            var strategy = run == 0 ? SnapshotUpdateStrategy.OverwriteWithoutFailure : SnapshotUpdateStrategy.Disallow;
            ValidateWithDefaultNaming("first", context, sourceFile, lineNumber: 10, strategy);
            ValidateWithDefaultNaming("second", context, sourceFile, lineNumber: 20, strategy);
            ValidateWithDefaultNaming("third", context, sourceFile, lineNumber: 30, strategy);
        }

        Assert.Equal(["C_M.verified.txt", "C_M~2.verified.txt", "C_M~3.verified.txt"], GetSnapshotFileNames(directory));
        Assert.Equal("first", File.ReadAllText(directory / "__snapshots__" / "C_M.verified.txt"));
        Assert.Equal("second", File.ReadAllText(directory / "__snapshots__" / "C_M~2.verified.txt"));
        Assert.Equal("third", File.ReadAllText(directory / "__snapshots__" / "C_M~3.verified.txt"));
    }

    [Fact]
    public void Validate_KeepsTheSnapshotsOfTwoCallsWithDifferentTypes()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "M", "M");

        ValidateWithDefaultNaming("text", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming(new byte[] { 1, 2, 3 }, context, sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure, SnapshotType.Png);

        // The first call used to delete the file of the second one, and the other way around.
        ValidateWithDefaultNaming("text", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.Disallow);
        ValidateWithDefaultNaming(new byte[] { 1, 2, 3 }, context, sourceFile, lineNumber: 20, SnapshotUpdateStrategy.Disallow, SnapshotType.Png);

        Assert.Equal(["C_M.verified.txt", "C_M~2.verified.png"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_ReplacesTheSnapshotOfACallWhoseTypeChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "M", "M");

        ValidateWithDefaultNaming("text", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming(new byte[] { 1, 2, 3 }, context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure, SnapshotType.Png);

        Assert.Equal(["C_M.verified.png"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_KeepsTheSnapshotOfAnotherTypeOfATestWithSeveralCalls()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        // 'if (OperatingSystem.IsWindows()) Validate(png); Validate(text);' created these files on Windows. Elsewhere, the
        // text call is the first one of the test, so it is named 'C_M'.
        var snapshotDirectory = directory / "__snapshots__";
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllBytes(snapshotDirectory / "C_M.verified.png", [1, 2, 3]);
        File.WriteAllText(snapshotDirectory / "C_M~2.verified.txt", "text");

        ValidateWithDefaultNaming("text", CreateDetectedTestContext("C", "M", "M"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure);

        Assert.Equal(["C_M.verified.png", "C_M.verified.txt", "C_M~2.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_ReportsAndRenamesASnapshotFileWhoseNameDiffersByCase()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "ValidateURL", "ValidateURL");

        // The test used to be called 'ValidateUrl'.
        var snapshotDirectory = directory / "__snapshots__";
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(snapshotDirectory / "C_ValidateUrl.verified.txt", "value");

        var exception = Assert.Throws<SnapshotAssertionException>(() => ValidateWithDefaultNaming("value", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.Disallow));
        Assert.Contains("differ from the snapshot name by case only", exception.Message);
        Assert.Contains("C_ValidateUrl.verified.txt => C_ValidateURL.verified.txt", exception.Message);

        // On a case-sensitive file system, the snapshot is also reported as missing, so an actual file is written too.
        Assert.Equal(["C_ValidateUrl.verified.txt"], GetSnapshotFileNames(directory).Where(name => name.Contains(".verified.", StringComparison.Ordinal)).ToArray());

        ValidateWithDefaultNaming("value", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        Assert.Equal(["C_ValidateURL.verified.txt"], GetSnapshotFileNames(directory));
        Assert.Equal("value", File.ReadAllText(snapshotDirectory / "C_ValidateURL.verified.txt"));

        ValidateWithDefaultNaming("value", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.Disallow);
    }

    [Fact]
    public void Validate_ReportsTwoTestsThatShareASnapshotName()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        // Two facts with the same display name.
        ValidateWithDefaultNaming("a", CreateDetectedTestContext("C", "WorksA", "Works"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("b", CreateDetectedTestContext("C", "WorksB", "Works"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("'C_Works'", exception.Message);
        Assert.Contains("C.WorksA", exception.Message);
        Assert.Contains("C.WorksB", exception.Message);
        Assert.Equal("a", File.ReadAllText(directory / "__snapshots__" / "C_Works.verified.txt"));
    }

    [Fact]
    public void Validate_ReportsATestNameThatAlreadyStartsWithTheClassName()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        ValidateWithDefaultNaming("a", CreateDetectedTestContext("Parser", "Parser_Empty", "Parser_Empty"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("b", CreateDetectedTestContext("Parser", "Empty", "Empty"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("'Parser_Empty'", exception.Message);
    }

    [Fact]
    public void Validate_ReportsTestsWithArgumentsThatDoNotOverrideToString()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        ValidateWithDefaultNaming("a", CreateDetectedTestContext("C", "M", "M_System.Object") with { AmbiguousTestId = "1" }, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming("a", CreateDetectedTestContext("C", "M", "M_System.Object") with { AmbiguousTestId = "1" }, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.Disallow);
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("b", CreateDetectedTestContext("C", "M", "M_System.Object") with { AmbiguousTestId = "2" }, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("ToString", exception.Message);
    }

    [Fact]
    public void Validate_ReportsSnapshotNamesThatOnlyDifferByCase()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        ValidateWithDefaultNaming("a", CreateDetectedTestContext("C", "Case", "Case_A"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("b", CreateDetectedTestContext("C", "Case", "Case_a"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("only differ by case", exception.Message);
        Assert.Contains("'C_Case_A'", exception.Message);
        Assert.Contains("'C_Case_a'", exception.Message);
    }

    [Fact]
    public void Validate_ReportsTestContextsThatOnlyDifferByMetadata()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        ValidateWithDefaultNaming("a", new SnapshotTestContext("Case", new Dictionary<string, string?>(StringComparer.Ordinal) { ["id"] = "1" }), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("b", new SnapshotTestContext("Case", new Dictionary<string, string?>(StringComparer.Ordinal) { ["id"] = "2" }), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("Metadata is not part of the snapshot name", exception.Message);
    }

    [Fact]
    public void Validate_AllowsATestContextNameToBeSharedOnPurpose()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        // The same explicit test name, used by two call sites that run as different tests.
        ValidateWithDefaultNaming("shared", new SnapshotTestContext("Shared") { ClassName = "C", MethodName = "First" }, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming("shared", new SnapshotTestContext("Shared") { ClassName = "C", MethodName = "Second" }, sourceFile, lineNumber: 20, SnapshotUpdateStrategy.Disallow);

        Assert.Equal(["C_Shared.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_UsesTheSnapshotNamedByAnEarlierVersion()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "Dbl", "Dbl_1.5") with { LegacyTestName = "5)_1.5" };

        // The name the version that preceded the naming fixes gave to '[InlineData(1.5)] void Dbl(double)'.
        var legacyPath = directory / "__snapshots__" / "C_5__1.5_c40b6b3e.verified.txt";
        legacyPath.CreateParentDirectory();
        File.WriteAllText(legacyPath, "value");

        ValidateWithDefaultNaming("value", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.Disallow);

        Assert.Equal(["C_5__1.5_c40b6b3e.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_UsesTheCurrentName_WhenNoSnapshotWasNamedByAnEarlierVersion()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var context = CreateDetectedTestContext("C", "Dbl", "Dbl_1.5") with { LegacyTestName = "5)_1.5" };

        ValidateWithDefaultNaming("value", context, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);

        Assert.Equal(["C_Dbl_1.5.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_UsesTheCurrentName_WhenTheEarlierNameBelongsToAnotherTest()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);

        // Earlier versions named '[InlineData(null)]' and '[InlineData("null")]' the same.
        ValidateWithDefaultNaming("null", CreateDetectedTestContext("C", "Nul", "Nul_null"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        ValidateWithDefaultNaming("string", CreateDetectedTestContext("C", "Nul", "Nul_\"null\"") with { LegacyTestName = "Nul_null" }, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);

        Assert.HasCount(2, GetSnapshotFileNames(directory));
        Assert.Equal("null", File.ReadAllText(directory / "__snapshots__" / "C_Nul_null.verified.txt"));
    }

    [Fact]
    public void Validate_ReportsATestUsingTheSnapshotOfAnotherTestNamedByAnEarlierVersion()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var legacyPath = directory / "__snapshots__" / "C_Nul_null.verified.txt";
        legacyPath.CreateParentDirectory();
        File.WriteAllText(legacyPath, "shared");

        ValidateWithDefaultNaming("string", CreateDetectedTestContext("C", "Nul", "Nul_\"null\"") with { LegacyTestName = "Nul_null" }, sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure);
        var exception = Assert.Throws<SnapshotException>(() => ValidateWithDefaultNaming("null", CreateDetectedTestContext("C", "Nul", "Nul_null"), sourceFile, lineNumber: 10, SnapshotUpdateStrategy.OverwriteWithoutFailure));

        Assert.Contains("earlier version", exception.Message);
        Assert.Contains("C_Nul_null.verified.*", exception.Message);
    }

    [Fact]
    public void Validate_DoesNotDeleteAnIndexedSnapshotThatMayBelongToAnotherTest()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var otherTestPath = directory / "__snapshots__" / "C_Render_a_2.verified.txt";
        otherTestPath.CreateParentDirectory();
        File.WriteAllText(otherTestPath, "other");

        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "Render_a", "Render_a")))
        {
            Snapshot.Validate("sample", type: null, settings, sourceFile.Value, callerLineNumber: 10);

            // 'Render_a_2' may be the snapshot of a test named 'Render_a_2' or a file this assertion wrote when it
            // produced three snapshots: it is reported, but only the user can delete it.
            Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", type: null, settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow }, sourceFile.Value, callerLineNumber: 10));
        }

        Assert.Equal(["C_Render_a_0.verified.txt", "C_Render_a_1.verified.txt", "C_Render_a_2.verified.txt"], GetSnapshotFileNames(directory));
        Assert.Equal("other", File.ReadAllText(otherTestPath));
    }

    [Fact]
    public void Validate_DoesNotDeleteTheActualFileOfAnIndexedSnapshotThatMayBelongToAnotherTest()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var otherTestPath = directory / "__snapshots__" / "C_Render_b_2.verified.txt";
        var otherTestActualPath = directory / "__snapshots__" / "C_Render_b_2.actual.txt";
        otherTestPath.CreateParentDirectory();
        File.WriteAllText(otherTestPath, "other");
        File.WriteAllText(otherTestActualPath, "pending");

        // Written by an earlier run, so only the ownership of the verified file protects it.
        File.SetLastWriteTimeUtc(otherTestActualPath, DateTime.UtcNow.AddDays(-1));

        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "Render_b", "Render_b")))
        {
            Snapshot.Validate("sample", type: null, settings, sourceFile.Value, callerLineNumber: 10);
        }

        Assert.Equal("pending", File.ReadAllText(otherTestActualPath));
    }

    [Fact]
    public void Validate_IgnoresAnIndexedSnapshotOfAnotherTestOfTheProcess()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        ValidateWithDefaultNaming("other", CreateDetectedTestContext("C", "Render_a_2", "Render_a_2"), sourceFile, lineNumber: 20, SnapshotUpdateStrategy.OverwriteWithoutFailure);

        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "Render_a", "Render_a")))
        {
            Snapshot.Validate("sample", type: null, settings, sourceFile.Value, callerLineNumber: 10);
            Snapshot.Validate("sample", type: null, settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow }, sourceFile.Value, callerLineNumber: 10);
        }

        Assert.Equal(["C_Render_a_0.verified.txt", "C_Render_a_1.verified.txt", "C_Render_a_2.verified.txt"], GetSnapshotFileNames(directory));
    }

    [Fact]
    public void Validate_KeepsTheIndexedSnapshots_WhenTheAssertionNowProducesASingleSnapshotAndUpdatesAreDisallowed()
    {
        // The assertion used to produce two snapshots and now produces one, so the indexed files it left
        // behind are reported as unexpected and the file it produces now is reported as missing.
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var snapshotDirectory = directory / "__snapshots__";
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(snapshotDirectory / "C_M_0.verified.txt", "value_0");
        File.WriteAllText(snapshotDirectory / "C_M_1.verified.txt", "value_1");

        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        settings.Serializers.Add(new FixedCountSerializer(count: 1));
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "M", "M")))
        {
            Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", type: null, settings, sourceFile.Value, callerLineNumber: 10));
        }

        Assert.Equal(["C_M.actual.txt", "C_M_0.verified.txt", "C_M_1.verified.txt"], GetSnapshotFileNames(directory));
        Assert.Equal("value_0", File.ReadAllText(snapshotDirectory / "C_M.actual.txt"));
        Assert.Equal("value_0", File.ReadAllText(snapshotDirectory / "C_M_0.verified.txt"));
        Assert.Equal("value_1", File.ReadAllText(snapshotDirectory / "C_M_1.verified.txt"));
    }

    [Fact]
    public void Validate_WritesAnActualFileForTheNewSnapshot_WhenTheAssertionNowProducesMoreSnapshotsAndUpdatesAreDisallowed()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var snapshotDirectory = directory / "__snapshots__";
        Directory.CreateDirectory(snapshotDirectory);
        File.WriteAllText(snapshotDirectory / "C_M_0.verified.txt", "value_0");

        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "M", "M")))
        {
            Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", type: null, settings, sourceFile.Value, callerLineNumber: 10));
        }

        Assert.Equal(["C_M_0.verified.txt", "C_M_1.actual.txt"], GetSnapshotFileNames(directory));
        Assert.Equal("value_0", File.ReadAllText(snapshotDirectory / "C_M_0.verified.txt"));
        Assert.Equal("value_1", File.ReadAllText(snapshotDirectory / "C_M_1.actual.txt"));
    }

    [Fact]
    public void Validate_NamesBinarySnapshotsAfterTheirType()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure };

        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "ByteArrayWithTypeName", "ByteArrayWithTypeName")))
        {
            Snapshot.Validate(new byte[] { 0x42, 0x00, 0x43 }, "png", settings, sourceFile.Value, callerLineNumber: 10);
        }

        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "ByteArray", "ByteArray")))
        {
            Snapshot.Validate(new byte[] { 0x42, 0x00, 0x43 }, SnapshotType.Png, settings, sourceFile.Value, callerLineNumber: 20);
        }

        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "Stream", "Stream")))
        {
            using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04]);
            Snapshot.Validate(stream, SnapshotType.Png, settings, sourceFile.Value, callerLineNumber: 30);
        }

        Assert.Equal(["C_ByteArray.verified.png", "C_ByteArrayWithTypeName.verified.png", "C_Stream.verified.png"], GetSnapshotFileNames(directory));
        Assert.Equal([0x42, 0x00, 0x43], File.ReadAllBytes(directory / "__snapshots__" / "C_ByteArrayWithTypeName.verified.png"));
        Assert.Equal([0x42, 0x00, 0x43], File.ReadAllBytes(directory / "__snapshots__" / "C_ByteArray.verified.png"));
        Assert.Equal([0x01, 0x02, 0x03, 0x04], File.ReadAllBytes(directory / "__snapshots__" / "C_Stream.verified.png"));
    }

    [Fact]
    public void Validate_MatchesABmpSnapshotWhoseMetadataDiffers_WhenTheImageComparerIsEnabled()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFile = CreateSourceFile(directory);
        var verifiedPath = directory / "__snapshots__" / "C_M.verified.bmp";
        var verifiedData = ImageTestData.CreateBmp24(width: 1, height: 1, pixels: [0xFF010203u], pixelsPerMeter: 2835);
        verifiedPath.CreateParentDirectory();
        File.WriteAllBytes(verifiedPath, verifiedData);

        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        settings.Comparers.AddImageComparer();
        using (new SnapshotTestContextScope(CreateDetectedTestContext("C", "M", "M")))
        {
            var actualData = ImageTestData.CreateBmp24(width: 1, height: 1, pixels: [0xFF010203u], pixelsPerMeter: 3780);
            Snapshot.Validate(actualData, SnapshotType.Bmp, settings, sourceFile.Value, callerLineNumber: 10);
        }

        Assert.Equal(["C_M.verified.bmp"], GetSnapshotFileNames(directory));
        Assert.Equal(verifiedData, File.ReadAllBytes(verifiedPath));
    }

    [Fact]
    public void ResolveSourceFilePath_UsesTheSourceRootMatchingThePrefix()
    {
        // Two source roots - a repository and its submodule - containing the same relative path.
        using var directory = TemporaryDirectory.Create();
        var repositoryFile = directory.GetFullPath("repository/src/file.cs");
        var submoduleFile = directory.GetFullPath("submodule/src/file.cs");
        repositoryFile.CreateParentDirectory();
        submoduleFile.CreateParentDirectory();
        File.WriteAllText(repositoryFile, "class C {}");
        File.WriteAllText(submoduleFile, "class C {}");

        var prefix = "/_" + Guid.NewGuid().ToString("N");
        Snapshot.RegisterSourceRootMapping(prefix + "_0/", directory.GetFullPath("repository").Value.Replace('\\', '/') + "/");
        Snapshot.RegisterSourceRootMapping(prefix + "_1/", directory.GetFullPath("submodule").Value.Replace('\\', '/') + "/");

        Assert.Equal(repositoryFile, SnapshotCallerContext.ResolveSourceFilePath(prefix + "_0/src/file.cs"));
        Assert.Equal(submoduleFile, SnapshotCallerContext.ResolveSourceFilePath(prefix + "_1/src/file.cs"));
    }

    private static SnapshotTestContext CreateDetectedTestContext(string className, string methodName, string testName)
    {
        return new SnapshotTestContext(testName) { ClassName = className, MethodName = methodName, IsDetectedFromTestFramework = true };
    }

    private static FullPath CreateSourceFile(TemporaryDirectory directory)
    {
        var path = directory.GetFullPath("Tests.cs");
        File.WriteAllText(path, "");
        return path;
    }

    private static void ValidateWithDefaultNaming(object value, SnapshotTestContext context, FullPath sourceFile, int lineNumber, SnapshotUpdateStrategy strategy, SnapshotType? type = null)
    {
        var settings = new SnapshotSettings { AutoDetectContinuousEnvironment = false, SnapshotUpdateStrategy = strategy };
        using (new SnapshotTestContextScope(context))
        {
            Snapshot.Validate(value, type, settings, sourceFile.Value, lineNumber);
        }
    }

    private static string[] GetSnapshotFileNames(TemporaryDirectory directory)
    {
        return [.. Directory.GetFiles(directory / "__snapshots__").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
    }

    // Sets the process-wide SNAPSHOTTESTING_STRATEGY variable, which every 'new SnapshotSettings()' reads, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void SnapshotUpdateStrategy_Default_EnvironmentVariableNamingTheDefaultStrategy_UsesDisallow()
    {
        // Resolving 'Default' through the property lookup it is computed by used to recurse until the
        // process ran out of stack.
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, nameof(SnapshotUpdateStrategy.Default));

        var settings = new SnapshotSettings();

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void Validate_DeletesTheActualFileLeftByAnEarlierFailedRun_WhenTheSnapshotMatches()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "correct");
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "correct");
        File.WriteAllText(directory.GetFullPath("snapshot.actual.txt"), "wrong");
        File.SetLastWriteTimeUtc(directory.GetFullPath("snapshot.actual.txt"), DateTime.UtcNow.AddDays(-1));

        Snapshot.Validate("sample", settings);

        // Approving the actual files would otherwise replace the correct snapshot with the stale output.
        Assert.False(File.Exists(directory.GetFullPath("snapshot.actual.txt")));
        Assert.Equal("correct", File.ReadAllText(directory.GetFullPath("snapshot.verified.txt")));
    }

    [Fact]
    public void Validate_DeletesTheActualFileThisProcessWrote_WhenTheSnapshotMatchesAgain()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "correct");

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", CreateDeterministicSnapshotSettings(directory, "wrong")));
        Assert.Equal("wrong", File.ReadAllText(directory.GetFullPath("snapshot.actual.txt")));

        Snapshot.Validate("sample", CreateDeterministicSnapshotSettings(directory, "correct"));

        Assert.False(File.Exists(directory.GetFullPath("snapshot.actual.txt")));
    }

    // A test project that targets several frameworks runs one test process per framework. When the output differs by
    // framework, the process whose snapshot matches must not delete the actual file the other one just wrote.
    [Fact]
    public void Validate_KeepsTheActualFileAnotherProcessWrote_WhenTheSnapshotMatches()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "correct");
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "correct");
        File.WriteAllText(directory.GetFullPath("snapshot.actual.txt"), "output of the other process");

        Snapshot.Validate("sample", settings);

        Assert.Equal("output of the other process", File.ReadAllText(directory.GetFullPath("snapshot.actual.txt")));
    }

    [Fact]
    public void Validate_OverwriteWithoutFailure_UpdatesTheSnapshotWhenAnotherProcessRemovedTheActualFile()
    {
        using var directory = TemporaryDirectory.Create();
        var verifiedPath = directory.GetFullPath("snapshot.verified.txt");
        var settings = CreateDeterministicSnapshotSettings(directory, "new") with { SnapshotUpdateStrategy = new RemoveActualFilesStrategy(SnapshotUpdateStrategy.OverwriteWithoutFailure) };
        File.WriteAllText(verifiedPath, "old");

        Snapshot.Validate("sample", settings);

        Assert.Equal("new", File.ReadAllText(verifiedPath));
        Assert.False(File.Exists(directory.GetFullPath("snapshot.actual.txt")));
    }

    /// <summary>Removes the actual files before the update, as another process validating the same snapshot may do.</summary>
    private sealed class RemoveActualFilesStrategy(SnapshotUpdateStrategy strategy) : SnapshotUpdateStrategy
    {
        public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => strategy.CanUpdateSnapshot(settings, path, expectedSnapshot, actualSnapshot);

        public override bool MustReportError(SnapshotSettings settings, string path) => strategy.MustReportError(settings, path);

        public override void UpdateFiles(SnapshotSettings settings, IReadOnlyList<SnapshotUpdateFile> filesToUpdate, IReadOnlyList<string> filesToDelete)
        {
            foreach (var file in filesToUpdate)
            {
                File.Delete(file.ActualFilePath);
            }

            strategy.UpdateFiles(settings, filesToUpdate, filesToDelete);
        }

        public override void UpdateFile(SnapshotSettings settings, string verifiedFilePath, string actualFilePath) => throw new InvalidOperationException();
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.Overwrite))]
    [InlineData(nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void Validate_OverwriteStrategies_DoNotLeaveAnActualFile(string strategyName)
    {
        using var directory = TemporaryDirectory.Create();
        var strategy = GetSnapshotUpdateStrategy(strategyName);
        var settings = CreateDeterministicSnapshotSettings(directory, "new") with { SnapshotUpdateStrategy = strategy };
        File.WriteAllText(directory.GetFullPath("snapshot.verified.txt"), "old");

        if (strategy.MustReportError(settings, directory.FullPath))
        {
            Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
        }
        else
        {
            Snapshot.Validate("sample", settings);
        }

        Assert.Equal("new", File.ReadAllText(directory.GetFullPath("snapshot.verified.txt")));
        Assert.False(File.Exists(directory.GetFullPath("snapshot.actual.txt")));
    }

    [Fact]
    public void Validate_DeletesTheStaleActualFileOfTheSnapshotsThatMatch_WhenAnotherSnapshotChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.txt"),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));

        File.WriteAllText(directory.GetFullPath("snapshot_0.verified.txt"), "value_0");
        File.WriteAllText(directory.GetFullPath("snapshot_0.actual.txt"), "stale");
        File.SetLastWriteTimeUtc(directory.GetFullPath("snapshot_0.actual.txt"), DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(directory.GetFullPath("snapshot_1.verified.txt"), "outdated");

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.False(File.Exists(directory.GetFullPath("snapshot_0.actual.txt")));
        Assert.Equal("value_1", File.ReadAllText(directory.GetFullPath("snapshot_1.actual.txt")));
    }

    // A test project that targets several frameworks runs one test process per framework, and they update the same
    // snapshot files at the same time.
    [Fact]
    public void Validate_OverwriteWithoutFailure_ToleratesConcurrentUpdatesOfTheSameSnapshot()
    {
        using var directory = TemporaryDirectory.Create();
        var verifiedPath = directory.GetFullPath("snapshot.verified.txt");
        var settings = CreateDeterministicSnapshotSettings(directory, "new") with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure };

        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            File.WriteAllText(verifiedPath, "old");
            Parallel.For(0, 4, _ =>
            {
                try
                {
                    Snapshot.Validate("sample", settings);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });
        }

        Assert.Empty(errors);
        Assert.Equal("new", File.ReadAllText(verifiedPath));
        Assert.False(File.Exists(directory.GetFullPath("snapshot.actual.txt")));
    }

    [Fact]
    public void Validate_DoesNotApplyScrubbersToBinarySnapshots()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot.verified" + context.Type.FileExtension),
        };
        settings.ScrubLinesContaining("x");

        var png = CreateSingleFramePng();
        Snapshot.Validate(png, SnapshotType.Png, settings);
        Assert.Equal(png, File.ReadAllBytes(directory.GetFullPath("snapshot.verified.png")));

        // Bytes that happen to be valid UTF-8 are still binary content.
        var utf8Bytes = "x\nkept"u8.ToArray();
        Snapshot.Validate(utf8Bytes, SnapshotType.Bmp, settings);
        Assert.Equal(utf8Bytes, File.ReadAllBytes(directory.GetFullPath("snapshot.verified.bmp")));
    }

    [Fact]
    public void Validate_TreatsASnapshotWithoutFormatAsBinary()
    {
        using var directory = TemporaryDirectory.Create();
        var comparer = new RecordingSnapshotComparer();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot.verified." + context.Extension),
        };
        settings.ScrubLinesContaining("x");
        settings.Comparers.Set(SnapshotType.Create("bin"), comparer);

        // The default path strategy stores such a snapshot as a 'bin' file, whose content is never scrubbed.
        var utf8Bytes = "x\nkept"u8.ToArray();
        Snapshot.Validate(utf8Bytes, SnapshotType.None, settings);
        Assert.Equal(utf8Bytes, File.ReadAllBytes(directory.GetFullPath("snapshot.verified.bin")));

        Snapshot.Validate(utf8Bytes, SnapshotType.None, settings);
        Assert.True(comparer.InvocationCount > 0, "The comparer registered for 'bin' was not used.");
    }

    [Fact]
    public void Validate_AppliesScrubbersToTextSnapshotsOfAnyTextFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = _ => directory / "snapshot.verified.json",
        };
        settings.ScrubLinesContaining("secret");

        Snapshot.Validate("line1\nsecret\nline3"u8.ToArray(), SnapshotType.Create("json"), settings);

        Assert.Equal("line1\nline3", File.ReadAllText(directory.GetFullPath("snapshot.verified.json")));
    }

    [Fact]
    public void Validate_AppliesScrubbersToTheFirstLineOfATextWithAByteOrderMark()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesMatching("^Date:");

        byte[] content = [0xEF, 0xBB, 0xBF, .. "Date: 2026-09-15\nkept"u8];
        Snapshot.Validate(content, SnapshotType.Default, settings);

        Assert.Equal("kept"u8.ToArray(), File.ReadAllBytes(directory.GetFullPath("snapshot.verified.txt")));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("yaml")]
    [InlineData("xml")]
    [InlineData("cs")]
    [InlineData("html")]
    [InlineData("md")]
    [InlineData("csv")]
    [InlineData("txt")]
    public void Validate_TextSnapshots_IgnoreLineEndingsAndByteOrderMark(string extension)
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / ("snapshot.verified." + extension);
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = _ => path,
        };

        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. "line1\r\nline2\r\n"u8]);

        Snapshot.Validate("line1\nline2\n"u8.ToArray(), SnapshotType.Create(extension), settings);

        Assert.False(File.Exists(directory / ("snapshot.actual." + extension)));
    }

    [Fact]
    public void Validate_BinarySnapshots_AreComparedByteForByte()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.bin";
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = _ => path,
        };

        File.WriteAllBytes(path, "line1\r\nline2"u8.ToArray());

        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("line1\nline2"u8.ToArray(), SnapshotType.Create("bin"), settings));
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' }, new byte[] { (byte)'a' })]
    [InlineData(new byte[] { (byte)'a' }, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n' }, new byte[] { (byte)'a', (byte)'\n' })]
    public void TextComparer_IgnoresALeadingByteOrderMark(byte[] expected, byte[] actual)
    {
        var comparer = new SnapshotSettings().Comparers.Get(SnapshotType.Default);

        Assert.True(comparer.Equals(new SnapshotData("txt", expected), new SnapshotData("txt", actual)));
    }

    [Fact]
    public void Validate_CustomComparerReceivesTheExtensionInTheSameFormForBothSnapshots()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "snapshot.verified.json";
        File.WriteAllText(path, "\"value\"");

        var comparer = new ExtensionRecordingSnapshotComparer();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = _ => path,
        };

        // The comparer is registered with a different case than the one the assertion uses.
        settings.Comparers.Set(SnapshotType.Create("JSON"), comparer);

        Snapshot.Validate("\"value\""u8.ToArray(), SnapshotType.Create("json"), settings);

        Assert.Equal("json", comparer.ExpectedExtension);
        Assert.Equal("json", comparer.ActualExtension);
    }

    [Fact]
    public void Validate_UnexpectedSnapshotFilesMessage_DoesNotListAnActualFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.txt"),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));
        File.WriteAllText(directory.GetFullPath("snapshot_0.verified.txt"), "value_0");
        File.WriteAllText(directory.GetFullPath("snapshot_1.verified.txt"), "value_1");
        File.WriteAllText(directory.GetFullPath("snapshot_2.verified.txt"), "value_2");

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.Contains("Unexpected snapshot files:", exception.Message);
        Assert.Contains(directory.GetFullPath("snapshot_2.verified.txt").Value, exception.Message);
        Assert.DoesNotContain("snapshot_2.actual.txt", exception.Message);
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.Disallow))]
    [InlineData(nameof(SnapshotUpdateStrategy.Overwrite))]
    [InlineData(nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void SnapshotUpdateStrategy_ToString_ReturnsThePublicName(string strategyName)
    {
        Assert.Equal(strategyName, GetSnapshotUpdateStrategy(strategyName).ToString());
    }

    // The detection targets every environment where the developer does not expect a diff tool, not only build servers
    [Theory]
    [InlineData("CI", "true", "CI")]
    [InlineData("CI", "false", "CI")]
    [InlineData("TF_BUILD", "True", "TF_BUILD")]
    [InlineData("TF_BUILD", "False", null)]
    [InlineData("GITHUB_ACTION", "__run", "GITHUB_ACTION")]
    [InlineData("BuildRunner", "MyGet", "BuildRunner")]
    [InlineData("WSL_DISTRO_NAME", "Ubuntu", "WSL_DISTRO_NAME")]
    [InlineData("DOTNET_RUNNING_IN_CONTAINER", "true", "DOTNET_RUNNING_IN_CONTAINER")]
    [InlineData("PATH", "/usr/bin", null)]
    public void BuildServerDetector_DetectsNonInteractiveEnvironments(string name, string value, string? expectedVariable)
    {
        Assert.Equal(expectedVariable, BuildServerDetector.Detect(variable => variable == name ? value : null));
    }

    // Sets the process-wide SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT variable, which every 'new SnapshotSettings()' reads, so it must not run beside any other test
    [Theory(DisableParallelization = true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    public void AutoDetectContinuousEnvironment_Default_CanBeConfiguredUsingEnvironmentVariable(string? value, bool expected)
    {
        // SnapshotSettings.Default reads the variable when it is created; create it now so no other test observes this value.
        _ = SnapshotSettings.Default;
        using var scope = new EnvironmentVariableScope("SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT", value);

        Assert.Equal(expected, new SnapshotSettings().AutoDetectContinuousEnvironment);
        Assert.False(new SnapshotSettings { AutoDetectContinuousEnvironment = false }.AutoDetectContinuousEnvironment);
    }

    // Replaces the process-wide environment detection, so it must not run beside any other test
    [Fact(DisableParallelization = true)]
    public void Validate_ErrorMessageExplainsThatTheDetectedEnvironmentDisabledUpdates()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "new") with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Overwrite,
            AutoDetectContinuousEnvironment = true,
        };
        var verifiedPath = directory.GetFullPath("snapshot.verified.txt");
        File.WriteAllText(verifiedPath, "old");

        ContinuousEnvironmentDetector.DescriptionOverride = () => "an LLM agent (ClaudeCode)";
        try
        {
            var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
            Assert.Contains("Snapshot updates are disabled because an LLM agent (ClaudeCode) was detected.", exception.Message);
            Assert.Contains("set the SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT environment variable to false, or set SnapshotSettings.AutoDetectContinuousEnvironment to false", exception.Message);
            Assert.Equal("old", File.ReadAllText(verifiedPath));

            var disabledDetectionSettings = settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow, AutoDetectContinuousEnvironment = false };
            exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", disabledDetectionSettings));
            Assert.DoesNotContain("Snapshot updates are disabled", exception.Message);
        }
        finally
        {
            ContinuousEnvironmentDetector.DescriptionOverride = null;
        }
    }

    private sealed class ExtensionRecordingSnapshotComparer : ISnapshotComparer
    {
        public string? ExpectedExtension { get; private set; }

        public string? ActualExtension { get; private set; }

        public bool Equals(SnapshotData expected, SnapshotData actual)
        {
            ExpectedExtension = expected.Extension;
            ActualExtension = actual.Extension;
            return expected.Data.AsSpan().SequenceEqual(actual.Data);
        }
    }

    // Mirrors the default path strategy: several snapshots get an index suffix, a single one keeps the bare name.
    private static SnapshotPathStrategy CreateIndexedSnapshotPathStrategy(TemporaryDirectory directory)
    {
        return context => directory / (context.SnapshotCount > 1
            ? "snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.txt"
            : "snapshot.verified.txt");
    }

    /// <summary>
    /// A test declared in a base class runs once per derived class. The test framework reports the class the
    /// test ran in, whereas the call stack could only report the declaring class, so each derived class gets
    /// its own snapshot name.
    /// </summary>
    public abstract class InheritedSnapshotTestsBase
    {
        [Fact]
        public void Validate_NamesTheSnapshotAfterTheRunningClass_WhenTheTestIsInherited()
        {
            using var directory = TemporaryDirectory.Create();
            SnapshotPathContext? capturedContext = null;
            var settings = new SnapshotSettings()
            {
                AutoDetectContinuousEnvironment = false,
                SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
                SnapshotPathStrategy = context =>
                {
                    capturedContext = context;
                    return directory / (SnapshotSettings.Default.SnapshotPathStrategy(context).Name);
                },
            };

            Snapshot.Validate("sample", settings);

            Assert.NotNull(capturedContext);
            Assert.False(capturedContext.StackWalkPerformed);
            Assert.Equal(GetType().Name, capturedContext.ClassName);
            Assert.Equal(nameof(Validate_NamesTheSnapshotAfterTheRunningClass_WhenTheTestIsInherited), capturedContext.MethodName);

            var files = Directory.GetFiles(directory.FullPath);
            Assert.Single(files);
            Assert.Equal(GetType().Name + "_" + nameof(Validate_NamesTheSnapshotAfterTheRunningClass_WhenTheTestIsInherited) + ".verified.txt", Path.GetFileName(files[0]));
        }
    }

    public sealed class InheritedSnapshotTests : InheritedSnapshotTestsBase;
}
