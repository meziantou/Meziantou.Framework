namespace Meziantou.Framework.Tests;

public sealed class CombinedValuesRuleTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ReportDiagnostic_ForConditionalExpressions()
    {
        await VerifyAsync("""
            class Sample
            {
                Guid M(bool condition, [ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                    => condition ? orderId : {|MFTV0003:projectId|};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForCoalesceExpressions()
    {
        await VerifyAsync("""
            class Sample
            {
                Guid M([ValueTag("OrderId")] Guid? orderId, [ValueTag("ProjectId")] Guid projectId)
                    => orderId ?? {|MFTV0003:projectId|};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForSwitchExpressions()
    {
        await VerifyAsync("""
            class Sample
            {
                Guid M(int value, [ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                    => value switch
                    {
                        0 => orderId,
                        1 => Guid.Empty,
                        _ => {|MFTV0003:projectId|},
                    };
            }
            """);
    }

    [Theory]
    [InlineData("orderId + {|MFTV0003:projectId|}")]
    [InlineData("orderId - {|MFTV0003:projectId|}")]
    public async Task ReportDiagnostic_ForAdditionsOfDifferentTags(string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                void M([ValueTag("OrderId")] int orderId, [ValueTag("ProjectId")] int projectId) => _ = {{expression}};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForCompoundAdditionsOfDifferentTags()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] int orderId, [ValueTag("ProjectId")] int projectId) => orderId += {|MFTV0003:projectId|};
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForMultiplicationsOfDifferentTags()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("Meters")] double meters, [ValueTag("Seconds")] double seconds) => _ = meters / seconds;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForOperandsOfAUserDefinedOperator()
    {
        await VerifyAsync("""
            readonly record struct Distance(double Value)
            {
                public static Distance operator +([ValueTag("Meters")] Distance left, [ValueTag("Meters")] Distance right) => new(left.Value + right.Value);
            }

            class Sample
            {
                void M([ValueTag("Meters")] Distance meters, [ValueTag("Feet")] Distance feet) => _ = meters + {|MFTV0002:feet|};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForCollectionElements()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    Guid[] array = new[] { orderId, {|MFTV0003:projectId|} };
                    Guid[] collection = [orderId, {|MFTV0003:projectId|}];
                }
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenBranchesAgreeOrAreNotTagged()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("OrderId")] Guid orderId) { }

                void M(bool condition, [ValueTag("OrderId")] Guid orderId1, [ValueTag("OrderId")] Guid? orderId2, [ValueTag("ProjectId")] Guid projectId)
                {
                    Load(condition ? orderId1 : Guid.Empty);
                    Load(orderId2 ?? orderId1);
                    Load({|MFTV0002:condition ? projectId : Guid.Empty|});
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForTheOperandOfAUserDefinedCompoundAssignment()
    {
        await VerifyAsync("""
            readonly record struct Distance(double Value)
            {
                public static Distance operator +(Distance left, [ValueTag("Meters")] Distance right) => new(left.Value + right.Value);
            }

            class Sample
            {
                void M(Distance total, [ValueTag("Feet")] Distance feet) => total += {|MFTV0002:feet|};
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForParamsArgumentsAndCollectionsOfObjects()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }
                [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
                [ValueTag("OrderCode")] public string Code { get; set; } = "";
                [ValueTag("ProjectCode")] public string ProjectCode { get; set; } = "";
            }

            class Sample
            {
                [return: ValueTag("OrderId")]
                static Task<Guid> GetOrderIdAsync() => Task.FromResult(Guid.Empty);

                [return: ValueTag("ProjectId")]
                static Task<Guid> GetProjectIdAsync() => Task.FromResult(Guid.Empty);

                static void Log(string message, params object[] args) { }
                static void Delete(params Guid[] ids) { }

                async Task M(Order order)
                {
                    Log("{0} {1}", order.Id, order.ProjectId);
                    _ = string.Format("{0} {1} {2}", order.Id, order.ProjectId, order.Code);
                    _ = string.Join("-", order.Code, order.ProjectCode);
                    Delete(order.Id, order.ProjectId);
                    await Task.WhenAll(GetOrderIdAsync(), GetProjectIdAsync());
                    _ = new object[] { order.Id, order.ProjectId };
                    object[] values = [order.Id, order.ProjectId];
                    _ = new List<object> { order.Id, order.ProjectId };
                    _ = new Dictionary<string, object> { ["order"] = order.Id, ["project"] = order.ProjectId };
                    _ = new[] { order.Id, {|MFTV0003:order.ProjectId|} };
                    Delete(new[] { order.Id, {|MFTV0003:order.ProjectId|} });
                }
            }
            """);
    }
}
