using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Parsing;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal static partial class JsInterpreter
{
    /// <summary>A call whose callee is named eval: a direct eval when the callee is %eval% of the current realm.</summary>
    private static JsValue CallEval(Frame frame, JsValue function, JsValue thisValue, JsValue[] arguments, EvalContext context)
    {
        if (function.Cell is not JsObject { IsCallable: true } callable)
        {
            ThrowNotCallable(frame, function);
            return default;
        }

        if (!ReferenceEquals(callable, frame.Realm.Intrinsics.Eval))
            return callable.Call(thisValue, arguments);

        if (arguments.Length == 0)
            return default;

        return PerformEval(frame.Realm, arguments[0], frame, context);
    }

    /// <summary>The indirect eval (a call of %eval% that is not a direct eval).</summary>
    public static JsValue IndirectEval(JsRealm realm, JsValue argument) => PerformEval(realm, argument, caller: null, context: null);

    /// <summary>PerformEval(x, strictCaller, direct) (https://tc39.es/ecma262/#sec-performeval).</summary>
    private static JsValue PerformEval(JsRealm realm, JsValue argument, Frame? caller, EvalContext? context)
    {
        var code = realm.Engine.HostGetCodeForEval(argument);
        if (code is null)
            return argument;

        realm.Engine.HostEnsureCanCompileStrings(realm, [], code, code, caller is not null ? JsCompilationType.DirectEval : JsCompilationType.IndirectEval, [], argument);
        var options = context is null
            ? ParserOptions.Script
            : new ParserOptions
            {
                IsStrict = context.IsStrict,
                AllowNewTarget = context.AllowNewTarget,
                AllowSuperProperty = context.AllowSuperProperty,
                AllowSuperCall = context.AllowSuperCall,
                InClassFieldInitializer = context.InClassFieldInitializer,
                PrivateNames = context.PrivateNames,
            };

        var source = new SourceText(code, GetEvalUrl(code));
        Ast.Program program;
        FunctionTemplate template;
        try
        {
            program = JsParser.ParseScript(source, options);
            template = CodeGenerator.CompileProgram(program, source, ProgramKind.Eval, program.IsStrict || (context?.IsStrict ?? false), context);
        }
        catch (JsSyntaxErrorException ex)
        {
            throw ThrowHelper.CreateException(ErrorType.SyntaxError, ex.Message);
        }

        var environment = caller?.Environment ?? realm.GlobalEnvironment;
        var frame = new Frame(template, realm, environment, [])
        {
            Script = caller?.Script,
            This = caller?.This ?? realm.GlobalThis,
        };

        return RunWithDepth(realm.Engine, frame);
    }

    /// <summary>
    /// PerformShadowRealmEval(sourceText, callerRealm, evalRealm) (https://tc39.es/proposal-shadowrealm/#sec-performshadowrealmeval):
    /// the code runs as an indirect eval of the ShadowRealm (its lexical declarations are local to the evaluation). A
    /// SyntaxError is thrown in the caller realm; an exception of the code becomes a TypeError of the caller realm.
    /// </summary>
    public static JsValue PerformShadowRealmEval(JsValue sourceText, JsRealm callerRealm, JsRealm evalRealm)
    {
        var engine = evalRealm.Engine;
        var code = sourceText.AsString();
        engine.HostEnsureCanCompileStrings(evalRealm, [], code, code, JsCompilationType.IndirectEval, [], sourceText);

        var source = new SourceText(code, GetEvalUrl(code));
        FunctionTemplate template;
        try
        {
            // The script goal: new.target, super() and super properties are early errors
            var program = JsParser.ParseScript(source, ParserOptions.Script);
            template = CodeGenerator.CompileProgram(program, source, ProgramKind.Eval, program.IsStrict);
        }
        catch (JsSyntaxErrorException ex)
        {
            throw ThrowHelper.CreateException(ErrorType.SyntaxError, ex.Message);
        }

        var frame = new Frame(template, evalRealm, evalRealm.GlobalEnvironment, [])
        {
            This = evalRealm.GlobalThis,
        };

        JsValue result;
        try
        {
            result = RunWithDepth(engine, frame);
        }
        catch (JsException ex)
        {
            throw JsWrappedFunction.CreateTypeErrorCopy(callerRealm, ex.Value);
        }

        return JsWrappedFunction.GetWrappedValue(callerRealm, result);
    }

    /// <summary>The URL of eval code: the //# sourceURL= comment of the code, if any.</summary>
    private static string? GetEvalUrl(string code)
    {
        const string Marker = "//# sourceURL=";
        var index = code.LastIndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var start = index + Marker.Length;
        var end = start;
        while (end < code.Length && !char.IsWhiteSpace(code[end]))
        {
            end++;
        }

        return end > start ? code[start..end] : null;
    }

    /// <summary>The steps of CreateDynamicFunction after the source text is assembled: parse, compile, create the function.</summary>
    public static JsObject CreateDynamicFunction(JsBuiltinFunction constructor, JsObject newTarget, DynamicFunctionKind kind, DynamicFunctionSource source, ReadOnlySpan<JsValue> arguments)
    {
        var realm = constructor.Realm;
        var parameterArguments = arguments.Length > 1 ? arguments[..^1].ToArray() : [];
        var bodyArgument = arguments.Length > 0 ? arguments[^1] : default;
        realm.Engine.HostEnsureCanCompileStrings(realm, source.ParameterStrings, source.Body, source.SourceText, JsCompilationType.Function, parameterArguments, bodyArgument);
        var isAsync = kind is DynamicFunctionKind.Async or DynamicFunctionKind.AsyncGenerator;
        var isGenerator = kind is DynamicFunctionKind.Generator or DynamicFunctionKind.AsyncGenerator;
        Ast.FunctionNode function;
        SourceText sourceText;
        FunctionTemplate template;
        try
        {
            (function, sourceText) = JsParser.ParseDynamicFunction(source.Parameters, source.Body, isAsync, isGenerator);
            template = CodeGenerator.CompileDynamicFunction(function, sourceText);
        }
        catch (JsSyntaxErrorException ex)
        {
            throw ThrowHelper.CreateException(ErrorType.SyntaxError, ex.Message);
        }

        var fallback = kind switch
        {
            DynamicFunctionKind.Generator => (Func<Intrinsics, JsObject>)(static intrinsics => intrinsics.GeneratorFunctionPrototype),
            DynamicFunctionKind.Async => static intrinsics => intrinsics.AsyncFunctionPrototype,
            DynamicFunctionKind.AsyncGenerator => static intrinsics => intrinsics.AsyncGeneratorFunctionPrototype,
            _ => static intrinsics => intrinsics.FunctionPrototype,
        };

        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, fallback);
        // OrdinaryFunctionCreate: the [[ScriptOrModule]] of the function is the active script or module (the code that
        // created it, not the code that calls it: import() resolves its specifiers from it)
        var closure = CreateClosure(realm, template, realm.GlobalEnvironment, realm.Engine.FindActiveScriptOrModule());
        closure.SetPrototypeDirect(prototype);
        return closure;
    }
}
