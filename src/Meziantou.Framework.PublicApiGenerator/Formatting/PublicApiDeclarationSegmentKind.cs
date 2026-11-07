namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Identifies the kind of a <see cref="PublicApiDeclarationSegment"/>.</summary>
public enum PublicApiDeclarationSegmentKind
{
    /// <summary>Text that has no specific meaning, such as a preprocessor directive.</summary>
    Text,
    Keyword,

    /// <summary>The name of the declared symbol. <see cref="PublicApiDeclarationSegment.Symbol"/> references it.</summary>
    Identifier,

    /// <summary>The name of a referenced type. <see cref="PublicApiDeclarationSegment.TypeReference"/> references it. Keywords such as <c>int</c> are type names.</summary>
    TypeName,

    /// <summary>The name of a generic parameter, in a declaration or in a reference.</summary>
    TypeParameterName,
    ParameterName,

    /// <summary>The name of a member of another type, such as an enum member used as attribute argument, or an explicitly implemented interface member.</summary>
    MemberName,
    Punctuation,
    Operator,
    StringLiteral,
    NumericLiteral,
    Space,
    LineBreak,
}
