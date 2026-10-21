namespace Meziantou.Framework.JavaScriptEngine.Parsing;

/// <summary>The goal and the context of a parse (https://tc39.es/ecma262/#sec-parsetext).</summary>
internal sealed class ParserOptions
{
    public static ParserOptions Script { get; } = new();

    public static ParserOptions Module { get; } = new() { IsModule = true };

    /// <summary>Gets whether the goal symbol is <c>Module</c> (strict, <c>await</c> is reserved, top-level await, import/export).</summary>
    public bool IsModule { get; init; }

    /// <summary>Gets whether the code starts in strict mode (e.g. the code of a direct eval called from strict code).</summary>
    public bool IsStrict { get; init; }

    /// <summary>
    /// Gets whether the code is the code of a direct eval inside a function: <c>new.target</c> is allowed
    /// (https://tc39.es/ecma262/#sec-performeval step 6: inFunction).
    /// </summary>
    public bool AllowNewTarget { get; init; }

    /// <summary>Gets whether the code is the code of a direct eval inside a method: <c>super.x</c> is allowed (inMethod).</summary>
    public bool AllowSuperProperty { get; init; }

    /// <summary>Gets whether the code is the code of a direct eval inside a derived constructor: <c>super()</c> is allowed (inDerivedConstructor).</summary>
    public bool AllowSuperCall { get; init; }

    /// <summary>Gets whether the code is the code of a direct eval inside a class field initializer: <c>arguments</c> is a SyntaxError (inClassFieldInitializer).</summary>
    public bool InClassFieldInitializer { get; init; }

    /// <summary>Gets the private names of the classes that enclose the code of a direct eval (they can be referenced).</summary>
    public IReadOnlyCollection<string>? PrivateNames { get; init; }

    /// <summary>
    /// Gets whether the syntax of the Annex B is enabled (HTML-like comments, legacy octal literals and escapes in sloppy
    /// code, labelled function declarations, function declarations in if statements, RegExp extensions...). It is always
    /// enabled, as in the web browsers.
    /// </summary>
    public bool AnnexB { get; init; } = true;
}
