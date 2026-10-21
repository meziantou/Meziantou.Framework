using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Parsing;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Tests.Parsing;

public sealed class JsParserTests
{
    private static Ast.Program Parse(string code) => JsParser.ParseScript(new SourceText(code), ParserOptions.Script);

    private static Ast.Program ParseModule(string code) => JsParser.ParseModule(new SourceText(code));

    private static Node GetExpression(Ast.Program program, int index = 0) => ((ExpressionStatement)program.Body[index]).Expression;

    private static string GetText(string code, Node node) => code[node.Start..node.End];

    private static JsSyntaxErrorException AssertSyntaxError(string code)
    {
        return Assert.Throws<JsSyntaxErrorException>(() => Parse(code));
    }

    [Fact]
    public void Positions_Expressions()
    {
        const string Code = "foo + bar * 2;\nlet x = `a${b}c`;";
        var program = Parse(Code);
        var statement = (ExpressionStatement)program.Body[0];
        Assert.Equal("foo + bar * 2;", GetText(Code, statement));
        var binary = (BinaryExpression)statement.Expression;
        Assert.Equal("foo + bar * 2", GetText(Code, binary));
        Assert.Equal("foo", GetText(Code, binary.Left));
        Assert.Equal("bar * 2", GetText(Code, binary.Right));

        var declaration = (VariableDeclaration)program.Body[1];
        Assert.Equal("let x = `a${b}c`;", GetText(Code, declaration));
        var template = (TemplateLiteral)declaration.Declarations[0].Init!;
        Assert.Equal("`a${b}c`", GetText(Code, template));
        Assert.Equal("a", GetText(Code, template.Quasis[0]));
        Assert.Equal("c", GetText(Code, template.Quasis[1]));
        Assert.Equal(0, program.Start);
        Assert.Equal(Code.Length, program.End);
    }

    [Theory]
    [InlineData("/* before */function /* a */ f /* b */ ( /* c */ x /* d */ ) /* e */ { /* f */ }/* after */", "function /* a */ f /* b */ ( /* c */ x /* d */ ) /* e */ { /* f */ }")]
    [InlineData("/* before */async /* a */ function /* b */ * f() {}/* after */", "async /* a */ function /* b */ * f() {}")]
    [InlineData("/* before */function* g() {}/* after */", "function* g() {}")]
    public void FunctionSourceText_Declaration(string code, string expected)
    {
        var function = ((FunctionDeclaration)Parse(code).Body[0]).Function;
        Assert.Equal(expected, GetText(code, function));
    }

    [Theory]
    [InlineData("x = /* before */async /* a */ ( /* b */ a /* c */ ) /* d */ => /* e */ a /* after */;", "async /* a */ ( /* b */ a /* c */ ) /* d */ => /* e */ a")]
    [InlineData("x = /* before */a => { return a; }/* after */;", "a => { return a; }")]
    [InlineData("x = /* before */async a => a/* after */;", "async a => a")]
    [InlineData("x = /* before */(function /* a */ () {})/* after */;", "function /* a */ () {}")]
    public void FunctionSourceText_Expression(string code, string expected)
    {
        var assignment = (AssignmentExpression)GetExpression(Parse(code));
        var function = assignment.Right switch
        {
            ArrowFunctionExpression arrow => arrow.Function,
            FunctionExpression expression => expression.Function,
            _ => throw new InvalidOperationException("Unexpected node " + assignment.Right.Type),
        };

        Assert.Equal(expected, GetText(code, function));
    }

    [Theory]
    [InlineData("({ /* before */get /* a */ [ /* b */ 'f' /* c */ ] /* d */ ( /* e */ ) /* f */ { /* g */ }/* after */ })", "get /* a */ [ /* b */ 'f' /* c */ ] /* d */ ( /* e */ ) /* f */ { /* g */ }")]
    [InlineData("({ /* before */async /* a */ * /* b */ f /* c */ () {}/* after */ })", "async /* a */ * /* b */ f /* c */ () {}")]
    [InlineData("({ /* before */* f() {}/* after */ })", "* f() {}")]
    [InlineData("({ /* before */set f(v) {}/* after */ })", "set f(v) {}")]
    public void FunctionSourceText_ObjectMethod(string code, string expected)
    {
        var obj = (ObjectExpression)GetExpression(Parse(code));
        var property = (Property)obj.Properties[0];
        var function = ((FunctionExpression)property.Value).Function;
        Assert.Equal(expected, GetText(code, function));
        Assert.Equal(function.Start, property.Value.Start);
    }

    [Theory]
    [InlineData("class A { /* before */static /* a */ f /* b */ () {}/* after */ }", "f /* b */ () {}")]
    [InlineData("class A { /* before */static /* a */ async /* b */ * f () {}/* after */ }", "async /* b */ * f () {}")]
    [InlineData("class A { /* before */get /* a */ #f () { return 1; }/* after */ }", "get /* a */ #f () { return 1; }")]
    [InlineData("class A { /* before */constructor(a) {}/* after */ }", "constructor(a) {}")]
    public void FunctionSourceText_ClassMethod(string code, string expected)
    {
        var classNode = ((ClassDeclaration)Parse(code).Body[0]).Class;
        var method = (MethodDefinition)classNode.Body[0];
        Assert.Equal(expected, GetText(code, method.Value));
    }

    [Fact]
    public void FunctionFlags()
    {
        var program = Parse("""
            class A extends B {
                constructor() { super(); }
                get x() { return 1; }
                set x(v) {}
                static async *m([a], b = 1) {}
            }
            function f(a, b) { 'use strict'; }
            """);
        var classNode = ((ClassDeclaration)program.Body[0]).Class;
        var constructor = ((MethodDefinition)classNode.Body[0]).Value;
        Assert.Equal(FunctionKind.ClassConstructor, constructor.Kind);
        Assert.True(constructor.IsDerivedConstructor);
        Assert.True(constructor.IsStrict);
        Assert.Equal(MethodKind.Constructor, ((MethodDefinition)classNode.Body[0]).Kind);
        Assert.Equal(FunctionKind.Getter, ((MethodDefinition)classNode.Body[1]).Value.Kind);
        Assert.Equal(FunctionKind.Setter, ((MethodDefinition)classNode.Body[2]).Value.Kind);

        var method = (MethodDefinition)classNode.Body[3];
        Assert.True(method.Static);
        Assert.Equal(FunctionKind.Method, method.Value.Kind);
        Assert.True(method.Value.IsAsync);
        Assert.True(method.Value.IsGenerator);
        Assert.False(method.Value.HasSimpleParameterList);
        Assert.False(method.Value.IsDerivedConstructor);

        var function = ((FunctionDeclaration)program.Body[1]).Function;
        Assert.Equal(FunctionKind.Normal, function.Kind);
        Assert.True(function.HasSimpleParameterList);
        Assert.True(function.IsStrict);
        Assert.False(program.IsStrict);

        var arrow = ((ArrowFunctionExpression)GetExpression(Parse("x => x + 1"))).Function;
        Assert.Equal(FunctionKind.Arrow, arrow.Kind);
        Assert.True(arrow.IsExpressionBody);
        Assert.False(arrow.IsStrict);
    }

    [Fact]
    public void Directives()
    {
        var program = Parse("'use strict'; \"other\"; x;");
        Assert.True(program.IsStrict);
        Assert.False(program.IsModule);
        Assert.Equal("use strict", ((ExpressionStatement)program.Body[0]).Directive);
        Assert.Equal("other", ((ExpressionStatement)program.Body[1]).Directive);
        Assert.Null(((ExpressionStatement)program.Body[2]).Directive);

        // An escape or parentheses: not a Use Strict Directive
        Assert.False(Parse("'use\\x20strict'; with (a) {}").IsStrict);
        Assert.False(Parse("('use strict'); with (a) {}").IsStrict);
        Assert.Null(((ExpressionStatement)Parse("('use strict')").Body[0]).Directive);

        // After the prologue, a string is not a directive
        Assert.False(Parse("x; 'use strict'; with (a) {}").IsStrict);

        Assert.True(ParseModule("x;").IsStrict);
        Assert.True(JsParser.ParseScript(new SourceText("x"), new ParserOptions { IsStrict = true }).IsStrict);
    }

    [Theory]
    [InlineData("'use strict'; 010")]
    [InlineData("'use strict'; '\\07'")]
    [InlineData("'\\07'; 'use strict';")]
    [InlineData("function f() { '\\8'; 'use strict'; }")]
    [InlineData("function f(eval) { 'use strict'; }")]
    [InlineData("function eval() { 'use strict'; }")]
    [InlineData("function f(a, a) { 'use strict'; }")]
    [InlineData("function f(a = 1) { 'use strict'; }")]
    [InlineData("(a, a) => 1")]
    [InlineData("function f() { 'use strict'; with (a) {} }")]
    public void StrictMode_Errors(string code)
    {
        AssertSyntaxError(code);
    }

    [Theory]
    [InlineData("function f(a, a) {}")]
    [InlineData("function f() { 'use strict'; } with (a) {}")]
    [InlineData("010; '\\07'; '\\8'")]
    public void SloppyMode_Valid(string code)
    {
        Parse(code);
    }

    [Fact]
    public void CoverGrammar_ObjectPattern()
    {
        var assignment = (AssignmentExpression)GetExpression(Parse("({ a = 1, b: [c, ...d], ...e } = x)"));
        var pattern = Assert.IsType<ObjectPattern>(assignment.Left);
        var a = (Property)pattern.Properties[0];
        Assert.True(a.Shorthand);
        var defaultValue = Assert.IsType<AssignmentPattern>(a.Value);
        Assert.Equal("a", ((Identifier)defaultValue.Left).Name);
        var b = (Property)pattern.Properties[1];
        var array = Assert.IsType<ArrayPattern>(b.Value);
        Assert.IsType<RestElement>(array.Elements[1]);
        Assert.IsType<RestElement>(pattern.Properties[2]);
    }

    [Fact]
    public void CoverGrammar_ArrowParameters()
    {
        var arrow = Assert.IsType<ArrowFunctionExpression>(GetExpression(Parse("({ a = 1 }, [b], ...c) => a")));
        Assert.IsType<ObjectPattern>(arrow.Function.Params[0]);
        Assert.IsType<ArrayPattern>(arrow.Function.Params[1]);
        Assert.IsType<RestElement>(arrow.Function.Params[2]);

        Assert.IsType<CallExpression>(GetExpression(Parse("async(a, b)")));
        var asyncArrow = Assert.IsType<ArrowFunctionExpression>(GetExpression(Parse("async (a, b) => a")));
        Assert.True(asyncArrow.Function.IsAsync);
        Assert.HasCount(2, asyncArrow.Function.Params);
        Assert.IsType<SequenceExpression>(GetExpression(Parse("(a, b)")));
        Assert.IsType<ChainExpression>(GetExpression(Parse("a?.b.c()")));
    }

    [Theory]
    [InlineData("({ a = 1 })")]
    [InlineData("({ a = 1 }).b = 1")]
    [InlineData("({ __proto__: 1, __proto__: 2 })")]
    [InlineData("[...a, b] = c")]
    [InlineData("([a]) = b")]
    [InlineData("((a)) => 1")]
    [InlineData("a?.b = 1")]
    [InlineData("a?.b`c`")]
    [InlineData("a ?? b || c")]
    [InlineData("-a ** 2")]
    [InlineData("async (await) => 1")]
    [InlineData("function* g() { (a = yield) => 1; }")]
    [InlineData("let let = 1")]
    [InlineData("let a; var a;")]
    [InlineData("{ let a; { var a; } }")]
    [InlineData("try {} catch (e) { let e; }")]
    [InlineData("label: label: ;")]
    [InlineData("while (1) { break missing; }")]
    [InlineData("a: { continue a; }")]
    [InlineData("class A { #a; #a; }")]
    [InlineData("class A { m() { this.#b; } }")]
    [InlineData("class A { constructor() { super(); } }")]
    [InlineData("class A { static prototype() {} }")]
    [InlineData("class A { x = arguments; }")]
    [InlineData("new.target")]
    [InlineData("import.meta")]
    [InlineData("using x = null;")]
    [InlineData("/a/gg")]
    [InlineData("/(/")]
    [InlineData("/[/]/v")]
    [InlineData("`\\u{110000}`")]
    [InlineData("1_")]
    [InlineData("0b12")]
    public void EarlyErrors(string code)
    {
        AssertSyntaxError(code);
    }

    [Theory]
    [InlineData("({ __proto__: a, __proto__: b } = x)")]
    [InlineData("({ __proto__: a, __proto__: b }) => 1")]
    [InlineData("[(a), b.c] = d")]
    [InlineData("tag`\\u{110000}`")]
    [InlineData("a ?? (b || c)")]
    [InlineData("(-a) ** 2")]
    [InlineData("function f() { { function g() {} function g() {} } }")]
    [InlineData("if (a) function f() {}")]
    [InlineData("try {} catch (e) { var e; }")]
    [InlineData("class A { get #a() {} set #a(v) {} m() { return #a in this; } }")]
    [InlineData("class A { #if = 1; static #class() {} m() { return this.#if + A.#class(); } }")]
    [InlineData("class A extends B { constructor() { (() => super())(); } }")]
    [InlineData("{ using x = null; }")]
    [InlineData("var let = 1; let\nx = 1")]
    [InlineData("f() = 1")]
    [InlineData("/[/]/u; /[\\/]/v; /(?<a>x)\\k<a>/u")]
    [InlineData("x = 1 <!-- HTML comment\n--> also a comment")]
    public void ValidCode(string code)
    {
        Parse(code);
    }

    [Fact]
    public void Module()
    {
        var program = ParseModule("import a, { b as c, 'd' as e } from 'm' with { type: 'json' }; export { a, c as 'f' }; export default class {} await x;");
        Assert.True(program.IsModule);
        var import = (ImportDeclaration)program.Body[0];
        Assert.HasCount(3, import.Specifiers);
        Assert.Equal("m", import.Source.Value);
        Assert.Equal("json", import.Attributes[0].Value.Value);
        Assert.IsType<AwaitExpression>(GetExpression(program, 3));
    }

    [Theory]
    [InlineData("export { x };")]
    [InlineData("export var a; export { a };")]
    [InlineData("export { 'a' };")]
    [InlineData("import { a } from 'm'; import { a } from 'n';")]
    [InlineData("x = 1 <!-- not a comment in a module")]
    [InlineData("var await;")]
    [InlineData("import x from 'm' with { type: 'json', type: 'json' };")]
    public void Module_EarlyErrors(string code)
    {
        Assert.Throws<JsSyntaxErrorException>(() => ParseModule(code));
    }

    [Fact]
    public void ErrorLocation()
    {
        var exception = Assert.Throws<JsSyntaxErrorException>(() => JsParser.ParseScript(new SourceText("var a;\nvar b = );", "file.js"), ParserOptions.Script));
        Assert.Equal("file.js", exception.Url);
        Assert.Equal(2, exception.Line);
        Assert.Equal(9, exception.Column);
        Assert.Equal(15, exception.Offset);
    }

    [Theory]
    [InlineData("(", ")")]
    [InlineData("[", "]")]
    [InlineData("{", "}")]
    [InlineData("!", "")]
    [InlineData("a = ", "")]
    [InlineData("function f() {", "}")]
    public void DeepNesting_ReportsAnError(string open, string close)
    {
        const int Depth = 100_000;
        var code = string.Concat(Enumerable.Repeat(open, Depth)) + "0" + string.Concat(Enumerable.Repeat(close, Depth));
        var exception = AssertSyntaxError(code);
        Assert.Contains("nested too deeply", exception.Message);
    }

    [Fact]
    public void LongFlatCode_DoesNotRecurse()
    {
        Parse("x = 0" + string.Concat(Enumerable.Repeat(" + 1", 100_000)) + ";" + string.Concat(Enumerable.Repeat("a.b", 1)) + string.Concat(Enumerable.Repeat(".c", 100_000)));
    }

    [Fact]
    public void DynamicFunction()
    {
        var (function, source) = JsParser.ParseDynamicFunction("a, b = 1", "return a + b;", isAsync: false, isGenerator: false);
        Assert.Equal("function anonymous(a, b = 1\n) {\nreturn a + b;\n}", source.Text);
        Assert.Equal(source.Text, GetText(source.Text, function));
        Assert.Null(function.Id);
        Assert.HasCount(2, function.Params);
        Assert.False(function.IsAsync);

        (function, source) = JsParser.ParseDynamicFunction("", "await x; yield y;", isAsync: true, isGenerator: true);
        Assert.Equal("async function* anonymous(\n) {\nawait x; yield y;\n}", source.Text);
        Assert.True(function.IsAsync);
        Assert.True(function.IsGenerator);
    }

    [Theory]
    [InlineData("/*", "*/){")]
    [InlineData("a){", "}; (function(")]
    [InlineData("", "}")]
    [InlineData("a, a", "'use strict';")]
    [InlineData("a = 1", "'use strict';")]
    [InlineData("", "super.x")]
    public void DynamicFunction_Errors(string parameters, string body)
    {
        Assert.Throws<JsSyntaxErrorException>(() => JsParser.ParseDynamicFunction(parameters, body, isAsync: false, isGenerator: false));
    }

    [Fact]
    public void Literals()
    {
        var program = Parse("0x1F; 0o17; 0b101; 1_000.5e1; 017; 08; 123n; 0x1fn; .5; 'a\\x41\\u{1F600}\\\r\nb'; /a[/]b/gimsuyd;");
        Assert.Equal(31d, ((NumericLiteral)GetExpression(program, 0)).Value);
        Assert.Equal(15d, ((NumericLiteral)GetExpression(program, 1)).Value);
        Assert.Equal(5d, ((NumericLiteral)GetExpression(program, 2)).Value);
        Assert.Equal(10005d, ((NumericLiteral)GetExpression(program, 3)).Value);
        Assert.Equal(15d, ((NumericLiteral)GetExpression(program, 4)).Value);
        Assert.Equal(8d, ((NumericLiteral)GetExpression(program, 5)).Value);
        Assert.Equal(123, (int)((BigIntLiteral)GetExpression(program, 6)).Value);
        Assert.Equal(31, (int)((BigIntLiteral)GetExpression(program, 7)).Value);
        Assert.Equal(0.5d, ((NumericLiteral)GetExpression(program, 8)).Value);
        Assert.Equal("aA\U0001F600b", ((StringLiteral)GetExpression(program, 9)).Value);
        var regex = (RegExpLiteral)GetExpression(program, 10);
        Assert.Equal("a[/]b", regex.Pattern);
        Assert.Equal("gimsuyd", regex.Flags);
    }

    [Fact]
    public void Templates()
    {
        var template = (TaggedTemplateExpression)GetExpression(Parse("tag`a\r\nb${x}\\u{110000}`"));
        Assert.Equal("a\nb", template.Quasi.Quasis[0].Cooked);
        Assert.Equal("a\nb", template.Quasi.Quasis[0].Raw);
        Assert.False(template.Quasi.Quasis[0].Tail);
        Assert.Null(template.Quasi.Quasis[1].Cooked);
        Assert.Equal("\\u{110000}", template.Quasi.Quasis[1].Raw);
        Assert.True(template.Quasi.Quasis[1].Tail);
    }

    [Fact]
    public void Identifiers()
    {
        var program = Parse("var \\u0061b\\u{63}, \u00E9t\u00E9, \U0001D49C;");
        var declaration = (VariableDeclaration)program.Body[0];
        Assert.Equal("abc", ((Identifier)declaration.Declarations[0].Id).Name);
        Assert.Equal("\u00E9t\u00E9", ((Identifier)declaration.Declarations[1].Id).Name);
        Assert.Equal("\U0001D49C", ((Identifier)declaration.Declarations[2].Id).Name);

        AssertSyntaxError("var v\\u0061r;");
        AssertSyntaxError("\\u0076ar x;");
        AssertSyntaxError("var a\\u002Db;");
    }

    [Fact]
    public void EvalOptions()
    {
        Assert.Throws<JsSyntaxErrorException>(() => JsParser.ParseScript(new SourceText("new.target"), ParserOptions.Script));
        JsParser.ParseScript(new SourceText("new.target"), new ParserOptions { AllowNewTarget = true });
        JsParser.ParseScript(new SourceText("super.x"), new ParserOptions { AllowSuperProperty = true });
        JsParser.ParseScript(new SourceText("super()"), new ParserOptions { AllowSuperCall = true });
        JsParser.ParseScript(new SourceText("this.#x"), new ParserOptions { PrivateNames = ["x"] });
        JsParser.ParseScript(new SourceText("class A { m() { return this.#x; } }"), new ParserOptions { PrivateNames = ["x"] });
        Assert.Throws<JsSyntaxErrorException>(() => JsParser.ParseScript(new SourceText("this.#y"), new ParserOptions { PrivateNames = ["x"] }));
        Assert.Throws<JsSyntaxErrorException>(() => JsParser.ParseScript(new SourceText("arguments"), new ParserOptions { InClassFieldInitializer = true }));
        JsParser.ParseScript(new SourceText("(function () { arguments; })"), new ParserOptions { InClassFieldInitializer = true });
    }
}
