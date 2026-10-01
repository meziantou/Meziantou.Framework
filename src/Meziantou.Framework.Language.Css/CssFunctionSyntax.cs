namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a function in a value, such as <c>rgb(0 0 0)</c>.</summary>
public sealed partial class CssFunctionSyntax
{
    /// <summary>Gets the name of the function, without the parenthesis and with its escapes resolved, such as <c>rgb</c>.</summary>
    public string Name => FunctionToken.ValueText;
}
