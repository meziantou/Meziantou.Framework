namespace Meziantou.Framework.Tests;

public sealed class CrossAssemblyTests : TaggedValuesAnalyzerTestBase
{
    [Fact]
    public async Task TagsAreReadFromReferencedAssemblies()
    {
        var library = await CreateLibraryReferenceAsync("""
            public class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }
                [ValueTag("OrderId")] public List<Guid> RelatedOrderIds { get; } = [];

                public static void Load([ValueTag("OrderId")] Guid orderId) { }

                [return: ValueTag("ProjectId")]
                public static Guid GetProjectId() => Guid.Empty;
            }

            public record Project([ValueTag("ProjectId")] Guid Id);
            """);

        var test = CreateAnalyzerTest("""
            class Sample
            {
                void M(Order order, Project project)
                {
                    Order.Load({|MFTV0002:Order.GetProjectId()|});
                    Order.Load({|MFTV0002:project.Id|});
                    Order.Load(order.RelatedOrderIds.First());
                    _ = {|MFTV0001:order.Id == project.Id|};
                }
            }
            """);
        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task AssemblyAttributeTagsMembersOfOtherTypes()
    {
        await VerifyAsync("""
            [assembly: ValueTag(typeof(System.Diagnostics.Process), nameof(System.Diagnostics.Process.Id), "ProcessId")]
            [assembly: ValueTag(typeof(Entity), nameof(Entity.Id), "EntityId")]

            class Entity
            {
                public int Id { get; set; }
            }

            class User : Entity { }

            class JobRunner
            {
                [ValueTag("ProcessId")] public int ProcessId { get; set; }
                [ValueTag("JobId")] public int JobId { get; set; }

                void Track(System.Diagnostics.Process process, User user)
                {
                    ProcessId = process.Id;
                    JobId = {|MFTV0002:process.Id|};
                    JobId = {|MFTV0002:user.Id|};
                }
            }
            """);
    }

    [Fact]
    public async Task AssemblyAttributesAreReadFromReferencedAssemblies()
    {
        var library = await CreateLibraryReferenceAsync("""
            [assembly: ValueTag(typeof(System.Diagnostics.Process), nameof(System.Diagnostics.Process.Id), "ProcessId")]

            public static class Marker { }
            """);

        var test = CreateAnalyzerTest("""
            class JobRunner
            {
                [ValueTag("JobId")] public int JobId { get; set; }

                void Track(System.Diagnostics.Process process)
                {
                    JobId = {|MFTV0002:process.Id|};
                }
            }
            """);
        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task AssemblyAttributeOnAnInterfaceMember_TagsTheImplementations()
    {
        await VerifyAsync("""
            [assembly: ValueTag(typeof(IEntity), nameof(IEntity.Id), "EntityId")]

            interface IEntity
            {
                Guid Id { get; }
            }

            class User : IEntity
            {
                public Guid Id { get; set; }
            }

            class Sample
            {
                bool M(User user, [ValueTag("ProjectId")] Guid projectId) => {|MFTV0001:user.Id == projectId|};
            }
            """);
    }
}
