using System.Runtime.CompilerServices;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

public sealed partial class ArchitectureAssert
{
    /// <summary>Asserts that the members do not use any of the forbidden types, in their declaration or in their code.</summary>
    /// <param name="members">The members to check.</param>
    /// <param name="forbiddenTypes">The types that must not be used, such as the types of another layer.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <param name="forbiddenTypesExpression">The expression of <paramref name="forbiddenTypes"/>.</param>
    /// <exception cref="AssertionException">A member uses a forbidden type, or <paramref name="members"/> or <paramref name="forbiddenTypes"/> is empty.</exception>
    public void DoesNotDependOn(IEnumerable<MemberElement> members, IEnumerable<TypeElement> forbiddenTypes, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null, [CallerArgumentExpression(nameof(forbiddenTypes))] string? forbiddenTypesExpression = null)
    {
        const string Name = nameof(DoesNotDependOn);
        var memberList = GetItems(members, nameof(members));
        var forbiddenList = GetTypes(forbiddenTypes, nameof(forbiddenTypes));
        var rows = new (string, string?)[] { ("Members expression", membersExpression), ("Forbidden expression", forbiddenTypesExpression) };
        EnsureNotEmpty(Name, memberList, message, rows, "No member to check");
        EnsureNotEmpty(Name, forbiddenList, message, rows, "No forbidden type");

        var forbidden = forbiddenList.ToHashSet();
        var violations = FindDependencies(memberList, member => member.Dependencies, dependency => dependency.Target.Definition is { } target && forbidden.Contains(target));
        ThrowIfViolations(Name, violations, "{0} member depends on a forbidden type", "{0} members depend on forbidden types", message, rows);
    }

    /// <summary>Asserts that the members do not use any type that matches a predicate, including the types defined outside the loaded assemblies.</summary>
    /// <param name="members">The members to check.</param>
    /// <param name="predicate">Returns <see langword="true"/> for a forbidden type, such as <c>t =&gt; t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore")</c>.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <param name="predicateExpression">The expression of <paramref name="predicate"/>.</param>
    /// <exception cref="AssertionException">A member uses a forbidden type, or <paramref name="members"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_MemberDependencies" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var publicMembers = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp") &amp;&amp; t.IsPublic).SelectMany(t =&gt; t.Members).Where(member =&gt; member.IsPublic);
    /// var infrastructure = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));
    ///
    /// // The signatures and the code of the public members do not use Entity Framework Core
    /// Assert.Architecture.DoesNotDependOn(publicMembers, t =&gt; t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));
    ///
    /// // The controllers get the infrastructure by injection: OrdersController.CreateRepository breaks the rule
    /// var controllerMethods = architecture.Types.Where(t =&gt; t.IsInNamespace("SampleApp.Web")).SelectMany(t =&gt; t.Methods);
    /// var exception = Assert.Throws&lt;AssertionException&gt;(() =&gt; Assert.Architecture.DoesNotDependOn(controllerMethods, infrastructure));
    /// Assert.StartsWith("""
    ///     Assert.Architecture.DoesNotDependOn() assertion failed: 1 member depends on a forbidden type.
    ///     Members expression:   controllerMethods
    ///     Forbidden expression: infrastructure
    ///     - SampleApp.Web.OrdersController.CreateRepository() depends on SampleApp.Infrastructure.SqlOrderRepository
    ///         SampleApp.Web.OrdersController.CreateRepository creates SampleApp.Infrastructure.SqlOrderRepository..ctor
    ///     """.ReplaceLineEndings("\n"), exception.Message);
    /// </code>
    /// </example>
    public void DoesNotDependOn(IEnumerable<MemberElement> members, Func<TypeReference, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        DoesNotDependOnCore(members, predicate, message, [("Members expression", membersExpression), ("Forbidden expression", predicateExpression)]);
    }

    /// <summary>Asserts that the members do not use <paramref name="type"/>, in their declaration or in their code.</summary>
    /// <param name="members">The members to check.</param>
    /// <param name="type">The forbidden type, or a generic type definition (<c>typeof(List&lt;&gt;)</c>) to forbid all its instantiations.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <exception cref="AssertionException">A member uses <paramref name="type"/>, or <paramref name="members"/> is empty.</exception>
    public void DoesNotDependOn(IEnumerable<MemberElement> members, Type type, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var fullName = MetadataNames.GetFullName(type);
        DoesNotDependOnCore(members, target => string.Equals(target.FullName, fullName, StringComparison.Ordinal), message, [("Members expression", membersExpression), ("Forbidden type", fullName)]);
    }

    /// <summary>Asserts that the members do not use <typeparamref name="T"/>, in their declaration or in their code.</summary>
    /// <typeparam name="T">The forbidden type.</typeparam>
    /// <param name="members">The members to check.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <exception cref="AssertionException">A member uses <typeparamref name="T"/>, or <paramref name="members"/> is empty.</exception>
    public void DoesNotDependOn<T>(IEnumerable<MemberElement> members, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null)
        => DoesNotDependOn(members, typeof(T), message, membersExpression);

    /// <summary>
    /// Asserts that the members only use the types that match a predicate, including the types defined outside the loaded assemblies.
    /// The types that declare the checked members are always allowed.
    /// </summary>
    /// <param name="members">The members to check.</param>
    /// <param name="predicate">Returns <see langword="true"/> for an allowed type, such as <c>t =&gt; t.IsInNamespaceOrChildren("System")</c>.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <param name="predicateExpression">The expression of <paramref name="predicate"/>.</param>
    /// <exception cref="AssertionException">A member uses a type that is not allowed, or <paramref name="members"/> is empty.</exception>
    public void DependsOnlyOn(IEnumerable<MemberElement> members, Func<TypeReference, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        const string Name = nameof(DependsOnlyOn);
        ArgumentNullException.ThrowIfNull(predicate);
        var memberList = GetItems(members, nameof(members));
        var rows = new (string, string?)[] { ("Members expression", membersExpression), ("Allowed expression", predicateExpression) };
        EnsureNotEmpty(Name, memberList, message, rows, "No member to check");

        var declaringTypes = memberList.Select(member => member.DeclaringType).ToHashSet();
        var violations = FindDependencies(memberList, member => member.Dependencies, dependency => !(dependency.Target.Definition is { } target && declaringTypes.Contains(target)) && !predicate(dependency.Target));
        ThrowIfViolations(Name, violations, "{0} member depends on a type that is not allowed", "{0} members depend on types that are not allowed", message, rows);
    }

    /// <summary>Asserts that the members do not use a member of <paramref name="type"/>: call a method or a constructor, read or write a field, a property or an event.</summary>
    /// <param name="members">The members to check.</param>
    /// <param name="type">The type that declares the member.</param>
    /// <param name="memberName">The name of the member (<c>nameof(DateTime.Now)</c>), or <c>.ctor</c> for the constructors.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <exception cref="AssertionException">A member uses the member, or <paramref name="members"/> is empty.</exception>
    public void DoesNotAccessMember(IEnumerable<MemberElement> members, Type type, string memberName, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null)
    {
        const string Name = nameof(DoesNotAccessMember);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(memberName);
        var memberList = GetItems(members, nameof(members));
        var fullName = MetadataNames.GetFullName(type);
        var rows = new (string, string?)[] { ("Members expression", membersExpression), ("Member", fullName + "." + memberName) };
        EnsureNotEmpty(Name, memberList, message, rows, "No member to check");

        var violations = FindMemberAccesses(memberList, member => member.Dependencies, fullName, memberName);
        ThrowIfViolations(Name, violations, "{0} member accesses the member", "{0} members access the member", message, rows);
    }

    /// <summary>Asserts that the members do not use a member of <typeparamref name="T"/>: call a method or a constructor, read or write a field, a property or an event.</summary>
    /// <typeparam name="T">The type that declares the member.</typeparam>
    /// <param name="members">The members to check.</param>
    /// <param name="memberName">The name of the member (<c>nameof(DateTime.Now)</c>), or <c>.ctor</c> for the constructors.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <exception cref="AssertionException">A member uses the member, or <paramref name="members"/> is empty.</exception>
    public void DoesNotAccessMember<T>(IEnumerable<MemberElement> members, string memberName, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null)
        => DoesNotAccessMember(members, typeof(T), memberName, message, membersExpression);

    private static void DoesNotDependOnCore(IEnumerable<MemberElement> members, Func<TypeReference, bool> isForbidden, string? message, (string Label, string? Value)[] rows)
    {
        const string Name = nameof(DoesNotDependOn);
        var memberList = GetItems(members, nameof(members));
        EnsureNotEmpty(Name, memberList, message, rows, "No member to check");
        var violations = FindDependencies(memberList, member => member.Dependencies, dependency => isForbidden(dependency.Target));
        ThrowIfViolations(Name, violations, "{0} member depends on a forbidden type", "{0} members depend on forbidden types", message, rows);
    }
}
