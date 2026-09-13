namespace Meziantou.Framework.Tests;

public sealed class FlowMismatchRuleTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ReportDiagnostic_ForArguments()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId) { }

                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    Load(orderId, projectId);
                    Load({|MFTV0002:projectId|}, {|MFTV0002:orderId|});
                    Load(projectId: projectId, orderId: orderId);
                    Load(projectId: {|MFTV0002:orderId|}, orderId: {|MFTV0002:projectId|});
                    Load(Guid.NewGuid(), Guid.Empty);
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForConstructorIndexerAndDelegateArguments()
    {
        await VerifyAsync("""
            delegate void OrderHandler([ValueTag("OrderId")] Guid orderId);

            class Sample
            {
                public Sample([ValueTag("OrderId")] Guid orderId) { }

                public int this[[ValueTag("OrderId")] Guid orderId] => 0;

                void M([ValueTag("ProjectId")] Guid projectId, OrderHandler handler)
                {
                    _ = new Sample({|MFTV0002:projectId|});
                    _ = this[{|MFTV0002:projectId|}];
                    handler({|MFTV0002:projectId|});
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForRefAndOutArguments()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Get([ValueTag("OrderId")] out Guid orderId) => orderId = Guid.Empty;
                static void Update([ValueTag("OrderId")] ref Guid orderId) { }

                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Get(out {|MFTV0002:projectId|});
                    Update(ref {|MFTV0002:projectId|});
                    Get(out var orderId);
                    Update(ref orderId);
                    Update(ref {|MFTV0002:projectId|});
                }
            }
            """);
    }

    [Fact]
    public async Task OutArgument_ToAVariableWithADifferentTag_IsReported()
    {
        await VerifyAsync("""
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                void M([ValueTag("ProjectId")] Guid projectId) => TryGet(out {|MFTV0002:projectId|});
            }
            """);
    }

    [Fact]
    public async Task OutArgument_ToAFieldWithADifferentTag_IsReported()
    {
        await VerifyAsync("""
            class Sample
            {
                [ValueTag("ProjectId")] Guid _projectId;

                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                void M() => TryGet(out {|MFTV0002:_projectId|});
            }
            """);
    }

    [Fact]
    public async Task OutArgument_ToAVariableWithTheSameTag_IsNotReported()
    {
        await VerifyAsync("""
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                void M([ValueTag("OrderId")] Guid orderId) => TryGet(out orderId);
            }
            """);
    }

    [Fact]
    public async Task OutVariable_WithACommentThatDisagreesWithTheParameter_IsReported()
    {
        await VerifyAsync("""
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                void M()
                {
                    TryGet(out {|MFTV0002:var /* ValueTag=ProjectId */ a|});
                    TryGet(out {|MFTV0002:Guid /* ValueTag=ProjectId */ b|});
                    TryGet(out var /* ValueTag=OrderId */ c);
                    TryGet(out var d);
                    TryGet(out _);
                }
            }
            """);
    }

    [Fact]
    public async Task OutVariable_TakesTheTagOfTheParameter()
    {
        await VerifyAsync("""
            class Sample
            {
                static bool TryGet([ValueTag("OrderId")] out Guid orderId) { orderId = Guid.Empty; return true; }

                bool M([ValueTag("ProjectId")] Guid projectId) => TryGet(out var id) && {|MFTV0001:id == projectId|};
            }
            """);
    }

    [Fact]
    public async Task OutParameter_AssignedAValueWithADifferentTag_IsReported()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Get([ValueTag("OrderId")] out Guid orderId, [ValueTag("ProjectId")] Guid projectId) => orderId = {|MFTV0002:projectId|};
            }
            """);
    }

    [Fact]
    public async Task GenericOutParameter_TakesTheTagOfTheReceiver()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("OrderId")] Guid orderId)
                {
                    map.TryGetValue(orderId, out {|MFTV0002:var /* ValueTag=CustomerId */ customerId|});
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForAssignments()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }
                [ValueTag("OrderId")] Guid _id;
                [ValueTag("OrderId")] Guid? _nullableId;

                void M([ValueTag("ProjectId")] Guid projectId, [ValueTag("OrderId")] Guid orderId)
                {
                    Id = {|MFTV0002:projectId|};
                    _id = {|MFTV0002:projectId|};
                    _nullableId ??= {|MFTV0002:projectId|};
                    Id = orderId;
                    _id = Guid.NewGuid();
                    _ = new Order { Id = {|MFTV0002:projectId|} };
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForMemberInitializers()
    {
        await VerifyAsync("""
            class Sample
            {
                [ValueTag("ProjectId")] static readonly Guid DefaultProjectId = Guid.Empty;

                [ValueTag("OrderId")] Guid _orderId = {|MFTV0002:DefaultProjectId|};
                [ValueTag("OrderId")] public Guid OrderId { get; } = {|MFTV0002:DefaultProjectId|};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForWithExpressions()
    {
        await VerifyAsync("""
            record Order([ValueTag("OrderId")] Guid Id);

            class Sample
            {
                Order M(Order order, [ValueTag("ProjectId")] Guid projectId) => order with { Id = {|MFTV0002:projectId|} };
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForReturnValues()
    {
        await VerifyAsync("""
            class Sample
            {
                [ValueTag("ProjectId")] Guid _projectId;

                [return: ValueTag("OrderId")]
                Guid GetOrderId() => {|MFTV0002:_projectId|};

                [return: ValueTag("OrderId")]
                Guid GetOrderIdBlock()
                {
                    return {|MFTV0002:_projectId|};
                }

                [ValueTag("OrderId")]
                Guid OrderId => {|MFTV0002:_projectId|};

                [ValueTag("OrderId")]
                Guid OrderIdWithGetter
                {
                    get { return {|MFTV0002:_projectId|}; }
                }

                [return: ValueTag("OrderId")]
                async Task<Guid> GetOrderIdAsync()
                {
                    await Task.Yield();
                    return {|MFTV0002:_projectId|};
                }

                [return: ValueTag("OrderId")]
                IEnumerable<Guid> GetOrderIds()
                {
                    yield return {|MFTV0002:_projectId|};
                }

                void LocalFunctionAndLambda()
                {
                    [return: ValueTag("OrderId")]
                    Guid Local() => {|MFTV0002:_projectId|};

                    Func<Guid> lambda = [return: ValueTag("OrderId")] () => {|MFTV0002:_projectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForPropertySetterValue()
    {
        await VerifyAsync("""
            class Sample
            {
                [ValueTag("ProjectId")] Guid _projectId;

                [ValueTag("OrderId")]
                public Guid OrderId
                {
                    get => Guid.Empty;
                    set => _projectId = {|MFTV0002:value|};
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForAwaitedValues()
    {
        await VerifyAsync("""
            class Sample
            {
                [return: ValueTag("ProjectId")]
                static Task<Guid> GetProjectIdAsync() => Task.FromResult(Guid.Empty);

                static void Load([ValueTag("OrderId")] Guid orderId) { }

                async Task M()
                {
                    Load({|MFTV0002:await GetProjectIdAsync()|});
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenGenericArgumentsHaveDifferentTags()
    {
        await VerifyAsync("""
            class Sample
            {
                static T Same<T>(T first, T second) => first;
                static void Load([ValueTag("OrderId")] Guid orderId) { }

                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    _ = Same(orderId, {|MFTV0002:projectId|});
                    Load({|MFTV0002:Same(projectId, projectId)|});
                    Load(Same(orderId, Guid.Empty));
                }
            }
            """);
    }

    [Fact]
    public async Task InheritsTags_FromOverriddenAndImplementedMembers()
    {
        await VerifyAsync("""
            interface IOrderRepository
            {
                void Load([ValueTag("OrderId")] Guid orderId);

                [ValueTag("OrderId")]
                Guid CurrentId { get; }
            }

            abstract class Base
            {
                [return: ValueTag("OrderId")]
                public abstract Guid GetOrderId();
            }

            class Repository : Base, IOrderRepository
            {
                public void Load(Guid orderId) { }
                public Guid CurrentId => Guid.Empty;
                public override Guid GetOrderId() => Guid.Empty;

                void M([ValueTag("ProjectId")] Guid projectId)
                {
                    Load({|MFTV0002:projectId|});
                    _ = {|MFTV0001:CurrentId == projectId|};
                    _ = {|MFTV0001:GetOrderId() == projectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task InheritsTags_FromRecordPrimaryConstructorParameters()
    {
        await VerifyAsync("""
            record Order([ValueTag("OrderId")] Guid Id, [property: ValueTag("CustomerId")] Guid CustomerId);

            class Sample
            {
                bool M(Order order, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:order.Id == projectId|} || {|MFTV0001:order.CustomerId == projectId|};
            }
            """);
    }

    [Fact]
    public async Task AssignmentExpression_TakesTheTagOfTheAssignedValue()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid orderId) { }

                void M(Guid id, [ValueTag("ProjectId")] Guid projectId) => Load({|MFTV0002:id = projectId|});
            }
            """);
    }

    [Fact]
    public async Task CoalesceAssignmentExpression_TakesTheTagOfItsOperands()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid? orderId) { }

                void M([ValueTag("ProjectId")] Guid? projectId, [ValueTag("ProjectId")] Guid? otherProjectId) => Load({|MFTV0002:projectId ??= otherProjectId|});
            }
            """);
    }

    [Fact]
    public async Task ConditionalAccess_TakesTheTagOfTheMember()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }
            }

            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid? projectId) { }

                void M(Order? order) => Load({|MFTV0002:order?.Id|});
            }
            """);
    }

    [Fact]
    public async Task ConditionalAccess_OnATaggedCollection_TakesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid? projectId) { }

                void M([ValueTag("OrderId")] List<Guid>? ids) => Load({|MFTV0002:ids?.First()|});
            }
            """);
    }

}
