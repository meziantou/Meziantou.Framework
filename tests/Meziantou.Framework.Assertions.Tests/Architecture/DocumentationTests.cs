using SampleApp.Domain;
using SampleApp.Web;

namespace Meziantou.Framework.Assertions.Architecture.Tests;

// The examples of the documentation and of the XML documentation, run on the sample application of the SampleApp folder
public sealed class DocumentationTests
{
    [Fact]
    public void DoesNotDependOn()
    {
        #region Architecture_DoesNotDependOn
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));
        var infrastructure = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

        Assert.Architecture.DoesNotDependOn(domain, infrastructure, "the domain stays persistence-ignorant");
        Assert.Architecture.DoesNotDependOn(domain, t => t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));
        Assert.Architecture.DoesNotDependOn<HttpClient>(domain);
        #endregion
    }

    [Fact]
    public void DependsOnlyOn()
    {
        #region Architecture_DependsOnlyOn
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));

        // The types of the domain may use each other and the types of .NET, nothing else
        Assert.Architecture.DependsOnlyOn(domain, t => t.IsInNamespaceOrChildren("System"));
        #endregion
    }

    [Fact]
    public void DoesNotAccessMember()
    {
        #region Architecture_DoesNotAccessMember
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var types = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp"));

        Assert.Architecture.DoesNotAccessMember<DateTime>(types, "Now", "the current time comes from TimeProvider");
        Assert.Architecture.DoesNotAccessMember<DateTime>(types, nameof(DateTime.UtcNow), "the current time comes from TimeProvider");
        #endregion
    }

    [Fact]
    public void ShapeRules()
    {
        #region Architecture_ShapeRules
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var repositories = architecture.Types.Where(t => t.IsClass && t.Implements<IOrderRepository>());

        Assert.Architecture.AllMatch(repositories, t => t.Name.EndsWith("Repository", StringComparison.Ordinal));
        Assert.Architecture.AllMatch(repositories, t => t.IsSealed);
        Assert.Architecture.AllMatch(architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain") && !t.IsInterface), t => t.IsImmutable);
        #endregion
    }

    [Fact]
    public void MemberRules()
    {
        #region Architecture_MemberRules
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var handlers = architecture.Types.Where(t => t.IsInNamespace("SampleApp.Application") && t.Name.EndsWith("Handler", StringComparison.Ordinal));
        var controllers = architecture.Types.Where(t => t.IsInNamespace("SampleApp.Web") && t.Name.EndsWith("Controller", StringComparison.Ordinal));

        Assert.Architecture.AllMatch(handlers.SelectMany(t => t.Methods).Where(method => method.IsPublic), method => method.IsAsync);
        var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));
        Assert.Architecture.AllMatch(domain.SelectMany(t => t.Properties), property => property.Setter is null || property.IsInitOnly, "the domain is immutable");

        // OrdersController.CreateRepository is a public method that does not return a Task
        var actions = controllers.SelectMany(t => t.Methods).Where(method => method.IsPublic);
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(actions, method => method.ReturnType.Is(typeof(Task<>))));
        Assert.StartsWith("""
            Assert.Architecture.AllMatch() assertion failed: 1 member does not match the predicate.
            Members expression:   actions
            Predicate expression: method => method.ReturnType.Is(typeof(Task<>))
            - SampleApp.Web.OrdersController.CreateRepository()
            """.ReplaceLineEndings("\n"), exception.Message);
        #endregion
    }

    [Fact]
    public void MemberDependencies()
    {
        #region Architecture_MemberDependencies
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var publicMembers = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp") && t.IsPublic).SelectMany(t => t.Members).Where(member => member.IsPublic);
        var infrastructure = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

        // The signatures and the code of the public members do not use Entity Framework Core
        Assert.Architecture.DoesNotDependOn(publicMembers, t => t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));

        // The controllers get the infrastructure by injection: OrdersController.CreateRepository breaks the rule
        var controllerMethods = architecture.Types.Where(t => t.IsInNamespace("SampleApp.Web")).SelectMany(t => t.Methods);
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(controllerMethods, infrastructure));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 member depends on a forbidden type.
            Members expression:   controllerMethods
            Forbidden expression: infrastructure
            - SampleApp.Web.OrdersController.CreateRepository() depends on SampleApp.Infrastructure.SqlOrderRepository
                SampleApp.Web.OrdersController.CreateRepository creates SampleApp.Infrastructure.SqlOrderRepository..ctor
            """.ReplaceLineEndings("\n"), exception.Message);
        #endregion
    }

    [Fact]
    public void Layers()
    {
        #region Architecture_Layers
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var web = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Web"));
        var application = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Application"));
        var domain = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain"));
        var infrastructure = architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

        Assert.Architecture.DoesNotDependOn(domain, [.. application, .. web, .. infrastructure]);
        Assert.Architecture.DoesNotDependOn(application, [.. web, .. infrastructure]);
        Assert.Architecture.DoesNotDependOn(infrastructure, [.. application, .. web]);

        // OrdersController creates the repository itself
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(web, infrastructure, "the web layer gets the repository by injection"));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 type depends on a forbidden type.
            Message: the web layer gets the repository by injection
            Types expression:     web
            Forbidden expression: infrastructure
            - SampleApp.Web.OrdersController depends on SampleApp.Infrastructure.SqlOrderRepository
                SampleApp.Web.OrdersController.CreateRepository creates SampleApp.Infrastructure.SqlOrderRepository..ctor
            """.ReplaceLineEndings("\n"), exception.Message);
        #endregion
    }

    [Fact]
    public void Slices()
    {
        #region Architecture_Slices
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var modules = architecture.Types.SliceByNamespace("SampleApp.Modules.{slice}");

        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotHaveCycles(modules));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotHaveCycles() assertion failed: 1 cycle between slices.
            Slices expression: modules
            - Billing -> Shipping -> Billing
                SampleApp.Modules.Billing.Invoice.Shipment has a property of type SampleApp.Modules.Shipping.Shipment
            """.ReplaceLineEndings("\n"), exception.Message);
        #endregion
    }

    [Fact]
    public void Model()
    {
        #region Architecture_Model
        var architecture = ArchitectureModel.FromAssemblyContaining<Order>();
        var controller = architecture.GetTypeElement(typeof(OrdersController));

        var usedTypes = controller.Dependencies
            .Select(dependency => dependency.Target.FullName)
            .Where(name => name.StartsWith("SampleApp.", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        string[] expected = ["SampleApp.Application.PlaceOrderHandler", "SampleApp.Domain.IOrderRepository", "SampleApp.Domain.OrderLine", "SampleApp.Infrastructure.SqlOrderRepository"];
        Assert.Equal(expected, usedTypes);
        #endregion
    }
}
