namespace Meziantou.Framework.Yaml.Syntax;

/// <summary>
/// Options used by <see cref="YamlSyntaxTree"/> parsing.
/// </summary>
public sealed class YamlSyntaxOptions
{
    /// <summary>Gets or sets a value indicating whether trivia (whitespace/newline/comments) should be included.</summary>
    public bool IncludeTrivia { get; set; } = true;
}
