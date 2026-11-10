using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions.Architecture;

public sealed partial class ArchitectureAssert
{
    /// <summary>
    /// Asserts that all the types match a predicate, such as a naming convention or a modifier. Unlike <c>Assert.All</c>, the message lists every type that
    /// does not match, with its location.
    /// </summary>
    /// <param name="types">The types to check.</param>
    /// <param name="predicate">Returns <see langword="true"/> for a valid type, such as <c>t =&gt; t.IsSealed</c>.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <param name="predicateExpression">The expression of <paramref name="predicate"/>.</param>
    /// <exception cref="AssertionException">A type does not match the predicate, or <paramref name="types"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_ShapeRules" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var repositories = architecture.Types.Where(t =&gt; t.IsClass &amp;&amp; t.Implements&lt;IOrderRepository&gt;());
    ///
    /// Assert.Architecture.AllMatch(repositories, t =&gt; t.Name.EndsWith("Repository", StringComparison.Ordinal));
    /// Assert.Architecture.AllMatch(repositories, t =&gt; t.IsSealed);
    /// Assert.Architecture.AllMatch(architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Domain") &amp;&amp; !t.IsInterface), t =&gt; t.IsImmutable);
    /// </code>
    /// </example>
    public void AllMatch(IEnumerable<TypeElement> types, Func<TypeElement, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        const string Name = nameof(AllMatch);
        ArgumentNullException.ThrowIfNull(predicate);
        var typeList = GetTypes(types, nameof(types));
        var rows = new (string, string?)[] { ("Types expression", typesExpression), ("Predicate expression", predicateExpression) };
        EnsureNotEmpty(Name, typeList, message, rows, "No type to check");

        var violations = FindMismatches(typeList, predicate, type => type.Location);
        ThrowIfViolations(Name, violations, "{0} type does not match the predicate", "{0} types do not match the predicate", message, rows);
    }

    /// <summary>
    /// Asserts that all the members match a predicate, such as "the public methods of the controllers are <c>async</c>" or "the fields are not public".
    /// Unlike <c>Assert.All</c>, the message lists every member that does not match, with its location.
    /// </summary>
    /// <typeparam name="TMember">The type of the members: <see cref="MethodElement"/>, <see cref="PropertyElement"/>, <see cref="FieldElement"/>, <see cref="EventElement"/> or <see cref="MemberElement"/>.</typeparam>
    /// <param name="members">The members to check, such as <c>types.SelectMany(t =&gt; t.Methods)</c>.</param>
    /// <param name="predicate">Returns <see langword="true"/> for a valid member, such as <c>method =&gt; method.IsAsync</c>.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="membersExpression">The expression of <paramref name="members"/>.</param>
    /// <param name="predicateExpression">The expression of <paramref name="predicate"/>.</param>
    /// <exception cref="AssertionException">A member does not match the predicate, or <paramref name="members"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_MemberRules" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var handlers = architecture.Types.Where(t =&gt; t.IsInNamespace("SampleApp.Application") &amp;&amp; t.Name.EndsWith("Handler", StringComparison.Ordinal));
    /// var controllers = architecture.Types.Where(t =&gt; t.IsInNamespace("SampleApp.Web") &amp;&amp; t.Name.EndsWith("Controller", StringComparison.Ordinal));
    ///
    /// Assert.Architecture.AllMatch(handlers.SelectMany(t =&gt; t.Methods).Where(method =&gt; method.IsPublic), method =&gt; method.IsAsync);
    /// var domain = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Domain"));
    /// Assert.Architecture.AllMatch(domain.SelectMany(t =&gt; t.Properties), property =&gt; property.Setter is null || property.IsInitOnly, "the domain is immutable");
    ///
    /// // OrdersController.CreateRepository is a public method that does not return a Task
    /// var actions = controllers.SelectMany(t =&gt; t.Methods).Where(method =&gt; method.IsPublic);
    /// var exception = Assert.Throws&lt;AssertionException&gt;(() =&gt; Assert.Architecture.AllMatch(actions, method =&gt; method.ReturnType.Is(typeof(Task&lt;&gt;))));
    /// Assert.StartsWith("""
    ///     Assert.Architecture.AllMatch() assertion failed: 1 member does not match the predicate.
    ///     Members expression:   actions
    ///     Predicate expression: method =&gt; method.ReturnType.Is(typeof(Task&lt;&gt;))
    ///     - SampleApp.Web.OrdersController.CreateRepository()
    ///     """.ReplaceLineEndings("\n"), exception.Message);
    /// </code>
    /// </example>
    public void AllMatch<TMember>(IEnumerable<TMember> members, Func<TMember, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(members))] string? membersExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
        where TMember : MemberElement
    {
        const string Name = nameof(AllMatch);
        ArgumentNullException.ThrowIfNull(predicate);
        var memberList = GetItems(members, nameof(members));
        var rows = new (string, string?)[] { ("Members expression", membersExpression), ("Predicate expression", predicateExpression) };
        EnsureNotEmpty(Name, memberList, message, rows, "No member to check");

        var violations = FindMismatches(memberList, predicate, member => member.Location);
        ThrowIfViolations(Name, violations, "{0} member does not match the predicate", "{0} members do not match the predicate", message, rows);
    }
}
