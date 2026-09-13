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

    [Theory]
    [InlineData("orderId + 1")]
    [InlineData("1 + orderId")]
    [InlineData("orderId - offset")]
    [InlineData("orderId * 2")]
    [InlineData("2 * orderId")]
    [InlineData("orderId / 2")]
    [InlineData("-orderId")]
    [InlineData("+orderId")]
    [InlineData("(orderId + 1) * 2")]
    [InlineData("orderId++")]
    [InlineData("--orderId")]
    [InlineData("(orderId += 1)")]
    public async Task ReportDiagnostic_WhenArithmeticKeepsTheTag(string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                bool M([ValueTag("OrderId")] int orderId, [ValueTag("ProjectId")] int projectId, int offset) => {|MFTV0001:{{expression}} == projectId|};
            }
            """);
    }

    [Theory]
    [InlineData("orderId % 10")]
    [InlineData("orderId & 0xFF")]
    [InlineData("orderId << 1")]
    [InlineData("orderId * orderCount")]
    [InlineData("orderId / orderCount")]
    public async Task NoDiagnostic_WhenArithmeticCreatesAnotherKindOfValue(string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                bool M([ValueTag("OrderId")] int orderId, [ValueTag("OrderCount")] int orderCount, [ValueTag("ProjectId")] int projectId) => ({{expression}}) == projectId;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForDecimalArithmetic()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("Price")] decimal price, [ValueTag("Discount")] decimal discount) => {|MFTV0001:price * 2 == discount|};
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForMemberAccess()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] int orderId, [ValueTag("ProjectId")] int projectId) => orderId.ToString() == projectId.ToString();
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForStringConcatenation()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("UserName")] string userName, [ValueTag("Email")] string email) => userName + "@example.com" == email;
            }
            """);
    }

    [Fact]
    public async Task UserDefinedOperator_TakesTheTagOfItsReturnValue()
    {
        await VerifyAsync("""
            readonly record struct Distance(double Value)
            {
                [return: ValueTag("Meters")]
                public static Distance operator +(Distance left, Distance right) => new(left.Value + right.Value);

                public static Distance operator -(Distance left, Distance right) => new(left.Value - right.Value);
            }

            class Sample
            {
                bool M([ValueTag("Meters")] Distance a, [ValueTag("Meters")] Distance b, [ValueTag("Feet")] Distance feet)
                    => {|MFTV0001:a + b == feet|} || a - b == feet;
            }
            """);
    }

    [Fact]
    public async Task UserDefinedOperator_DoesNotKeepTheTagOfItsOperands()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("DueDate")] DateTime dueDate, [ValueTag("StartDate")] DateTime startDate, [ValueTag("Timeout")] TimeSpan timeout)
                    => dueDate - startDate == timeout;
            }
            """);
    }

    [Fact]
    public async Task UserDefinedConversion_TakesTheTagOfItsReturnValue()
    {
        await VerifyAsync("""
            readonly record struct Distance(double Value)
            {
                [return: ValueTag("Meters")]
                public static implicit operator double(Distance distance) => distance.Value;
            }

            class Sample
            {
                bool M(Distance distance, [ValueTag("Feet")] double feet) => {|MFTV0001:distance == feet|};
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

    [Fact]
    public async Task ReportDiagnostic_ForLiftedArithmetic()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] int? orderId, [ValueTag("ProjectId")] int projectId) => {|MFTV0001:orderId + 1 == projectId|};
            }
            """);
    }

    [Fact]
    public async Task UserDefinedUnaryOperators_TakeTheTagOfTheirReturnValue()
    {
        await VerifyAsync("""
            readonly record struct Distance(double Value)
            {
                [return: ValueTag("Meters")]
                public static Distance operator -(Distance value) => new(-value.Value);

                [return: ValueTag("Meters")]
                public static Distance operator ++(Distance value) => new(value.Value + 1);
            }

            class Sample
            {
                bool M(Distance distance, [ValueTag("Feet")] Distance feet) => {|MFTV0001:-distance == feet|} || {|MFTV0001:distance++ == feet|};
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_ForCompareMethodsWithDifferentParameterTypes()
    {
        await VerifyAsync("""
            class Sample
            {
                int M([ValueTag("OrderName")] string orderName, [ValueTag("ProjectName")] string projectName) => string.Compare(orderName, 0, projectName, 0, 1, StringComparison.Ordinal);
            }
            """);
    }
}
