using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// HashiCorp Configuration Language (HCL 2 native syntax), including Terraform.
/// </summary>
/// <remarks>
/// highlight.js has no HCL grammar, so this one is written from scratch following the scopes of the other grammars:
/// block types are sections, attribute names are attrs, function calls are built-ins, and template interpolations
/// (<c>${...}</c>) and directives (<c>%{...}</c>) are substitutions.
/// See https://github.com/hashicorp/hcl/blob/main/hclsyntax/spec.md.
/// </remarks>
internal static class Hcl
{
    // Identifiers can contain dashes (`local-exec` is an identifier, `a - b` is a subtraction).
    private const string IdentifierRe = @"[a-zA-Z_][\w-]*";

    // An identifier can only start where no identifier character precedes it, which also keeps the patterns below
    // from being retried from every character of a long identifier.
    private static readonly string IdentifierStart = CommonModes.RunStart(@"\w-", "a-zA-Z_");

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var expressionKeywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ["for", "in", "if"],
            ["literal"] = ["true", "false", "null"],
            ["type"] = ["string", "number", "bool", "any"],
        });

        var directiveKeywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ["for", "in", "if", "else", "endif", "endfor"],
            ["literal"] = ["true", "false", "null"],
        });

        var number = new Mode
        {
            Scope = "number",
            Match = @"\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?",
        };

        // `$${` and `%%{` are a literal `${` and `%{`.
        var escapedTemplateSequence = new Mode { Match = @"\$\$\{|%%\{" };

        var interpolation = new Mode
        {
            Scope = "subst",
            Begin = @"\$\{~?",
            End = @"~?\}",
            Keywords = expressionKeywords,
            KeywordPattern = IdentifierRe,
        };

        var directive = new Mode
        {
            Scope = "subst",
            Begin = @"%\{~?",
            End = @"~?\}",
            Keywords = directiveKeywords,
            KeywordPattern = IdentifierRe,
        };

        var quotedString = new Mode
        {
            Scope = "string",
            Begin = "\"",

            // A quoted string cannot span lines, so an unterminated one ends with its line.
            End = "\"|$",
            Contains = [CommonModes.BackslashEscape, escapedTemplateSequence, interpolation, directive],
        };

        // The closing marker is alone on its line and can be indented, for `<<EOT` as well as `<<-EOT`.
        var heredoc = new Mode
        {
            Scope = "string",
            Begin = @"<<-?(" + IdentifierRe + @")[ \t]*$",
            End = @"^[ \t]*(" + IdentifierRe + ")$",
            EndSameAsBegin = true,
            Contains = [escapedTemplateSequence, interpolation, directive],
        };

        // `var.region`, `each.key`, `count.index`, ...
        var namedValue = new Mode
        {
            Scope = "variable.language",
            Match = @"(?<![\w.-])(?:var|local|module|data|each|count|self|path|terraform)(?=\.)",
        };

        // Type constraints: `list(string)`, `map(object({ ... }))`, `optional(number, 1)`.
        var typeConstructor = new Mode
        {
            Scope = "type",
            Match = IdentifierStart + @"(?:list|map|set|object|tuple|optional)(?=\()",
        };

        // Terraform has no user-defined functions: every call is a built-in or a provider-defined function
        // (`provider::aws::arn_parse(...)`).
        var functionCall = new Mode
        {
            Scope = "built_in",
            Match = IdentifierStart + IdentifierRe + "(?:::" + IdentifierRe + @")*(?=\()",
        };

        // An attribute access is not a keyword: `var.list`, `each.value.if`.
        var attributeAccess = new Mode { Match = @"\." + IdentifierRe };

        var comments = new[] { CommonModes.HashCommentMode, CommonModes.CLineCommentMode, CommonModes.CBlockCommentMode };
        var expression = new List<Mode> { heredoc, quotedString, number, namedValue, typeConstructor, functionCall, attributeAccess };

        // `name = value`, but not `a == b` or `k => v`.
        var attribute = new Mode
        {
            Scope = "attr",
            Match = IdentifierStart + IdentifierRe + @"(?=[ \t]*=(?![=>]))",
        };

        // An object constructor inside an interpolation must not end it: `${jsonencode({ a = 1 })}`.
        var nestedBraces = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Keywords = expressionKeywords,
            KeywordPattern = IdentifierRe,
        };
        nestedBraces.Contains = [.. comments, attribute, .. expression, Mode.Self];
        interpolation.Contains = [.. expression, nestedBraces];
        directive.Contains = [.. expression];

        // `resource "aws_instance" "web" {`, `lifecycle {`, `dynamic "ingress" {`. Terraform blocks have at most two
        // labels; bounding them keeps a long line of words from being rescanned from each word.
        var block = new Mode
        {
            Scope = "section",
            Match = IdentifierStart + IdentifierRe + @"(?=(?:[ \t]+(?:""(?:[^""\\\n]|\\.)*""|" + IdentifierRe + @")){0,3}[ \t]*\{)",
        };

        return new Mode
        {
            Keywords = expressionKeywords,
            KeywordPattern = IdentifierRe,
            Contains = [.. comments, attribute, block, .. expression],
        };
    }
}
