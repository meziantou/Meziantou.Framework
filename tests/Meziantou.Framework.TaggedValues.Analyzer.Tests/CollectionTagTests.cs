namespace Meziantou.Framework.Tests;

public sealed class CollectionTagTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task ForEachVariable_TakesTheElementTagOfAList()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId)
                {
                    foreach (var id in ids)
                    {
                        _ = {|MFTV0001:id == projectId|};
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task ForEachVariable_TakesTheElementTagOfAnArray()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] Guid[] ids, [ValueTag("ProjectId")] Guid projectId)
                {
                    foreach (var id in ids)
                    {
                        _ = {|MFTV0001:id == projectId|};
                    }
                }
            }
            """);
    }

    [Theory]
    [InlineData("ids[0]")]
    [InlineData("ids.First()")]
    [InlineData("ids.Last()")]
    [InlineData("ids.Single()")]
    [InlineData("ids.ElementAt(0)")]
    [InlineData("ids.Max()")]
    [InlineData("ids.ToList()[0]")]
    [InlineData("ids.Where(id => id != Guid.Empty).OrderBy(id => id).First()")]
    public async Task ElementReturningExpression_TakesTheElementTag(string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:{{expression}} == projectId|};
            }
            """);
    }

    [Fact]
    public async Task ArrayElement_TakesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid[] ids, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:ids[0] == projectId|};
            }
            """);
    }

    [Fact]
    public async Task UserDefinedExtension_PreservesTheElementTag()
    {
        await VerifyAsync("""
            static class Extensions
            {
                public static IEnumerable<T> TakePage<T>(this IEnumerable<T> source, int page) => source.Skip(page * 10).Take(10);
            }

            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:ids.TakePage(1).First() == projectId|};
            }
            """);
    }

    [Fact]
    public async Task NonElementMember_IsNotTagged()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectCount")] int projectCount) => ids.Count == projectCount;
            }
            """);
    }

    [Fact]
    public async Task LambdaParameter_TakesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => _ = ids.Where(id => {|MFTV0001:id == projectId|});
            }
            """);
    }

    [Fact]
    public async Task LambdaParameter_WithTheSameTag_IsNotReported()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("OrderId")] Guid orderId) => ids.Any(id => id == orderId);
            }
            """);
    }

    [Fact]
    public async Task LambdaParameter_OfAnExpressionTree_TakesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] IQueryable<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => _ = ids.Where(id => {|MFTV0001:id == projectId|});
            }
            """);
    }

    [Fact]
    public async Task QueryExpressionVariable_TakesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => _ = from id in ids where {|MFTV0001:id == projectId|} select id;
            }
            """);
    }

    [Fact]
    public async Task LambdaParameter_OfAnInstanceMethod_TakesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid projectId) { }

                void M([ValueTag("OrderId")] List<Guid> ids) => ids.ForEach(id => Load({|MFTV0002:id|}));
            }
            """);
    }

    [Fact]
    public async Task Add_ExpectsTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    ids.Add(orderId);
                    ids.Add({|MFTV0002:projectId|});
                }
            }
            """);
    }

    [Theory]
    [InlineData("List<Guid>", "ids.Contains({|MFTV0002:projectId|})")]
    [InlineData("List<Guid>", "ids.IndexOf({|MFTV0002:projectId|})")]
    [InlineData("Guid[]", "ids.Contains({|MFTV0002:projectId|})")]
    [InlineData("HashSet<Guid>", "ids.Contains({|MFTV0002:projectId|})")]
    public async Task SearchMethods_ExpectTheElementTag(string collectionType, string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                void M([ValueTag("OrderId")] {{collectionType}} ids, [ValueTag("ProjectId")] Guid projectId) => _ = {{expression}};
            }
            """);
    }

    [Fact]
    public async Task Select_TakesTheTagOfTheLambdaBody()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
            }

            class Sample
            {
                bool M(List<Order> orders, [ValueTag("OrderId")] Guid orderId) => {|MFTV0001:orders.Select(order => order.ProjectId).First() == orderId|};
            }
            """);
    }

    [Fact]
    public async Task Select_TakesTheReturnTagOfTheMethodGroup()
    {
        await VerifyAsync("""
            class Sample
            {
                [return: ValueTag("ProjectId")]
                static Guid ToProjectId(Guid orderId) => Guid.Empty;

                bool M(List<Guid> ids, [ValueTag("OrderId")] Guid orderId) => {|MFTV0001:ids.Select(ToProjectId).First() == orderId|};
            }
            """);
    }

    [Fact]
    public async Task Select_WithAnIdentityLambda_PreservesTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:ids.Select(id => id).First() == projectId|};
            }
            """);
    }

    [Fact]
    public async Task Select_WithAnUntaggedLambdaBody_DropsTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectName")] string projectName) => ids.Select(id => id.ToString()).First() == projectName;
            }
            """);
    }

    [Fact]
    public async Task DictionaryIndexer_ReturnsTheValueTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("OrderId")] Guid orderId) => {|MFTV0001:map[orderId] == orderId|};
            }
            """);
    }

    [Theory]
    [InlineData("map[{|MFTV0002:projectId|}]")]
    [InlineData("map.ContainsKey({|MFTV0002:projectId|})")]
    [InlineData("map.Remove({|MFTV0002:projectId|})")]
    public async Task DictionaryKeyArguments_ExpectTheKeyTag(string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                void M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("ProjectId")] Guid projectId) => _ = {{expression}};
            }
            """);
    }

    [Fact]
    public async Task DictionaryAdd_ExpectsTheKeyAndValueTags()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    map.Add(orderId, projectId);
                    map.Add({|MFTV0002:projectId|}, {|MFTV0002:orderId|});
                }
            }
            """);
    }

    [Fact]
    public async Task DictionaryTryGetValue_TagsTheOutVariableWithTheValueTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("OrderId")] Guid orderId)
                    => map.TryGetValue(orderId, out var projectId) && {|MFTV0001:projectId == orderId|};
            }
            """);
    }

    [Fact]
    public async Task DictionaryForEach_TagsTheKeyAndTheValueOfThePair()
    {
        await VerifyAsync("""
            class Sample
            {
                void M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("OrderId")] Guid orderId)
                {
                    foreach (var pair in map)
                    {
                        _ = pair.Key == orderId;
                        _ = {|MFTV0001:pair.Value == orderId|};
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task DictionaryKeys_TakeTheKeyTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag(Key = "OrderId", Value = "ProjectId")] Dictionary<Guid, Guid> map, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:map.Keys.First() == projectId|};
            }
            """);
    }

    [Theory]
    [InlineData("Guid?", "id.Value")]
    [InlineData("Guid?", "id.GetValueOrDefault()")]
    [InlineData("Lazy<Guid>", "id.Value")]
    [InlineData("Task<Guid>", "id.Result")]
    public async Task WrapperValue_TakesTheTagOfTheWrapper(string wrapperType, string expression)
    {
        await VerifyAsync($$"""
            class Sample
            {
                bool M([ValueTag("OrderId")] {{wrapperType}} id, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:{{expression}} == projectId|};
            }
            """);
    }

    [Fact]
    public async Task AwaitedValue_TakesTheTagOfTheTask()
    {
        await VerifyAsync("""
            class Sample
            {
                async Task<bool> M([ValueTag("OrderId")] Task<Guid> id, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:await id == projectId|};
            }
            """);
    }

    [Theory]
    [InlineData("{|MFTV0002:ids|}")]
    [InlineData("{|MFTV0002:ids.Where(id => id != Guid.Empty)|}")]
    [InlineData("{|MFTV0002:new List<Guid>(ids)|}")]
    [InlineData("{|MFTV0002:[ids[0]]|}")]
    public async Task Collection_FlowsToACollectionWithADifferentTag(string argument)
    {
        await VerifyAsync($$"""
            class Sample
            {
                static void Load([ValueTag("ProjectId")] IEnumerable<Guid> projectIds) { }

                void M([ValueTag("OrderId")] List<Guid> ids) => Load({{argument}});
            }
            """);
    }

    [Fact]
    public async Task AnonymousTypeProperty_TakesTheTagOfItsInitializer()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId)
                {
                    var item = new { OrderId = orderId };
                    return {|MFTV0001:item.OrderId == projectId|};
                }
            }
            """);
    }

    [Fact]
    public async Task AnonymousTypeProjection_KeepsTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] List<Guid> ids, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:ids.Select(id => new { Id = id }).First().Id == projectId|};
            }
            """);
    }

    [Fact]
    public async Task ArrayCreation_TakesTheTagOfItsElements()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid[] projectIds) { }

                void M([ValueTag("OrderId")] Guid orderId) => Load({|MFTV0002:new[] { orderId }|});
            }
            """);
    }

    [Fact]
    public async Task SpreadElement_TakesTheTagOfTheSpreadCollection()
    {
        await VerifyAsync("""
            class Sample
            {
                static void Load([ValueTag("ProjectId")] Guid[] projectIds) { }

                void M([ValueTag("OrderId")] List<Guid> orderIds) => Load({|MFTV0002:[.. orderIds]|});
            }
            """);
    }

    [Fact]
    public async Task GenericArrayParameter_BindsTheElementTag()
    {
        await VerifyAsync("""
            class Sample
            {
                static T FirstOf<T>(T[] items) => items[0];

                bool M([ValueTag("OrderId")] Guid[] ids, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:FirstOf(ids) == projectId|};
            }
            """);
    }

    [Fact]
    public async Task GenericKeyValuePairResult_TakesTheTagsOfItsArguments()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:KeyValuePair.Create(orderId, projectId).Value == orderId|};
            }
            """);
    }

    [Fact]
    public async Task LambdaWithAReturnTag_TagsTheSelectedValues()
    {
        await VerifyAsync("""
            class Sample
            {
                bool M(List<Guid> ids, [ValueTag("OrderId")] Guid orderId) => {|MFTV0001:ids.Select([return: ValueTag("ProjectId")] (Guid id) => Guid.Empty).First() == orderId|};
            }
            """);
    }

    [Fact]
    public async Task NestedCollectionInitializer_ExpectsTheElementTagOfTheMember()
    {
        await VerifyAsync("""
            class Order
            {
                [ValueTag("OrderId")] public List<Guid> RelatedIds { get; } = [];
            }

            class Sample
            {
                Order M([ValueTag("ProjectId")] Guid projectId) => new Order { RelatedIds = { {|MFTV0002:projectId|} } };
            }
            """);
    }

    [Fact]
    public async Task GenericArrayResult_TakesTheTagOfTheBoundTypeParameter()
    {
        await VerifyAsync("""
            class Sample
            {
                static T[] Wrap<T>(T item) => [item];

                bool M([ValueTag("OrderId")] Guid orderId, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:Wrap(orderId)[0] == projectId|};
            }
            """);
    }
}
