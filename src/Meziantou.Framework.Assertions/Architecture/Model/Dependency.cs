using System.Text;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A use of a type by a type of the architecture, in a declaration or in the code of a method.</summary>
public sealed class Dependency
{
    internal Dependency(TypeElement source, MemberElement? sourceMember, TypeReference target, MemberReference? targetMember, DependencyKind kind, SourceLocation? location)
    {
        Source = source;
        SourceMember = sourceMember;
        Target = target;
        TargetMember = targetMember;
        Kind = kind;
        Location = location;
    }

    /// <summary>Gets the type that depends on <see cref="Target"/>. The code of lambdas, local functions, iterators and async methods belongs to the type that declares them.</summary>
    public TypeElement Source { get; }

    /// <summary>Gets the member of <see cref="Source"/> whose declaration or code uses <see cref="Target"/>, or <see langword="null"/> for a dependency of the type declaration.</summary>
    public MemberElement? SourceMember { get; }

    /// <summary>Gets the type the source depends on, without generic arguments: <c>List&lt;Order&gt;</c> gives one dependency on <c>List&lt;T&gt;</c> and one on <c>Order</c>.</summary>
    public TypeReference Target { get; }

    /// <summary>Gets the method or field used, for <see cref="DependencyKind.MethodCall"/>, <see cref="DependencyKind.ObjectCreation"/> and <see cref="DependencyKind.FieldAccess"/>.</summary>
    public MemberReference? TargetMember { get; }

    /// <summary>Gets how the source uses the target.</summary>
    public DependencyKind Kind { get; }

    /// <summary>Gets the statement that uses the target, or the start of the member for a declaration, when the PDB is available.</summary>
    public SourceLocation? Location { get; }

    /// <summary>Returns a description of the dependency, such as <c>MyApp.Order.Save calls MyApp.Database.Execute (Order.cs:12:9)</c>.</summary>
    /// <returns>A description of the dependency.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Source);
        if (SourceMember is not null)
        {
            sb.Append('.').Append(SourceMember.Name);
        }

        sb.Append(' ').Append(GetVerb(Kind)).Append(' ');
        if (TargetMember is not null && (Kind is DependencyKind.MethodCall or DependencyKind.FieldAccess or DependencyKind.ObjectCreation) && string.Equals(TargetMember.DeclaringType.FullName, Target.FullName, StringComparison.Ordinal))
        {
            sb.Append(TargetMember);
        }
        else
        {
            sb.Append(Target);
        }

        if (Location is { } location)
        {
            sb.Append(" (").Append(location).Append(')');
        }

        return sb.ToString();
    }

    private static string GetVerb(DependencyKind kind)
    {
        return kind switch
        {
            DependencyKind.BaseType => "inherits from",
            DependencyKind.Interface => "implements",
            DependencyKind.Attribute => "uses attribute",
            DependencyKind.GenericConstraint => "has a generic constraint on",
            DependencyKind.FieldType => "has a field of type",
            DependencyKind.PropertyType => "has a property of type",
            DependencyKind.EventType => "has an event of type",
            DependencyKind.ParameterType => "has a parameter of type",
            DependencyKind.ReturnType => "returns",
            DependencyKind.LocalVariable => "has a variable of type",
            DependencyKind.MethodCall => "calls",
            DependencyKind.FieldAccess => "accesses",
            DependencyKind.ObjectCreation => "creates",
            DependencyKind.TypeUsage => "uses",
            DependencyKind.CatchClause => "catches",
            _ => "depends on",
        };
    }
}
