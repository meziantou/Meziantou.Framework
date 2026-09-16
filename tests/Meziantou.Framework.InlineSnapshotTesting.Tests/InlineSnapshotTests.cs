using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Microsoft.CodeAnalysis;
using TestUtilities;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;

public sealed partial class InlineSnapshotTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task WithSerializer()
    {
        await AssertSnapshot(
            """"
            InlineSnapshot
                .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
                .Validate(new { B = 1, A = 2 }, "");
            """",
            """"
            InlineSnapshot
                .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
                .Validate(new { B = 1, A = 2 }, """
                    A: 2
                    B: 1
                    """);
            """");
    }

    [Fact]
    public async Task WithSettings_WithSerializer()
    {
        await AssertSnapshot(
            """"
            InlineSnapshot
                .WithSettings(InlineSnapshotSettings.Default)
                .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
                .Validate(new { B = 1, A = 2 }, "");
            """",
            """"
            InlineSnapshot
                .WithSettings(InlineSnapshotSettings.Default)
                .WithSerializer(options => options.PropertyOrder = StringComparer.Ordinal)
                .Validate(new { B = 1, A = 2 }, """
                    A: 2
                    B: 1
                    """);
            """");
    }

    [Fact]
    public void DefaultBuilder_UsesTheDefaultSettings()
    {
        // default(InlineSnapshotBuilder) does not run the constructor, so the settings field is null.
        InlineSnapshotBuilder builder = default;

        builder.Validate(new object(), "{}");
    }

    [Fact]
    public void Validate_WithSettings()
    {
        InlineSnapshot.Validate(new object(), InlineSnapshotSettings.Default, "{}");
    }

    [Fact]
    public void ForceUpdateSnapshots_WithDisallowStrategy_DoesNotThrow()
    {
        // The force-update path used to call FileEditor.UpdateFile without asking the strategy first, so
        // Disallow reported a bare InvalidOperationException for a snapshot that actually matched.
        var settings = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            AutoDetectContinuousEnvironment = false,
            ForceUpdateSnapshots = true,
        };

        InlineSnapshot.Validate(new object(), settings, "{}");
    }

    [Fact]
    public void Validate_DoesNotComputeCallerContextWhenSnapshotMatches()
    {
        var exception = Record.Exception(() => InlineSnapshot.Validate(new object(), InlineSnapshotSettings.Default, "{}", "invalid\0path", 1));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ComputesCallerContextWhenSnapshotDiffers()
    {
        var settings = InlineSnapshotSettings.Default with { AutoDetectContinuousEnvironment = false };
        Assert.Throws<ArgumentException>(() => InlineSnapshot.Validate(new object(), settings, "invalid snapshot", "invalid\0path", 1));
    }

    [Fact]
    public async Task UpdateSnapshotUsingQuotedString_WithSettings()
    {
        await AssertSnapshot(
            """
            var settings = InlineSnapshotSettings.Default;
            InlineSnapshot.Validate(new object(), settings, "");
            """,
            """
            var settings = InlineSnapshotSettings.Default;
            InlineSnapshot.Validate(new object(), settings, "{}");
            """);
    }

    [Fact]
    public async Task UpdateSnapshotWhenExpectedIsNull_WithSettings()
    {
        await AssertSnapshot(
            """
            var settings = InlineSnapshotSettings.Default;
            InlineSnapshot.Validate(new object(), settings, expected: null);
            """,
            """
            var settings = InlineSnapshotSettings.Default;
            InlineSnapshot.Validate(new object(), settings, expected: "{}");
            """);
    }

    [Fact]
    public async Task UpdateSnapshotUsingQuotedString()
    {
        await AssertSnapshot(
            """
            InlineSnapshot.Validate(new object(), "");
            """,
            """
            InlineSnapshot.Validate(new object(), "{}");
            """);
    }

    [Fact]
    public async Task UpdateSnapshot_UsingMappedCallerFilePath()
    {
        await AssertSnapshot(
            """"
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            InlineSnapshot.RegisterSourceRootMapping("/_inline_snapshot_/", projectRoot.Replace('\\', '/') + "/");
            var settings = InlineSnapshotSettings.Default with
            {
                ValidateSourceFilePathUsingPdbInfoWhenAvailable = false,
                ValidateLineNumberUsingPdbInfoWhenAvailable = false,
            };
            InlineSnapshot.Validate(new { Value = 1 }, settings, "", filePath: "/_inline_snapshot_/Program.cs");
            """",
            """"
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            InlineSnapshot.RegisterSourceRootMapping("/_inline_snapshot_/", projectRoot.Replace('\\', '/') + "/");
            var settings = InlineSnapshotSettings.Default with
            {
                ValidateSourceFilePathUsingPdbInfoWhenAvailable = false,
                ValidateLineNumberUsingPdbInfoWhenAvailable = false,
            };
            InlineSnapshot.Validate(new { Value = 1 }, settings, "Value: 1", filePath: "/_inline_snapshot_/Program.cs");
            """");
    }

    [Fact]
    public async Task UpdateSnapshotPreserveComments()
    {
        await AssertSnapshot(
            """
            InlineSnapshot.Validate(new object(), /*start*/expected: /* middle */ "" /* after */);
            """,
            """
            InlineSnapshot.Validate(new object(), /*start*/expected: /* middle */ "{}" /* after */);
            """);
    }

    [Fact]
    public async Task UpdateSnapshotSupportIfDirective()
    {
        await AssertSnapshot(preprocessorSymbols: ["SampleDirective"],
            source: """
            #if SampleDirective
            InlineSnapshot.Validate(new object(), /*start*/expected: /* middle */ "" /* after */);
            #endif
            """,
            expected: """
            #if SampleDirective
            InlineSnapshot.Validate(new object(), /*start*/expected: /* middle */ "{}" /* after */);
            #endif
            """);
    }

    [Theory]
    [InlineData(nameof(CSharpStringFormats.Quoted), "\"{}\"")]
    [InlineData(nameof(CSharpStringFormats.Verbatim), "@\"{}\"")]
    [InlineData(nameof(CSharpStringFormats.Raw), "\"\"\"\n    {}\n    \"\"\"")]
    [InlineData(nameof(CSharpStringFormats.LeftAlignedRaw), "\"\"\"\n{}\n\"\"\"")]
    public async Task UpdateSnapshotSupportIfDirective_WithExplicitStringFormat(string format, string expectedLiteral)
    {
        // The compilation symbols come from the PDB. They must be read whatever the allowed string formats are,
        // otherwise the assertion sits in a disabled region of the syntax tree and cannot be found.
        await AssertSnapshot(preprocessorSymbols: ["SampleDirective"],
            source: $$"""
            #if SampleDirective
            var settings = InlineSnapshotSettings.Default with { AllowedStringFormats = CSharpStringFormats.{{format}} };
            InlineSnapshot.Validate(new object(), settings, "");
            #endif
            """,
            expected: $$"""
            #if SampleDirective
            var settings = InlineSnapshotSettings.Default with { AllowedStringFormats = CSharpStringFormats.{{format}} };
            InlineSnapshot.Validate(new object(), settings, {{expectedLiteral}});
            #endif
            """);
    }

    [Fact]
    public async Task UpdateSnapshotWhenExpectedIsNull()
    {
        await AssertSnapshot(
            """
            InlineSnapshot.Validate(new object(), expected: null);
            """,
            """
            InlineSnapshot.Validate(new object(), expected: "{}");
            """);
    }

    [Fact]
    public async Task UpdateSnapshotUsingRawString()
    {
        await AssertSnapshot(
            """"
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            InlineSnapshot.Validate(data, "");
            """",
            """"
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            InlineSnapshot.Validate(data, """
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """);
            """");
    }

    [Fact]
    public async Task UpdateSnapshotUsingRawString_Indentation()
    {
        await AssertSnapshot(
            """"
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            InlineSnapshot.
                Validate(data, "");
            """",
            """"
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            InlineSnapshot.
                Validate(data, """
                    FirstName: Gérald
                    LastName: Barré
                    NickName: meziantou
                    """);
            """");
    }

    [Fact]
    public async Task UpdateSnapshotUsingVerbatimWhenCSharpLanguageIs10()
    {
        await AssertSnapshot(
            """"
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            InlineSnapshot.Validate(data, "");
            """",
            """"
            var data = new
            {
                FirstName = "Gérald",
                LastName = "Barré",
                NickName = "meziantou",
            };
            InlineSnapshot.Validate(data, @"FirstName: Gérald
            LastName: Barré
            NickName: meziantou");
            """",
            languageVersion: "10", forceUpdateSnapshots: true);
    }

    [Fact]
    public async Task SupportHelperMethods()
    {
        await AssertSnapshot(
            """"
            Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
            }
            """",
            """"
            Helper("{}");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task SupportHelperMethods_SnapshotParameterIsNotTheFirstParameter()
    {
        // The parameter lookup used to bound its loop by the length of the parameter *name*, so a short name on a
        // later parameter was never found and the value-matching fallback rewrote the first matching argument
        // instead: "Helper(null, null)" became "Helper(\"<null>\", null)".
        await AssertSnapshot(
            """"
            Helper(null, null);

            [InlineSnapshotAssertion(nameof(v))]
            static void Helper(object data, string v, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, v, filePath, lineNumber);
            }
            """",
            """"
            Helper(null, "<null>");

            [InlineSnapshotAssertion(nameof(v))]
            static void Helper(object data, string v, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, v, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public void HelperMethod_WithUnknownParameterName_ReportsTheAttribute()
    {
        var exception = Assert.Throws<InlineSnapshotException>(() => HelperWithUnknownParameterName(new object(), "not the actual snapshot"));

        Assert.Contains(nameof(InlineSnapshotAssertionAttribute), exception.Message);
        Assert.Contains("thisParameterDoesNotExist", exception.Message);
    }

    [InlineSnapshotAssertion("thisParameterDoesNotExist")]
    private static void HelperWithUnknownParameterName(object data, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        // The call is only located when the environment allows updating the snapshot
        var settings = InlineSnapshotSettings.Default with { AutoDetectContinuousEnvironment = false };
        InlineSnapshot.Validate(data, settings, expected, filePath, lineNumber);
    }

    [Fact]
    public async Task SupportAsyncHelperMethods()
    {
        await AssertSnapshot(
            """"
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
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """",
            """"
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
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """");
    }

    [Fact]
    public async Task SupportAsyncHelperMethods_WithAsyncCode()
    {
        await AssertSnapshot(
            """"
            await Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """",
            """"
            await Helper("""
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """);

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task SupportAsyncHelperMethods_WithAsyncCodeAndMultipleInvocation()
    {
        await AssertSnapshot(
            """"
            await Helper("", GetValue());

            string GetValue() => "";

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper(string expected, string dummy, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """",
            """"
            await Helper("""
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """, GetValue());

            string GetValue() => "";

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper(string expected, string dummy, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task SupportMultipleAsyncHelperMethods_WithAsyncCode()
    {
        await AssertSnapshot(
            """"
            await Helper1("");

            await Helper2("");

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper1(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper2(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """",
            """"
            await Helper1("""
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """);

            await Helper2("""
                FirstName: Gérald
                LastName: Barré
                NickName: meziantou
                """);

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper1(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }

            [InlineSnapshotAssertion(nameof(expected))]
            static async System.Threading.Tasks.Task Helper2(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                await System.Threading.Tasks.Task.Yield();
                var data = new
                {
                    FirstName = "Gérald",
                    LastName = "Barré",
                    NickName = "meziantou",
                };
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task SupportAsyncGenericHelperMethods()
    {
        await AssertSnapshot(
            """"
            await Helper<int>("");

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper<T>(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """",
            """"
            await Helper<int>("{}");

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper<T>(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """");
    }

    [Fact]
    public async Task SupportMultiLevelsHelperMethods()
    {
        await AssertSnapshot(
            """"
            Helper("");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                Helper2(expected, filePath, lineNumber);
            }

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper2(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
            }
            """",
            """"
            Helper("{}");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                Helper2(expected, filePath, lineNumber);
            }

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper2(string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(new object(), expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task UpdateMultipleSnapshots()
    {
        await AssertSnapshot(
            """"
            Console.WriteLine("first");
            InlineSnapshot.Validate(new { A = 1, B = 2 }, "");
            Console.WriteLine("Second");
            InlineSnapshot.Validate(new { A = 3, B = 4 }, "");
            """",
            """"
            Console.WriteLine("first");
            InlineSnapshot.Validate(new { A = 1, B = 2 }, """
                A: 1
                B: 2
                """);
            Console.WriteLine("Second");
            InlineSnapshot.Validate(new { A = 3, B = 4 }, """
                A: 3
                B: 4
                """);
            """");
    }

    [Fact]
    public async Task UpdateMultipleSnapshots_NonLinearOrder()
    {
        await AssertSnapshot(
            """"
            B(); A();

            void A() => InlineSnapshot.Validate(new { A = 1, B = 2 }, "");
            void B() => InlineSnapshot.Validate(new { A = 3, B = 4 }, "");
            """",
            """"
            B(); A();

            void A() => InlineSnapshot.Validate(new { A = 1, B = 2 }, """
                A: 1
                B: 2
                """);
            void B() => InlineSnapshot.Validate(new { A = 3, B = 4 }, """
                A: 3
                B: 4
                """);
            """");
    }

    [Fact]
    public async Task UpdateMultipleSnapshots_InsideLambda()
    {
        // After the first update, the updated call used to be searched as the largest call around its position, which is
        // the enclosing Invoke call. The line shift was then wrong and the second snapshot could not be found.
        await AssertSnapshot(
            """"
            Invoke(() =>
            {
                InlineSnapshot.Validate(new { A = 1, B = 2 }, "");
                InlineSnapshot.Validate(new { A = 3, B = 4 }, "");
            });

            static void Invoke(Action action) => action();
            """",
            """"
            Invoke(() =>
            {
                InlineSnapshot.Validate(new { A = 1, B = 2 }, """
                    A: 1
                    B: 2
                    """);
                InlineSnapshot.Validate(new { A = 3, B = 4 }, """
                    A: 3
                    B: 4
                    """);
            });

            static void Invoke(Action action) => action();
            """");
    }

    [Fact]
    public async Task UpdateMultipleSnapshots_SameLine()
    {
        await AssertSnapshot(
            """"
            InlineSnapshot.Validate(1, ""); InlineSnapshot.Validate(2, "");
            """",
            """"
            InlineSnapshot.Validate(1, "1"); InlineSnapshot.Validate(2, "2");
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_SameCallExecutedTwice()
    {
        // The second execution was compiled with the previous snapshot, but the file already holds the new one. It used to
        // throw, which is also what happened to the test process of another target framework updating the same file.
        await AssertSnapshot(
            """"
            for (var i = 0; i < 2; i++)
            {
                InlineSnapshot.Validate(new { A = 1 }, "");
            }
            """",
            """"
            for (var i = 0; i < 2; i++)
            {
                InlineSnapshot.Validate(new { A = 1 }, "A: 1");
            }
            """");
    }

    [Fact]
    public void Overwrite_WritesThroughSymbolicLink()
    {
        using var directory = TemporaryDirectory.Create();
        var target = directory.CreateTextFile("Target.cs", "old");
        var link = directory.GetFullPath("Link.cs");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            global::Xunit.Assert.Skip("Creating a symbolic link requires a privilege: " + ex.Message);
        }

        var newFile = directory.CreateTextFile("New.cs", "new");
        new FileInfo(newFile).IsReadOnly = true;

        SnapshotUpdateStrategy.Overwrite.UpdateFile(InlineSnapshotSettings.Default, link, newFile);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(newFile));
    }

    [Fact]
    public async Task UpdateSnapshot_FileEditedSinceBuild()
    {
        // The call is no longer on the line reported by the compiler, as it happens when another test process edits the file
        await AssertSnapshot(
            """"
            System.IO.File.WriteAllText(GetPath(), "// Inserted line\n" + System.IO.File.ReadAllText(GetPath()));
            InlineSnapshot.Validate(new { A = 1 }, "");

            static string GetPath([CallerFilePath] string path = null) => path;
            """",
            """"
            // Inserted line
            System.IO.File.WriteAllText(GetPath(), "// Inserted line\n" + System.IO.File.ReadAllText(GetPath()));
            InlineSnapshot.Validate(new { A = 1 }, "A: 1");

            static string GetPath([CallerFilePath] string path = null) => path;
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_HelperResultAssignedToVariable()
    {
        // The PDB reports the column of the statement. Only a call starting the statement used to be found.
        await AssertSnapshot(
            """"
            var result = Helper(new { A = 1 }, "");

            [InlineSnapshotAssertion(nameof(expected))]
            static int Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return 0;
            }
            """",
            """"
            var result = Helper(new { A = 1 }, "A: 1");

            [InlineSnapshotAssertion(nameof(expected))]
            static int Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return 0;
            }
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_HelperNestedInAnotherCall()
    {
        await AssertSnapshot(
            """"
            GC.KeepAlive(Helper(new { A = 1 },
                ""));

            [InlineSnapshotAssertion(nameof(expected))]
            static int Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return 0;
            }
            """",
            """"
            GC.KeepAlive(Helper(new { A = 1 },
                "A: 1"));

            [InlineSnapshotAssertion(nameof(expected))]
            static int Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return 0;
            }
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_AwaitWithConfigureAwait()
    {
        await AssertSnapshot(
            """"
            await Helper(new { A = 1 }, "").ConfigureAwait(false);

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """",
            """"
            await Helper(new { A = 1 }, "A: 1").ConfigureAwait(false);

            [InlineSnapshotAssertion(nameof(expected))]
            static System.Threading.Tasks.Task Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
            {
                InlineSnapshot.Validate(data, expected, filePath, lineNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_NullConditionalInvocation()
    {
        await AssertSnapshot(
            """"
            var checker = new Checker();
            checker?.Check(new { A = 1 }, "");

            sealed class Checker
            {
                [InlineSnapshotAssertion("expected")]
                public void Check(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    => InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """",
            """"
            var checker = new Checker();
            checker?.Check(new { A = 1 }, "A: 1");

            sealed class Checker
            {
                [InlineSnapshotAssertion("expected")]
                public void Check(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    => InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task SupportExtensionHelperMethods()
    {
        // The snapshot parameter index counts the receiver, which is not an argument when using the extension syntax
        await AssertSnapshot(
            """"
            new { A = 1 }.Check();
            new { A = 2 }.CheckWithMessage("", "message");
            HelperExtensions.Check(new { A = 3 });

            static class HelperExtensions
            {
                [InlineSnapshotAssertion("expected")]
                public static void Check(this object data, string expected = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    => InlineSnapshot.Validate(data, expected, filePath, lineNumber);

                [InlineSnapshotAssertion("expected")]
                public static void CheckWithMessage(this object data, string expected, string message, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    => InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """",
            """"
            new { A = 1 }.Check("A: 1");
            new { A = 2 }.CheckWithMessage("A: 2", "message");
            HelperExtensions.Check(new { A = 3 }, "A: 3");

            static class HelperExtensions
            {
                [InlineSnapshotAssertion("expected")]
                public static void Check(this object data, string expected = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    => InlineSnapshot.Validate(data, expected, filePath, lineNumber);

                [InlineSnapshotAssertion("expected")]
                public static void CheckWithMessage(this object data, string expected, string message, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                    => InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            }
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_AddParameter_AfterOmittedOptionalParameter()
    {
        // A positional argument would bind to the message parameter
        await AssertSnapshot(
            """"
            Helper(new { A = 1 });

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(object data, string message = null, string expected = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                => InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            """",
            """"
            Helper(new { A = 1 }, expected: "A: 1");

            [InlineSnapshotAssertion(nameof(expected))]
            static void Helper(object data, string message = null, string expected = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
                => InlineSnapshot.Validate(data, expected, filePath, lineNumber);
            """");
    }

    [Fact]
    public async Task MergeToolStrategy_WhenDiffToolsAreDisabled_UpdatesTheNextSnapshotsOfTheFile()
    {
        // The strategy deletes the temporary file when no merge tool starts, but the line shifts of the edits it contained
        // were kept. The next snapshots of the file were then searched on the wrong lines.
        // The last snapshot is only updated when the merge-tool snapshots report a plain snapshot difference.
        await AssertSnapshot(
            """"
            var settings = InlineSnapshotSettings.Default with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool };
            try { InlineSnapshot.Validate(new { A = 1, B = 2 }, settings, ""); } catch (InlineSnapshotAssertionException) { }
            try { InlineSnapshot.Validate(new { A = 3, B = 4 }, settings, ""); } catch (InlineSnapshotAssertionException) { }
            InlineSnapshot.Validate(new { A = 5, B = 6 }, "");
            """",
            """"
            var settings = InlineSnapshotSettings.Default with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool };
            try { InlineSnapshot.Validate(new { A = 1, B = 2 }, settings, ""); } catch (InlineSnapshotAssertionException) { }
            try { InlineSnapshot.Validate(new { A = 3, B = 4 }, settings, ""); } catch (InlineSnapshotAssertionException) { }
            InlineSnapshot.Validate(new { A = 5, B = 6 }, """
                A: 5
                B: 6
                """);
            """");
    }

    [Fact]
    public void Validate_HelperNotForwardingCallerInformation_ReportsTheMissingParameters()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = new NoOpUpdateStrategy(),
        };

        var exception = Assert.Throws<InlineSnapshotException>(() => HelperNotForwardingCallerInformation(settings, "not the snapshot"));
        Assert.Contains("[CallerFilePath] and [CallerLineNumber]", exception.Message);

        // The snapshot difference is reported along with the reason the snapshot cannot be updated
        Assert.Contains("- not the snapshot", exception.Message);
        Assert.Contains("+ {}", exception.Message);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void HelperNotForwardingCallerInformation(InlineSnapshotSettings settings, string expected)
    {
        InlineSnapshot.Validate(new object(), settings, expected);
    }

    private sealed class NoOpUpdateStrategy : SnapshotUpdateStrategy
    {
        public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

        public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

        public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
        {
        }
    }

    [Fact]
    public async Task UpdateSnapshotWhenForceUpdateSnapshotsIsEnabled()
    {
        await AssertSnapshot(forceUpdateSnapshots: true,
            source: """"
            InlineSnapshot.Validate(new object(), """
                {}
                """);
            """",
            expected: """
            InlineSnapshot.Validate(new object(), "{}");
            """);
    }

    [Fact]
    public async Task DoNotForceUpdateSnapshotOnCI()
    {
        // Reformatting a matching snapshot is still a write to the source file, so continuous integration
        // detection must stop it just like it stops updating a failing one.
        await AssertSnapshot(forceUpdateSnapshots: true,
            autoDetectCI: true,
            environmentVariables: new[] { new KeyValuePair<string, string>("CI", "true") },
            source: """"
            InlineSnapshot.Validate(new object(), """
                {}
                """);
            """");
    }

    [Fact]
    public async Task DoNotUpdateSnapshotWhenForceUpdateSnapshotsIsDisableAndTheValueIsOk()
    {
        await AssertSnapshot(forceUpdateSnapshots: false,
            source: """"
            InlineSnapshot.Validate(new object(), """
                {}
                """);
            """");
    }

    [Fact]
    public async Task UpdateSnapshot_AddParameter()
    {
        await AssertSnapshot(
            """
            InlineSnapshot.Validate("");
            """,
            """
            InlineSnapshot.Validate("", "");
            """);
    }

    [Fact]
    public async Task UpdateSnapshot_MultiLine_AddParameter()
    {
        await AssertSnapshot(
            """
            InlineSnapshot
                .Validate("");
            """,
            """
            InlineSnapshot
                .Validate("", "");
            """);
    }

    [Fact]
    public async Task UpdateSnapshot_Builder_MultiLine_AddParameter()
    {
        await AssertSnapshot(
            """
            InlineSnapshot.WithSettings(default(InlineSnapshotSettings))
                .Validate("");
            """,
            """
            InlineSnapshot.WithSettings(default(InlineSnapshotSettings))
                .Validate("", "");
            """);
    }

    [Fact]
    public void ScrubLinesMatching_Regex()
    {
        InlineSnapshot
            .WithSettings(settings => settings.ScrubLinesMatching(Line2Regex()))
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
    public void ScrubLinesMatching_InvalidPattern_ThrowsWhenConfigured()
    {
        var settings = new InlineSnapshotSettings();
        Assert.ThrowsAny<ArgumentException>(() => settings.ScrubLinesMatching("Line["));
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

    [Theory]
    [InlineData("Line1\nLine2\nLine3", "Line1\nLine2")]
    [InlineData("Line1\r\nLine2\r\nLine3", "Line1\r\nLine2")]
    [InlineData("Line1\nLine2\nLine3\n", "Line1\nLine2\n")]
    [InlineData("Line3", "")]
    public void ScrubLines_RemoveLastLine_DoesNotLeaveTrailingLineBreak(string text, string expected)
    {
        var settings = new InlineSnapshotSettings();
        settings.ScrubLinesContaining(StringComparison.Ordinal, "Line3");

        Assert.Equal(expected, Assert.Single(settings.Scrubbers).Scrub(text));
    }

    [Theory]
    [InlineData("a\nb\n", "// a\n// b\n")]
    [InlineData("a\r\nb", "// a\r\n// b")]
    [InlineData("a\r\rb", "// a\r// \r// b")]
    [InlineData("", "")]
    [InlineData("a\u2028b\fc\u0085d\u2029e", "// a\u2028b\fc\u0085d\u2029e")]
    public void ScrubLinesWithReplace_SplitsOnlyOnLineBreaks(string text, string expected)
    {
        var settings = new InlineSnapshotSettings();
        settings.ScrubLinesWithReplace(line => "// " + line);

        Assert.Equal(expected, Assert.Single(settings.Scrubbers).Scrub(text));
    }

    [Fact]
    public void ScrubLinesContaining_DoesNotSplitOnUnicodeLineSeparators()
    {
        var settings = new InlineSnapshotSettings();
        settings.ScrubLinesContaining(StringComparison.Ordinal, "Name");

        Assert.Equal("Other", Assert.Single(settings.Scrubbers).Scrub("Other\nName: x\u2028Password: y"));
    }

    [Fact]
    public void Scrub_Guid()
    {
        var guids = new[]
        {
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("6ff5182f-7644-4bc1-a3a4-38092cb3663a"),
            Guid.Empty,
        };

        // Use parallelism to be sure Guids are not shared between serializations
        Parallel.For(1, 1000, _ =>
        {
            InlineSnapshot
                .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.ScrubGuid()))
                .Validate(guids, """
                    - 00000000-0000-0000-0000-000000000001
                    - 00000000-0000-0000-0000-000000000001
                    - 00000000-0000-0000-0000-000000000002
                    - 00000000-0000-0000-0000-000000000000
                    """);
        });
    }

    [Fact]
    public void Scrub_UseRelativeTimeSpan()
    {
        var start = TimeSpan.FromSeconds(1);
        var values = new[]
        {
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

        InlineSnapshot
            .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.UseRelativeTimeSpan(start)))
            .Validate(values, """
                - -00:00:01
                - 00:00:00
                - 00:00:01
                """);
    }

    [Fact]
    public void Scrub_UseRelativeDateTime()
    {
        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var values = new[]
        {
            start,
            start.AddSeconds(1),
            start.AddSeconds(2),
        };

        InlineSnapshot
            .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.UseRelativeDateTime(start)))
            .Validate(values, """
                - 00:00:00
                - 00:00:01
                - 00:00:02
                """);
    }

    [Fact]
    public void Scrub_UseRelativeDateTimeOffset()
    {
        var start = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var values = new[]
        {
            start,
            start.AddSeconds(1),
            start.AddSeconds(2),
        };

        InlineSnapshot
            .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.UseRelativeDateTimeOffset(start)))
            .Validate(values, """
                - 00:00:00
                - 00:00:01
                - 00:00:02
                """);
    }

    [Fact]
    public void Scrub_Value_Default()
    {
        var value = new
        {
            ints = new int[] { 1, 2 },
            longs = new long[] { 1, 2 },
        };
        InlineSnapshot
            .WithSerializer(options => options.ScrubValue<int>())
            .Validate(value, """
                ints:
                  - Int32_0
                  - Int32_1
                longs:
                  - 1
                  - 2
                """);
    }

    [Fact]
    public void Scrub_Value()
    {
        var value = new
        {
            ints = new int[] { 1, 2 },
            longs = new long[] { 1, 2 },
        };
        InlineSnapshot
            .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.ScrubValue<int>(i => (i + 1).ToString(CultureInfo.InvariantCulture))))
            .Validate(value, """
                ints:
                  - 2
                  - 3
                longs:
                  - 1
                  - 2
                """);
    }

    [Fact]
    public void Scrub_Value_2()
    {
        var value = new
        {
            strs = new string[] { "a", "b" },
        };
        InlineSnapshot
            .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.ScrubValue<string>((value, index) => "prefix-" + index.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)))
            .Validate(value, """
                strs:
                  - prefix-0
                  - prefix-1
                """);
    }

    [Fact]
    public void Scrub_Value_Incremental_MultipleTypes()
    {
        var value = new
        {
            a = new string[] { "a", "b" },
            b = new int[] { 1, 2, 2 },
        };
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubValue<string>((value, index) => "str-" + index.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
                options.ScrubValue<int>((value, index) => "int-" + index.ToString(CultureInfo.InvariantCulture));
            })
            .Validate(value, """
                a:
                  - str-0
                  - str-1
                b:
                  - int-0
                  - int-1
                  - int-1
                """);
    }

    [Fact]
    public void Scrub_Json()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$.prop", node => "[redacted]");
            })
            .Validate(new JsonObject() { ["prop"] = "value", ["other"] = "dummy" }, """
                {
                  "prop": "[redacted]",
                  "other": "dummy"
                }
                """);
    }

    [Theory]
    [InlineData("application/json", "")]
    [InlineData("application/json", "not json")]
    [InlineData("application/xml", "")]
    [InlineData("application/xml", "not xml")]
    public void Scrub_ValueThatCannotBeParsed_IsFormattedAsWithoutScrubber(string mediaType, string body)
    {
        // The formatters write a value they cannot parse as-is, while the scrubbers used to throw
        // Serializing a content computes its Content-Length header, so each serialization gets its own content
        using var unscrubbedContent = new StringContent(body, Encoding.UTF8, mediaType);
        using var content = new StringContent(body, Encoding.UTF8, mediaType);
        var expected = HumanReadableSerializer.Serialize(unscrubbedContent, CreateOptions(scrub: false));
        var actual = HumanReadableSerializer.Serialize(content, CreateOptions(scrub: true));

        Assert.Equal(expected, actual);

        static HumanReadableSerializerOptions CreateOptions(bool scrub)
        {
            var options = new HumanReadableSerializerOptions();
            options.AddHttpConverters(new HumanReadableHttpOptions());
            options.AddJsonFormatter(new JsonFormatterOptions { WriteIndented = true });
            options.AddXmlFormatter(new XmlFormatterOptions { WriteIndented = true });
            if (scrub)
            {
                options.ScrubJsonValue("$.a", _ => "[redacted]");
                options.ScrubXmlAttribute("//@a", _ => "[redacted]");
                options.ScrubXmlNode("//a", node => node);
            }

            return options;
        }
    }

    [Fact]
    public void ScrubXml_InnerFormatterNotIndented_IsNotIndented()
    {
        // The scrubbers used to indent the document before handing it to a formatter configured not to indent
        using var scrubbedContent = new StringContent("""<root><item a="[redacted]" /></root>""", Encoding.UTF8, "application/xml");
        using var content = new StringContent("""<root><item a="1" /></root>""", Encoding.UTF8, "application/xml");
        var expected = HumanReadableSerializer.Serialize(scrubbedContent, CreateOptions(scrub: false));
        var actual = HumanReadableSerializer.Serialize(content, CreateOptions(scrub: true));

        Assert.Equal(expected, actual);

        static HumanReadableSerializerOptions CreateOptions(bool scrub)
        {
            var options = new HumanReadableSerializerOptions();
            options.AddHttpConverters(new HumanReadableHttpOptions());
            options.AddXmlFormatter(new XmlFormatterOptions { WriteIndented = false });
            if (scrub)
            {
                options.ScrubXmlAttribute("//@a", _ => "[redacted]");
            }

            return options;
        }
    }

    [Fact]
    public void Scrub_Json_PreserveInnerOptions()
    {
        var subject = new JsonObject()
        {
            ["prop"] = "value",
            ["other"] = "dummy",
        };
        InlineSnapshot
            .WithSettings(settings =>
            {
                settings.UseHumanReadableSerializer(settings =>
                {
                    settings.AddJsonFormatter(new JsonFormatterOptions() { WriteIndented = true, OrderProperties = true });
                });
            })
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$.prop", node => "[redacted]");
            })
            .Validate(subject, """
                {
                  "other": "dummy",
                  "prop": "[redacted]"
                }
                """);
    }

    [Fact]
    public void Scrub_Json_PreserveInnerOptions2()
    {
        var subject = new JsonObject()
        {
            ["prop"] = "value",
            ["other"] = "dummy",
        };
        InlineSnapshot
           .WithSettings(settings =>
           {
               settings.UseHumanReadableSerializer(settings =>
               {
                   settings.AddJsonFormatter(new JsonFormatterOptions() { WriteIndented = true, OrderProperties = false });
               });
           })
           .WithSerializer(options =>
           {
               options.ScrubJsonValue("$.prop", node => "[redacted]");
           })
           .Validate(subject, """
                {
                  "prop": "[redacted]",
                  "other": "dummy"
                }
                """);
    }

    [Fact]
    public void Scrub_Json_DocumentRoot_Replace()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => "[redacted]");
            })
            .Validate(new JsonObject() { ["secret"] = "sensitive-value" }, """
                "[redacted]"
                """);
    }

    [Fact]
    public void Scrub_Json_DocumentRoot_ReplaceWithNode()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => (JsonNode)new JsonObject() { ["scrubbed"] = true });
            })
            .Validate(new JsonObject() { ["secret"] = "sensitive-value" }, """
                {
                  "scrubbed": true
                }
                """);
    }

    [Fact]
    public void Scrub_Json_DocumentRoot_Remove()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => (JsonNode?)null);
            })
            .Validate(new JsonObject() { ["secret"] = "sensitive-value" }, "null");
    }

    [Fact]
    public void Scrub_Json_DocumentRoot_ReturnSameInstance()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => node);
            })
            .Validate(new JsonObject() { ["secret"] = "value" }, """
                {
                  "secret": "value"
                }
                """);
    }

    [Fact]
    public void Scrub_Json_ArrayDocumentRoot_Replace()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => "[redacted]");
            })
            .Validate(new JsonArray("sensitive-value"), """
                "[redacted]"
                """);
    }

    [Fact]
    public void Scrub_Json_ArrayDocumentRoot_Remove()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => (JsonNode?)null);
            })
            .Validate(new JsonArray("sensitive-value"), "null");
    }

    [Fact]
    public void Scrub_Json_ScalarDocumentRoot_Replace()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => "[redacted]");
            })
            .Validate(JsonValue.Create("sensitive-value"), """
                "[redacted]"
                """);
    }

    [Fact]
    public void Scrub_Json_ScalarDocumentRoot_Remove()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => (JsonNode?)null);
            })
            .Validate(JsonValue.Create("sensitive-value"), "null");
    }

    [Fact]
    public void Scrub_Json_NullProperty_IsNotScrubbed()
    {
        // A JSON null has no JsonNode to hand to the scrubber, so the match is left as-is.
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$.secret", node => "[redacted]");
            })
            .Validate(new JsonObject() { ["secret"] = null, ["other"] = "dummy" }, """
                {
                  "secret": null,
                  "other": "dummy"
                }
                """);
    }

    [Fact]
    public void Scrub_Json_NullArrayElement_IsNotScrubbed()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$.values[*]", node => "[redacted]");
            })
            .Validate(new JsonObject() { ["values"] = new JsonArray("a", null, "b") }, """
                {
                  "values": [
                    "[redacted]",
                    null,
                    "[redacted]"
                  ]
                }
                """);
    }

    [Fact]
    public void Scrub_Json_NullDocumentRoot_IsNotScrubbed()
    {
        using var document = JsonDocument.Parse("null");
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$", node => "[redacted]");
            })
            .Validate(document, "null");
    }

    [Fact]
    public void Scrub_Json_ArrayElement_ReturnSameInstance()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubJsonValue("$.values[0]", node => node);
            })
            .Validate(new JsonObject() { ["values"] = new JsonArray("a", "b") }, """
                {
                  "values": [
                    "a",
                    "b"
                  ]
                }
                """);
    }

    [Fact]
    public void ScrubXmlAttribute_Remove()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlAttribute("//item/@a", attribute => null);
            })
            .Validate(XDocument.Parse("""
                <root>
                  <item a="1">test1</item>
                  <item a="2">test2</item>
                </root>
                """), """
                <root>
                  <item>test1</item>
                  <item>test2</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlAttribute_RemoveSeveralAttributesOfTheSameElement()
    {
        // Removing an attribute during the lazy XPath enumeration used to stop the enumeration of the element's other attributes
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlAttribute("//@*", attribute => null);
            })
            .Validate(XDocument.Parse("""
                <root a="1" b="2" c="3">
                  <item d="4" e="5">test</item>
                </root>
                """), """
                <root>
                  <item>test</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlAttribute_UpdateValue()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlAttribute("//item/@a", attribute => "dummy");
            })
            .Validate(XDocument.Parse("""
                <root>
                  <item a="1">test1</item>
                  <item a="2">test2</item>
                </root>
                """), """
                <root>
                  <item a="dummy">test1</item>
                  <item a="dummy">test2</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlAttribute_Xmlns()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                var ns = new XmlNamespaceManager(new NameTable());
                ns.AddNamespace("sample", "https://example.com");
                options.ScrubXmlAttribute("//sample:item/@a", ns, attribute => "dummy");
            })
            .Validate(XDocument.Parse("""
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                  <item a="2">test2</item>
                </root>
                """), """
                <root xmlns:ns="https://example.com">
                  <ns:item a="dummy">test1</ns:item>
                  <item a="2">test2</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlNode_Remove()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("//item", node => null);
            })
            .Validate(XDocument.Parse("""
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                  <item a="2">test2</item>
                </root>
                """), """
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlNode_ReturnSameInstance()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("//item", node => node);
            })
            .Validate(XDocument.Parse("""
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                  <item a="2">test2</item>
                </root>
                """), """
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                  <item a="2">test2</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlNode_SetValue()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("//item", node => ((XElement)node).SetValue("dummy"));
            })
            .Validate(XDocument.Parse("""
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                  <item a="2">test2</item>
                </root>
                """), """
                <root xmlns:ns="https://example.com">
                  <ns:item a="1">test1</ns:item>
                  <item a="2">dummy</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlNode_DocumentElement_ReturnSameInstance()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("/root", node => node);
            })
            .Validate(XDocument.Parse("""
                <root>
                  <item>test1</item>
                </root>
                """), """
                <root>
                  <item>test1</item>
                </root>
                """);
    }

    [Fact]
    public void ScrubXmlNode_DocumentElement_SetValue()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("/root", node => ((XElement)node).SetValue("dummy"));
            })
            .Validate(XDocument.Parse("""
                <root>
                  <item>sensitive-value</item>
                </root>
                """), "<root>dummy</root>");
    }

    [Fact]
    public void ScrubXmlNode_DocumentElement_Replace()
    {
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("/root", node => new XElement("redacted"));
            })
            .Validate(XDocument.Parse("""
                <root>
                  <item>sensitive-value</item>
                </root>
                """), "<redacted />");
    }

    [Fact]
    public void ScrubXmlNode_DocumentElement_Remove()
    {
        // Removing the document element leaves an empty document.
        InlineSnapshot
            .WithSerializer(options =>
            {
                options.ScrubXmlNode("/root", node => null);
            })
            .Validate(XDocument.Parse("""
                <root>
                  <item>sensitive-value</item>
                </root>
                """), "");
    }

    [Theory]
    [InlineData("CI", "true")]
    [InlineData("CI", "TRUE")]
    [InlineData("CI", "TruE")]
    [InlineData("GITLAB_CI", "true")]
    public async Task DoNotUpdateOnCI(string key, string value)
    {
        await AssertSnapshot($$"""
            InlineSnapshot.Validate(new object(), "");
            """,
            autoDetectCI: true,
            environmentVariables: new[] { new KeyValuePair<string, string>(key, value) });
    }

    [Fact]
    public async Task GeneratedSourceRootFile_IsEmbeddedInBinlog()
    {
        await using var directory = TemporaryDirectory.Create();
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = repositoryRoot / "src" / "Meziantou.Framework.InlineSnapshotTesting" / "Meziantou.Framework.InlineSnapshotTesting.csproj";
        var targetsPath = repositoryRoot / "src" / "Meziantou.Framework.InlineSnapshotTesting" / "build" / "Meziantou.Framework.InlineSnapshotTesting.targets";

        CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{{targetsPath}}" />
              <PropertyGroup>
                <TargetFramework>{{TargetFrameworkHelper.GetTargetFrameworkMoniker()}}</TargetFramework>
                <DeterministicSourcePaths>true</DeterministicSourcePaths>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{projectPath}}" />
              </ItemGroup>
            </Project>
            """);

        CreateTextFile("Class1.cs", """
            public static class Class1
            {
                public static int Value => 42;
            }
            """);

        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        var binlogPath = directory.GetFullPath("build.binlog");
        await ExecuteDotNet(dotnetPath, directory.FullPath, ["build", "--disable-build-servers", "/bl:" + binlogPath], expectedExitCode: 0);

        AssertBinlogContains(binlogPath, "InlineSnapshotTestingSourceRoot.g.cs");

        FullPath CreateTextFile(string path, string content)
        {
            var fullPath = directory.GetFullPath(path);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }
    }

    [SuppressMessage("Design", "MA0042:Do not use blocking calls in an async method", Justification = "Not supported on .NET Framework")]
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Not supported on .NET Framework")]
    private async Task AssertSnapshot([StringSyntax("c#-test")] string source, [StringSyntax("c#-test")] string? expected = null, bool launchDebugger = false, string languageVersion = "11", bool autoDetectCI = false, bool forceUpdateSnapshots = false, IEnumerable<KeyValuePair<string, string>>? environmentVariables = null, string[]? preprocessorSymbols = null)
    {
        await using var directory = TemporaryDirectory.Create();
        var projectPath = CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetType>exe</TargetType>
                <TargetFramework>{{TargetFrameworkHelper.GetTargetFrameworkMoniker()}}</TargetFramework>
                <LangVersion>{{languageVersion}}</LangVersion>
                <Nullable>disable</Nullable>
                <DebugType>portable</DebugType>
                <DefineConstants>{{string.Join(';', preprocessorSymbols ?? [])}}</DefineConstants>
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

        testOutputHelper.WriteLine("Project:\n" + File.ReadAllText(projectPath));

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

                    System.Console.WriteLine(InlineSnapshotSettings.Default.ToString());

                    System.Console.WriteLine();
                    System.Console.WriteLine("Environment variables:");
                    foreach(System.Collections.DictionaryEntry e in System.Environment.GetEnvironmentVariables())
                    {
                        System.Console.WriteLine($"{e.Key}={e.Value}");
                    }
                }
            }
            """");

        var mainPath = CreateTextFile("Program.cs", source);

        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        testOutputHelper.WriteLine("Using dotnet: " + dotnetPath);
        Assert.NotNull(dotnetPath);

        // force language-version because otherwise it may be "preview" instead of a numeric value, so autodection won't work.
        testOutputHelper.WriteLine("Restoring project");
        await ExecuteDotNet($"restore --disable-build-servers -p:LangVersion={languageVersion}", expectedExitCode: 0);

        testOutputHelper.WriteLine("Building project");
        await ExecuteDotNet($"build --no-restore --disable-build-servers -p:LangVersion={languageVersion}", expectedExitCode: 0);

        testOutputHelper.WriteLine("Running project");
        await ExecuteDotNet($"run --no-build --disable-build-servers -p:LangVersion={languageVersion}");

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

        static string GetPackageReferences()
        {
            var names = typeof(InlineSnapshotTests).Assembly.GetManifestResourceNames();
            using var stream = typeof(InlineSnapshotTests).Assembly.GetManifestResourceStream("Meziantou.Framework.InlineSnapshotTesting.Tests.Meziantou.Framework.InlineSnapshotTesting.csproj");
            Assert.NotNull(stream);
            var doc = XDocument.Load(stream);
            Assert.NotNull(doc.Root);
            var items = doc.Root.Descendants("PackageReference");

            var packages = items.Where(item => item.Parent?.Attribute("Condition") is null).ToList();
            return string.Join('\n', packages.Select(item => item.ToString()));
        }

        async Task ExecuteDotNet(string command, int? expectedExitCode = null)
        {
            var psi = new ProcessStartInfo(dotnetPath, command)
            {
                WorkingDirectory = directory.FullPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            psi.EnvironmentVariables.Remove("CI");
            foreach (var entry in psi.EnvironmentVariables.Cast<DictionaryEntry>().ToArray())
            {
                var key = (string)entry.Key;
                if (key == "GITHUB_WORKSPACE")
                    continue;

                if (key.StartsWith("GITHUB", StringComparison.Ordinal))
                {
                    psi.EnvironmentVariables.Remove(key);
                    continue;
                }

                if (key.StartsWith("COMPlus_Dbg", StringComparison.Ordinal) || key.StartsWith("DOTNET_Dbg", StringComparison.Ordinal))
                {
                    psi.EnvironmentVariables.Remove(key);
                }
            }

            psi.EnvironmentVariables["DiffEngine_Disabled"] = "true";
            if (environmentVariables is not null)
            {
                foreach (var variable in environmentVariables)
                {
                    psi.EnvironmentVariables[variable.Key] = variable.Value;
                }
            }

            using var process = Process.Start(psi);
            Assert.NotNull(process);
            process.OutputDataReceived += (_, e) => testOutputHelper.WriteLine(e.Data ?? "");
            process.ErrorDataReceived += (_, e) => testOutputHelper.WriteLine(e.Data ?? "");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            testOutputHelper.WriteLine("Exit code: " + process.ExitCode);
            if (expectedExitCode.HasValue)
            {
                Assert.Equal(expectedExitCode.Value, process.ExitCode);
            }
        }
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var sourceFilePath = FullPath.FromPath(filePath);
        if (File.Exists(sourceFilePath))
            return sourceFilePath.Parent.Parent.Parent;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var targetsPath = Path.Combine(directory.FullName, "src", "Meziantou.Framework.InlineSnapshotTesting", "build", "Meziantou.Framework.InlineSnapshotTesting.targets");
            if (File.Exists(targetsPath))
                return FullPath.FromPath(directory.FullName);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find the repository root.");
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

    private static async Task ExecuteDotNet(string dotnetPath, FullPath workingDirectory, IReadOnlyList<string> arguments, int expectedExitCode)
    {
        var psi = new ProcessStartInfo(dotnetPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.EnvironmentVariables.Remove("CI");
        foreach (var entry in psi.EnvironmentVariables.Cast<DictionaryEntry>().ToArray())
        {
            var key = (string)entry.Key;
            if (key == "GITHUB_WORKSPACE")
                continue;

            if (key.StartsWith("GITHUB", StringComparison.Ordinal))
            {
                psi.EnvironmentVariables.Remove(key);
            }
        }

        psi.EnvironmentVariables["DiffEngine_Disabled"] = "true";
        psi.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";
        psi.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedExitCode, process.ExitCode, message: $"dotnet {string.Join(' ', arguments)} returned exit code {process.ExitCode} but {expectedExitCode} was expected.{Environment.NewLine}{output}");

        static void AppendLine(StringBuilder builder, string? line)
        {
            lock (builder)
            {
                builder.AppendLine(line);
            }
        }
    }

    [GeneratedRegex("Line[2]", RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex Line2Regex();
}
