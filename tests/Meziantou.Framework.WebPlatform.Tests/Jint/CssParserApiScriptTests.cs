using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The CSS Parser API (https://wicg.github.io/css-parser-api/) in the scripts.</summary>
public sealed class CssParserApiScriptTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    // Runs a script whose promise sets the title of the document
    private string Run(string script)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => $"<script>({script})().then(r => document.title = r, e => document.title = 'error: ' + e);</script>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void ParseStylesheet_ResolvesWithTheRules()
    {
        var result = Run("""
            async () => {
              const rules = await CSS.parseStylesheet('@import "a.css"; a { color: red; }');
              const [atRule, rule] = rules;
              return [
                rules.length,
                atRule instanceof CSSParserAtRule, atRule instanceof CSSParserRule, atRule.name, JSON.stringify(atRule.prelude), atRule.body,
                rule instanceof CSSParserQualifiedRule, rule.prelude[0] instanceof CSSKeywordValue, rule.prelude === rule.prelude, Object.isFrozen(rule.body),
                rule.body[0] instanceof CSSParserDeclaration, rule.body[0].name, String(rule.body[0].body[0]),
                String(rule),
              ].join('|');
            }
            """);

        Assert.Equal("2|true|true|import|[\" \",\"\\\"a.css\\\"\"]||true|true|true|true|true|color|red|a { color: red; }", result);
    }

    [Fact]
    public void ParseRuleListAndDeclarationList_ResolveWithTheItems()
    {
        var result = Run("""
            async () => {
              const rules = await CSS.parseRuleList('a {} @media print { b {} }');
              const declarations = await CSS.parseDeclarationList('a: b; c: d !important; @e;');
              return [rules.map(String).join(','), declarations.map(String).join(','), declarations.every(d => d instanceof CSSParserDeclaration)].join('|');
            }
            """);

        Assert.Equal("a {},@media print { b {} }|a: b,c: d !important|true", result);
    }

    [Fact]
    public void ParseRule_RejectsInvalidInputWithASyntaxError()
    {
        var result = Run("""
            async () => {
              const rule = await CSS.parseRule(' @font-face { src: x } ');
              const error = await CSS.parseRule('a {} b {}').catch(e => e);
              return [rule.constructor.name, String(rule), error instanceof DOMException, error.name].join('|');
            }
            """);

        Assert.Equal("CSSParserAtRule|@font-face { src: x; }|true|SyntaxError", result);
    }

    [Fact]
    public void SyncOperations_ReturnTheValues()
    {
        var result = Run("""
            async () => {
              const declaration = CSS.parseDeclaration('width: calc(1px + 2%)');
              const fn = declaration.body[0];
              const value = CSS.parseValue(' [a] ');
              const list = CSS.parseValueList('a 1px "b" #c');
              const groups = CSS.parseCommaValueList('a, b c');
              let error;
              try { CSS.parseDeclaration('a: b;'); } catch (e) { error = e.name; }
              return [
                fn instanceof CSSParserFunction, fn instanceof CSSParserValue, fn.name, fn.args.length, fn.args[0][0] instanceof CSSUnitValue, fn.args[0][0].unit, fn.args[0][2],
                value instanceof CSSParserBlock, value.name, value.body[0].value,
                list.map(v => typeof v === 'string' ? JSON.stringify(v) : v.constructor.name).join(' '),
                groups.length, groups[1].length,
                error,
              ].join('|');
            }
            """);

        Assert.Equal("true|true|calc|1|true|px|+|true|[]|a|CSSKeywordValue \" \" CSSUnitValue \" \" \"\\\"b\\\"\" \" \" \"#c\"|2|4|SyntaxError", result);
    }

    [Fact]
    public void Constructors_CreateTheObjects()
    {
        var result = Run("""
            async () => {
              const block = new CSSParserBlock('()', ['a', ' ', CSS.px(1), 2]);
              const fn = new CSSParserFunction('f', [[block], ['b']]);
              const rule = new CSSParserQualifiedRule(['p', ' '], [new CSSParserDeclaration('color', [fn])]);
              const atRule = new CSSParserAtRule('media', [' ', 'print', ' '], [rule]);
              let ruleError, valueError, blockError;
              try { new CSSParserRule(); } catch (e) { ruleError = e.constructor.name; }
              try { new CSSParserValue(); } catch (e) { valueError = e.constructor.name; }
              try { new CSSParserBlock('<>', []); } catch (e) { blockError = e.constructor.name; }
              return [String(block), String(fn), String(atRule), new CSSParserAtRule('import', []).body, new CSSParserQualifiedRule([]).body.length, ruleError, valueError, blockError].join('|');
            }
            """);

        Assert.Equal("(a 1px/**/2)|f((a 1px/**/2),b)|@media print { p { color: f((a 1px/**/2),b); } }||0|TypeError|TypeError|TypeError", result);
    }

    [Fact]
    public void AtRulesOption_SelectsTheProductionOfTheBlocks()
    {
        var result = Run("""
            async () => {
              const rules = await CSS.parseStylesheet('@font-face { a {} src: x; } @keyframes k { from {} c: d; }', { atRules: { 'font-face': 'declaration-list', keyframes: 'qualified-rule-list' } });
              const error = await CSS.parseStylesheet('', { atRules: { media: 'rules' } }).catch(e => e.constructor.name);
              return rules.map(String).join(',') + '|' + error;
            }
            """);

        Assert.Equal("@font-face { src: x; },@keyframes k { from {} }|TypeError", result);
    }

    [Fact]
    public void ReadableStream_IsDecodedAndParsed()
    {
        var result = Run("""
            async () => {
              // The bytes of a style sheet are decoded with its @charset rule (windows-1252: 0xE9 is U+00E9)
              const stylesheet = await CSS.parseStylesheet(new Blob([new Uint8Array([...[...'@charset "windows-1252"; a { content: "'].map(c => c.charCodeAt(0)), 0xE9, 0x22, 0x7D])]).stream());
              // The other inputs are UTF-8
              const rule = await CSS.parseRule(new Blob(['\uFEFFb { content: "\u00E9" }']).stream());
              const declarations = await CSS.parseDeclarationList(new Blob(['c: d']).stream());
              const rules = await CSS.parseRuleList(new Blob(['e {}']).stream());
              const notBytes = await CSS.parseStylesheet(new ReadableStream({ start(c) { c.enqueue('a {}'); c.close(); } })).catch(e => e.constructor.name);
              const stream = new Blob(['a {}']).stream();
              stream.getReader();
              const locked = await CSS.parseStylesheet(stream).catch(e => e.constructor.name);
              return [stylesheet.map(String).join(','), String(rule), declarations.map(String).join(','), rules.map(String).join(','), notBytes, locked].join('|');
            }
            """);

        Assert.Equal("@charset \"windows-1252\";,a { content: \"\u00E9\"; }|b { content: \"\u00E9\"; }|c: d|e {}|TypeError|TypeError", result);
    }

    [Fact]
    public void Operations_HaveTheIdlLengths()
    {
        var result = Run("""
            async () => ['parseStylesheet', 'parseRuleList', 'parseRule', 'parseDeclarationList', 'parseDeclaration', 'parseValue', 'parseValueList', 'parseCommaValueList']
              .map(name => CSS[name].name + CSS[name].length + Object.getOwnPropertyDescriptor(CSS, name).enumerable).join()
            """);

        Assert.Equal("parseStylesheet1true,parseRuleList1true,parseRule1true,parseDeclarationList1true,parseDeclaration1true,parseValue1true,parseValueList1true,parseCommaValueList1true", result);
    }
}
