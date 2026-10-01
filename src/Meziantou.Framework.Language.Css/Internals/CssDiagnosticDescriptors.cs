namespace Meziantou.Framework.Language.Css.Internals;

/// <summary>Every diagnostic the CSS parser can produce.</summary>
/// <remarks>
/// An error is something a browser would not accept: it drops the declaration, the rule, or the whole construct the
/// error is in. A warning is something a browser accepts, or that may only be newer than this parser.
/// </remarks>
internal static class CssDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor UnterminatedComment = Error("CSS0001", "Unterminated comment", "The comment has no closing '*/'.");
    public static readonly DiagnosticDescriptor UnterminatedString = Error("CSS0002", "Unterminated string", "The string has no closing quote.");
    public static readonly DiagnosticDescriptor BadUrl = Error("CSS0003", "Invalid URL", "An unquoted URL cannot contain {0}.");
    public static readonly DiagnosticDescriptor UnterminatedUrl = Error("CSS0003", "Unterminated URL", "The URL has no closing ')'.");
    public static readonly DiagnosticDescriptor InvalidEscape = Error("CSS0004", "Invalid escape", "A backslash followed by a line break is not an escape outside a string.");

    public static readonly DiagnosticDescriptor ExpectedToken = Error("CSS0010", "Expected a token", "Expected '{0}'.");
    public static readonly DiagnosticDescriptor InvalidDeclaration = Error("CSS0011", "Invalid declaration", "'{0}' is neither a declaration nor a rule.");
    public static readonly DiagnosticDescriptor CustomPropertyLikeRule = Error("CSS0011", "Invalid rule", "A rule cannot start with what looks like a custom property.");
    public static readonly DiagnosticDescriptor EmptyDeclarationValue = Error("CSS0011", "Empty declaration", "The declaration '{0}' has no value.");
    public static readonly DiagnosticDescriptor UnexpectedToken = Error("CSS0012", "Unexpected token", "Unexpected '{0}'.");
    public static readonly DiagnosticDescriptor DeclarationNotAllowed = Error("CSS0013", "Declaration not allowed", "Declarations are not allowed {0}.");
    public static readonly DiagnosticDescriptor RuleNotAllowed = Error("CSS0014", "Rule not allowed", "{0} is not allowed {1}.");
    public static readonly DiagnosticDescriptor BlockRequired = Error("CSS0015", "Block required", "'@{0}' requires a block.");
    public static readonly DiagnosticDescriptor BlockNotAllowed = Error("CSS0015", "Block not allowed", "'@{0}' ends with ';' rather than a block.");
    public static readonly DiagnosticDescriptor NestingTooDeep = Error("CSS0016", "Nesting too deep", "Blocks and functions cannot nest more than {0} deep.");
    public static readonly DiagnosticDescriptor MisplacedRule = Warning("CSS0017", "Misplaced rule", "'@{0}' must come before every rule but {1}, so a browser ignores it.");
    public static readonly DiagnosticDescriptor InvalidImportant = Error("CSS0018", "Invalid !important", "'!' is not followed by 'important', so it is part of the value.");
    public static readonly DiagnosticDescriptor UnknownAtRule = Warning("CSS0019", "Unknown at-rule", "Unknown at-rule '@{0}'.");

    public static readonly DiagnosticDescriptor ExpectedSelector = Error("CSS0020", "Expected a selector", "Expected a selector.");
    public static readonly DiagnosticDescriptor UnexpectedSelectorToken = Error("CSS0021", "Unexpected token in selector", "Unexpected '{0}' in a selector.");
    public static readonly DiagnosticDescriptor WhitespaceNotAllowed = Error("CSS0022", "Whitespace not allowed", "Whitespace is not allowed {0}.");
    public static readonly DiagnosticDescriptor InvalidCompoundOrder = Error("CSS0023", "Invalid compound selector", "{0}");
    public static readonly DiagnosticDescriptor InvalidAttributeSelector = Error("CSS0024", "Invalid attribute selector", "Invalid attribute selector: {0}.");
    public static readonly DiagnosticDescriptor InvalidAnPlusB = Error("CSS0025", "Invalid An+B", "'{0}' is not a valid An+B expression.");
    public static readonly DiagnosticDescriptor InvalidIdSelector = Error("CSS0026", "Invalid ID selector", "'{0}' is not a valid ID selector, because an identifier cannot start with a digit.");
    public static readonly DiagnosticDescriptor LeadingCombinator = Error("CSS0027", "Leading combinator", "A selector here cannot start with a combinator.");
    public static readonly DiagnosticDescriptor UnknownPseudoClass = Warning("CSS0028", "Unknown pseudo-class", "Unknown pseudo-class ':{0}'.");
    public static readonly DiagnosticDescriptor UnknownPseudoElement = Warning("CSS0028", "Unknown pseudo-element", "Unknown pseudo-element '::{0}'.");
    public static readonly DiagnosticDescriptor InvalidPseudoArgument = Error("CSS0029", "Invalid pseudo-class argument", "Invalid argument for '{0}': {1}.");
    public static readonly DiagnosticDescriptor IgnoredForgivingSelector = Warning("CSS0030", "Selector ignored", "This selector does not parse, so it matches nothing: {0}");

    public static readonly DiagnosticDescriptor InvalidPrelude = Error("CSS0040", "Invalid prelude", "Invalid prelude for '@{0}': {1}.");
    public static readonly DiagnosticDescriptor InvalidMediaQuery = Error("CSS0041", "Invalid media query", "This media query does not parse, so it matches nothing: {0}.");
    public static readonly DiagnosticDescriptor MixedAndOr = Error("CSS0042", "'and' and 'or' mixed", "'and' and 'or' cannot be mixed without parentheses.");
    public static readonly DiagnosticDescriptor InvalidRange = Error("CSS0043", "Invalid range", "Invalid range: {0}.");
    public static readonly DiagnosticDescriptor InvalidCondition = Error("CSS0044", "Invalid condition", "Invalid condition: {0}.");
    public static readonly DiagnosticDescriptor GeneralEnclosedCondition = Warning("CSS0045", "Condition always false", "This condition is not understood, so it is always false: {0}");
    public static readonly DiagnosticDescriptor InvalidLayerName = Error("CSS0046", "Invalid layer name", "Invalid layer name: {0}.");
    public static readonly DiagnosticDescriptor ExpectedKeyframeSelector = Error("CSS0047", "Expected a keyframe selector", "Expected a keyframe selector.");
    public static readonly DiagnosticDescriptor InvalidKeyframeSelector = Error("CSS0047", "Invalid keyframe selector", "'{0}' is not a keyframe selector.");
    public static readonly DiagnosticDescriptor InvalidPageSelector = Error("CSS0048", "Invalid page selector", "Invalid page selector: {0}.");
    public static readonly DiagnosticDescriptor InvalidName = Error("CSS0050", "Invalid name", "'{0}' is not a valid name for '@{1}': {2}.");
    public static readonly DiagnosticDescriptor UndeclaredNamespacePrefix = Error("CSS0052", "Undeclared namespace prefix", "The namespace prefix '{0}' is not declared by a '@namespace' rule.");
    public static readonly DiagnosticDescriptor InvalidCharset = Warning("CSS0051", "Invalid @charset", "'@charset' must be the very first thing in the file, written exactly '@charset \"name\";'.");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Error);

    private static DiagnosticDescriptor Warning(string id, string title, string messageFormat) => new(id, title, messageFormat, DiagnosticSeverity.Warning);
}
