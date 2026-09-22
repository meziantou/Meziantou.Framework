namespace Meziantou.Framework.Tests;

public sealed class InheritedTagMismatchRuleTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ReportDiagnostic_WhenOverrideChangesTheTag()
    {
        await VerifyAsync("""
            abstract class Base
            {
                [return: ValueTag("OrderId")]
                public abstract Guid GetId([ValueTag("OrderId")] Guid id);

                [ValueTag("OrderId")]
                public abstract Guid Id { get; }
            }

            class Derived : Base
            {
                [return: ValueTag("ProjectId")]
                public override Guid {|MFTV0004:GetId|}([ValueTag("ProjectId")] Guid {|MFTV0004:id|}) => id;

                [ValueTag("ProjectId")]
                public override Guid {|MFTV0004:Id|} => Guid.Empty;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WhenImplementationChangesTheTag()
    {
        await VerifyAsync("""
            interface IRepository
            {
                void Load([ValueTag("OrderId")] Guid id);
            }

            class Implicit : IRepository
            {
                public void Load([ValueTag("ProjectId")] Guid {|MFTV0004:id|}) { }
            }

            class Explicit : IRepository
            {
                void IRepository.Load([ValueTag("ProjectId")] Guid {|MFTV0004:id|}) { }
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenTagsAreCompatibleOrOmitted()
    {
        await VerifyAsync("""
            interface IRepository
            {
                void Load([ValueTag("OrderId")] Guid id);
                void Save([ValueTag("OrderId")] Guid id);
            }

            class Repository : IRepository
            {
                public void Load(Guid id) { }
                public void Save([ValueTag("OrderId", "ProjectId")] Guid id) { }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForRecordParametersIndexersAndImplementationsInABaseType()
    {
        await VerifyAsync("""
            interface IHasId
            {
                [ValueTag("OrderId")] Guid Id { get; }
            }

            record Order([ValueTag("ProjectId")] Guid {|MFTV0004:Id|}) : IHasId;

            interface IStore
            {
                Guid this[[ValueTag("OrderId")] Guid key] { get; }
            }

            class Store : IStore
            {
                public Guid this[[ValueTag("ProjectId")] Guid {|MFTV0004:key|}] => Guid.Empty;
            }

            interface IRepository
            {
                void Load([ValueTag("OrderId")] Guid id);
            }

            class Repository
            {
                public void Load([ValueTag("ProjectId")] Guid id) { }
            }

            class {|MFTV0004:DerivedRepository|} : Repository, IRepository
            {
            }
            """);
    }
}
