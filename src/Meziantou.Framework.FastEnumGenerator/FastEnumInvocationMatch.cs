using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.FastEnumGenerator;

internal readonly record struct FastEnumInvocationMatch(FastEnumMethodKind MethodKind, INamedTypeSymbol EnumType);
