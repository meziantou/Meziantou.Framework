using System.Reflection;
using System.Reflection.Emit;

namespace Meziantou.Framework.Assertions.Architecture.Tests;

public sealed class ModelTests
{
    private static List<string> Describe(TypeElement type) =>
        [.. type.Dependencies.Select(dependency => $"{dependency.Kind} {dependency.Target.FullName}{(dependency.SourceMember is null ? "" : " from " + dependency.SourceMember.Name)}{(dependency.TargetMember is null ? "" : " via " + dependency.TargetMember.Name)}")];

    [Fact]
    public void Declarations()
    {
        var architecture = TestCompiler.Load("""
            namespace App;
            public class Base { }
            public interface IMarker { }
            public class Tag : System.Attribute { public Tag(System.Type type) { } }
            [Tag(typeof(System.Uri))]
            public class Sample<T> : Base, IMarker where T : System.IDisposable
            {
                public System.Text.StringBuilder? Field;
                public System.Version? Property { get; set; }
                public event System.EventHandler? Changed;
                public System.Guid Method(System.TimeSpan value) => default;
            }
            """);

        var type = architecture.FindType("App.Sample`1")!;
        var dependencies = Describe(type);
        Assert.Contains("BaseType App.Base", dependencies);
        Assert.Contains("Interface App.IMarker", dependencies);
        Assert.Contains("Attribute App.Tag", dependencies);
        Assert.Contains("Attribute System.Uri", dependencies);
        Assert.Contains("GenericConstraint System.IDisposable", dependencies);
        Assert.Contains("FieldType System.Text.StringBuilder from Field", dependencies);
        Assert.Contains("PropertyType System.Version from Property", dependencies);
        Assert.Contains("EventType System.EventHandler from Changed", dependencies);
        Assert.Contains("ReturnType System.Guid from Method", dependencies);
        Assert.Contains("ParameterType System.TimeSpan from Method", dependencies);
        Assert.DoesNotContain("ReturnType System.Void from Method", dependencies);
        Assert.DoesNotContain(type.Dependencies, dependency => dependency.Target.FullName == "App.Sample`1");
    }

    [Fact]
    public void MethodBodies()
    {
        var architecture = TestCompiler.Load("""
            using System;
            using System.Collections.Generic;
            namespace App;
            public class Order { public static int Count; }
            public class Sample
            {
                public object Run(object value)
                {
                    var list = new List<Order>();
                    list.Add(new Order());
                    list.Add(new Order());
                    Order.Count++;
                    try
                    {
                        return (Uri)value;
                    }
                    catch (FormatException)
                    {
                        return typeof(Version);
                    }
                }
            }
            """);

        var dependencies = Describe(architecture.FindType("App.Sample")!);
        Assert.Contains("ObjectCreation System.Collections.Generic.List`1 from Run via .ctor", dependencies);
        Assert.Contains("ObjectCreation App.Order from Run via .ctor", dependencies);
        Assert.Contains("MethodCall System.Collections.Generic.List`1 from Run via Add", dependencies);
        Assert.Contains("MethodCall App.Order from Run via Add", dependencies);
        Assert.Contains("FieldAccess App.Order from Run via Count", dependencies);
        Assert.Contains("TypeUsage System.Uri from Run", dependencies);
        Assert.Contains("TypeUsage System.Version from Run", dependencies);
        Assert.Contains("CatchClause System.FormatException from Run", dependencies);
        Assert.Contains("LocalVariable System.Collections.Generic.List`1 from Run", dependencies);

        // Two calls to the same method give one dependency
        Assert.Single(dependencies, dependency => dependency == "MethodCall System.Collections.Generic.List`1 from Run via Add");
    }

    [Fact]
    public void CompilerGeneratedCodeBelongsToTheMember()
    {
        var architecture = TestCompiler.Load("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            namespace App;
            public class Sample
            {
                public int Lambda(int[] values)
                {
                    var random = new Random();
                    return values.Select(value => value + random.Next() + new Version().Major).Sum();
                }

                public void LocalFunction()
                {
                    Local();
                    static void Local() => Console.WriteLine(new Uri("https://example.com"));
                }

                public async Task Async()
                {
                    await Task.Yield();
                    _ = new System.Text.StringBuilder();
                }

                public IEnumerable<int> Iterator()
                {
                    yield return new Uri("https://example.com").Port;
                }

                public object Anonymous() => new { Name = "a" };
            }
            """);

        var sample = architecture.FindType("App.Sample")!;
        Assert.DoesNotContain(architecture.Types, type => type.Name.StartsWith("<", StringComparison.Ordinal));
        Assert.Empty(sample.NestedTypes);
        Assert.Equal(["Lambda", "LocalFunction", "Async", "Iterator", "Anonymous"], sample.Methods.Select(method => method.Name).ToArray());

        var dependencies = Describe(sample);
        Assert.Contains("ObjectCreation System.Version from Lambda via .ctor", dependencies);
        Assert.Contains("LocalVariable System.Random from Lambda", dependencies);
        Assert.Contains("ObjectCreation System.Uri from LocalFunction via .ctor", dependencies);
        Assert.Contains("ObjectCreation System.Text.StringBuilder from Async via .ctor", dependencies);
        Assert.Contains("ObjectCreation System.Uri from Iterator via .ctor", dependencies);
        Assert.DoesNotContain(sample.Dependencies, dependency => dependency.Target.Name.StartsWith("<", StringComparison.Ordinal));

        var async = sample.Methods.Single(method => method.Name == "Async");
        Assert.True(async.IsAsync);
        Assert.Equal(new SourceLocation(TestCompiler.GetSourcePath("Sample"), 21, 5), async.Location);
    }

    [Fact]
    public void Locations()
    {
        var architecture = TestCompiler.Load("""
            namespace App;
            public class Sample
            {
                public void Run()
                {
                    var value = 1;
                    System.Console.WriteLine(value);
                }
            }
            """);

        var dependency = architecture.FindType("App.Sample")!.Dependencies.Single(dependency => dependency.Target.FullName == "System.Console");
        Assert.Equal(new SourceLocation(TestCompiler.GetSourcePath("Sample"), 7, 9), dependency.Location);
        Assert.Equal("App.Sample.Run calls System.Console.WriteLine (/src/Sample.cs:7:9)", dependency.ToString());
    }

    [Fact]
    public void Kinds()
    {
        var architecture = TestCompiler.Load("""
            namespace App;
            public sealed record class RecordClass(int Value);
            public record struct RecordStruct(int Value);
            public struct Struct;
            public enum Enum { A }
            public delegate void Delegate();
            public interface IInterface;
            public abstract class AbstractClass;
            public static class StaticClass;
            internal sealed class InternalClass
            {
                public required string Required { get; init; }
                private protected int Field;
            }
            """);

        static TypeElement Get(ArchitectureModel architecture, string name) => architecture.FindType("App." + name)!;
        Assert.Equal(TypeElementKind.Class, Get(architecture, "RecordClass").Kind);
        Assert.True(Get(architecture, "RecordClass").IsRecord);
        Assert.True(Get(architecture, "RecordClass").IsSealed);
        Assert.Equal(TypeElementKind.Struct, Get(architecture, "RecordStruct").Kind);
        Assert.True(Get(architecture, "RecordStruct").IsRecord);
        Assert.False(Get(architecture, "Struct").IsRecord);
        Assert.Equal(TypeElementKind.Enum, Get(architecture, "Enum").Kind);
        Assert.Equal(TypeElementKind.Delegate, Get(architecture, "Delegate").Kind);
        Assert.Equal(TypeElementKind.Interface, Get(architecture, "IInterface").Kind);
        Assert.False(Get(architecture, "IInterface").IsAbstract);
        Assert.True(Get(architecture, "AbstractClass").IsAbstract);
        Assert.True(Get(architecture, "StaticClass").IsStatic);
        Assert.False(Get(architecture, "StaticClass").IsAbstract);
        Assert.False(Get(architecture, "StaticClass").IsSealed);

        var internalClass = Get(architecture, "InternalClass");
        Assert.Equal(ElementAccessibility.Internal, internalClass.Accessibility);
        var property = Assert.Single(internalClass.Properties);
        Assert.True(property.IsRequired);
        Assert.True(property.IsInitOnly);
        Assert.Equal("System.String", property.Type.FullName);
        var field = Assert.Single(internalClass.Fields);
        Assert.Equal(ElementAccessibility.PrivateProtected, field.Accessibility);
    }

    [Fact]
    public void Names()
    {
        var architecture = TestCompiler.Load("""
            namespace App.Data;
            public class Repository<TEntity>
            {
                public class Page { }
            }
            """);

        var repository = architecture.FindType("App.Data.Repository`1")!;
        Assert.Equal("Repository", repository.Name);
        Assert.Equal("Repository`1", repository.MetadataName);
        Assert.Equal("App.Data", repository.Namespace);
        Assert.Equal("App.Data.Repository<TEntity>", repository.ToString());

        var page = Assert.Single(repository.NestedTypes);
        Assert.Equal("App.Data.Repository`1+Page", page.FullName);
        Assert.Equal("App.Data", page.Namespace);
        Assert.Same(repository, page.DeclaringType);
        Assert.True(page.IsNested);
        Assert.Same(page, architecture.FindType("App.Data.Repository`1+Page"));
    }

    [Fact]
    public void HierarchyAcrossAssemblies()
    {
        var library = TestCompiler.Compile("Library", """
            namespace Library;
            public interface IEntity { }
            public abstract class Entity : IEntity { }
            """);
        var application = TestCompiler.Compile("Application", """
            namespace Application;
            public class Order : Library.Entity { }
            public class Orders : System.Collections.ObjectModel.Collection<Order> { }
            """, library);

        var architecture = ArchitectureModel.FromBytes(library, application);
        var order = architecture.FindType("Application.Order")!;
        Assert.True(order.InheritsFrom("Library.Entity"));
        Assert.True(order.Implements("Library.IEntity"));
        Assert.True(order.IsAssignableTo("Library.IEntity"));
        Assert.Same(architecture.FindType("Library.Entity"), order.BaseType!.Definition);

        // The base types of the framework are read from the assemblies of the running framework, following the type forwarders
        var orders = architecture.FindType("Application.Orders")!;
        Assert.True(orders.InheritsFrom(typeof(System.Collections.ObjectModel.Collection<>)));
        Assert.True(orders.Implements(typeof(IList<>)));
        Assert.True(orders.Implements(typeof(System.Collections.IEnumerable)));
        Assert.False(orders.Implements(typeof(IDisposable)));
        Assert.Equal("System.Collections.ObjectModel.Collection<Application.Order>", orders.BaseType!.ToString());
    }

    [Fact]
    public void Attributes()
    {
        var architecture = TestCompiler.Load("""
            using System;
            namespace App;
            public class InfoAttribute(Type type, AttributeTargets targets, params int[] values) : Attribute
            {
                public string? Name { get; set; }
            }
            [Info(typeof(Uri), AttributeTargets.Class, 1, 2, Name = "n")]
            public class Sample { }
            """);

        var attribute = Assert.Single(architecture.FindType("App.Sample")!.Attributes);
        Assert.Equal("App.InfoAttribute", attribute.Type.FullName);
        Assert.Equal("System.Uri", Assert.IsType<TypeReference>(attribute.ConstructorArguments[0]).FullName);
        Assert.Equal((int)AttributeTargets.Class, attribute.ConstructorArguments[1]);
        var values = (IReadOnlyList<object?>)attribute.ConstructorArguments[2]!;
        Assert.Equal([1, 2], values.Cast<int>().ToArray());
        Assert.Equal("n", attribute.NamedArguments["Name"]);
    }

    [Fact]
    public void ExtensionBlocksAndFileLocalTypes()
    {
        var architecture = TestCompiler.Load("""
            namespace App;
            public static class Extensions
            {
                extension(string value)
                {
                    public System.Uri ToUri() => new(value);
                }
            }
            file sealed class Helper
            {
                public static System.Version Create() => new();
            }
            """);

        Assert.Equal(["App.Extensions", architecture.Types.Single(type => type.Name.Contains("Helper", StringComparison.Ordinal)).FullName], architecture.Types.Select(type => type.FullName).ToArray());
        var extensions = architecture.FindType("App.Extensions")!;
        Assert.Contains("ObjectCreation System.Uri from ToUri via .ctor", Describe(extensions));
    }

    [Fact]
    public void Signatures()
    {
        var architecture = TestCompiler.Load("""
            namespace App;
            public unsafe class Sample
            {
                public System.Uri[,]? Array;
                public System.Guid* Pointer;
                public delegate*<System.Version, void> FunctionPointer;
                public ref System.TimeSpan Reference(ref System.TimeSpan value) => ref value;
            }
            """);

        var sample = architecture.FindType("App.Sample")!;
        var array = sample.Fields.Single(field => field.Name == "Array").Type;
        Assert.Equal(TypeReferenceKind.Array, array.Kind);
        Assert.Equal(2, array.ArrayRank);
        Assert.Equal("System.Uri[,]", array.ToString());
        Assert.Equal(TypeReferenceKind.UnmanagedPointer, sample.Fields.Single(field => field.Name == "Pointer").Type.Kind);
        Assert.Equal(TypeReferenceKind.FunctionPointer, sample.Fields.Single(field => field.Name == "FunctionPointer").Type.Kind);
        Assert.Equal(TypeReferenceKind.ByReference, sample.Methods.Single().ReturnType.Kind);

        var dependencies = Describe(sample);
        Assert.Contains("FieldType System.Uri from Array", dependencies);
        Assert.Contains("FieldType System.Guid from Pointer", dependencies);
        Assert.Contains("FieldType System.Version from FunctionPointer", dependencies);
        Assert.Contains("ReturnType System.TimeSpan from Reference", dependencies);
    }

    [Fact]
    public void LoadFromFile()
    {
        var architecture = ArchitectureModel.FromAssemblyContaining<ModelTests>();
        var assembly = Assert.Single(architecture.Assemblies);
        Assert.Equal(typeof(ModelTests).Assembly.Location, assembly.FilePath);
        Assert.Same(architecture.GetTypeElement(typeof(ModelTests)), architecture.FindType(typeof(ModelTests).FullName!));

        // The PDB next to the assembly gives the locations
        var method = architecture.GetTypeElement(typeof(ModelTests)).Methods.Single(method => method.Name == nameof(LoadFromFile));
        Assert.EndsWith("ModelTests.cs", method.Location?.FilePath);
    }

    [Fact]
    public void LoadAssemblyWithoutFile()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Dynamic"), AssemblyBuilderAccess.Run);
        Assert.Throws<ArgumentException>("assemblies", () => ArchitectureModel.FromAssemblies(assembly));
    }

    [Fact]
    public void LoadAssemblyTwice()
    {
        var architecture = ArchitectureModel.FromAssemblies(typeof(ModelTests).Assembly, typeof(ModelTests).Assembly);
        Assert.Single(architecture.Assemblies);
    }

    [Fact]
    public void GetTypeElementThrowsForUnknownType()
    {
        var architecture = TestCompiler.Load("namespace App; public class Sample;");
        Assert.Null(architecture.FindType(typeof(Uri)));
        Assert.Throws<ArgumentException>("type", () => architecture.GetTypeElement(typeof(Uri)));
    }
}
