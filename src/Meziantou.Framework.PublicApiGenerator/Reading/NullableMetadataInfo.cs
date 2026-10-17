using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

// The content of the NullableAttribute applied to a target, and the NullableContextAttribute that applies to it
internal readonly record struct NullableMetadataInfo(ImmutableArray<byte> Flags, byte ContextFlag);
