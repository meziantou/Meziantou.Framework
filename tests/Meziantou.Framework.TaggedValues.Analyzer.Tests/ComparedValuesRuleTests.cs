namespace Meziantou.Framework.Tests;

public sealed class ComparedValuesRuleTests : TaggedValuesAnalyzerTestBase
{
    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public async Task ReportDiagnostic_WhenOperandsHaveDifferentTags(string op)
    {
        await VerifyAsync($$"""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:orderId {{op}} projectId|};
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenOperandsHaveTheSameTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId1, [ValueTag("OrderId")] Guid orderId2) => orderId1 == orderId2;
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenOneOperandIsNotTagged()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, Guid other) => orderId == other || orderId == Guid.Empty || orderId == Guid.NewGuid();
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenUnionsShareATag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId", "ProjectId")] Guid id, [ValueTag("ProjectId")] Guid projectId) => id == projectId;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenUnionsShareNoTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId", "ProjectId")] Guid id, [ValueTag("CustomerId")] Guid customerId) => {|MFTV0001:id == customerId|};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForFieldsPropertiesAndMethodReturnValues()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }
                [ValueTag("ProjectId")] public Guid ProjectId;

                [return: ValueTag("CustomerId")]
                public Guid GetCustomerId() => Guid.Empty;

                bool M(Order other) => {|MFTV0001:Id == other.ProjectId|} || {|MFTV0001:GetCustomerId() == other.Id|};
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForEqualsAndCompareMethods()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId, [ValueTag("OrderName")] string orderName, [ValueTag("ProjectName")] string projectName)
                {
                    _ = {|MFTV0001:orderId.Equals(projectId)|};
                    _ = {|MFTV0001:orderId.CompareTo(projectId)|};
                    _ = {|MFTV0001:object.Equals(orderId, projectId)|};
                    _ = {|MFTV0001:EqualityComparer<Guid>.Default.Equals(orderId, projectId)|};
                    _ = {|MFTV0001:Comparer<Guid>.Default.Compare(orderId, projectId)|};
                    _ = {|MFTV0001:string.Equals(orderName, projectName, StringComparison.Ordinal)|};
                    _ = {|MFTV0001:StringComparer.Ordinal.Equals(orderName, projectName)|};
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForTupleElements()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId, int a) => ({|MFTV0001:orderId|}, a) == (projectId, a);
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForLiftedNullableComparison()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid? orderId, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:orderId == projectId|} || {|MFTV0001:orderId.Value == projectId|};
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenTheTagIsDroppedByCastingThroughObject()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId) => (Guid)(object)orderId == projectId;
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForArithmeticOrMemberAccess()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] int orderId, [ValueTag("ProjectId")] int projectId) => orderId + 1 == projectId || orderId.ToString() == projectId.ToString();
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WithNumericAndStringValues()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] int orderId, [ValueTag("ProjectId")] long projectId, [ValueTag("Email")] string email, [ValueTag("UserName")] string userName)
                    => {|MFTV0001:orderId == projectId|} || {|MFTV0001:email == userName|};
            }
            """);
    }
}
