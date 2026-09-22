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

    [Fact]
    public async Task InstanceCompoundAssignmentOperator_ChecksItsParameter()
    {
        await VerifyAsync("""
            class Distance
            {
                public void operator +=([ValueTag("Meters")] double value) { }
            }

            class Sample
            {
                void M(Distance distance, [ValueTag("Feet")] double feet) => distance += {|MFTV0002:feet|};
            }
            """);
    }

    [Fact]
    public async Task RecordProperty_IsNotTaggedByTheParametersOfOtherConstructors()
    {
        await VerifyAsync("""
            record Order(Guid Id)
            {
                public Order([ValueTag("ProjectId")] Guid Id, int version) : this(Id) { }
            }

            class Sample
            {
                bool M(Order order, [ValueTag("OrderId")] Guid orderId) => order.Id == orderId;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForDeconstructionAssignments()
    {
        await VerifyAsync("""
            class Sample
            {
                [ValueTag("OrderId")] Guid _orderId;
                [ValueTag("ProjectId")] Guid _projectId;

                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    (_orderId, _projectId) = ({|MFTV0002:projectId|}, {|MFTV0002:orderId|});
                    (_orderId, _projectId) = (orderId, projectId);
                    (_orderId, _) = ({|MFTV0002:projectId|}, orderId);
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForOutArgumentsToExistingVariables()
    {
        await VerifyAsync("""
            class Sample
            {
                [ValueTag("ProjectId")] Guid _projectId;

                static bool TryGet([ValueTag("OrderId")] out Guid id)
                {
                    id = Guid.NewGuid();
                    return true;
                }

                void M()
                {
                    Guid /* ValueTag=ProjectId */ local = Guid.Empty;
                    TryGet(out {|#0:local|});
                    TryGet(out {|MFTV0002:_projectId|});
                    Guid /* ValueTag=OrderId */ orderId = Guid.Empty;
                    TryGet(out orderId);
                    TryGet(out _);
                }
            }
            """, inferTagsFromNames: false, Diagnostic("MFTV0002").WithLocation(0).WithLocation(18, 39).WithMessage("parameter 'id' of 'Sample.TryGet' (line 10) is [ValueTag(\"OrderId\")] and flows to local 'local', which is [ValueTag(\"ProjectId\")]"));
    }

    [Fact]
    public async Task ReportDiagnostic_ForTheOperandsOfUserDefinedComparisonAndCompoundAssignmentOperators()
    {
        await VerifyAsync("""
            readonly struct Money
            {
                public static bool operator <([ValueTag("EUR")] Money left, [ValueTag("EUR")] Money right) => true;
                public static bool operator >([ValueTag("EUR")] Money left, [ValueTag("EUR")] Money right) => true;

                [return: ValueTag("EUR")]
                public static Money operator +([ValueTag("EUR")] Money left, [ValueTag("EUR")] Money right) => left;
            }

            class Sample
            {
                [ValueTag("USD")] Money _usd;
                [ValueTag("EUR")] Money _eur;
                [ValueTag("EUR")] Money _otherEur;

                void M()
                {
                    _ = {|MFTV0001:{|MFTV0002:_usd|} < _eur|};
                    _ = _eur < _otherEur;
                    {|MFTV0002:{|MFTV0002:_usd|} += _eur|};
                    _eur += _otherEur;
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForTheTaggedParametersOfAComparer()
    {
        await VerifyAsync("""
            class OrderIdComparer : IComparer<Guid>
            {
                public int Compare([ValueTag("OrderId")] Guid x, [ValueTag("OrderId")] Guid y) => x.CompareTo(y);
            }

            class Sample
            {
                int M([ValueTag("ProjectId")] Guid projectId, OrderIdComparer comparer) => comparer.Compare({|MFTV0002:projectId|}, {|MFTV0002:projectId|});
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForMembersThatImplementATaggedInterfaceMember()
    {
        await VerifyAsync("""
            interface ILoader
            {
                void Load([ValueTag("OrderId")] Guid id);

                Guid this[[ValueTag("OrderId")] Guid id] { get; }
            }

            interface IStaticLoader
            {
                static abstract void LoadStatic([ValueTag("OrderId")] Guid id);
            }

            class Base
            {
                public void Load(Guid id) { }

                public Guid this[Guid id] => id;
            }

            class Derived : Base, ILoader
            {
            }

            abstract class AbstractBase
            {
                public abstract void Load(Guid id);
            }

            class Overriding : AbstractBase, ILoader
            {
                public override void Load(Guid id) { }

                public Guid this[Guid id] => id;
            }

            class StaticLoader : IStaticLoader
            {
                public static void LoadStatic(Guid id) { }
            }

            class Sample
            {
                void M([ValueTag("ProjectId")] Guid projectId, Derived derived, Overriding overriding)
                {
                    derived.Load({|MFTV0002:projectId|});
                    _ = derived[{|MFTV0002:projectId|}];
                    overriding.Load({|MFTV0002:projectId|});
                    _ = overriding[{|MFTV0002:projectId|}];
                    StaticLoader.LoadStatic({|MFTV0002:projectId|});
                }
            }
            """);
    }

    [Fact]
    public async Task DeconstructMethodsAndPositionalPatternsKeepTheTags()
    {
        await VerifyAsync("""
            record Order([ValueTag("OrderId")] Guid Id, [ValueTag("ProjectId")] Guid ProjectId);

            class Pair
            {
                public void Deconstruct([ValueTag("OrderId")] out Guid orderId, [ValueTag("ProjectId")] out Guid projectId)
                {
                    orderId = Guid.Empty;
                    projectId = Guid.Empty;
                }
            }

            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid id) { }

                void M(Order order, Pair pair)
                {
                    var (id, projectId) = order;
                    Load(id);
                    Load({|MFTV0002:projectId|});
                    if (order is (var a, var b))
                    {
                        Load(a);
                        Load({|MFTV0002:b|});
                    }

                    var (c, d) = pair;
                    Load(c);
                    Load({|MFTV0002:d|});
                }
            }
            """);
    }
}
