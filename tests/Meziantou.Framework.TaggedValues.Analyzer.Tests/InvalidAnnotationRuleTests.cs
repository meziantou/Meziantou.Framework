using Microsoft.CodeAnalysis.Testing;
using RemoveValueTagCodeFixProviderType = Meziantou.Framework.TaggedValues.CodeFix.RemoveValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class InvalidAnnotationRuleTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ReportDiagnostic_ForEmptyTags()
    {
        await VerifyAsync("""
            class Sample
            {
                [{|MFTV0005:ValueTag("")|}] public Guid A { get; set; }
                [{|MFTV0005:ValueTag("OrderId", " ")|}] public Guid B { get; set; }
                [{|MFTV0005:ValueTag|}] public Guid C { get; set; }
                [{|MFTV0005:ValueTag(Key = "")|}] public Dictionary<Guid, Guid> D { get; set; } = [];
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForTheWrongAttributeForm()
    {
        await VerifyAsync("""
            [assembly: {|MFTV0005:ValueTag("OrderId")|}]

            class Sample
            {
                [{|MFTV0005:ValueTag(typeof(Sample), "Id", "OrderId")|}] public Guid Id { get; set; }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenTheExternalMemberDoesNotExist()
    {
        await VerifyAsync("""
            [assembly: ValueTag(typeof(System.Diagnostics.Process), nameof(System.Diagnostics.Process.Id), "ProcessId")]
            [assembly: {|MFTV0005:ValueTag(typeof(System.Diagnostics.Process), "ProcessId", "ProcessId")|}]
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForKeyAndValueMisuse()
    {
        await VerifyAsync("""
            class Sample
            {
                [{|MFTV0005:ValueTag(Key = "OrderId")|}] public Guid A { get; set; }
                [{|MFTV0005:ValueTag("OrderId")|}] public Dictionary<Guid, Guid> B { get; set; } = [];
                [{|MFTV0005:ValueTag("OrderId", Value = "ProjectId")|}] public Dictionary<Guid, Guid> C { get; set; } = [];
                [ValueTag(Key = "OrderId", Value = "ProjectId")] public IReadOnlyDictionary<Guid, Guid> D { get; set; } = null!;
                [ValueTag(Value = "ProjectId")] public Task<Dictionary<string, Guid>> E { get; set; } = null!;

                [return: {|MFTV0005:ValueTag(Key = "OrderId")|}]
                public Guid M() => Guid.Empty;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForInvalidOrMisplacedComments()
    {
        await VerifyAsync("""
            class Sample
            {
                Guid {|MFTV0005:/* ValueTag=OrderId */|} _field;

                void M()
                {
                    Guid {|MFTV0005:/* ValueTag= */|} a = Guid.Empty;
                    Guid {|MFTV0005:/* ValueTag OrderId */|} b = Guid.Empty;
                    Guid {|MFTV0005:/* ValueTag Key=A Other=B */|} c = Guid.Empty;
                    Guid d = {|MFTV0005:/* ValueTag=OrderId */|} Guid.Empty;
                    Guid /* ValueTag=OrderId */ e = Guid.Empty;
                    var f = new Tuple<{|MFTV0005:/* ValueTag=OrderId */|} Guid, Guid>(Guid.Empty, Guid.Empty);
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForInvalidOrMisplacedLineComments()
    {
        await VerifyAsync("""
            class Sample
            {
                {|MFTV0005:// ValueTag=OrderId|}
                Guid _field;

                void M()
                {
                    {|MFTV0005:// ValueTag=|}
                    var a = Guid.Empty;
                    {|MFTV0005:// ValueTag=OrderId because it is an order|}
                    var b = Guid.Empty;
                    var c = Guid.Empty; {|MFTV0005:// ValueTag=OrderId|}
                    {|MFTV0005:// ValueTag=OrderId|}
                    Console.WriteLine();
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForInvalidTypeArgumentComments()
    {
        await VerifyAsync("""
            class Sample
            {
                Dictionary<{|MFTV0005:/* ValueTag=OrderId */|} Guid, Guid> _field = new Dictionary</* ValueTag=OrderId */ Guid, Guid>();

                void M(List<{|MFTV0005:/* ValueTag=OrderId */|} Guid> ids)
                {
                    var a = new Tuple<{|MFTV0005:/* ValueTag=OrderId */|} Guid>(Guid.Empty);
                    var b = new Dictionary<{|MFTV0005:/* ValueTag Key=OrderId */|} Guid, Guid>();
                    var c = Enumerable.Empty<{|MFTV0005:/* ValueTag=OrderId */|} Guid>();
                    var d = new Tuple<Guid, List<{|MFTV0005:/* ValueTag=OrderId */|} Guid>>(Guid.Empty, []);
                    var e = new Dictionary<Guid, List</* ValueTag=OrderId */ Guid>>();
                }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_RemovesTheAttribute()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [{|MFTV0005:ValueTag("")|}]
                public Guid A { get; set; }

                [Obsolete, {|MFTV0005:ValueTag("")|}]
                public Guid B { get; set; }
            }
            """,
            """
            class Sample
            {
                public Guid A { get; set; }

                [Obsolete]
                public Guid B { get; set; }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_RemovesTheComment()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M()
                {
                    Guid {|MFTV0005:/* ValueTag= */|} a = Guid.Empty;
                }
            }
            """,
            """
            class Sample
            {
                void M()
                {
                    Guid a = Guid.Empty;
                }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_RemovesTheLineComment()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M()
                {
                    {|MFTV0005:// ValueTag=|}
                    var a = Guid.Empty;
                    var b = Guid.Empty; {|MFTV0005:// ValueTag=OrderId|}
                    var c = Guid.Empty;
                }
            }
            """,
            """
            class Sample
            {
                void M()
                {
                    var a = Guid.Empty;
                    var b = Guid.Empty;
                    var c = Guid.Empty;
                }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_RemovesTheTypeArgumentComment()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M()
                {
                    var a = new Tuple<Guid, {|MFTV0005:/* ValueTag=OrderId */|} Guid>(Guid.Empty, Guid.Empty);
                }
            }
            """,
            """
            class Sample
            {
                void M()
                {
                    var a = new Tuple<Guid, Guid>(Guid.Empty, Guid.Empty);
                }
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForAnInternalAttributeWithTheSameName()
    {
        var test = CreateAnalyzerTest("""
            class Sample
            {
                bool M([Meziantou.Framework.TaggedValues.ValueTag("OrderId")] Guid orderId, [Meziantou.Framework.TaggedValues.ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:orderId == projectId|};
            }
            """);

        // The package reference is replaced by an internal copy of the attribute
        test.TestState.AdditionalReferences.Clear();
        test.TestState.Sources.Add("""
            namespace Meziantou.Framework.TaggedValues
            {
                [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
                internal sealed class ValueTagAttribute : System.Attribute
                {
                    public ValueTagAttribute(params string[] tags) { }
                    public string? Key { get; set; }
                    public string? Value { get; set; }
                }
            }
            """);
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task ReportDiagnostic_ForARepeatedKeyInAComment()
    {
        await VerifyAsync("""
            class Sample
            {
                void M()
                {
                    var {|MFTV0005:/* ValueTag Key=OrderId Key=ProjectId */|} map = new Dictionary<Guid, Guid>();
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForKeyAndValueOnTheAssembly()
    {
        await VerifyAsync("""
            [assembly: {|MFTV0005:ValueTag(typeof(System.Diagnostics.Process), nameof(System.Diagnostics.Process.Id), "ProcessId", Key = "ProcessId")|}]
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForKeyAndValueOnIndexers()
    {
        await VerifyAsync("""
            class Sample
            {
                [{|MFTV0005:ValueTag(Key = "OrderId")|}]
                public Guid this[int index] => Guid.Empty;
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenTheCompilationDoesNotReferenceTheAttribute()
    {
        var test = CreateAnalyzerTest("""
            class Sample
            {
                void M()
                {
                    Guid /* ValueTag= */ id = Guid.Empty;
                }
            }
            """);

        // Without the package, the using directive of the test does not compile
        test.TestState.AdditionalReferences.Clear();
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheAnnotationCannotBeRemoved()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            [assembly: {|MFTV0005:ValueTag(typeof(System.Diagnostics.Process), "ProcessId", "ProcessId")|}]
            """,
            """
            [assembly: {|MFTV0005:ValueTag(typeof(System.Diagnostics.Process), "ProcessId", "ProcessId")|}]
            """);
    }

    [Theory]
    [InlineData("/* ValueTag */")]
    [InlineData("/* ValueTag Key */")]
    [InlineData("/* ValueTag Key=OrderId, Value= */")]
    public async Task ReportDiagnostic_ForIncompleteComments(string comment)
    {
        await VerifyAsync($$"""
            class Sample
            {
                void M()
                {
                    var {|MFTV0005:{{comment}}|} map = new Dictionary<Guid, Guid>();
                }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_RemovesTheCommentAtTheEndOfALine()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M()
                {
                    var id = Guid.Empty; {|MFTV0005:/* ValueTag=OrderId */|}
                }
            }
            """,
            """
            class Sample
            {
                void M()
                {
                    var id = Guid.Empty;
                }
            }
            """);
    }
}
