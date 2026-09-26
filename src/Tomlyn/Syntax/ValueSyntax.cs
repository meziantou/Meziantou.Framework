namespace Tomlyn.Syntax
{
    /// <summary>
    /// Base class for all TOML values.
    /// </summary>
    public abstract class ValueSyntax : SyntaxNode
    {
        protected ValueSyntax(SyntaxKind kind) : base(kind)
        {
        }
    }
}