using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.AspNetCore.ServiceDefaults.AutoRegister;

internal sealed class InterceptionData : IEquatable<InterceptionData?>
{
    public required string OrderKey { get; set; }
    public required InterceptionMethodKind Kind { get; set; }
    public required InterceptableLocation? InterceptableLocation { get; set; }

    public override bool Equals(object? obj) => Equals(obj as InterceptionData);
    public bool Equals(InterceptionData? other) => other is not null && Kind == other.Kind && EqualityComparer<InterceptableLocation>.Default.Equals(InterceptableLocation, other.InterceptableLocation);
    public override int GetHashCode() => HashCode.Combine(Kind, InterceptableLocation);
}
