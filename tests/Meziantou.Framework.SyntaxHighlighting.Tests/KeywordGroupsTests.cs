using System.Collections;
using System.Reflection;

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class KeywordGroupsTests
{
    /// <summary>
    /// A word listed in two keyword groups of the same mode is resolved by
    /// <c>Compiler.BuildKeywordMap</c> as "last group wins", which lets a specific scope override
    /// the generic <c>keyword</c> group. That is deliberate, but a duplicate introduced by
    /// accident would silently change the rendered scope of a keyword, and the golden tests only
    /// cover the words they happen to use. This pins the reviewed set: adding a word to a second
    /// group now shows up here as an explicit diff.
    /// </summary>
    [Fact]
    public void KeywordGroups_CrossGroupDuplicates_MatchTheReviewedSet()
    {
        string[] expected =
        [
        "cpp:false:keyword+literal",
        "cpp:nullptr:keyword+literal",
        "cpp:true:keyword+literal",
        "csharp:dynamic:keyword+built_in",
        "msil:native:built_in+keyword",
        "sql:bigint:keyword+type",
        "sql:binary:keyword+type",
        "sql:blob:keyword+type",
        "sql:boolean:keyword+type",
        "sql:char:keyword+type",
        "sql:character:keyword+type",
        "sql:clob:keyword+type",
        "sql:current_catalog:keyword+built_in",
        "sql:current_date:keyword+built_in",
        "sql:current_default_transform_group:keyword+built_in",
        "sql:current_path:keyword+built_in",
        "sql:current_role:keyword+built_in",
        "sql:current_schema:keyword+built_in",
        "sql:current_time:keyword+built_in",
        "sql:current_timestamp:keyword+built_in",
        "sql:current_transform_group_for_type:keyword+built_in",
        "sql:current_user:keyword+built_in",
        "sql:date:keyword+type",
        "sql:dec:keyword+type",
        "sql:decfloat:keyword+type",
        "sql:decimal:keyword+type",
        "sql:false:keyword+literal",
        "sql:float:keyword+type",
        "sql:int:keyword+type",
        "sql:integer:keyword+type",
        "sql:interval:keyword+type",
        "sql:localtime:keyword+built_in",
        "sql:localtimestamp:keyword+built_in",
        "sql:national:keyword+type",
        "sql:nchar:keyword+type",
        "sql:nclob:keyword+type",
        "sql:numeric:keyword+type",
        "sql:real:keyword+type",
        "sql:row:keyword+type",
        "sql:session_user:keyword+built_in",
        "sql:smallint:keyword+type",
        "sql:system_time:keyword+built_in",
        "sql:system_user:keyword+built_in",
        "sql:time:keyword+type",
        "sql:timestamp:keyword+type",
        "sql:true:keyword+literal",
        "sql:unknown:keyword+literal",
        "sql:varbinary:keyword+type",
        "sql:varchar:keyword+type",
        "sql:varying:keyword+type",
        "typescript:void:keyword+built_in",
        "vbnet:new:keyword+built_in",
        "x86asm:equ:keyword+built_in",
        "x86asm:incbin:keyword+built_in",
        ];

        Assert.Equal(expected, FindCrossGroupDuplicates());
    }

    private static string[] FindCrossGroupDuplicates()
    {
        string[] languages =
        [
            "bash", "bnf", "cpp", "csharp", "css", "dockerfile", "dos", "fsharp", "graphql",
            "html", "http", "ini", "javascript", "json", "less", "markdown", "msil", "nginx",
            "php", "powershell", "razor", "scss", "sql", "typescript", "urlencoded", "vbnet",
            "x86asm", "xml", "yaml",
        ];

        var assembly = typeof(SyntaxHighlighter).Assembly;
        var registry = assembly.GetType("Meziantou.Framework.SyntaxHighlighting.Languages.LanguageRegistry", throwOnError: true)!;
        var get = registry.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!;

        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var language in languages)
        {
            var root = get.Invoke(null, [language])!;
            CollectDuplicates(language, root, new HashSet<object>(ReferenceEqualityComparer.Instance), result);
        }

        return [.. result];
    }

    private static void CollectDuplicates(string language, object? compiledMode, HashSet<object> visited, SortedSet<string> result)
    {
        if (compiledMode is null || !visited.Add(compiledMode))
            return;

        var type = compiledMode.GetType();
        var source = type.GetField("Source")!.GetValue(compiledMode)!;
        if (source.GetType().GetProperty("Keywords")!.GetValue(source) is { } keywords)
        {
            var groups = (IEnumerable)keywords.GetType().GetProperty("Groups")!.GetValue(keywords)!;
            var scopesByWord = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var groupType = group.GetType();
                var scope = (string)groupType.GetProperty("Key")!.GetValue(group)!;
                foreach (var raw in (string[])groupType.GetProperty("Value")!.GetValue(group)!)
                {
                    var word = raw.Split('|')[0];
                    if (!scopesByWord.TryGetValue(word, out var scopes))
                    {
                        scopesByWord[word] = scopes = [];
                    }

                    if (!scopes.Contains(scope, StringComparer.Ordinal))
                    {
                        scopes.Add(scope);
                    }
                }
            }

            foreach (var (word, scopes) in scopesByWord)
            {
                if (scopes.Count > 1)
                {
                    result.Add($"{language}:{word}:{string.Join('+', scopes)}");
                }
            }
        }

        foreach (var child in (IEnumerable)type.GetField("Contains")!.GetValue(compiledMode)!)
        {
            CollectDuplicates(language, child, visited, result);
        }

        CollectDuplicates(language, type.GetField("Starts")!.GetValue(compiledMode), visited, result);
    }

    // The pinned list above records *which* words are declared in two groups. These record what
    // that actually produces in rendered output, so the precedence rule can be read off real
    // highlighted code rather than inferred from the grammars.

    [Fact]
    public void Precedence_Cpp_LiteralGroupOverridesKeywordGroup()
    {
        AssertHighlighter("cpp",
"""
bool ok = true; int* p = nullptr;
""",
"""
<span class="hljs-type">bool</span> ok = <span class="hljs-literal">true</span>; <span class="hljs-type">int</span>* p = <span class="hljs-literal">nullptr</span>;
""");
    }

    [Fact]
    public void Precedence_Sql_TypeGroupOverridesKeywordGroup()
    {
        AssertHighlighter("sql",
"""
CREATE TABLE t (a bigint, b boolean);
""",
"""
<span class="hljs-keyword">CREATE TABLE</span> t (a <span class="hljs-type">bigint</span>, b <span class="hljs-type">boolean</span>);
""");
    }

    [Fact]
    public void Precedence_Sql_BuiltInGroupOverridesKeywordGroup()
    {
        AssertHighlighter("sql",
"""
SELECT current_user, current_date;
""",
"""
<span class="hljs-keyword">SELECT</span> <span class="hljs-built_in">current_user</span>, <span class="hljs-built_in">current_date</span>;
""");
    }

    [Fact]
    public void Precedence_CSharp_BuiltInGroupOverridesKeywordGroup()
    {
        AssertHighlighter("csharp",
"""
dynamic d = 1;
""",
"""
<span class="hljs-built_in">dynamic</span> d = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Precedence_TypeScript_BuiltInGroupOverridesKeywordGroup()
    {
        AssertHighlighter("typescript",
"""
function f(): void { }
""",
"""
<span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>): <span class="hljs-built_in">void</span> { }
""");
    }

    [Fact]
    public void Precedence_VbNet_BuiltInGroupOverridesKeywordGroup()
    {
        AssertHighlighter("vbnet",
"""
Dim x = New Object()
""",
"""
<span class="hljs-keyword">Dim</span> x = <span class="hljs-built_in">New</span> <span class="hljs-type">Object</span>()
""");
    }

    /// <summary>
    /// MSIL declares <c>native</c> in <c>built_in</c> first and <c>keyword</c> second, so here the
    /// generic group wins. Precedence is positional, not "the more specific scope always wins".
    /// </summary>
    [Fact]
    public void Precedence_Msil_LaterGroupWinsEvenWhenItIsTheGenericOne()
    {
        AssertHighlighter("msil",
"""
ldind.i native int
""",
"""
<span class="hljs-keyword">ldind.i</span> <span class="hljs-keyword">native</span> <span class="hljs-built_in">int</span>
""");
    }
}
