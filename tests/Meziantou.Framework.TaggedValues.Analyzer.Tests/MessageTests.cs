namespace Meziantou.Framework.Tests;

public sealed class MessageTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ComparedValuesMessage()
    {
        await VerifyAsync(
            """
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId", "CustomerId")] Guid id) => {|#0:orderId == id|};
            }
            """,
            inferTagsFromNames: false,
            Diagnostic("MFTV0001").WithLocation(0).WithMessage("parameter 'orderId' of 'Sample.M' is [ValueTag(\"OrderId\")] and is compared with parameter 'id' of 'Sample.M', which is [ValueTag(\"CustomerId\", \"ProjectId\")]"));
    }

    [Fact]
    public async Task FlowMismatchMessage()
    {
        await VerifyAsync(
            """
            class Order
            {
                [ValueTag("ProjectId")] public Guid ProjectId { get; set; }

                static void Load(
                    [ValueTag("OrderId")] Guid orderId) { }

                void M() => Load({|#0:ProjectId|});
            }
            """,
            inferTagsFromNames: false,
            Diagnostic("MFTV0002").WithLocation(0).WithLocation(11, 36).WithMessage("property 'Order.ProjectId' is [ValueTag(\"ProjectId\")] and flows to parameter 'orderId' of 'Order.Load' (line 11), which is [ValueTag(\"OrderId\")]"));
    }

    [Fact]
    public async Task FlowMismatchMessage_ForDictionaries()
    {
        await VerifyAsync(
            """
            class Sample
            {
                [ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> _projectIdByOrderId = [];
                [ValueTag(Key = "OrderId", Value = "CustomerId")] Dictionary<Guid, Guid> _customerIdByOrderId = [];

                void M() => _projectIdByOrderId = {|#0:_customerIdByOrderId|};
            }
            """,
            inferTagsFromNames: false,
            Diagnostic("MFTV0002").WithLocation(0).WithLocation(8, 77).WithMessage("field 'Sample._customerIdByOrderId' is [ValueTag(Key = \"OrderId\", Value = \"CustomerId\")] and flows to field 'Sample._projectIdByOrderId' (line 8), which is [ValueTag(Key = \"OrderId\", Value = \"ProjectId\")]"));
    }

    [Fact]
    public async Task FlowMismatchMessage_ForIdParameterConvention()
    {
        await VerifyAsync(
            """
            class Bar { }

            class Sample
            {
                [ValueTag("ProjectId")] Bar _projectBar = new();

                static void LoadBar(Bar id) { }

                void M() => LoadBar({|#0:_projectBar|});
            }
            """,
            inferTagsFromNames: true,
            Diagnostic("MFTV0002").WithLocation(0).WithLocation(12, 29).WithMessage("field 'Sample._projectBar' is [ValueTag(\"ProjectId\")] and flows to parameter 'id' of 'Sample.LoadBar' (line 12), which is [ValueTag(\"BarId\")]"));
    }

    [Fact]
    public async Task CombinedValuesMessage()
    {
        await VerifyAsync(
            """
            class Sample
            {
                Guid M(bool condition, [ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId) => condition ? orderId : {|#0:projectId|};
            }
            """,
            inferTagsFromNames: false,
            Diagnostic("MFTV0003").WithLocation(0).WithMessage("parameter 'projectId' of 'Sample.M' is [ValueTag(\"ProjectId\")] but parameter 'orderId' of 'Sample.M' is [ValueTag(\"OrderId\")]"));
    }

    [Fact]
    public async Task InheritedTagMismatchMessage()
    {
        await VerifyAsync(
            """
            interface IRepository
            {
                void Load([ValueTag("OrderId")] Guid id);
            }

            class Repository : IRepository
            {
                public void Load([ValueTag("ProjectId")] Guid {|#0:id|}) { }
            }
            """,
            inferTagsFromNames: false,
            Diagnostic("MFTV0004").WithLocation(0).WithLocation(0).WithMessage("parameter 'id' of 'Repository.Load' is [ValueTag(\"ProjectId\")] but parameter 'id' of 'IRepository.Load' (line 8) is [ValueTag(\"OrderId\")]"));
    }

    [Fact]
    public async Task MissingReturnTagMessage()
    {
        await VerifyAsync(
            """
            class Order
            {
                [ValueTag("OrderId")] Guid _id;

                Guid {|#0:GetId|}() => _id;

                Guid {|#1:Id|} => _id;
            }
            """,
            inferTagsFromNames: false,
            Diagnostic("MFTV0008").WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info).WithLocation(0).WithLocation(0).WithMessage("Every value returned by method 'Order.GetId' is [ValueTag(\"OrderId\")]; add [return: ValueTag(\"OrderId\")] so the tag flows to the callers"),
            Diagnostic("MFTV0008").WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Info).WithLocation(1).WithLocation(1).WithMessage("Every value returned by property 'Order.Id' is [ValueTag(\"OrderId\")]; add [ValueTag(\"OrderId\")] so the tag flows to the callers"));
    }

    [Fact]
    public async Task UntaggedValueMessage_ForComparisons()
    {
        var test = CreateAnalyzerTest(
            """
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId) => {|#0:orderId == Guid.NewGuid()|};
            }
            """,
            strict: true);
        test.ExpectedDiagnostics.Add(Diagnostic("MFTV0009").WithLocation(0).WithMessage("parameter 'orderId' of 'Sample.M' is [ValueTag(\"OrderId\")] and is compared with the return value of 'Guid.NewGuid', which is not tagged"));
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task UntaggedValueMessage_ForUntaggedValuesFlowingToTaggedDeclarations()
    {
        var test = CreateAnalyzerTest(
            """
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid orderId) { }

                void M(Guid {|#1:other|}) => Load({|#0:other|});
            }
            """,
            strict: true);
        test.ExpectedDiagnostics.Add(Diagnostic("MFTV0009").WithLocation(0).WithLocation(1).WithMessage("parameter 'other' of 'Sample.M' is not tagged and flows to parameter 'orderId' of 'Sample.Load' (line 8), which is [ValueTag(\"OrderId\")]"));
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task UntaggedValueMessage_ForTaggedValuesFlowingToUntaggedDeclarations()
    {
        var test = CreateAnalyzerTest(
            """
            class Sample
            {
                static void Load(Guid {|#1:id|}) { }

                void M([ValueTag("OrderId")] Guid orderId) => Load({|#0:orderId|});
            }
            """,
            strict: true);
        test.ExpectedDiagnostics.Add(Diagnostic("MFTV0009").WithLocation(0).WithLocation(1).WithMessage("parameter 'orderId' of 'Sample.M' is [ValueTag(\"OrderId\")] and flows to parameter 'id' of 'Sample.Load' (line 8), which is not tagged"));
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task AmbiguousConventionMessage()
    {
        await VerifyAsync(
            """
            namespace Sales { class Order { public Guid {|#0:Id|} { get; set; } } }
            namespace Billing { class Order { public Guid {|#1:Id|} { get; set; } } }
            """,
            inferTagsFromNames: true,
            Diagnostic("MFTV0006").WithLocation(0).WithMessage("property 'Sales.Order.Id' and property 'Billing.Order.Id' both infer the tag 'OrderId' from their type name; add an explicit [ValueTag] with a distinct tag to at least one of them"),
            Diagnostic("MFTV0006").WithLocation(1).WithMessage("property 'Billing.Order.Id' and property 'Sales.Order.Id' both infer the tag 'OrderId' from their type name; add an explicit [ValueTag] with a distinct tag to at least one of them"));
    }
}
