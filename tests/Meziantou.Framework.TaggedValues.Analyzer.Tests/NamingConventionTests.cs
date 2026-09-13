using Microsoft.CodeAnalysis;
using RemoveValueTagCodeFixProviderType = Meziantou.Framework.Analyzers.TaggedValues.RemoveValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class NamingConventionTests : TaggedValuesAnalyzerTestBase
{
    private const string Source = """
        class Order
        {
            public Guid Id { get; set; }
            public Guid ProjectId { get; set; }
            Guid _customerId;

            static void Load(Guid orderId) { }

            void M(Guid projectId)
            {
                _ = [|Id == ProjectId|];
                _ = [|_customerId == ProjectId|];
                Load([|ProjectId|]);
                _ = projectId == ProjectId;
            }
        }
        """;

    [Fact]
    public async Task ConventionsAreDisabledByDefault()
    {
        await VerifyAsync(Source.Replace("[|", "", StringComparison.Ordinal).Replace("|]", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConventionsInferTagsFromNames()
    {
        var source = Source
            .Replace("[|Id == ProjectId|]", "{|MFTV0001:Id == ProjectId|}", StringComparison.Ordinal)
            .Replace("[|_customerId == ProjectId|]", "{|MFTV0001:_customerId == ProjectId|}", StringComparison.Ordinal)
            .Replace("[|ProjectId|]", "{|MFTV0002:ProjectId|}", StringComparison.Ordinal);

        await VerifyAsync(source, inferTagsFromNames: true);
    }

    [Fact]
    public async Task ConventionsIgnoreFieldPrefixes()
    {
        await VerifyAsync("""
            class Order
            {
                static Guid s_projectId;
                static Guid s_id;
                Guid _projectId;
                Guid __customerId;

                static void Load(Guid orderId) { }

                void M()
                {
                    Load({|MFTV0002:s_projectId|});
                    Load(s_id);
                    _ = s_projectId == _projectId;
                    _ = {|MFTV0001:__customerId == s_projectId|};
                }
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task ExplicitTagsWinOverConventions()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("ProjectId")]
                public Guid Id { get; set; }

                public Guid ProjectId { get; set; }

                bool M() => Id == ProjectId;
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task InheritedIdIsAnIdOfEveryTypeInTheChain()
    {
        await VerifyAsync("""
            abstract class Entity
            {
                public Guid Id { get; set; }
            }

            class Order : Entity { }
            class Project : Entity { }

            class Sample
            {
                static void Load(Guid orderId, Guid entityId) { }

                void M(Order order, Project project)
                {
                    Load(order.Id, order.Id);
                    Load({|MFTV0002:project.Id|}, project.Id);
                }
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task ConventionsIgnoreCollectionsIndexersAndPlainIdParameters()
    {
        await VerifyAsync("""
            class Order
            {
                public List<Guid> ProjectId { get; } = [];
                public Guid this[Guid customerId] => customerId;

                bool M(Guid id, Guid orderId) => id == orderId;
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenTypesWithTheSameNameDeclareId()
    {
        await VerifyAsync("""
            namespace Sales
            {
                class Order
                {
                    public Guid {|MFTV0006:Id|} { get; set; }
                }
            }

            namespace Billing
            {
                class Order
                {
                    public Guid {|MFTV0006:Id|} { get; set; }
                }

                class Invoice
                {
                    [ValueTag("BillingInvoiceId")]
                    public Guid Id { get; set; }
                }
            }

            namespace Other
            {
                class Invoice
                {
                    public Guid Id { get; set; }
                }
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenTheTagIsRedundant()
    {
        await VerifyAsync(
            """
            class Order
            {
                [{|#0:ValueTag("OrderId")|}]
                public Guid Id { get; set; }

                [ValueTag("CustomerId")]
                public Guid ProjectId { get; set; }

                void M([{|#1:ValueTag("ProjectId")|}] Guid projectId) { }
            }
            """,
            inferTagsFromNames: true,
            Diagnostic("MFTV0007").WithSeverity(DiagnosticSeverity.Info).WithLocation(0).WithArguments("[ValueTag(\"OrderId\")]", "property 'Order.Id'"),
            Diagnostic("MFTV0007").WithSeverity(DiagnosticSeverity.Info).WithLocation(1).WithArguments("[ValueTag(\"ProjectId\")]", "parameter 'projectId' of 'Order.M'"));
    }

    [Fact]
    public async Task CodeFix_RemovesTheRedundantTag()
    {
        await VerifyCodeFixAsync<RemoveValueTagCodeFixProviderType>(
            """
            class Order
            {
                [{|MFTV0007:ValueTag("OrderId")|}]
                public Guid Id { get; set; }
            }
            """,
            """
            class Order
            {
                public Guid Id { get; set; }
            }
            """,
            inferTagsFromNames: true);
    }
}
