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
    public void AllMatchTypes()
    {
        var domain = Domain;
        Assert.Architecture.AllMatch(domain, t => t.IsClass);
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(domain, t => t.IsSealed, "the classes are sealed"));
        Assert.Equal("""
            Assert.Architecture.AllMatch() assertion failed: 1 type does not match the predicate.
            Message: the classes are sealed
            Types expression:     domain
            Predicate expression: t => t.IsSealed
            - App.Domain.Customer (/src/Sample.cs:12:31)
            """.ReplaceLineEndings("\n"), exception.Message);
    }

    [Fact]
    public void AllMatchMembers()
    {
        var fields = App.Value.Types.SelectMany(t => t.Fields);
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(fields, field => !field.IsPublic));
        Assert.Equal("""
            Assert.Architecture.AllMatch() assertion failed: 1 member does not match the predicate.
            Members expression:   fields
            Predicate expression: field => !field.IsPublic
            - App.Domain.Customer.Database
            """.ReplaceLineEndings("\n"), exception.Message);

        var properties = Domain.SelectMany(t => t.Properties);
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(properties, property => property.Setter is null || property.IsInitOnly));
        Assert.Equal("""
            Assert.Architecture.AllMatch() assertion failed: 1 member does not match the predicate.
            Members expression:   properties
            Predicate expression: property => property.Setter is null || property.IsInitOnly
            - App.Domain.Customer.Name (/src/Sample.cs:12:31)
            """.ReplaceLineEndings("\n"), exception.Message);

        var methods = App.Value.Types.SelectMany(t => t.Methods);
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(methods, method => method.IsStatic));
        Assert.Equal("""
            Assert.Architecture.AllMatch() assertion failed: 1 member does not match the predicate.
            Members expression:   methods
            Predicate expression: method => method.IsStatic
            - App.Data.Database.Save(App.Domain.Order) (/src/Sample.cs:21:53)
            """.ReplaceLineEndings("\n"), exception.Message);

        Assert.Architecture.AllMatch(App.Value.Types.SelectMany(t => t.Members), member => member.IsPublic || member is MethodElement { IsConstructor: true });
    }

    [Fact]
    public void MemberDependencies()
    {
        var members = Domain.SelectMany(t => t.Members);
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(members, Data, "the domain stays persistence-ignorant"));
        Assert.Equal("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 member depends on a forbidden type.
            Message: the domain stays persistence-ignorant
            Members expression:   members
            Forbidden expression: Data
            - App.Domain.Customer.Database depends on App.Data.Database
                App.Domain.Customer.Database has a field of type App.Data.Database
            """.ReplaceLineEndings("\n"), exception.Message);

        var properties = Domain.SelectMany(t => t.Properties);
        Assert.Architecture.DoesNotDependOn(properties, Data);
        Assert.Architecture.DoesNotDependOn(properties, t => t.IsInNamespaceOrChildren("App.Data"));
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(members, t => t.IsInNamespaceOrChildren("App.Data")));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 member depends on a forbidden type.
            Members expression:   members
            Forbidden expression: t => t.IsInNamespaceOrChildren("App.Data")
            - App.Domain.Customer.Database depends on App.Data.Database
            """.ReplaceLineEndings("\n"), exception.Message);

        var orderProperties = App.Value.FindType("App.Domain.Order")!.Properties;
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn<Guid>(orderProperties));
        Assert.StartsWith("""
            Assert.Architecture.DoesNotDependOn() assertion failed: 1 member depends on a forbidden type.
            Members expression: orderProperties
            Forbidden type:     System.Guid
            - App.Domain.Order.Id depends on System.Guid
            """.ReplaceLineEndings("\n"), exception.Message);
        Assert.Architecture.DoesNotDependOn<Uri>(orderProperties);
        Assert.Architecture.DoesNotDependOn(orderProperties.Where(property => property.Name == "CreatedAt"), typeof(Guid));
    }

    [Fact]
    public void MembersDependOnlyOn()
    {
        var members = Domain.SelectMany(t => t.Members);
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(members, t => t.IsInNamespaceOrChildren("System")));
        Assert.StartsWith("""
            Assert.Architecture.DependsOnlyOn() assertion failed: 1 member depends on a type that is not allowed.
            Members expression: members
            Allowed expression: t => t.IsInNamespaceOrChildren("System")
            - App.Domain.Customer.Database depends on App.Data.Database
            """.ReplaceLineEndings("\n"), exception.Message);

        // The types that declare the checked members are allowed: Database.Save uses Order, and Order declares a checked member
        var order = App.Value.FindType("App.Domain.Order")!;
        var database = App.Value.FindType("App.Data.Database")!;
        Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(database.Methods, t => t.IsInNamespaceOrChildren("System")));
        Assert.Architecture.DependsOnlyOn([.. database.Methods, .. order.Properties], t => t.IsInNamespaceOrChildren("System"));
    }

    [Fact]
    public void MembersDoNotAccessMember()
    {
        var order = App.Value.FindType("App.Domain.Order")!;
        var constructors = order.Constructors;
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>(constructors, "Now"));
        Assert.Equal("""
            Assert.Architecture.DoesNotAccessMember() assertion failed: 1 member accesses the member.
            Members expression: constructors
            Member:             System.DateTime.Now
            - App.Domain.Order..ctor() accesses System.DateTime.get_Now
                App.Domain.Order..ctor calls System.DateTime.get_Now (/src/Sample.cs:7:46)
            """.ReplaceLineEndings("\n"), exception.Message);

        Assert.Architecture.DoesNotAccessMember<DateTime>(order.Properties, "Now");
        Assert.Architecture.DoesNotAccessMember(constructors, typeof(DateTime), nameof(DateTime.UtcNow));
    }

    [Fact]
    public void AccessorsHaveTheDependenciesOfTheirCode()
    {
        var architecture = TestCompiler.Load("""
            using System;
            namespace App
            {
                public sealed class Clock
                {
                    private DateTime _value;

                    public DateTime Value
                    {
                        get => DateTime.Now;
                        set => _value = value.ToUniversalTime();
                    }

                    public Func<DateTime> Factory => () => DateTime.Now;

                    public event EventHandler? Changed
                    {
                        add => Console.WriteLine(value);
                        remove { }
                    }
                }
            }
            """);
        var clock = architecture.FindType("App.Clock")!;
        var value = clock.Properties.Single(property => property.Name == "Value");
        var getter = value.Getter!;
        var setter = value.Setter!;

        IEnumerable<MemberElement> getters = [getter];
        var exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>(getters, "Now"));
        Assert.Equal("""
            Assert.Architecture.DoesNotAccessMember() assertion failed: 1 member accesses the member.
            Members expression: getters
            Member:             System.DateTime.Now
            - App.Clock.get_Value() accesses System.DateTime.get_Now
                App.Clock.get_Value calls System.DateTime.get_Now (/src/Sample.cs:10:20)
            """.ReplaceLineEndings("\n"), exception.Message);

        // Each accessor has the dependencies of its own code; the property has the dependencies of both
        Assert.Architecture.DoesNotAccessMember<DateTime>([setter], "Now");
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>([setter], nameof(DateTime.ToUniversalTime)));
        Assert.Architecture.DoesNotAccessMember<DateTime>([getter], nameof(DateTime.ToUniversalTime));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>([value], "Now"));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>([value], nameof(DateTime.ToUniversalTime)));
        Assert.All(getter.Dependencies, dependency => dependency.SourceMember == getter);
        Assert.All(value.Dependencies, dependency => dependency.SourceMember == value);

        // The code of a lambda belongs to the accessor that declares it
        var factory = clock.Properties.Single(property => property.Name == "Factory");
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>([factory.Getter!], "Now"));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>([factory], "Now"));

        var changed = clock.Events.Single();
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn([changed.Adder!], typeof(Console)));
        Assert.Architecture.DoesNotDependOn([changed.Remover!], typeof(Console));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn([changed], typeof(Console)));
    }

    [Fact]
    public void MemberIsPublic()
    {
        var customer = App.Value.FindType("App.Domain.Customer")!;
        Assert.True(customer.Fields.Single().IsPublic);
        Assert.True(customer.Properties.Single().IsPublic);
        Assert.True(customer.Constructors.Single().IsPublic);
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
        Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(typo, t => true));

        var noMembers = typo.SelectMany(t => t.Methods);
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.AllMatch(noMembers, method => true));
        Assert.Equal("""
            Assert.Architecture.AllMatch() assertion failed: No member to check.
            Members expression:   noMembers
            Predicate expression: method => true
            """.ReplaceLineEndings("\n"), exception.Message);
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(noMembers, Data));
        Assert.StartsWith("Assert.Architecture.DoesNotDependOn() assertion failed: No member to check.\n", exception.Message);
        exception = Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(Domain.SelectMany(t => t.Members), typo));
        Assert.StartsWith("Assert.Architecture.DoesNotDependOn() assertion failed: No forbidden type.\n", exception.Message);
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn(noMembers, t => true));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotDependOn<DateTime>(noMembers));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DependsOnlyOn(noMembers, t => true));
        Assert.Throws<AssertionException>(() => Assert.Architecture.DoesNotAccessMember<DateTime>(noMembers, "Now"));

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
        Assert.Throws<ArgumentNullException>("types", () => Assert.Architecture.DoesNotDependOn((IEnumerable<TypeElement>)null!, Data));
        Assert.Throws<ArgumentNullException>("forbiddenTypes", () => Assert.Architecture.DoesNotDependOn(Domain, (IEnumerable<TypeElement>)null!));
        Assert.Throws<ArgumentNullException>("predicate", () => Assert.Architecture.DoesNotDependOn(Domain, (Func<TypeReference, bool>)null!));
        Assert.Throws<ArgumentNullException>("type", () => Assert.Architecture.DoesNotDependOn(Domain, (Type)null!));
        Assert.Throws<ArgumentNullException>("memberName", () => Assert.Architecture.DoesNotAccessMember<DateTime>(Domain, null!));
        Assert.Throws<ArgumentNullException>("slices", () => Assert.Architecture.DoesNotHaveCycles(null!));

        var members = Domain.SelectMany(t => t.Members);
        Assert.Throws<ArgumentNullException>("types", () => Assert.Architecture.AllMatch((IEnumerable<TypeElement>)null!, t => true));
        Assert.Throws<ArgumentNullException>("predicate", () => Assert.Architecture.AllMatch(Domain, null!));
        Assert.Throws<ArgumentNullException>("members", () => Assert.Architecture.AllMatch((IEnumerable<FieldElement>)null!, field => true));
        Assert.Throws<ArgumentNullException>("predicate", () => Assert.Architecture.AllMatch(members, (Func<MemberElement, bool>)null!));
        Assert.Throws<ArgumentNullException>("members", () => Assert.Architecture.DoesNotDependOn((IEnumerable<MemberElement>)null!, Data));
        Assert.Throws<ArgumentNullException>("forbiddenTypes", () => Assert.Architecture.DoesNotDependOn(members, (IEnumerable<TypeElement>)null!));
        Assert.Throws<ArgumentNullException>("predicate", () => Assert.Architecture.DoesNotDependOn(members, (Func<TypeReference, bool>)null!));
        Assert.Throws<ArgumentNullException>("type", () => Assert.Architecture.DoesNotDependOn(members, (Type)null!));
        Assert.Throws<ArgumentNullException>("predicate", () => Assert.Architecture.DependsOnlyOn(members, null!));
        Assert.Throws<ArgumentNullException>("memberName", () => Assert.Architecture.DoesNotAccessMember<DateTime>(members, null!));
    }
}
