namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A realm (https://tc39.es/ecma262/#sec-code-realms): a global object, its global environment and the intrinsics.</summary>
public sealed partial class JsRealm
{
    internal JsRealm(JsEngine engine)
    {
        Engine = engine;
        var previousRealm = engine.CurrentRealm;
        engine.CurrentRealm = this;
        try
        {
            Intrinsics = new Intrinsics(this);
            GlobalObject = new JsObject(Intrinsics.ObjectPrototype);
            GlobalThis = new JsValue(GlobalObject);
            GlobalEnvironment = new Interpreter.GlobalEnvironment(this);
            Intrinsics.Initialize();
            Intrinsics.DefineGlobalBindings(GlobalObject);
        }
        finally
        {
            engine.CurrentRealm = previousRealm ?? this;
        }
    }

    /// <summary>Gets the engine (the agent) of the realm.</summary>
    public JsEngine Engine { get; }

    /// <summary>Gets the global object ([[GlobalObject]]).</summary>
    public JsObject GlobalObject { get; }

    /// <summary>Gets the <c>this</c> value of the global code ([[GlobalThisValue]] of the global environment).</summary>
    public JsValue GlobalThis { get; }

    internal Intrinsics Intrinsics { get; }

    /// <summary>Gets the global environment of the realm.</summary>
    internal Interpreter.GlobalEnvironment GlobalEnvironment { get; }

    /// <summary>Gets the template objects of the tagged templates ([[TemplateMap]]).</summary>
    internal System.Runtime.CompilerServices.ConditionalWeakTable<Compiler.TemplateSite, JsArray> TemplateRegistry { get; } = [];

    /// <summary>Runs a script in this realm (ScriptEvaluation) and returns its completion value.</summary>
    /// <param name="code">The source text of the script.</param>
    /// <param name="url">The URL (or file name) of the script, used in the stack traces and the error locations.</param>
    /// <exception cref="JsException">The script threw an exception (a SyntaxError for an invalid script).</exception>
    public JsValue Execute(string code, string? url = null)
    {
        using var scope = Engine.Enter();
        var source = new Text.SourceText(code, url);
        return Run(source, Compile(source), hostDefined: null, mutedErrors: false);
    }

    /// <summary>Runs a prepared script in this realm (ScriptEvaluation) and returns its completion value.</summary>
    /// <param name="script">The script.</param>
    /// <param name="hostDefined">The data of the host associated with the script ([[HostDefined]]).</param>
    /// <param name="mutedErrors">Whether the errors of the script are muted (a cross-origin script fetched without CORS).</param>
    /// <exception cref="JsException">The script threw an exception.</exception>
    public JsValue Execute(JsPreparedScript script, object? hostDefined = null, bool mutedErrors = false)
    {
        ArgumentNullException.ThrowIfNull(script);
        using var scope = Engine.Enter();
        return Run(script.Source, script.Template, hostDefined, mutedErrors);
    }

    private JsValue Run(Text.SourceText source, Compiler.FunctionTemplate template, object? hostDefined, bool mutedErrors)
    {
        var script = new JsScript(this, source, hostDefined, mutedErrors);
        var frame = new Interpreter.Frame(template, this, GlobalEnvironment, [])
        {
            Script = script,
            This = GlobalThis,
        };

        return Interpreter.JsInterpreter.Run(frame);
    }

    private Compiler.FunctionTemplate Compile(Text.SourceText source)
    {
        var previousRealm = Engine.CurrentRealm;
        Engine.CurrentRealm = this;
        try
        {
            Ast.Program program;
            try
            {
                program = Parsing.JsParser.ParseScript(source, Parsing.ParserOptions.Script);
            }
            catch (JsSyntaxErrorException ex)
            {
                throw ThrowHelper.CreateException(ErrorType.SyntaxError, ex.Message);
            }

            return Compiler.CodeGenerator.CompileProgram(program, source, Compiler.ProgramKind.Script, isStrict: false);
        }
        finally
        {
            Engine.CurrentRealm = previousRealm;
        }
    }

    /// <summary>Parses a module (ParseModule). The module is then loaded, linked and evaluated with the methods of <see cref="JsModule"/>.</summary>
    /// <param name="code">The source text of the module.</param>
    /// <param name="url">The URL (or file name) of the module, used to resolve its imports and in the stack traces.</param>
    /// <param name="hostDefined">The data of the host associated with the module.</param>
    /// <exception cref="JsException">The module is not valid (a SyntaxError).</exception>
    public JsSourceTextModule ParseModule(string code, string? url = null, object? hostDefined = null)
    {
        using var scope = Engine.Enter();
        var previousRealm = Engine.CurrentRealm;
        Engine.CurrentRealm = this;
        try
        {
            return JsSourceTextModule.Parse(this, new Text.SourceText(code, url), hostDefined);
        }
        finally
        {
            Engine.CurrentRealm = previousRealm;
        }
    }

    /// <summary>Creates a JSON module (ParseJSONModule): a module whose default export is the parsed JSON value.</summary>
    /// <exception cref="JsException">The text is not valid JSON (a SyntaxError).</exception>
    public JsSyntheticModule CreateJsonModule(string json, string? url = null, object? hostDefined = null)
    {
        using var scope = Engine.Enter();
        var previousRealm = Engine.CurrentRealm;
        Engine.CurrentRealm = this;
        try
        {
            var value = JsonParser.Parse(this, json, trackSource: false, out _);
            return new JsSyntheticModule(this, url, hostDefined, ["default"], module => module.SetExport("default", value));
        }
        finally
        {
            Engine.CurrentRealm = previousRealm;
        }
    }

    /// <summary>Creates a synthetic module whose exports are set by <paramref name="evaluate"/> with <see cref="JsSyntheticModule.SetExport"/> when the module is evaluated.</summary>
    public JsSyntheticModule CreateSyntheticModule(IEnumerable<string> exportNames, Action<JsSyntheticModule> evaluate, string? url = null, object? hostDefined = null)
    {
        ArgumentNullException.ThrowIfNull(exportNames);
        ArgumentNullException.ThrowIfNull(evaluate);
        return new JsSyntheticModule(this, url, hostDefined, exportNames, evaluate);
    }

    /// <summary>Evaluates code in this realm as an indirect <c>eval</c> and returns its completion value.</summary>
    /// <exception cref="JsException">The code threw an exception (a SyntaxError for invalid code).</exception>
    public JsValue Evaluate(string code)
    {
        using var scope = Engine.Enter();
        var previousRealm = Engine.CurrentRealm;
        Engine.CurrentRealm = this;
        try
        {
            return Interpreter.JsInterpreter.IndirectEval(this, code);
        }
        finally
        {
            Engine.CurrentRealm = previousRealm;
        }
    }

    /// <summary>Creates a native error object of this realm.</summary>
    internal JsObject CreateError(ErrorType type, string message)
    {
        var error = new JsErrorObject(Intrinsics.GetErrorPrototype(type));
        error.DefineBuiltIn("message", message);
        error.CaptureStackTrace(Engine);
        return error;
    }

    /// <summary>CreateBuiltinFunction: a function of this realm implemented by a .NET callback.</summary>
    internal JsBuiltinFunction CreateBuiltinFunction(PropertyKey name, int length, BuiltinFunctionCallback callback, bool isConstructor = false, JsObject? prototype = null, string? prefix = null)
    {
        var function = new JsBuiltinFunction(this, callback, isConstructor, prototype ?? Intrinsics.FunctionPrototype);
        function.SetFunctionLength(length);
        function.SetFunctionName(name, prefix);
        return function;
    }

    /// <summary>Defines a built-in method (writable, configurable, not enumerable).</summary>
    internal JsBuiltinFunction DefineMethod(JsObject target, PropertyKey name, int length, BuiltinFunctionCallback callback, PropertyFlags flags = PropertyFlags.BuiltIn)
    {
        var function = CreateBuiltinFunction(name, length, callback);
        target.DefineBuiltIn(name, function, flags);
        return function;
    }

    /// <summary>Defines a built-in accessor property; the getter is named "get name" and the setter "set name".</summary>
    internal void DefineAccessor(JsObject target, PropertyKey name, BuiltinFunctionCallback? getter, BuiltinFunctionCallback? setter, PropertyFlags flags = PropertyFlags.Configurable)
    {
        var getterFunction = getter is null ? null : CreateBuiltinFunction(name, 0, getter, prefix: "get");
        var setterFunction = setter is null ? null : CreateBuiltinFunction(name, 1, setter, prefix: "set");
        target.DefineBuiltInAccessor(name, getterFunction, setterFunction, flags);
    }
}
