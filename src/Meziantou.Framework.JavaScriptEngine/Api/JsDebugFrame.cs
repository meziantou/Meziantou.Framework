using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JavaScript frame of the stack of a statement given to a <see cref="JsDebugger"/>: a function call, or the top-level
/// code of a script, a module or eval code (<see cref="JsDebugEvent.GetCallStack"/>). It can be used until the callback
/// that received the event returns.
/// </summary>
public sealed class JsDebugFrame
{
    private readonly JsEngine _engine;
    private readonly Frame _frame;
    private readonly long _version;

    internal JsDebugFrame(JsEngine engine, Frame frame, long version, bool isTop)
    {
        _engine = engine;
        _frame = frame;
        _version = version;

        // A caller is at the instruction after its call
        Location = GetLocation(frame.Code, isTop ? frame.Pc : Math.Max(0, frame.Pc - 1));
        FunctionName = frame.Function?.InitialName is { Length: > 0 } name ? name : null;
        Function = frame.Function;
        Realm = frame.Realm;
    }

    /// <summary>Gets the name of the function of the frame, or null for the top-level code and the anonymous functions.</summary>
    public string? FunctionName { get; }

    /// <summary>Gets the function of the frame, or null for the top-level code of a script, a module or eval code.</summary>
    public JsFunction? Function { get; }

    /// <summary>Gets the realm of the code of the frame.</summary>
    public JsRealm Realm { get; }

    /// <summary>Gets the position of the statement of the frame (the paused statement), or of the call that the frame is running.</summary>
    public JsSourceLocation Location { get; }

    /// <summary>
    /// Gets the scopes of the frame at its position, the innermost first: the blocks, the function (<see cref="JsDebugScopeKind.Local"/>),
    /// the enclosing functions (<see cref="JsDebugScopeKind.Closure"/>), then the global scope. The scopes that have no
    /// variable are omitted, except the global object.
    /// </summary>
    /// <exception cref="InvalidOperationException">The callback that received the event returned.</exception>
    public IReadOnlyList<JsDebugScope> GetScopes()
    {
        EnsureActive();
        var result = new List<JsDebugScope>();

        // The environments from the [[Environment]] of the function are the ones of the enclosing code
        var closureEnvironment = _frame.Function is JsClosure closure ? closure.Environment : null;
        var isInClosure = false;

        // The environments of a function (its lexical declarations, its vars, its parameters) are one scope
        List<JsDebugVariable>? functionVariables = null;
        for (var environment = _frame.Environment; environment is not null; environment = environment.Outer)
        {
            if (ReferenceEquals(environment, closureEnvironment))
            {
                // An arrow function without parameters has no environment for them
                FlushFunctionScope(result, ref functionVariables, isInClosure);
                isInClosure = true;
            }

            if (environment is not DeclarativeEnvironment { Info: { Kind: ScopeKind.Function or ScopeKind.FunctionBody } or { IsFunctionLexicalScope: true } })
            {
                FlushFunctionScope(result, ref functionVariables, isInClosure);
            }

            switch (environment)
            {
                case DeclarativeEnvironment { Info: { Kind: ScopeKind.Function or ScopeKind.FunctionBody } or { IsFunctionLexicalScope: true } } declarative:
                    functionVariables ??= [];
                    functionVariables.InsertRange(0, GetVariables(declarative));

                    // The scope of the parameters is the outermost environment of a function
                    if (declarative.Info.Kind is ScopeKind.Function)
                    {
                        FlushFunctionScope(result, ref functionVariables, isInClosure);
                    }

                    break;

                case DeclarativeEnvironment declarative:
                    var kind = declarative.Info.Kind switch
                    {
                        ScopeKind.FunctionName => JsDebugScopeKind.Closure,
                        ScopeKind.Catch => JsDebugScopeKind.Catch,
                        ScopeKind.Module => JsDebugScopeKind.Module,
                        ScopeKind.Eval => JsDebugScopeKind.Eval,
                        _ => JsDebugScopeKind.Block,
                    };

                    AddScope(result, kind, GetVariables(declarative));
                    break;

                case ObjectEnvironment objectEnvironment:
                    result.Add(new JsDebugScope(JsDebugScopeKind.With, [], objectEnvironment.BindingObject));
                    break;

                case GlobalEnvironment global:
                    if (global.LexicalBindings.Count > 0)
                    {
                        var lexical = new JsDebugVariable[global.LexicalBindings.Count];
                        var index = 0;
                        foreach (var (name, binding) in global.LexicalBindings)
                        {
                            lexical[index++] = CreateVariable(name, binding.Value);
                        }

                        result.Add(new JsDebugScope(JsDebugScopeKind.Script, lexical, bindingObject: null));
                    }

                    result.Add(new JsDebugScope(JsDebugScopeKind.Global, [], global.GlobalObject));
                    break;
            }
        }

        FlushFunctionScope(result, ref functionVariables, isInClosure);
        return result;

        static void FlushFunctionScope(List<JsDebugScope> result, ref List<JsDebugVariable>? variables, bool isInClosure)
        {
            if (variables is not null)
            {
                AddScope(result, isInClosure ? JsDebugScopeKind.Closure : JsDebugScopeKind.Local, [.. variables]);
                variables = null;
            }
        }

        static void AddScope(List<JsDebugScope> result, JsDebugScopeKind kind, JsDebugVariable[] variables)
        {
            if (variables.Length > 0)
            {
                result.Add(new JsDebugScope(kind, variables, bindingObject: null));
            }
        }
    }

    /// <summary>
    /// Evaluates code at the position of the frame, like a direct <c>eval</c> written there: it reads and changes the
    /// variables of the frame, and uses its <c>this</c>, <c>new.target</c> and <c>super</c>. The code raises no event of
    /// the debugger, and the host is not asked whether it can be compiled (<see cref="JsEngineOptions.AllowStringCompilation"/>, <see cref="JsHost.EnsureCanCompileStrings"/>).
    /// </summary>
    /// <param name="code">The code, e.g. an expression.</param>
    /// <returns>The completion value of the code.</returns>
    /// <exception cref="InvalidOperationException">The callback that received the event returned.</exception>
    /// <exception cref="JsException">The code throws, or it has a syntax error.</exception>
    public JsValue Evaluate(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        EnsureActive();
        return JsInterpreter.EvaluateInFrame(_frame, code);
    }

    internal static JsSourceLocation GetLocation(FunctionTemplate code, int pc)
    {
        var offset = code.GetSourceOffset(pc);
        var (line, column) = code.Source.GetLineAndColumn(Math.Max(0, offset));
        return new JsSourceLocation(code.Source.Url, line, column);
    }

    private static JsDebugVariable[] GetVariables(DeclarativeEnvironment environment)
    {
        var info = environment.Info;
        var result = new List<JsDebugVariable>(info.SlotCount);
        for (var i = 0; i < info.SlotCount; i++)
        {
            // The hidden slots, the special bindings (this, new.target, super...) and the private names are not variables
            if (info.Names[i] is not { } name || name is "this" or "new.target" or ['%' or '#', ..])
                continue;

            var value = environment.Slots[i];
            if ((info.Flags[i] & ScopeSlotFlags.Import) != 0 && value.Cell is ImportBindingCell cell)
            {
                value = cell.Module.Environment is { } moduleEnvironment ? moduleEnvironment.Slots[cell.Slot] : JsValue.Hole;
            }

            result.Add(CreateVariable(name, value));
        }

        if (environment.EvalVars is { } evalVars)
        {
            foreach (var (name, value) in evalVars)
            {
                result.Add(CreateVariable(name, value));
            }
        }

        return [.. result];
    }

    private static JsDebugVariable CreateVariable(string name, JsValue value) => value.IsHole ? new JsDebugVariable(name, JsValue.Undefined, isInitialized: false) : new JsDebugVariable(name, value, isInitialized: true);

    private void EnsureActive()
    {
        if (!_engine.IsDebugEventActive(_version))
            throw new InvalidOperationException("The frame can only be used while the callback of the debugger that received it runs");
    }
}
