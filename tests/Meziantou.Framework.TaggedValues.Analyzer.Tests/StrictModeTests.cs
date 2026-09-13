using ChangeValueTagCodeFixProviderType = Meziantou.Framework.TaggedValues.CodeFix.ChangeValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class StrictModeTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task StrictMode_IsDisabledByDefault()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, Guid other) => orderId == other;
            }
            """);
    }

    [Fact]
    public async Task Comparison_WithAnUntaggedValue_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, Guid other) => {|MFTV0009:orderId == other|};
            }
            """);
    }

    [Fact]
    public async Task Comparison_WithANewValue_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId) => {|MFTV0009:orderId == Guid.NewGuid()|};
            }
            """);
    }

    [Theory]
    [InlineData("[ValueTag(\"OrderId\")] Guid id", "id == default")]
    [InlineData("[ValueTag(\"OrderId\")] Guid id", "id == Guid.Empty")]
    [InlineData("[ValueTag(\"OrderId\")] Guid id", "id.Equals(Guid.Empty)")]
    [InlineData("[ValueTag(\"OrderId\")] Guid? id", "id == null")]
    [InlineData("[ValueTag(\"OrderId\")] int id", "id > 0")]
    [InlineData("[ValueTag(\"OrderId\")] string id", "id == \"\"")]
    public async Task Comparison_WithADefaultValueOrAConstant_IsNotReported(string parameter, string comparison)
    {
        await VerifyStrictAsync($$"""
            class Sample
            {
                bool M({{parameter}}) => {{comparison}};
            }
            """);
    }

    [Fact]
    public async Task UntaggedValue_FlowingToATaggedParameter_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid orderId) { }

                void M(Guid other) => Load({|MFTV0009:other|});
            }
            """);
    }

    [Fact]
    public async Task UntaggedValue_AssignedToATaggedProperty_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                [ValueTag("OrderId")] public Guid OrderId { get; set; }

                void M(Guid other) => OrderId = {|MFTV0009:other|};
            }
            """);
    }

    [Fact]
    public async Task UntaggedMethodResult_FlowingToATaggedDeclaration_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                [ValueTag("OrderId")] public Guid OrderId { get; set; }

                static Guid CreateId() => Guid.NewGuid();

                void M() => OrderId = {|MFTV0009:CreateId()|};
            }
            """);
    }

    [Fact]
    public async Task UntaggedValue_ReturnedFromATaggedMethod_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                Guid _other;

                [return: ValueTag("OrderId")]
                Guid GetOrderId() => {|MFTV0009:_other|};
            }
            """);
    }

    [Fact]
    public async Task UntaggedValue_InitializingACommentedLocal_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                void M(Guid other)
                {
                    Guid /* ValueTag=OrderId */ orderId = {|MFTV0009:other|};
                }
            }
            """);
    }

    [Theory]
    [InlineData("Guid.NewGuid()")]
    [InlineData("new Guid(new byte[16])")]
    [InlineData("Guid.Parse(\"00000000-0000-0000-0000-000000000000\")")]
    [InlineData("default")]
    [InlineData("Guid.Empty")]
    public async Task NewOrDefaultValue_FlowingToATaggedDeclaration_IsNotReported(string value)
    {
        await VerifyStrictAsync($$"""
            class Sample
            {
                [ValueTag("OrderId")] public Guid OrderId { get; set; } = {{value}};

                void M()
                {
                    OrderId = {{value}};
                    Guid /* ValueTag=OrderId */ orderId = {{value}};
                }
            }
            """);
    }

    [Fact]
    public async Task TaggedValue_FlowingToAnUntaggedParameter_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                static void Load(Guid id) { }

                void M([ValueTag("OrderId")] Guid orderId) => Load({|MFTV0009:orderId|});
            }
            """);
    }

    [Fact]
    public async Task TaggedValue_AssignedToAnUntaggedField_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                Guid _id;

                void M([ValueTag("OrderId")] Guid orderId) => _id = {|MFTV0009:orderId|};
            }
            """);
    }

    [Fact]
    public async Task TaggedValue_AssignedToAnUntaggedLocal_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid orderId)
                {
                    var id = Guid.Empty;
                    id = {|MFTV0009:orderId|};
                }
            }
            """);
    }

    [Fact]
    public async Task TaggedValue_InitializingAnUntaggedField_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                [ValueTag("OrderId")] static readonly Guid DefaultOrderId = Guid.Empty;

                Guid _id = {|MFTV0009:DefaultOrderId|};
            }
            """);
    }

    [Fact]
    public async Task TaggedValue_InitializingAnInferredLocal_IsNotReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid orderId) { }

                void M([ValueTag("OrderId")] Guid orderId)
                {
                    var id = orderId;
                    Load(id);
                }
            }
            """);
    }

    [Fact]
    public async Task TaggedValue_FlowingToALibraryObjectOrGenericTarget_IsNotReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                static void Log(object value) { }
                static T Identity<T>(T value) => value;

                void M([ValueTag("OrderId")] Guid orderId, List<Guid> ids)
                {
                    Console.WriteLine(orderId);
                    Log(orderId);
                    _ = Identity(orderId);
                    ids.Add(orderId);
                }
            }
            """);
    }

    [Fact]
    public async Task CombinedValues_WithAnUntaggedValue_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                void M(bool condition, [ValueTag("OrderId")] Guid orderId, Guid other)
                {
                    Guid /* ValueTag=OrderId */ a = condition ? orderId : {|MFTV0009:other|};
                    Guid /* ValueTag=OrderId */ b = condition ? orderId : Guid.NewGuid();
                    Guid /* ValueTag=OrderId */ c = condition ? orderId : default;
                }
            }
            """);
    }

    [Fact]
    public async Task Addition_WithAnUntaggedValue_IsReported()
    {
        await VerifyStrictAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] int orderId, int offset) => _ = orderId + {|MFTV0009:offset|} + 1;
            }
            """);
    }

    [Fact]
    public async Task CodeFix_TagsTheUntaggedParameter()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load(Guid id) { }

                void M([ValueTag("OrderId")] Guid orderId) => Load({|MFTV0009:orderId|});
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("OrderId")] Guid orderId) => Load(orderId);
            }
            """,
            strict: true);
    }

    [Fact]
    public async Task CodeFix_TagsTheUntaggedFieldOfAComparison()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                Guid _other;

                bool M([ValueTag("OrderId")] Guid orderId) => {|MFTV0009:orderId == _other|};
            }
            """,
            """
            class Sample
            {
                [ValueTag("OrderId")]
                Guid _other;

                bool M([ValueTag("OrderId")] Guid orderId) => orderId == _other;
            }
            """,
            strict: true);
    }
}
