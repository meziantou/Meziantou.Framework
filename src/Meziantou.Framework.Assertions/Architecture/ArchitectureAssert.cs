using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>
/// The assertions on the dependencies of the types of an <see cref="ArchitectureModel"/>, available as <c>Assert.Architecture</c>.
/// Each assertion throws an <see cref="AssertionException"/> that lists every type that breaks it, with the statements that cause it.
/// To check the names or the shape of types, use <c>Assert.All</c> with the members of <see cref="TypeElement"/>.
/// </summary>
/// <remarks>
/// Each assertion fails when the types to check are empty, so that a typo in a namespace does not make it pass silently.
/// Add your own assertions with an extension block on <see cref="ArchitectureAssert"/>.
/// </remarks>
[StackTraceHidden]
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The assertions are called on the Assert.Architecture instance")]
public sealed class ArchitectureAssert
{
    internal static ArchitectureAssert Instance { get; } = new();

    private ArchitectureAssert()
    {
    }

    /// <summary>Asserts that the types do not use any of the forbidden types. The dependencies between the checked types themselves are ignored.</summary>
    /// <param name="types">The types to check.</param>
    /// <param name="forbiddenTypes">The types that must not be used, such as the types of another layer.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <param name="forbiddenTypesExpression">The expression of <paramref name="forbiddenTypes"/>.</param>
    /// <exception cref="AssertionException">A type uses a forbidden type, or <paramref name="types"/> or <paramref name="forbiddenTypes"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_DoesNotDependOn" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var domain = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Domain"));
    /// var infrastructure = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));
    ///
    /// Assert.Architecture.DoesNotDependOn(domain, infrastructure, "the domain stays persistence-ignorant");
    /// Assert.Architecture.DoesNotDependOn(domain, t =&gt; t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));
    /// Assert.Architecture.DoesNotDependOn&lt;HttpClient&gt;(domain);
    /// </code>
    /// </example>
    public void DoesNotDependOn(IEnumerable<TypeElement> types, IEnumerable<TypeElement> forbiddenTypes, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null, [CallerArgumentExpression(nameof(forbiddenTypes))] string? forbiddenTypesExpression = null)
    {
        const string Name = nameof(DoesNotDependOn);
        var typeList = GetTypes(types, nameof(types));
        var forbiddenList = GetTypes(forbiddenTypes, nameof(forbiddenTypes));
        var rows = new (string, string?)[] { ("Types expression", typesExpression), ("Forbidden expression", forbiddenTypesExpression) };
        EnsureNotEmpty(Name, typeList, message, rows, "No type to check");
        EnsureNotEmpty(Name, forbiddenList, message, rows, "No forbidden type");

        var checkedTypes = typeList.ToHashSet();
        var forbidden = forbiddenList.ToHashSet();
        var violations = FindDependencies(typeList, dependency => dependency.Target.Definition is { } target && forbidden.Contains(target) && !checkedTypes.Contains(target));
        ThrowIfViolations(Name, violations, "{0} type depends on a forbidden type", "{0} types depend on forbidden types", message, rows);
    }

    /// <summary>Asserts that the types do not use any type that matches a predicate. Unlike the other overloads, the predicate also sees the types defined outside the loaded assemblies.</summary>
    /// <param name="types">The types to check.</param>
    /// <param name="predicate">Returns <see langword="true"/> for a forbidden type, such as <c>t =&gt; t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore")</c>.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <param name="predicateExpression">The expression of <paramref name="predicate"/>.</param>
    /// <exception cref="AssertionException">A type uses a forbidden type, or <paramref name="types"/> is empty.</exception>
    public void DoesNotDependOn(IEnumerable<TypeElement> types, Func<TypeReference, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        DoesNotDependOnCore(types, predicate, message, [("Types expression", typesExpression), ("Forbidden expression", predicateExpression)]);
    }

    /// <summary>Asserts that the types do not use <paramref name="type"/>.</summary>
    /// <param name="types">The types to check.</param>
    /// <param name="type">The forbidden type, or a generic type definition (<c>typeof(List&lt;&gt;)</c>) to forbid all its instantiations.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <exception cref="AssertionException">A type uses <paramref name="type"/>, or <paramref name="types"/> is empty.</exception>
    public void DoesNotDependOn(IEnumerable<TypeElement> types, Type type, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var fullName = MetadataNames.GetFullName(type);
        DoesNotDependOnCore(types, target => string.Equals(target.FullName, fullName, StringComparison.Ordinal), message, [("Types expression", typesExpression), ("Forbidden type", fullName)]);
    }

    /// <summary>Asserts that the types do not use <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The forbidden type.</typeparam>
    /// <param name="types">The types to check.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <exception cref="AssertionException">A type uses <typeparamref name="T"/>, or <paramref name="types"/> is empty.</exception>
    public void DoesNotDependOn<T>(IEnumerable<TypeElement> types, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null)
        => DoesNotDependOn(types, typeof(T), message, typesExpression);

    /// <summary>
    /// Asserts that the types only use the allowed types and each other. Only the types of the loaded assemblies are checked: use the overload that takes a
    /// predicate to also check the types of .NET and of the packages.
    /// </summary>
    /// <param name="types">The types to check.</param>
    /// <param name="allowedTypes">The types that may be used.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <param name="allowedTypesExpression">The expression of <paramref name="allowedTypes"/>.</param>
    /// <exception cref="AssertionException">A type uses a type that is not allowed, or <paramref name="types"/> is empty.</exception>
    public void DependsOnlyOn(IEnumerable<TypeElement> types, IEnumerable<TypeElement> allowedTypes, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null, [CallerArgumentExpression(nameof(allowedTypes))] string? allowedTypesExpression = null)
    {
        const string Name = nameof(DependsOnlyOn);
        var typeList = GetTypes(types, nameof(types));
        var allowed = GetTypes(allowedTypes, nameof(allowedTypes)).ToHashSet();
        var rows = new (string, string?)[] { ("Types expression", typesExpression), ("Allowed expression", allowedTypesExpression) };
        EnsureNotEmpty(Name, typeList, message, rows, "No type to check");

        allowed.UnionWith(typeList);
        var violations = FindDependencies(typeList, dependency => dependency.Target.Definition is { } target && !allowed.Contains(target));
        ThrowIfViolations(Name, violations, "{0} type depends on a type that is not allowed", "{0} types depend on types that are not allowed", message, rows);
    }

    /// <summary>Asserts that the types only use each other and the types that match a predicate, including the types defined outside the loaded assemblies.</summary>
    /// <param name="types">The types to check.</param>
    /// <param name="predicate">Returns <see langword="true"/> for an allowed type, such as <c>t =&gt; t.IsInNamespaceOrChildren("System")</c>.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <param name="predicateExpression">The expression of <paramref name="predicate"/>.</param>
    /// <exception cref="AssertionException">A type uses a type that is not allowed, or <paramref name="types"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_DependsOnlyOn" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var domain = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Domain"));
    ///
    /// // The types of the domain may use each other and the types of .NET, nothing else
    /// Assert.Architecture.DependsOnlyOn(domain, t =&gt; t.IsInNamespaceOrChildren("System"));
    /// </code>
    /// </example>
    public void DependsOnlyOn(IEnumerable<TypeElement> types, Func<TypeReference, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        const string Name = nameof(DependsOnlyOn);
        ArgumentNullException.ThrowIfNull(predicate);
        var typeList = GetTypes(types, nameof(types));
        var rows = new (string, string?)[] { ("Types expression", typesExpression), ("Allowed expression", predicateExpression) };
        EnsureNotEmpty(Name, typeList, message, rows, "No type to check");

        var checkedTypes = typeList.ToHashSet();
        var violations = FindDependencies(typeList, dependency => !(dependency.Target.Definition is { } target && checkedTypes.Contains(target)) && !predicate(dependency.Target));
        ThrowIfViolations(Name, violations, "{0} type depends on a type that is not allowed", "{0} types depend on types that are not allowed", message, rows);
    }

    /// <summary>Asserts that the types do not use a member of <paramref name="type"/>: call a method or a constructor, read or write a field, a property or an event.</summary>
    /// <param name="types">The types to check.</param>
    /// <param name="type">The type that declares the member.</param>
    /// <param name="memberName">The name of the member (<c>nameof(DateTime.Now)</c>), or <c>.ctor</c> for the constructors.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <exception cref="AssertionException">A type uses the member, or <paramref name="types"/> is empty.</exception>
    public void DoesNotAccessMember(IEnumerable<TypeElement> types, Type type, string memberName, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null)
    {
        const string Name = nameof(DoesNotAccessMember);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(memberName);
        var typeList = GetTypes(types, nameof(types));
        var fullName = MetadataNames.GetFullName(type);
        var rows = new (string, string?)[] { ("Types expression", typesExpression), ("Member", fullName + "." + memberName) };
        EnsureNotEmpty(Name, typeList, message, rows, "No type to check");

        string[] names = [memberName, "get_" + memberName, "set_" + memberName, "add_" + memberName, "remove_" + memberName];
        var violations = new List<ArchitectureViolation>();
        foreach (var checkedType in typeList)
        {
            var dependencies = checkedType.Dependencies.Where(dependency => dependency.TargetMember is { } member
                && string.Equals(member.DeclaringType.FullName, fullName, StringComparison.Ordinal)
                && string.Equals(dependency.Target.FullName, fullName, StringComparison.Ordinal)
                && names.Contains(member.Name, StringComparer.Ordinal)).ToList();
            if (dependencies.Count > 0)
            {
                var members = string.Join(", ", dependencies.Select(dependency => dependency.TargetMember!.ToString()).Distinct(StringComparer.Ordinal));
                violations.Add(new ArchitectureViolation($"{checkedType} accesses {members}", checkedType, dependencies));
            }
        }

        ThrowIfViolations(Name, violations, "{0} type accesses the member", "{0} types access the member", message, rows);
    }

    /// <summary>Asserts that the types do not use a member of <typeparamref name="T"/>: call a method or a constructor, read or write a field, a property or an event.</summary>
    /// <typeparam name="T">The type that declares the member.</typeparam>
    /// <param name="types">The types to check.</param>
    /// <param name="memberName">The name of the member (<c>nameof(DateTime.Now)</c>), or <c>.ctor</c> for the constructors.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="typesExpression">The expression of <paramref name="types"/>.</param>
    /// <exception cref="AssertionException">A type uses the member, or <paramref name="types"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_DoesNotAccessMember" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var types = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp"));
    ///
    /// Assert.Architecture.DoesNotAccessMember&lt;DateTime&gt;(types, "Now", "the current time comes from TimeProvider");
    /// Assert.Architecture.DoesNotAccessMember&lt;DateTime&gt;(types, nameof(DateTime.UtcNow), "the current time comes from TimeProvider");
    /// </code>
    /// </example>
    public void DoesNotAccessMember<T>(IEnumerable<TypeElement> types, string memberName, string? message = null, [CallerArgumentExpression(nameof(types))] string? typesExpression = null)
        => DoesNotAccessMember(types, typeof(T), memberName, message, typesExpression);

    /// <summary>
    /// Asserts that the slices do not depend on each other in a cycle (<c>Orders → Billing → Orders</c>). A slice may use another one.
    /// Each group of slices that depend on each other is reported once, with one of the shortest cycles and the dependencies that make it.
    /// </summary>
    /// <param name="slices">The slices, such as the result of <see cref="TypeElementExtensions.SliceByNamespace"/> or of <c>GroupBy</c>. A type must not be in several slices.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="slicesExpression">The expression of <paramref name="slices"/>.</param>
    /// <exception cref="AssertionException">The slices depend on each other in a cycle, or <paramref name="slices"/> is empty.</exception>
    /// <exception cref="ArgumentException">Two slices have the same name, or a type is in several slices.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_Slices" ref:dedent="true">
    /// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
    /// var modules = architecture.Types.SliceByNamespace("SampleApp.Modules.{slice}");
    ///
    /// var exception = Assert.Throws&lt;AssertionException&gt;(() =&gt; Assert.Architecture.DoesNotHaveCycles(modules));
    /// Assert.StartsWith("""
    ///     Assert.Architecture.DoesNotHaveCycles() assertion failed: 1 cycle between slices.
    ///     Slices expression: modules
    ///     - Billing -&gt; Shipping -&gt; Billing
    ///         SampleApp.Modules.Billing.Invoice.Shipment has a property of type SampleApp.Modules.Shipping.Shipment
    ///     """.ReplaceLineEndings("\n"), exception.Message);
    /// </code>
    /// </example>
    public void DoesNotHaveCycles(IEnumerable<IGrouping<string, TypeElement>> slices, string? message = null, [CallerArgumentExpression(nameof(slices))] string? slicesExpression = null)
    {
        const string Name = nameof(DoesNotHaveCycles);
        var graph = SliceGraph.Create(slices, nameof(slices));
        var rows = new (string, string?)[] { ("Slices expression", slicesExpression) };
        EnsureNotEmpty(Name, graph.SliceNames, message, rows, "No slice to check");

        var violations = new List<ArchitectureViolation>();
        foreach (var cycle in graph.FindCycles())
        {
            var dependencies = new List<Dependency>();
            for (var i = 0; i < cycle.Count - 1; i++)
            {
                dependencies.AddRange(graph.Edges[(cycle[i], cycle[i + 1])]);
            }

            violations.Add(new ArchitectureViolation(string.Join(" -> ", cycle), type: null, dependencies));
        }

        ThrowIfViolations(Name, violations, "{0} cycle between slices", "{0} cycles between slices", message, rows);
    }

    /// <summary>Asserts that the slices do not use each other at all. Each type that uses another slice is reported.</summary>
    /// <param name="slices">The slices, such as the result of <see cref="TypeElementExtensions.SliceByNamespace"/> or of <c>GroupBy</c>. A type must not be in several slices.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="slicesExpression">The expression of <paramref name="slices"/>.</param>
    /// <exception cref="AssertionException">A type uses another slice, or <paramref name="slices"/> is empty.</exception>
    /// <exception cref="ArgumentException">Two slices have the same name, or a type is in several slices.</exception>
    public void DoesNotDependOnEachOther(IEnumerable<IGrouping<string, TypeElement>> slices, string? message = null, [CallerArgumentExpression(nameof(slices))] string? slicesExpression = null)
    {
        const string Name = nameof(DoesNotDependOnEachOther);
        var graph = SliceGraph.Create(slices, nameof(slices));
        var rows = new (string, string?)[] { ("Slices expression", slicesExpression) };
        EnsureNotEmpty(Name, graph.SliceNames, message, rows, "No slice to check");

        var violations = graph.TypeDependencies
            .Select(item => new ArchitectureViolation($"{item.Type} in slice '{item.Slice}' depends on slice '{item.TargetSlice}'", item.Type, item.Dependencies))
            .ToList();
        ThrowIfViolations(Name, violations, "{0} type depends on another slice", "{0} types depend on other slices", message, rows);
    }

    private static void DoesNotDependOnCore(IEnumerable<TypeElement> types, Func<TypeReference, bool> isForbidden, string? message, (string Label, string? Value)[] rows)
    {
        const string Name = nameof(DoesNotDependOn);
        var typeList = GetTypes(types, nameof(types));
        EnsureNotEmpty(Name, typeList, message, rows, "No type to check");
        var violations = FindDependencies(typeList, dependency => isForbidden(dependency.Target));
        ThrowIfViolations(Name, violations, "{0} type depends on a forbidden type", "{0} types depend on forbidden types", message, rows);
    }

    private static List<TypeElement> GetTypes(IEnumerable<TypeElement> types, string paramName)
    {
        ArgumentNullException.ThrowIfNull(types, paramName);
        var result = types.ToList();
        foreach (var type in result)
        {
            ArgumentNullException.ThrowIfNull(type, paramName);
        }

        return result;
    }

    private static List<ArchitectureViolation> FindDependencies(List<TypeElement> types, Func<Dependency, bool> isViolation)
    {
        var violations = new List<ArchitectureViolation>();
        foreach (var type in types)
        {
            var dependencies = type.Dependencies.Where(isViolation).ToList();
            if (dependencies.Count > 0)
            {
                var targets = string.Join(", ", dependencies.Select(dependency => dependency.Target.ToString()).Distinct(StringComparer.Ordinal));
                violations.Add(new ArchitectureViolation($"{type} depends on {targets}", type, dependencies));
            }
        }

        return violations;
    }

    private static void EnsureNotEmpty<T>(string name, IReadOnlyCollection<T> values, string? message, (string Label, string? Value)[] rows, string reason)
    {
        if (values.Count == 0)
            throw new AssertionException(FormatHeader(name, reason, message, rows).ToString());
    }

    private static void ThrowIfViolations(string name, List<ArchitectureViolation> violations, string singularFormat, string pluralFormat, string? message, (string Label, string? Value)[] rows)
    {
        if (violations.Count == 0)
            return;

        var reason = string.Format(CultureInfo.InvariantCulture, violations.Count == 1 ? singularFormat : pluralFormat, violations.Count);
        var sb = FormatHeader(name, reason, message, rows);
        foreach (var violation in violations)
        {
            sb.Append('\n');
            var first = true;
            foreach (var line in violation.ToString().Split('\n'))
            {
                sb.Append(first ? "- " : "\n  ").Append(line);
                first = false;
            }
        }

        throw new AssertionException(sb.ToString());
    }

    private static StringBuilder FormatHeader(string name, string reason, string? message, (string Label, string? Value)[] rows)
    {
        var header = new AssertionMessageBuilder($"Assert.Architecture.{name}() assertion failed: {reason}.")
            .AppendUserMessage("Message", message)
            .AppendGroup(rows);
        return new StringBuilder(header.ToString());
    }
}
