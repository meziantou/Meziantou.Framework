using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;

public sealed class InlineSnapshotTests(ITestOutputHelper testOutputHelper)
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
            .WithSettings(settings => settings.UseHumanReadableSerializer(options => options.ScrubValue<string>((value, index) => "prefix-" + index.ToString(CultureInfo.InvariantCulture))))
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
                options.ScrubValue<string>((value, index) => "str-" + index.ToString(CultureInfo.InvariantCulture));
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

    [SuppressMessage("Design", "MA0042:Do not use blocking calls in an async method", Justification = "Not supported on .NET Framework")]
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Not supported on .NET Framework")]
    private async Task AssertSnapshot([StringSyntax("c#-test")] string source, [StringSyntax("c#-test")] string expected = null, bool launchDebugger = false, string languageVersion = "11", bool autoDetectCI = false, bool forceUpdateSnapshots = false, IEnumerable<KeyValuePair<string, string>> environmentVariables = null, string[]? preprocessorSymbols = null)
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

        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        testOutputHelper.WriteLine("Using dotnet: " + dotnetPath);
        Assert.NotNull(dotnetPath);

        testOutputHelper.WriteLine("Restoring project");
        await ExecuteDotNet("restore", expectedExitCode: 0);

        testOutputHelper.WriteLine("Building project");
        await ExecuteDotNet("build", expectedExitCode: 0);

        testOutputHelper.WriteLine("Running project");
        await ExecuteDotNet($"run --project \"{projectPath}\"");

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
#elif NET8_0
            return "net8.0";
#elif NET9_0
            return "net9.0";
#elif NET10_0
            return "net10.0";
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

            using var process = Process.Start(psi);
            process.OutputDataReceived += (_, e) => testOutputHelper.WriteLine(e.Data ?? "");
            process.ErrorDataReceived += (_, e) => testOutputHelper.WriteLine(e.Data ?? "");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process!.WaitForExitAsync();
            if (expectedExitCode.HasValue)
            {
                Assert.Equal(expectedExitCode.Value, process.ExitCode);
            }
        }
    }
}