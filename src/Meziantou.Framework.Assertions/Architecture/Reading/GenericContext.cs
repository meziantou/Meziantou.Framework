using System.Collections.Immutable;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// The names of the generic parameters of the type and the method whose signatures are decoded
internal sealed record GenericContext(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static GenericContext Empty { get; } = new([], []);
}
