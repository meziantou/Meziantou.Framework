namespace Meziantou.Framework.Tests;

public sealed class CollectionTagTests : TaggedValuesAnalyzerTestBase
{
    private const string Declarations = """
        class Repository
        {
            [ValueTag("OrderId")] public List<Guid> OrderIds { get; } = [];
            [ValueTag("OrderId")] public Guid[] OrderIdArray { get; } = [];
            [ValueTag("OrderId")] public IQueryable<Guid> OrderIdQuery { get; } = null!;
            [ValueTag(Key = "OrderId", Value = "ProjectId")] public Dictionary<Guid, Guid> ProjectIdByOrderId { get; } = [];
            [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
            [ValueTag("OrderId")] public Guid OrderId { get; set; }

            public static void LoadOrder([ValueTag("OrderId")] Guid orderId) { }
            public static void LoadProject([ValueTag("ProjectId")] Guid projectId) { }
        }

        static class PagingExtensions
        {
            public static IEnumerable<T> TakePage<T>(this IEnumerable<T> source, int page) => source.Skip(page * 10).Take(10);
        }

        """;

    [Fact]
    public async Task ElementTag_FlowsThroughForEach()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                void M(Repository repository)
                {
                    foreach (var id in repository.OrderIds)
                    {
                        Repository.LoadOrder(id);
                        Repository.LoadProject({|MFTV0002:id|});
                    }

                    foreach (var id in repository.OrderIdArray)
                    {
                        Repository.LoadProject({|MFTV0002:id|});
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task ElementTag_FlowsThroughIndexersAndElementReturningMethods()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                async Task M(Repository repository, IAsyncEnumerable<Guid> source)
                {
                    Repository.LoadProject({|MFTV0002:repository.OrderIds[0]|});
                    Repository.LoadProject({|MFTV0002:repository.OrderIdArray[0]|});
                    Repository.LoadProject({|MFTV0002:repository.OrderIds.First()|});
                    Repository.LoadProject({|MFTV0002:repository.OrderIds.Where(id => id != Guid.Empty).OrderBy(id => id).Last()|});
                    Repository.LoadProject({|MFTV0002:repository.OrderIds.TakePage(1).Single()|});
                    Repository.LoadProject({|MFTV0002:repository.OrderIds.ToList()[0]|});
                    Repository.LoadProject({|MFTV0002:repository.OrderIds.Max()|});
                    _ = repository.OrderIds.Count;
                }
            }
            """);
    }

    [Fact]
    public async Task ElementTag_FlowsToLambdaParameters()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                void M(Repository repository)
                {
                    _ = repository.OrderIds.Where(id => {|MFTV0001:id == repository.ProjectId|});
                    _ = repository.OrderIds.Any(id => id == repository.OrderId);
                    _ = repository.OrderIdQuery.Where(id => {|MFTV0001:id == repository.ProjectId|});
                    _ = from id in repository.OrderIds where {|MFTV0001:id == repository.ProjectId|} select id;
                    repository.OrderIds.ForEach(id => Repository.LoadProject({|MFTV0002:id|}));
                }
            }
            """);
    }

    [Fact]
    public async Task ElementTag_IsCheckedWhenAddingOrSearching()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                void M(Repository repository)
                {
                    repository.OrderIds.Add({|MFTV0002:repository.ProjectId|});
                    repository.OrderIds.Add(repository.OrderId);
                    _ = repository.OrderIds.Contains({|MFTV0002:repository.ProjectId|});
                    _ = repository.OrderIds.IndexOf({|MFTV0002:repository.ProjectId|});
                    _ = repository.OrderIdArray.Contains({|MFTV0002:repository.ProjectId|});
                }
            }
            """);
    }

    [Fact]
    public async Task SelectTransformsTheElementTag()
    {
        await VerifyAsync(Declarations + """
            class Order
            {
                [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
            }

            class Sample
            {
                [return: ValueTag("ProjectId")]
                static Guid ToProjectId(Guid orderId) => Guid.Empty;

                void M(Repository repository, List<Order> orders)
                {
                    Repository.LoadOrder({|MFTV0002:orders.Select(order => order.ProjectId).First()|});
                    Repository.LoadOrder({|MFTV0002:repository.OrderIds.Select(ToProjectId).First()|});
                    Repository.LoadOrder(repository.OrderIds.Select(id => id).First());
                    _ = repository.OrderIds.Select(id => id.ToString()).Count();
                }
            }
            """);
    }

    [Fact]
    public async Task DictionaryKeyAndValueTags()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                void M(Repository repository)
                {
                    Repository.LoadOrder({|MFTV0002:repository.ProjectIdByOrderId[repository.OrderId]|});
                    _ = repository.ProjectIdByOrderId[{|MFTV0002:repository.ProjectId|}];
                    _ = repository.ProjectIdByOrderId.ContainsKey({|MFTV0002:repository.ProjectId|});
                    repository.ProjectIdByOrderId.Add(repository.OrderId, repository.ProjectId);
                    repository.ProjectIdByOrderId.Add({|MFTV0002:repository.ProjectId|}, {|MFTV0002:repository.OrderId|});
                    if (repository.ProjectIdByOrderId.TryGetValue(repository.OrderId, out var projectId))
                    {
                        Repository.LoadOrder({|MFTV0002:projectId|});
                    }

                    foreach (var pair in repository.ProjectIdByOrderId)
                    {
                        Repository.LoadOrder(pair.Key);
                        Repository.LoadOrder({|MFTV0002:pair.Value|});
                    }

                    foreach (var key in repository.ProjectIdByOrderId.Keys)
                    {
                        Repository.LoadProject({|MFTV0002:key|});
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task WrapperTags_FlowThroughNullableTaskAndLazy()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                [return: ValueTag("OrderId")]
                static Task<Guid> GetOrderIdAsync() => Task.FromResult(Guid.Empty);

                async Task M([ValueTag("OrderId")] Guid? orderId, [ValueTag("OrderId")] Lazy<Guid> lazyOrderId)
                {
                    Repository.LoadProject({|MFTV0002:orderId.Value|});
                    Repository.LoadProject({|MFTV0002:orderId.GetValueOrDefault()|});
                    Repository.LoadProject({|MFTV0002:lazyOrderId.Value|});
                    Repository.LoadProject({|MFTV0002:GetOrderIdAsync().Result|});
                    Repository.LoadProject({|MFTV0002:await GetOrderIdAsync()|});
                }
            }
            """);
    }

    [Fact]
    public async Task CollectionTag_IsCheckedForWholeCollections()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                static void LoadProjects([ValueTag("ProjectId")] IEnumerable<Guid> projectIds) { }

                void M(Repository repository)
                {
                    LoadProjects({|MFTV0002:repository.OrderIds|});
                    LoadProjects({|MFTV0002:repository.OrderIdArray.Where(id => id != Guid.Empty)|});
                    LoadProjects({|MFTV0002:new List<Guid>(repository.OrderIds)|});
                    LoadProjects([repository.ProjectId]);
                    LoadProjects({|MFTV0002:[repository.OrderId]|});
                }
            }
            """);
    }

    [Fact]
    public async Task AnonymousTypeProperties_InferTheirTagFromTheInitializer()
    {
        await VerifyAsync(Declarations + """
            class Sample
            {
                void M(Repository repository)
                {
                    var item = new { OrderId = repository.OrderId, Name = "" };
                    Repository.LoadProject({|MFTV0002:item.OrderId|});

                    var projected = repository.OrderIds.Select(id => new { Id = id }).First();
                    Repository.LoadProject({|MFTV0002:projected.Id|});
                }
            }
            """);
    }
}
