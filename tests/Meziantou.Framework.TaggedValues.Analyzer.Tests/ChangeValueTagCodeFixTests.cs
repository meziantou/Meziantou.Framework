using ChangeValueTagCodeFixProviderType = Meziantou.Framework.TaggedValues.CodeFix.ChangeValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class ChangeValueTagCodeFixTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ChangesTheTagOfTheParameter()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId) => Load(projectId);
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheProperty()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [Obsolete]
                [ValueTag("OrderId")]
                public Guid Id { get; set; }

                void M([ValueTag("ProjectId", "CustomerId")] Guid projectId) => Id = {|MFTV0002:projectId|};
            }
            """,
            """
            class Sample
            {
                [Obsolete]
                [ValueTag("CustomerId", "ProjectId")]
                public Guid Id { get; set; }

                void M([ValueTag("ProjectId", "CustomerId")] Guid projectId) => Id = projectId;
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheReturnValue()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [return: ValueTag("OrderId")]
                Guid M([ValueTag("ProjectId")] Guid projectId) => {|MFTV0002:projectId|};
            }
            """,
            """
            class Sample
            {
                [return: ValueTag("ProjectId")]
                Guid M([ValueTag("ProjectId")] Guid projectId) => projectId;
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheField_ForDictionaries()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [ValueTag(Key = "OrderId", Value = "ProjectId")]
                Dictionary<Guid, Guid> _first = [];

                [ValueTag(Key = "OrderId", Value = "CustomerId")]
                Dictionary<Guid, Guid> _second = [];

                void M() => _first = {|MFTV0002:_second|};
            }
            """,
            """
            class Sample
            {
                [ValueTag(Key = "OrderId", Value = "CustomerId")]
                Dictionary<Guid, Guid> _first = [];

                [ValueTag(Key = "OrderId", Value = "CustomerId")]
                Dictionary<Guid, Guid> _second = [];

                void M() => _first = _second;
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheLocalComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=OrderId */ id = {|MFTV0002:projectId|};
                }
            }
            """,
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=ProjectId */ id = projectId;
                }
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheLocalLineComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    // ValueTag=OrderId
                    Guid id = {|MFTV0002:projectId|};
                }
            }
            """,
            """
            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    // ValueTag=ProjectId
                    Guid id = projectId;
                }
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheOutVariableComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                void M() => TryGet(out {|MFTV0002:var /* ValueTag=ProjectId */ id|});
            }
            """,
            """
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                void M() => TryGet(out var /* ValueTag=OrderId */ id);
            }
            """);
    }

    [Fact]
    public async Task CopiesTheTagOfTheOverriddenMember()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            abstract class Base
            {
                [return: ValueTag("OrderId")]
                public abstract Guid GetId();
            }

            class Derived : Base
            {
                [return: ValueTag("ProjectId")]
                public override Guid {|MFTV0004:GetId|}() => Guid.Empty;
            }
            """,
            """
            abstract class Base
            {
                [return: ValueTag("OrderId")]
                public abstract Guid GetId();
            }

            class Derived : Base
            {
                [return: ValueTag("OrderId")]
                public override Guid GetId() => Guid.Empty;
            }
            """);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheSourceTagIsInferredFromItsName()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M(Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M(Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            inferTagsFromNames: true);
    }

    [Fact]
    public async Task ChangesTheKeyAndValueTagsOfTheLocalComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [return: ValueTag(Key = "OrderId", Value = "CustomerId")]
                static Dictionary<Guid, Guid> GetMap() => [];

                void M()
                {
                    var /* ValueTag Key=OrderId Value=ProjectId */ map = {|MFTV0002:GetMap()|};
                }
            }
            """,
            """
            class Sample
            {
                [return: ValueTag(Key = "OrderId", Value = "CustomerId")]
                static Dictionary<Guid, Guid> GetMap() => [];

                void M()
                {
                    var /* ValueTag Key=OrderId Value=CustomerId */ map = GetMap();
                }
            }
            """);
    }

    [Fact]
    public async Task NoCodeFix_ForAFieldDeclaringSeveralVariables()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                [ValueTag("OrderId")] Guid _first, _second;

                void M([ValueTag("ProjectId")] Guid projectId) => _first = {|MFTV0002:projectId|};
            }
            """,
            """
            class Sample
            {
                [ValueTag("OrderId")] Guid _first, _second;

                void M([ValueTag("ProjectId")] Guid projectId) => _first = {|MFTV0002:projectId|};
            }
            """);
    }

    [Fact]
    public async Task KeepsTagsThatContainTheSerializationSeparators()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("Order|Id", "Project\\Id")] Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("Order|Id", "Project\\Id")] Guid id) { }

                void M([ValueTag("Order|Id", "Project\\Id")] Guid projectId) => Load(projectId);
            }
            """);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheTagCannotBeWrittenInAComment()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                void M([ValueTag("Order Id")] Guid orderId)
                {
                    var /* ValueTag=ProjectId */ id = {|MFTV0002:orderId|};
                }
            }
            """,
            """
            class Sample
            {
                void M([ValueTag("Order Id")] Guid orderId)
                {
                    var /* ValueTag=ProjectId */ id = {|MFTV0002:orderId|};
                }
            }
            """);
    }

    [Fact]
    public async Task ChangesThePropertyTargetOfARecordParameter()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            record Order([ValueTag("OrderIdParameter")][property: ValueTag("OrderId")] Guid Id);

            class Sample
            {
                Order M(Order order, [ValueTag("ProjectId")] Guid projectId) => order with { Id = {|MFTV0002:projectId|} };
            }
            """,
            """
            record Order([ValueTag("OrderIdParameter")][property: ValueTag("ProjectId")] Guid Id);

            class Sample
            {
                Order M(Order order, [ValueTag("ProjectId")] Guid projectId) => order with { Id = projectId };
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfEveryPartOfAPartialMethod()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            partial class Sample
            {
                static partial void Load(Guid id);
                static partial void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId) => Load({|MFTV0002:projectId|});
            }
            """,
            """
            partial class Sample
            {
                static partial void Load([ValueTag("ProjectId")] Guid id);
                static partial void Load(Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId) => Load(projectId);
            }
            """);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheTagIsDeclaredByAnAssemblyAttribute()
    {
        const string Source = """
            [assembly: ValueTag(typeof(Order), nameof(Order.Id), "OrderId")]

            class Order
            {
                public Guid Id { get; set; }
            }

            class Sample
            {
                void M(Order order, [ValueTag("ProjectId")] Guid projectId) => order.Id = {|MFTV0002:projectId|};
            }
            """;
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(Source, Source);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheTagIsInheritedFromTheBaseMember()
    {
        const string Source = """
            class Base
            {
                [ValueTag("OrderId")]
                public virtual Guid Id { get; set; }
            }

            class Derived : Base
            {
                public override Guid Id { get; set; }
            }

            class Sample
            {
                void M(Derived derived, [ValueTag("ProjectId")] Guid projectId) => derived.Id = {|MFTV0002:projectId|};
            }
            """;
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(Source, Source);
    }

    [Fact]
    public async Task NoCodeFix_WhenTheTagCannotBeWrittenInABlockComment()
    {
        const string Source = """
            class Sample
            {
                void M([ValueTag("A*/B")] Guid projectId)
                {
                    Guid /* ValueTag=OrderId */ id = {|MFTV0002:projectId|};
                }
            }
            """;
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(Source, Source);
    }

    [Fact]
    public async Task AddsACommentToTheVariable_WhenTheCommentIsSharedWithOtherVariables()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=OrderId */ a = Guid.Empty, b = Guid.Empty;
                    a = {|MFTV0002:projectId|};
                    Load(b);
                }
            }
            """,
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Guid /* ValueTag=OrderId */ a /* ValueTag=ProjectId */ = Guid.Empty, b = Guid.Empty;
                    a = projectId;
                    Load(b);
                }
            }
            """);
    }

    [Fact]
    public async Task KeepsTheCommentsOfTheAttributes()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static void Load(
                    [ValueTag("OrderId")] // the order
                    Guid id) { }

                [Obsolete] // keep me
                [ValueTag("OrderId")]
                public Guid Id { get; set; }

                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Load({|MFTV0002:projectId|});
                    Id = {|MFTV0002:projectId|};
                }
            }
            """,
            """
            class Sample
            {
                static void Load(
                    [ValueTag("ProjectId")] // the order
                    Guid id) { }

                [Obsolete] // keep me
                [ValueTag("ProjectId")]
                public Guid Id { get; set; }

                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Load(projectId);
                    Id = projectId;
                }
            }
            """);
    }

    [Fact]
    public async Task ChangesTheTagOfTheVariable_ForAnOutArgument()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid id)
                {
                    id = Guid.NewGuid();
                    return true;
                }

                void M()
                {
                    Guid /* ValueTag=ProjectId */ id = Guid.Empty;
                    TryGet(out {|MFTV0002:id|});
                }
            }
            """,
            """
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid id)
                {
                    id = Guid.NewGuid();
                    return true;
                }

                void M()
                {
                    Guid /* ValueTag=OrderId */ id = Guid.Empty;
                    TryGet(out id);
                }
            }
            """);
    }
}
