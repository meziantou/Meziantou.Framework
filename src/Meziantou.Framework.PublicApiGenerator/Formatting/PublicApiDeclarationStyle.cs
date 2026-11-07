namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The style of the C# code produced by <see cref="PublicApiFormatter"/>.</summary>
public enum PublicApiDeclarationStyle
{
    /// <summary>
    /// A declaration suitable for display, without implementation: no member bodies, no constructor initializers, no type body.
    /// Attributes are written on their own lines before the declaration, generic constraints are written after the signature,
    /// and properties list their accessors (e.g. <c>public int Value { get; protected set; }</c>).
    /// Type names are fully qualified unless <see cref="PublicApiFormattingOptions.QualifyTypeNames"/> is <see langword="false"/>.
    /// </summary>
    Declaration,

    /// <summary>
    /// The compilable stub produced by <see cref="PublicApi.Generate(string, PublicApiOptions?)"/> (e.g. <c>public int M() => throw null;</c>).
    /// Formatting a type produces the type with all its members and nested types.
    /// </summary>
    Compilable,
}
