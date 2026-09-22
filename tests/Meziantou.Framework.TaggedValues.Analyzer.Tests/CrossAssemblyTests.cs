using Meziantou.Framework.TaggedValues;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

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

    [Fact]
    public async Task TagsAreReadFromReferencedProjects()
    {
        var test = CreateAnalyzerTest("""
            class Sample
            {
                [ValueTag("ProjectId")] Guid _projectId;

                void M(Order order)
                {
                    Order.Load({|MFTV0002:_projectId|});
                    _ = {|MFTV0001:order.Id == _projectId|};
                }
            }
            """);
        AddReferencedProject(test, """
            public class Order
            {
                [ValueTag("OrderId")] public Guid Id { get; set; }

                public static void Load([ValueTag("OrderId")] Guid orderId) { }
            }
            """);
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task StrictMode_AcceptsTheDeclarationsOfReferencedProjects()
    {
        var test = CreateAnalyzerTest("""
            class Sample
            {
                [ValueTag("OrderId")] Guid _orderId;

                void M()
                {
                    Library.Save(_orderId);
                    _orderId = Library.Create();
                }
            }
            """, strict: true);
        AddReferencedProject(test, """
            public static class Library
            {
                public static void Save(Guid id) { }

                public static Guid Create() => Guid.NewGuid();
            }
            """);
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task ConventionsDoNotApplyToReferencedProjects()
    {
        var test = CreateAnalyzerTest("""
            class Sample
            {
                bool M(Order order, Project project) => order.Id == project.Id;
            }
            """, inferTagsFromNames: true);
        AddReferencedProject(test, """
            public class Order
            {
                public Guid Id { get; set; }
            }

            public class Project
            {
                public Guid Id { get; set; }
            }
            """);
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task AssemblyAttributeOnAConstructedGenericTypeOnlyTagsThisType()
    {
        await VerifyAsync("""
            [assembly: ValueTag(typeof(Box<int>), nameof(Box<int>.Value), "OrderIndex")]

            class Box<T>
            {
                public T Value { get; set; } = default!;
            }

            class Sample
            {
                [ValueTag("Name")] string _name = "";
                [ValueTag("ProjectIndex")] int _projectIndex;

                bool M(Box<string> names, Box<int> indexes) => names.Value == _name || {|MFTV0001:indexes.Value == _projectIndex|};
            }
            """);
    }

    /// <summary>
    /// Adds a project referenced as a compilation, as the IDE does for a project reference, so its declarations are in source but not in the analyzed compilation.
    /// </summary>
    private static void AddReferencedProject(AnalyzerTest<DefaultVerifier> test, string source)
    {
        var project = test.TestState.AdditionalProjects["Library"];
        project.Sources.Add(("/Library/Library.cs", """
            using System;
            using Meziantou.Framework.TaggedValues;

            """ + source));
        project.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ValueTagAttribute).Assembly.Location));
        test.TestState.AdditionalProjectReferences.Add("Library");
    }
}
