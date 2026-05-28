using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class LanguageRegistry
{
    static LanguageRegistry()
    {
        // Wire up sub-language resolution (e.g. bash inside Dockerfile RUN, css inside <style>).
        Tokenizer.SubLanguageResolver = Get;
    }

#pragma warning disable CA1308 // Normalize strings to uppercase: language aliases are lowercase by convention.
    public static CompiledMode Get(string language) => language.ToLowerInvariant() switch
#pragma warning restore CA1308
    {
        "json" or "jsonc" => Json.Instance,
        "css" => Css.Instance,
        "csharp" or "cs" or "c#" => CSharp.Instance,
        "ini" or "toml" or "gitconfig" => Ini.Instance,
        "bnf" => Bnf.Instance,
        "x86asm" => X86Asm.Instance,
        "dos" or "bat" or "cmd" => Dos.Instance,
        "yaml" or "yml" => Yaml.Instance,
        "sql" => Sql.Instance,
        "nginx" or "nginxconf" => Nginx.Instance,
        "graphql" or "gql" => Graphql.Instance,
        "vbnet" or "vb" => VbNet.Instance,
        "fsharp" or "fs" or "f#" => FSharp.Instance,
        "cpp" or "c++" or "cc" or "h++" or "hpp" or "hh" or "hxx" or "cxx" => Cpp.Instance,
        "powershell" or "pwsh" or "ps" or "ps1" => PowerShell.Instance,
        "bash" or "sh" or "zsh" or "ksh" => Bash.Instance,
        "javascript" or "js" or "jsx" or "mjs" or "cjs" => Javascript.Instance,
        "typescript" or "ts" or "tsx" or "mts" or "cts" => Typescript.Instance,
        "less" => Less.Instance,
        "scss" => Scss.Instance,
        "php" => Php.Instance,
        "xml" or "xsd" or "xsl" or "plist" or "rss" or "atom" or "svg" => Xml.Instance,
        "html" or "htm" or "xhtml" => Html.Instance,
        "razor" or "cshtml" or "cshtml-razor" => Razor.Instance,
        "dockerfile" or "docker" => Dockerfile.Instance,
        "markdown" or "md" or "mkdown" or "mkd" => Markdown.Instance,
        "http" or "https" => Http.Instance,
        "urlencoded" or "x-www-form-urlencoded" => UrlEncoded.Instance,
        _ => throw new NotSupportedException($"Language '{language}' is not supported."),
    };
}
