using ChangeValueTagCodeFixProviderType = Meziantou.Framework.TaggedValues.CodeFix.ChangeValueTagCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class MissingReturnTagRuleTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ReportDiagnostic_WhenEveryReturnedValueHasTheSameTag()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] Guid _id;
                [ValueTag("OrderId")] Guid _parentId;

                Guid {|MFTV0008:GetId|}() => _id;

                Guid {|MFTV0008:GetIdOrParentId|}(bool parent)
                {
                    if (parent)
                        return _parentId;

                    return _id;
                }

                Guid {|MFTV0008:Id|} => _id;

                Guid {|MFTV0008:IdWithGetter|}
                {
                    get { return _id; }
                    set { }
                }

                Guid {|MFTV0008:this|}[int index] => _id;
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_ForLocalFunctionsAsyncMethodsAndIterators()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] Guid _id;

                async Task<Guid> {|MFTV0008:GetIdAsync|}()
                {
                    await Task.Yield();
                    return _id;
                }

                IEnumerable<Guid> {|MFTV0008:GetIds|}()
                {
                    yield return _id;
                    yield break;
                }

                void M()
                {
                    Guid {|MFTV0008:Local|}() => _id;
                    Func<Guid> lambda = () => _id;
                }
            }
            """);
    }

    [Fact]
    public async Task ReportDiagnostic_WithUnionsAndDictionaries()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId", "ProjectId")] Guid _id;
                [ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> _map = [];

                Guid {|MFTV0008:GetId|}() => _id;

                Dictionary<Guid, Guid> {|MFTV0008:GetMap|}() => _map;
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenReturnedValuesDifferOrAreNotTagged()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] Guid _id;
                [ValueTag("ProjectId")] Guid _projectId;
                [ValueTag("OrderId", "ProjectId")] Guid _anyId;

                Guid GetIdOrEmpty(bool empty)
                {
                    if (empty)
                        return Guid.Empty;

                    return _id;
                }

                Guid GetIdOrProjectId(bool project)
                {
                    if (project)
                        return _projectId;

                    return _id;
                }

                Guid GetIdOrAnyId(bool any)
                {
                    if (any)
                        return _anyId;

                    return _id;
                }

                Guid GetNewId() => Guid.NewGuid();

                Guid Throw() => throw new InvalidOperationException();

                void M() { }
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenTheReturnValueIsAlreadyTagged()
    {
        await VerifyAsync("""
            interface IOrder
            {
                [return: ValueTag("OrderId")]
                Guid GetId();
            }

            class Order : IOrder
            {
                [ValueTag("OrderId")] Guid _id;

                public Guid GetId() => _id;

                [return: ValueTag("OrderId")]
                Guid GetOtherId() => _id;

                [ValueTag("OrderId")]
                Guid Id => _id;
            }
            """);
    }

    [Fact]
    public async Task NoDiagnostic_WhenTheTagIsInferredFromANamingConvention()
    {
        await VerifyAsync("""
            class Order
            {
                Guid _orderId;

                Guid GetId() => _orderId;
            }
            """, inferTagsFromNames: true);
    }

    [Fact]
    public async Task CodeFix_TagsTheReturnValue()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Order
            {
                [ValueTag("OrderId")] Guid _id;

                [Obsolete]
                Guid {|MFTV0008:GetId|}() => _id;

                void M()
                {
                    Guid {|MFTV0008:Local|}() => _id;
                }
            }
            """,
            """
            class Order
            {
                [ValueTag("OrderId")] Guid _id;

                [Obsolete]
                [return: ValueTag("OrderId")]
                Guid GetId() => _id;

                void M()
                {
                    [return: ValueTag("OrderId")]
                    Guid Local() => _id;
                }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_TagsTheProperty()
    {
        await VerifyCodeFixAsync<ChangeValueTagCodeFixProviderType>(
            """
            class Order
            {
                [ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> _map = [];

                public Dictionary<Guid, Guid> {|MFTV0008:Map|} => _map;
            }
            """,
            """
            class Order
            {
                [ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> _map = [];

                [ValueTag(Key = "OrderId", Value = "ProjectId")]
                public Dictionary<Guid, Guid> Map => _map;
            }
            """);
    }
}
