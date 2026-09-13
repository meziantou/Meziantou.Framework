using Microsoft.CodeAnalysis;
using RemoveValueTagCodeFixProviderType = Meziantou.Framework.TaggedValues.CodeFix.RemoveValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class NamingConventionTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ConventionsAreDisabledByDefault()
    {
        await VerifyAsync("""
            class Order
            {
                public Guid Id { get; set; }
                public Guid ProjectId { get; set; }
                Guid _customerId;

                static void Load(Guid orderId) { }

                void M(Guid projectId)
                {
                    _ = Id == ProjectId;
                    _ = _customerId == ProjectId;
                    Load(ProjectId);
                    _ = projectId == ProjectId;
                }
            }
            """);
    }

    [Fact]
    public async Task ConventionsInferTagsFromNames()
    {
        await VerifyAsync("""
            class Order
            {
                public Guid Id { get; set; }
                public Guid ProjectId { get; set; }
                Guid _customerId;

                static void Load(Guid orderId) { }

                void M(Guid projectId)
                {
                    _ = {|MFTV0001:Id == ProjectId|};
                    _ = {|MFTV0001:_customerId == ProjectId|};
                    Load({|MFTV0002:ProjectId|});
                    _ = projectId == ProjectId;
                }
            }
            """, inferTagsFromNames: true);
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
    public async Task IdParameterTakesTheNameOfItsType()
    {
        await VerifyAsync("""
            class Bar { }
            readonly record struct UserId(Guid Value);
            enum Status { }

            class Sample
            {
                [ValueTag("ProjectId")] Bar _projectBar = new();
                [ValueTag("ProjectId")] UserId _projectUser;
                [ValueTag("ProjectId")] Guid _projectGuid;
                [ValueTag("ProjectId")] int _projectInt;
                [ValueTag("ProjectId")] string _projectString = "";
                [ValueTag("ProjectId")] Status _projectStatus;

                static void LoadBar(Bar id) { }
                static void LoadUser(UserId? id) { }
                static void LoadGuid(Guid id) { }
                static void LoadInt(int id) { }
                static void LoadString(string id) { }
                static void LoadStatus(Status id) { }
                static void LoadRedundant([{|MFTV0007:ValueTag("BarId")|}] Bar id) { }

                void M()
                {
                    LoadBar({|MFTV0002:_projectBar|});
                    LoadUser({|MFTV0002:_projectUser|});
                    LoadGuid(_projectGuid);
                    LoadInt(_projectInt);
                    LoadString(_projectString);
                    LoadStatus(_projectStatus);
                }
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task OutParameterNamedId_TakesTheNameOfItsType()
    {
        await VerifyAsync("""
            class Bar { }

            class Sample
            {
                static bool TryGet(out Bar id) { id = new(); return true; }

                bool M([ValueTag("ProjectId")] Bar projectBar) => TryGet(out var bar) && {|MFTV0001:bar == projectBar|};
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task TypeImplementingSeveralEnumerables_IsNotACollection()
    {
        await VerifyAsync("""
            class Ids : IEnumerable<Guid>, IEnumerable<int>
            {
                IEnumerator<Guid> IEnumerable<Guid>.GetEnumerator() => throw null!;
                IEnumerator<int> IEnumerable<int>.GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null!;
            }

            class Sample
            {
                public Ids ProjectId { get; set; } = new();

                bool M([ValueTag("OrderId")] Ids orderIds) => {|MFTV0001:ProjectId == orderIds|};
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
