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
}
