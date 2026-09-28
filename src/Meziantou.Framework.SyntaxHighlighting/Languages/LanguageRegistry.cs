using System.Collections.Concurrent;
using System.Collections.Frozen;
using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class LanguageRegistry
{
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
            ["go"] = () => Go.Instance,
            ["golang"] = () => Go.Instance,
            ["rust"] = () => Rust.Instance,
            ["rs"] = () => Rust.Instance,
            ["python"] = () => Python.Instance,
            ["py"] = () => Python.Instance,
            ["gyp"] = () => Python.Instance,
            ["ipython"] = () => Python.Instance,
            ["c"] = () => C.Instance,
            ["h"] = () => C.Instance,
            ["diff"] = () => Diff.Instance,
            ["patch"] = () => Diff.Instance,
            ["plaintext"] = () => Plaintext.Instance,
            ["text"] = () => Plaintext.Instance,
            ["txt"] = () => Plaintext.Instance,
            ["swift"] = () => Swift.Instance,
            ["objectivec"] = () => ObjectiveC.Instance,
            ["mm"] = () => ObjectiveC.Instance,
            ["objc"] = () => ObjectiveC.Instance,
            ["obj-c"] = () => ObjectiveC.Instance,
            ["obj-c++"] = () => ObjectiveC.Instance,
            ["objective-c++"] = () => ObjectiveC.Instance,
            ["dart"] = () => Dart.Instance,
            ["elixir"] = () => Elixir.Instance,
            ["ex"] = () => Elixir.Instance,
            ["exs"] = () => Elixir.Instance,
            ["erlang"] = () => Erlang.Instance,
            ["erl"] = () => Erlang.Instance,
            ["haskell"] = () => Haskell.Instance,
            ["hs"] = () => Haskell.Instance,
            ["clojure"] = () => Clojure.Instance,
            ["clj"] = () => Clojure.Instance,
            ["edn"] = () => Clojure.Instance,
            ["julia"] = () => Julia.Instance,
            ["jl"] = () => Julia.Instance,
            ["java"] = () => Java.Instance,
            ["jsp"] = () => Java.Instance,
            ["kotlin"] = () => Kotlin.Instance,
            ["kt"] = () => Kotlin.Instance,
            ["kts"] = () => Kotlin.Instance,
            ["scala"] = () => Scala.Instance,
            ["groovy"] = () => Groovy.Instance,
            ["shell"] = () => Shell.Instance,
            ["console"] = () => Shell.Instance,
            ["shellsession"] = () => Shell.Instance,
            ["makefile"] = () => Makefile.Instance,
            ["mk"] = () => Makefile.Instance,
            ["mak"] = () => Makefile.Instance,
            ["make"] = () => Makefile.Instance,
            ["protobuf"] = () => Protobuf.Instance,
            ["proto"] = () => Protobuf.Instance,
            ["properties"] = () => Properties.Instance,
            ["hcl"] = () => Hcl.Instance,
            ["terraform"] = () => Hcl.Instance,
            ["tf"] = () => Hcl.Instance,
            ["tfvars"] = () => Hcl.Instance,
            ["ruby"] = () => Ruby.Instance,
            ["rb"] = () => Ruby.Instance,
            ["gemspec"] = () => Ruby.Instance,
            ["podspec"] = () => Ruby.Instance,
            ["thor"] = () => Ruby.Instance,
            ["irb"] = () => Ruby.Instance,
            ["perl"] = () => Perl.Instance,
            ["pl"] = () => Perl.Instance,
            ["pm"] = () => Perl.Instance,
            ["lua"] = () => Lua.Instance,
            ["pluto"] = () => Lua.Instance,
            ["r"] = () => R.Instance,
            ["ocaml"] = () => Ocaml.Instance,
            ["ml"] = () => Ocaml.Instance,
            ["cmake"] = () => CMake.Instance,
            ["cmake.in"] = () => CMake.Instance,
            ["python-repl"] = () => Pycon.Instance,
            ["pycon"] = () => Pycon.Instance,
            ["gradle"] = () => Groovy.Instance,
            ["latex"] = () => Latex.Instance,
            ["tex"] = () => Latex.Instance,
            ["gherkin"] = () => Gherkin.Instance,
            ["feature"] = () => Gherkin.Instance,
            ["delphi"] = () => Delphi.Instance,
            ["dpr"] = () => Delphi.Instance,
            ["dfm"] = () => Delphi.Instance,
            ["pas"] = () => Delphi.Instance,
            ["pascal"] = () => Delphi.Instance,
            ["matlab"] = () => Matlab.Instance,
            ["pgsql"] = () => Pgsql.Instance,
            ["postgres"] = () => Pgsql.Instance,
            ["postgresql"] = () => Pgsql.Instance,
            ["handlebars"] = () => Handlebars.Instance,
            ["hbs"] = () => Handlebars.Instance,
            ["html.hbs"] = () => Handlebars.Instance,
            ["html.handlebars"] = () => Handlebars.Instance,
            ["htmlbars"] = () => Handlebars.Instance,
            ["mustache"] = () => Handlebars.Instance,
            ["django"] = () => Django.Instance,
            ["jinja"] = () => Django.Instance,
            ["jinja2"] = () => Django.Instance,
            ["j2"] = () => Django.Instance,
            ["twig"] = () => Twig.Instance,
            ["craftcms"] = () => Twig.Instance,
            ["erb"] = () => Erb.Instance,
            ["zig"] = () => Zig.Instance,
            ["zon"] = () => Zig.Instance,
            ["solidity"] = () => Solidity.Instance,
            ["sol"] = () => Solidity.Instance,
            ["bicep"] = () => Bicep.Instance,
            ["bicepparam"] = () => Bicep.Instance,
            ["node-repl"] = () => NodeRepl.Instance,
            ["julia-repl"] = () => JuliaRepl.Instance,
            ["jldoctest"] = () => JuliaRepl.Instance,
            ["erlang-repl"] = () => ErlangRepl.Instance,
            ["clojure-repl"] = () => ClojureRepl.Instance,
            ["arduino"] = () => Arduino.Instance,
            ["ino"] = () => Arduino.Instance,
            ["vbscript"] = () => VbScript.Instance,
            ["vbs"] = () => VbScript.Instance,
            ["vbscript-html"] = () => VbScriptHtml.Instance,
            ["wasm"] = () => Wasm.Instance,
            ["wat"] = () => Wasm.Instance,
            ["wast"] = () => Wasm.Instance,
            ["llvm"] = () => Llvm.Instance,
            ["ll"] = () => Llvm.Instance,
            ["armasm"] = () => ArmAsm.Instance,
            ["arm"] = () => ArmAsm.Instance,
            ["glsl"] = () => Glsl.Instance,
            ["vert"] = () => Glsl.Instance,
            ["frag"] = () => Glsl.Instance,
            ["verilog"] = () => Verilog.Instance,
            ["v"] = () => Verilog.Instance,
            ["sv"] = () => Verilog.Instance,
            ["svh"] = () => Verilog.Instance,
            ["vhdl"] = () => Vhdl.Instance,
            ["vhd"] = () => Vhdl.Instance,
            ["haml"] = () => Haml.Instance,
            ["vue"] = () => Vue.Instance,
            ["svelte"] = () => Svelte.Instance,
            ["nim"] = () => Nim.Instance,
            ["nims"] = () => Nim.Instance,
            ["crystal"] = () => Crystal.Instance,
            ["cr"] = () => Crystal.Instance,
            ["d"] = () => D.Instance,
            ["tcl"] = () => Tcl.Instance,
            ["tk"] = () => Tcl.Instance,
            ["coffeescript"] = () => CoffeeScript.Instance,
            ["coffee"] = () => CoffeeScript.Instance,
            ["cson"] = () => CoffeeScript.Instance,
            ["iced"] = () => CoffeeScript.Instance,
            ["fortran"] = () => Fortran.Instance,
            ["f90"] = () => Fortran.Instance,
            ["f95"] = () => Fortran.Instance,
            ["f03"] = () => Fortran.Instance,
            ["f08"] = () => Fortran.Instance,
            ["f77"] = () => Fortran.Instance,
            ["for"] = () => Fortran.Instance,
            ["f"] = () => Fortran.Instance,
            ["scheme"] = () => Scheme.Instance,
            ["scm"] = () => Scheme.Instance,
            ["lisp"] = () => Lisp.Instance,
            ["elisp"] = () => Lisp.Instance,
            ["emacs-lisp"] = () => Lisp.Instance,
            ["common-lisp"] = () => Lisp.Instance,
            ["elm"] = () => Elm.Instance,
            ["prolog"] = () => Prolog.Instance,
            ["nix"] = () => Nix.Instance,
            ["nixos"] = () => Nix.Instance,
            ["apache"] = () => Apache.Instance,
            ["apacheconf"] = () => Apache.Instance,
            ["htaccess"] = () => Apache.Instance,
            ["vim"] = () => Vim.Instance,
            ["vimscript"] = () => Vim.Instance,
            ["awk"] = () => Awk.Instance,
            ["gawk"] = () => Awk.Instance,
            ["mawk"] = () => Awk.Instance,
            ["nawk"] = () => Awk.Instance,
            ["puppet"] = () => Puppet.Instance,
            ["pp"] = () => Puppet.Instance,
            ["thrift"] = () => Thrift.Instance,
            ["dns"] = () => Dns.Instance,
            ["bind"] = () => Dns.Instance,
            ["zone"] = () => Dns.Instance,
            ["asciidoc"] = () => AsciiDoc.Instance,
            ["adoc"] = () => AsciiDoc.Instance,
            ["toml"] = () => Toml.Instance,
            ["mermaid"] = () => Mermaid.Instance,
            ["mmd"] = () => Mermaid.Instance,
            ["kql"] = () => Kql.Instance,
            ["kusto"] = () => Kql.Instance,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Regex timeouts are fixed when a regex is created, so a grammar is compiled again for each non-default match timeout.
    // Only the grammars of the last few timeouts are kept: a caller that derives a timeout per call (a remaining budget)
    // must not make the cache, about half a megabyte per grammar and timeout, grow forever.
    private const int MaxCachedMatchTimeouts = 4;

    private static readonly Lock GrammarsByTimeoutLock = new();
    private static volatile GrammarsForTimeout[] s_grammarsByTimeout = [];

    public static CompiledMode Get(string language) => Get(language, Compiler.DefaultMatchTimeout);

    public static CompiledMode Get(string language, TimeSpan matchTimeout) =>
        TryGet(language, matchTimeout, out var mode) ? mode : throw new NotSupportedException($"Language '{language}' is not supported.");

    public static bool TryGet(string language, TimeSpan matchTimeout, [NotNullWhen(true)] out CompiledMode? mode)
    {
        if (!Languages.TryGetValue(language, out var factory))
        {
            mode = null;
            return false;
        }

        mode = factory();
        if (matchTimeout != mode.MatchTimeout)
        {
            mode = GetGrammarsForTimeout(matchTimeout).Get(mode);
        }

        return true;
    }

    private static GrammarsForTimeout GetGrammarsForTimeout(TimeSpan matchTimeout)
    {
        foreach (var grammars in s_grammarsByTimeout)
        {
            if (grammars.MatchTimeout == matchTimeout)
                return grammars;
        }

        lock (GrammarsByTimeoutLock)
        {
            var current = s_grammarsByTimeout;
            foreach (var grammars in current)
            {
                if (grammars.MatchTimeout == matchTimeout)
                    return grammars;
            }

            var added = new GrammarsForTimeout(matchTimeout);
            s_grammarsByTimeout = [.. current.TakeLast(MaxCachedMatchTimeouts - 1), added];
            return added;
        }
    }

    public static bool IsSupported(string language) => Languages.ContainsKey(language);

    private static readonly IReadOnlyList<string> SortedLanguages = Array.AsReadOnly(Languages.Keys.Order(StringComparer.Ordinal).ToArray());

    public static IReadOnlyList<string> GetSupportedLanguages() => SortedLanguages;

    /// <summary>The grammars compiled with one match timeout, keyed by the grammar compiled with the default one.</summary>
    private sealed class GrammarsForTimeout(TimeSpan matchTimeout)
    {
        private readonly ConcurrentDictionary<CompiledMode, Lazy<CompiledMode>> _grammars = new(ReferenceEqualityComparer.Instance);

        public TimeSpan MatchTimeout { get; } = matchTimeout;

        public CompiledMode Get(CompiledMode grammar) => _grammars.GetOrAdd(grammar, grammar => new Lazy<CompiledMode>(() => Compiler.Compile(grammar.Source, MatchTimeout))).Value;
    }
}
