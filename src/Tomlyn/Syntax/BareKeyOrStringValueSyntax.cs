namespace Tomlyn.Syntax
{
    /// <summary>
    /// Base class for a <see cref="BareKeySyntax"/> or a <see cref="StringValueSyntax"/>
    /// </summary>
    public abstract class BareKeyOrStringValueSyntax : ValueSyntax
    {
        protected BareKeyOrStringValueSyntax(SyntaxKind kind) : base(kind)
        {
        }
    }
}