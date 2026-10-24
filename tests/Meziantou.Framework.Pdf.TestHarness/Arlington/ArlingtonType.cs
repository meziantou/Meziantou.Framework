namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>A type a key may have, with what applies to a value of that type.</summary>
/// <param name="Name">The type: <c>integer</c>, <c>name</c>, <c>dictionary</c>, <c>rectangle</c>, ...</param>
/// <param name="Indirect">Whether the value must be, or must not be, an indirect reference.</param>
/// <param name="PossibleValues">The values the key may have; an entry that starts with <c>fn:</c> is a predicate.</param>
/// <param name="Links">The definitions a value of this type is checked against, one of which must match.</param>
public sealed record ArlingtonType(string Name, ArlingtonIndirect Indirect, IReadOnlyList<string> PossibleValues, IReadOnlyList<string> Links);
