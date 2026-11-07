namespace Meziantou.Framework.Language;

/// <summary>How far down a <see cref="SyntaxWalker"/> goes.</summary>
public enum SyntaxWalkerDepth
{
    /// <summary>Visit nodes only.</summary>
    Node = 0,

    /// <summary>Visit nodes and the tokens among them.</summary>
    Token = 1,

    /// <summary>Visit nodes, tokens, and the trivia around each token.</summary>
    Trivia = 2,
}
