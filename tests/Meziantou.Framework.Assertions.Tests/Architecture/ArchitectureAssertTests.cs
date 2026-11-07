namespace Meziantou.Framework.Assertions.Architecture.Tests;

public sealed class ArchitectureAssertTests
{
    private static readonly Lazy<ArchitectureModel> App = new(() => TestCompiler.Load("""
        using System;
        namespace App.Domain
        {
            public sealed class Order
            {
                public Guid Id { get; init; }
                public DateTime CreatedAt { get; } = DateTime.Now;
            }

            public class Customer
            {
                public string? Name { get; set; }
                public App.Data.Database? Database;
            }
        }

        namespace App.Data
        {
            public sealed class Database
            {
                public void Save(App.Domain.Order order) => Console.WriteLine(order);
            }
        }

        namespace App.Modules.Orders
        {
            public sealed class OrderService
            {
                public App.Modules.Billing.InvoiceService? Invoices { get; set; }
            }
        }

        namespace App.Modules.Billing
        {
            public sealed class InvoiceService
            {
                public App.Modules.Shipping.ShippingService? Shipping { get; set; }
            }
        }

        namespace App.Modules.Shipping
        {
            public sealed class ShippingService
            {
                public App.Modules.Orders.OrderService? Orders { get; set; }
            }
        }
        """));

    private static IEnumerable<TypeElement> Domain => App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Domain"));

    private static IEnumerable<TypeElement> Data => App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Data"));

    [Fact]
    public void DoesNotDependOnReportsViolationsWithLocations()
    {
        var domain = Domain;
        var data = Data;
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(domain, data, "the domain stays persistence-ignorant"));
        Assert.Equal("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 type depends on a forbidden type.
            Message: the domain stays persistence-ignorant
            Types expression:     domain
            Forbidden expression: data
            - App.Domain.Customer depends on App.Data.Database
                App.Domain.Customer.Database has a field of type App.Data.Database
            """.ReplaceLineEndings("\n"), exception.Message);
    }

    [Fact]
    public void DoesNotDependOnSucceeds()
    {
        Assert.Architecture.DoesNotDependOn(Data, App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Modules")));

        // The dependencies between the checked types are ignored: Customer uses Database, and both are checked
        Assert.Architecture.DoesNotDependOn([.. Domain, .. Data], [.. Domain, .. Data]);
    }

    [Fact]
    public void DoesNotDependOnPredicate()
    {
        var orders = App.Value.Types.Where(t => t.IsInNamespace("App.Modules.Orders"));

        // OrderService uses App.Modules.Billing, a child namespace of App.Modules
        Assert.Architecture.DoesNotDependOn(orders, t => t.IsInNamespace("App.Modules"));
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(orders, t => t.IsInNamespaceOrChildren("App.Modules")));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 type depends on a forbidden type.
            Types expression:     orders
            Forbidden expression: t => t.IsInNamespaceOrChildren("App.Modules")
            - App.Modules.Orders.OrderService depends on App.Modules.Billing.InvoiceService
            """.ReplaceLineEndings("\n"), exception.Message);
    }

    [Fact]
    public void DoesNotDependOnType()
    {
        var data = Data;
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(data, typeof(Console)));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 type depends on a forbidden type.
            Types expression: data
            Forbidden type:   System.Console
            - App.Data.Database depends on System.Console
            """.ReplaceLineEndings("\n"), exception.Message);

        Assert.Architecture.DoesNotDependOn<Uri>(Domain);
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn<Guid>(Domain));
    }

    [Fact]
    public void DependsOnlyOnTypes()
    {
        Assert.Architecture.DependsOnlyOn(Data, Domain);

        var domain = Domain;
        var modules = App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Modules"));
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(domain, modules));
        Assert.StartsWith("""
            Assert.Architecture.DependsOnlyOn() assertion failed: 1 type depends on a type that is not allowed.
            Types expression:   domain
            Allowed expression: modules
            - App.Domain.Customer depends on App.Data.Database
            """.ReplaceLineEndings("\n"), exception.Message);
    }

    [Fact]
    public void DependsOnlyOnPredicate()
    {
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(Data, t => t.IsInNamespaceOrChildren("System")));
        Assert.Contains("\n- App.Data.Database depends on App.Domain.Order\n", exception.Message);

        var orders = App.Value.Types.Where(t => t.IsInNamespace("App.Modules.Orders"));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(orders, t => t.IsInNamespace("System") || t.IsInNamespace("App.Modules")));
        Assert.Architecture.DependsOnlyOn(orders, t => t.IsInNamespaceOrChildren("System") || t.IsInNamespaceOrChildren("App.Modules"));
    }

    [Fact]
    public void DoesNotAccessMember()
    {
        var types = App.Value.Types;
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>(types, "Now"));
        Assert.Equal("""
            Assert.Architecture.DoesNotAccessMember() assertion failed: 1 type accesses the member.
            Types expression: types
            Member:           System.DateTime.Now
            - App.Domain.Order accesses System.DateTime.get_Now
                App.Domain.Order..ctor calls System.DateTime.get_Now (/src/Sample.cs:7:46)
            """.ReplaceLineEndings("\n"), exception.Message);

        Assert.Architecture.DoesNotAccessMember<DateTime>(types, nameof(DateTime.UtcNow));
    }

    [Fact]
    public void EmptySetsFail()
    {
        var typo = App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Typo"));
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(typo, Data, "message"));
        Assert.Equal("""
            Assert.Architecture.DoesNotDependOn() assertion failed: No type to check.
            Message: message
            Types expression:     typo
            Forbidden expression: Data
            """.ReplaceLineEndings("\n"), exception.Message);

        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(Domain, typo));
        Assert.StartsWith("Assert.Architecture.DoesNotDependOn() assertion failed: No forbidden type.\n", exception.Message);

        Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(typo, t => true));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>(typo, "Now"));
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotHaveCycles(typo.SliceByNamespace("App.{slice}")));
        Assert.StartsWith("Assert.Architecture.DoesNotHaveCycles() assertion failed: No slice to check.\n", exception.Message);
    }

    [Fact]
    public void DoesNotHaveCycles()
    {
        var slices = App.Value.Types.SliceByNamespace("App.Modules.{slice}");
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotHaveCycles(slices, "the modules are independent"));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotHaveCycles() assertion failed: 1 cycle between slices.
            Message: the modules are independent
            Slices expression: slices
            - Billing -> Shipping -> Orders -> Billing
                App.Modules.Billing.InvoiceService.Shipping has a property of type App.Modules.Shipping.ShippingService (/src/Sample.cs:37:65)
            """.ReplaceLineEndings("\n"), exception.Message);

        // Domain -> Data -> Domain
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotHaveCycles(App.Value.Types.SliceByNamespace("App.{slice}")));
        Assert.Contains("\n- Data -> Domain -> Data\n", exception.Message);

        Assert.Architecture.DoesNotHaveCycles(App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Modules.Orders")).SliceByNamespace("App.{slice}"));
    }

    [Fact]
    public void DoesNotDependOnEachOther()
    {
        var slices = App.Value.Types.SliceByNamespace("App.Modules.{slice}");
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOnEachOther(slices));
        Assert.Equal(
            [
                "Assert.Architecture.DoesNotDependOnEachOther() assertion failed: 3 types depend on other slices.",
                "- App.Modules.Billing.InvoiceService in slice 'Billing' depends on slice 'Shipping'",
                "- App.Modules.Orders.OrderService in slice 'Orders' depends on slice 'Billing'",
                "- App.Modules.Shipping.ShippingService in slice 'Shipping' depends on slice 'Orders'",
            ],
            exception.Message.Split('\n').Where(line => !line.StartsWith(" ", StringComparison.Ordinal) && !line.StartsWith("Slices expression", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void SliceByNamespace()
    {
        var slices = App.Value.Types.SliceByNamespace("App.Modules.{slice}");
        Assert.Equal(["Billing", "Orders", "Shipping"], slices.Select(slice => slice.Key).ToArray());
        Assert.Equal(["App.Modules.Orders.OrderService"], slices[1].Select(type => type.FullName).ToArray());

        Assert.Equal(["Billing", "Orders", "Shipping"], App.Value.Types.SliceByNamespace("App.*.{slice}").Select(slice => slice.Key).ToArray());
        Assert.Equal(["Data", "Domain", "Modules"], App.Value.Types.SliceByNamespace("App.{slice}").Select(slice => slice.Key).ToArray());
    }

    [Theory]
    [InlineData("App.Modules")]
    [InlineData("App.{slice}.{slice}")]
    public void SliceByNamespaceValidatesThePattern(string value)
    {
        Assert.Throws<ArgumentException>("pattern", () => App.Value.Types.SliceByNamespace(value));
    }

    [Fact]
    public void SlicesFromGroupBy()
    {
        var slices = App.Value.Types.Where(t => t.IsInNamespaceOrChildren("App.Modules")).GroupBy(t => t.Name);
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotHaveCycles(slices));

        var types = App.Value.Types.ToList();
        Assert.Throws<ArgumentException>("slices", () => Assert.Architecture.DoesNotHaveCycles([.. types.GroupBy(t => "A"), .. types.GroupBy(t => "A")]));
        Assert.Throws<ArgumentException>("slices", () => Assert.Architecture.DoesNotHaveCycles([.. types.GroupBy(t => "A"), .. types.GroupBy(t => "B")]));
    }

    [Fact]
    public void IsImmutable()
    {
        Assert.True(App.Value.FindType("App.Domain.Order")!.IsImmutable);
        Assert.False(App.Value.FindType("App.Domain.Customer")!.IsImmutable);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>("types", () => Assert.Architecture.DoesNotDependOn(null!, Data));
        Assert.Throws<ArgumentNullException>("forbiddenTypes", () => Assert.Architecture.DoesNotDependOn(Domain, (IEnumerable<TypeElement>)null!));
        Assert.Throws<ArgumentNullException>("predicate", () => Assert.Architecture.DoesNotDependOn(Domain, (Func<TypeReference, bool>)null!));
        Assert.Throws<ArgumentNullException>("type", () => Assert.Architecture.DoesNotDependOn(Domain, (Type)null!));
        Assert.Throws<ArgumentNullException>("memberName", () => Assert.Architecture.DoesNotAccessMember<DateTime>(Domain, null!));
        Assert.Throws<ArgumentNullException>("slices", () => Assert.Architecture.DoesNotHaveCycles(null!));
    }
}
