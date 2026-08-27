using System.Collections.Frozen;
using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class LanguageRegistry
{
    static LanguageRegistry()
    {
        // Wire up sub-language resolution (e.g. bash inside Dockerfile RUN, css inside <style>).
        Tokenizer.SubLanguageResolver = Get;
    }

    // The values are factories rather than compiled grammars so that looking a language up — or
    // merely asking whether it is supported — does not compile every other grammar.
    private static readonly FrozenDictionary<string, Func<CompiledMode>> Languages =
        new Dictionary<string, Func<CompiledMode>>(StringComparer.OrdinalIgnoreCase)
        {
            ["json"] = () => Json.Instance,
            ["jsonc"] = () => Json.Instance,
            ["css"] = () => Css.Instance,
            ["csharp"] = () => CSharp.Instance,
            ["cs"] = () => CSharp.Instance,
            ["c#"] = () => CSharp.Instance,
            ["ini"] = () => Ini.Instance,
            ["toml"] = () => Ini.Instance,
            ["gitconfig"] = () => Ini.Instance,
            ["bnf"] = () => Bnf.Instance,
            ["x86asm"] = () => X86Asm.Instance,
            ["dos"] = () => Dos.Instance,
            ["bat"] = () => Dos.Instance,
            ["cmd"] = () => Dos.Instance,
            ["yaml"] = () => Yaml.Instance,
            ["yml"] = () => Yaml.Instance,
            ["sql"] = () => Sql.Instance,
            ["nginx"] = () => Nginx.Instance,
            ["nginxconf"] = () => Nginx.Instance,
            ["graphql"] = () => Graphql.Instance,
            ["gql"] = () => Graphql.Instance,
            ["vbnet"] = () => VbNet.Instance,
            ["vb"] = () => VbNet.Instance,
            ["fsharp"] = () => FSharp.Instance,
            ["fs"] = () => FSharp.Instance,
            ["f#"] = () => FSharp.Instance,
            ["cpp"] = () => Cpp.Instance,
            ["c++"] = () => Cpp.Instance,
            ["cc"] = () => Cpp.Instance,
            ["h++"] = () => Cpp.Instance,
            ["hpp"] = () => Cpp.Instance,
            ["hh"] = () => Cpp.Instance,
            ["hxx"] = () => Cpp.Instance,
            ["cxx"] = () => Cpp.Instance,
            ["powershell"] = () => PowerShell.Instance,
            ["pwsh"] = () => PowerShell.Instance,
            ["ps"] = () => PowerShell.Instance,
            ["ps1"] = () => PowerShell.Instance,
            ["bash"] = () => Bash.Instance,
            ["sh"] = () => Bash.Instance,
            ["zsh"] = () => Bash.Instance,
            ["ksh"] = () => Bash.Instance,
            ["javascript"] = () => Javascript.Instance,
            ["js"] = () => Javascript.Instance,
            ["jsx"] = () => Javascript.Instance,
            ["mjs"] = () => Javascript.Instance,
            ["cjs"] = () => Javascript.Instance,
            ["typescript"] = () => Typescript.Instance,
            ["ts"] = () => Typescript.Instance,
            ["tsx"] = () => Typescript.Instance,
            ["mts"] = () => Typescript.Instance,
            ["cts"] = () => Typescript.Instance,
            ["less"] = () => Less.Instance,
            ["scss"] = () => Scss.Instance,
            ["php"] = () => Php.Instance,
            ["xml"] = () => Xml.Instance,
            ["xsd"] = () => Xml.Instance,
            ["xsl"] = () => Xml.Instance,
            ["plist"] = () => Xml.Instance,
            ["rss"] = () => Xml.Instance,
            ["atom"] = () => Xml.Instance,
            ["svg"] = () => Xml.Instance,
            ["html"] = () => Html.Instance,
            ["htm"] = () => Html.Instance,
            ["xhtml"] = () => Html.Instance,
            ["razor"] = () => Razor.Instance,
            ["cshtml"] = () => Razor.Instance,
            ["cshtml-razor"] = () => Razor.Instance,
            ["dockerfile"] = () => Dockerfile.Instance,
            ["docker"] = () => Dockerfile.Instance,
            ["markdown"] = () => Markdown.Instance,
            ["md"] = () => Markdown.Instance,
            ["mkdown"] = () => Markdown.Instance,
            ["mkd"] = () => Markdown.Instance,
            ["http"] = () => Http.Instance,
            ["https"] = () => Http.Instance,
            ["urlencoded"] = () => UrlEncoded.Instance,
            ["x-www-form-urlencoded"] = () => UrlEncoded.Instance,
            ["msil"] = () => Msil.Instance,
            ["il"] = () => Msil.Instance,
            ["cil"] = () => Msil.Instance,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static CompiledMode Get(string language) =>
        TryGet(language, out var mode) ? mode : throw new NotSupportedException($"Language '{language}' is not supported.");

    public static bool TryGet(string language, [NotNullWhen(true)] out CompiledMode? mode)
    {
        if (Languages.TryGetValue(language, out var factory))
        {
            mode = factory();
            return true;
        }

        mode = null;
        return false;
    }

    public static bool IsSupported(string language) => Languages.ContainsKey(language);

    public static IEnumerable<string> GetSupportedLanguages() => Languages.Keys.Order(StringComparer.Ordinal);
}
