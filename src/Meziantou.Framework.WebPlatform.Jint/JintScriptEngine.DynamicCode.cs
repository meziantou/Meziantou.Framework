using System.Reflection;
using System.Runtime.CompilerServices;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Jint;

// The compilation of strings (https://w3c.github.io/webappsec-csp/#can-compile-strings): the HostEnsureCanCompileStrings
// hook of Jint (JsHost.EnsureCanCompileStrings) does not receive the code, which the content security policies need for
// the sample of the violations and Trusted Types for the default policy (and eval(TrustedScript), HostGetCodeForEval, is
// not supported by Jint). Once a realm of the engine has a CSP list, eval and the Function, AsyncFunction,
// GeneratorFunction and AsyncGeneratorFunction constructors of the realms are replaced by hooks that run the checks with
// the code, then call the intrinsics of Jint (whose own call of the hook is skipped). The hooks are installed when a realm
// is created, or when a script runs in a realm whose global object got a policy afterwards (e.g. from a meta element);
// the compilations through a reference to an intrinsic taken before are still checked by JsHost, without the code. The
// realms of the engines without content security policy nor Trusted Types are not changed.
public partial class JintScriptEngine
{
    private static readonly FieldInfo CallStackField = typeof(Engine).GetField("CallStack", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo ExecutionContextGetter = typeof(Engine).GetProperty("ExecutionContext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetMethod!;
    private static readonly FieldInfo ExecutionContextStrictField = Type.GetType("Jint.Runtime.Environments.ExecutionContext, Jint", throwOnError: true)!.GetField("Strict")!;
    private static readonly MethodInfo CallStackTryPeekMethod = Type.GetType("Jint.Runtime.CallStack.JintCallStack, Jint", throwOnError: true)!.GetMethod("TryPeek")!;
    private static readonly FieldInfo CallStackElementFunctionField = Type.GetType("Jint.Runtime.CallStack.CallStackElement, Jint", throwOnError: true)!.GetField("Function")!;
    private static readonly FieldInfo CallStackElementExpressionField = Type.GetType("Jint.Runtime.CallStack.CallStackElement, Jint", throwOnError: true)!.GetField("Expression")!;

    /// <summary>The global objects whose realm has the hooks of the dynamic code.</summary>
    private readonly ConditionalWeakTable<GlobalScope, object> _dynamicCodeHooks = [];

    /// <summary>The hooks of <c>eval</c>.</summary>
    private readonly ConditionalWeakTable<JsValue, object> _evalHooks = [];

    /// <summary>Whether a realm of the engine has a CSP list or used Trusted Types: the hooks are installed in all its realms.</summary>
    private bool _dynamicCodeHooksEnabled;

    /// <summary>The host of Jint, whose HostEnsureCanCompileStrings hook skips the compilations checked by the hooks.</summary>
    internal JsHost Host { get; }

    /// <summary><c>eval()</c> evaluates the data of a <see cref="TrustedScript"/> (HostGetCodeForEval), also without content security policy.</summary>
    internal override void OnTrustedTypePolicyFactoryCreated(GlobalScope global)
    {
        if (Agent.TryGetRealm(global) is { } realm)
        {
            InstallDynamicCodeHooks(realm, force: true);
        }
    }

    /// <summary>
    /// Installs the hooks of <c>eval</c> and of the <c>Function</c> constructors in the realms of the engine once the global
    /// object of one of them has a CSP list (or Trusted Types directives); nothing is done before. All the realms get the
    /// hooks: the built-in functions of Jint run in the realm of their caller, so the HostEnsureCanCompileStrings hook of
    /// Jint would check the policies of the caller instead of the ones of the realm of the function (e.g. <c>frame.Function</c>).
    /// </summary>
    private void InstallDynamicCodeHooks(WebIdlRealm<JintAdapter, JsValue> realm, bool force = false)
    {
        if (!_dynamicCodeHooksEnabled)
        {
            var document = realm.GlobalScope.EnvironmentDocument;
            if (!force && document.ContentSecurityPoliciesOrNull is null && document.TrustedTypesDirectivesOrNull is null)
                return;

            _dynamicCodeHooksEnabled = true;
            foreach (var otherRealm in Agent.Realms.ToArray())
            {
                InstallDynamicCodeHooksCore(otherRealm);
            }
        }

        InstallDynamicCodeHooksCore(realm);
    }

    private void InstallDynamicCodeHooksCore(WebIdlRealm<JintAdapter, JsValue> realm)
    {
        if (_dynamicCodeHooks.TryGetValue(realm.GlobalScope, out _))
            return;

        _dynamicCodeHooks.Add(realm.GlobalScope, realm);
        realm.Run(() =>
        {
            var intrinsics = Engine.Intrinsics;
            var global = realm.Global.AsObject();

            // eval: the global property (https://tc39.es/ecma262/#sec-eval-x), unless the scripts replaced it
            var evalIntrinsic = intrinsics.Eval;
            JsValue? evalHook = null;
            evalHook = new ClrFunction(Engine, "eval", (_, arguments) => CallEvalHook(realm, evalIntrinsic, evalHook!, arguments), 1, PropertyFlag.Configurable);
            _evalHooks.Add(evalHook, evalHook);
            if (ReferenceEquals(global.Get("eval"), evalIntrinsic))
            {
                global.DefineOwnProperty("eval", new PropertyDescriptor(evalHook, writable: true, enumerable: false, configurable: true));
            }

            // The Function constructors: the global property Function, and the constructor property of their prototype
            // (AsyncFunction, GeneratorFunction and AsyncGeneratorFunction are not global properties)
            var functionHook = ReplaceConstructor(intrinsics.Function, "Function", "function", parent: null);
            ReplaceConstructor((Constructor)GetAsyncFunctionConstructor(intrinsics), "AsyncFunction", "async function", functionHook);
            ReplaceConstructor((Constructor)GetGeneratorFunctionConstructor(intrinsics), "GeneratorFunction", "function*", functionHook);
            ReplaceConstructor((Constructor)GetAsyncGeneratorFunctionConstructor(intrinsics), "AsyncGeneratorFunction", "async function*", functionHook);
            if (ReferenceEquals(global.Get("Function"), intrinsics.Function))
            {
                global.DefineOwnProperty("Function", new PropertyDescriptor(functionHook, writable: true, enumerable: false, configurable: true));
            }

            return JsValue.Undefined;
        });

        JintDynamicFunctionConstructor ReplaceConstructor(Constructor intrinsic, string name, string prefix, JintDynamicFunctionConstructor? parent)
        {
            var prototype = intrinsic.Get("prototype").AsObject();
            var hook = new JintDynamicFunctionConstructor(this, realm, intrinsic, name, prefix);
            hook.DefineOwnProperty("length", new PropertyDescriptor(JsNumber.Create(1), PropertyFlag.Configurable));
            hook.DefineOwnProperty("prototype", new PropertyDescriptor(prototype, PropertyFlag.AllForbidden));
            if (parent is not null)
            {
                // The [[Prototype]] of the constructors of the other kinds of functions is %Function%
                hook.Prototype = parent;
            }

            var constructorProperty = prototype.GetOwnProperty("constructor");
            if (ReferenceEquals(constructorProperty.Value, intrinsic))
            {
                prototype.DefineOwnProperty("constructor", new PropertyDescriptor(hook, constructorProperty.Writable, constructorProperty.Enumerable, constructorProperty.Configurable));
            }

            return hook;
        }
    }

    /// <summary>
    /// The compilation of a string by Jint that the hooks did not check (e.g. <c>eval</c> called through a reference taken
    /// before the hooks were installed): the code is unknown. The realm is the one of the code (the realm of the caller for
    /// a ShadowRealm, which has no global object of the library).
    /// </summary>
    internal void EnsureCanCompileUnknownStrings(Realm callerRealm, Realm evalRealm)
    {
        if ((Agent.GetRealmByGlobal(evalRealm.GlobalObject) ?? Agent.GetRealmByGlobal(callerRealm.GlobalObject)) is not { } realm)
            return;

        // The next compilations of the realm are checked with their code
        InstallDynamicCodeHooks(realm);
        try
        {
            ContentSecurityPolicyEval.EnsureCanCompileStrings(realm.GlobalScope, [], "", codeString: null, StringCompilationType.Eval, [], bodyArg: null);
        }
        catch (EvalErrorException ex)
        {
            throw realm.ToJsException(ex);
        }
    }

    /// <summary>
    /// The hook of <c>eval</c> (https://tc39.es/ecma262/#sec-eval-x and https://tc39.es/ecma262/#sec-performeval, with
    /// https://tc39.es/proposal-dynamic-code-brand-checks/): HostGetCodeForEval gives the data of a <see cref="TrustedScript"/>,
    /// HostEnsureCanCompileStrings checks the code, then the code is evaluated by PerformEval of Jint. A call of the hook is a
    /// direct eval when its callee is the identifier <c>eval</c> (Jint only recognizes its intrinsic): PerformEval then uses
    /// the running execution context, which is the one of the caller (the functions of the host do not create an execution context).
    /// </summary>
    private JsValue CallEvalHook(WebIdlRealm<JintAdapter, JsValue> realm, EvalFunction evalIntrinsic, JsValue evalHook, JsValue[] arguments)
    {
        var x = arguments.Length > 0 ? arguments[0] : JsValue.Undefined;
        var direct = IsDirectEvalCall(realm, evalHook);

        // HostGetCodeForEval (https://html.spec.whatwg.org/multipage/webappapis.html#hostgetcodeforeval(argument))
        string code;
        TrustedScript? trustedScript = null;
        if (x.IsString())
        {
            code = x.AsString();
        }
        else if (realm.Unwrap(x) is TrustedScript script)
        {
            trustedScript = script;
            code = script.Data;
        }
        else
        {
            return x;
        }

        if (!Host.StringCompilationChecked)
        {
            try
            {
                ContentSecurityPolicyEval.EnsureCanCompileStrings(realm.GlobalScope, [], code, code, StringCompilationType.Eval, [], trustedScript);
            }
            catch (EvalErrorException ex)
            {
                throw realm.ToJsException(ex);
            }
        }

        var strictCaller = direct && (bool)ExecutionContextStrictField.GetValue(ExecutionContextGetter.Invoke(Engine, parameters: null))!;
        return Host.RunTrustedCompilation(() => PerformEval(evalIntrinsic, JsString.Create(code), GetFunctionRealm(evalIntrinsic), strictCaller, direct));
    }

    /// <summary>
    /// Whether the running call of the hook of <c>eval</c> is a direct eval (https://tc39.es/ecma262/#sec-function-calls-runtime-semantics-evaluation):
    /// the callee of the call is the identifier <c>eval</c>, in the realm of the hook. An optional call (<c>eval?.(x)</c>) is
    /// not distinguished.
    /// </summary>
    private bool IsDirectEvalCall(WebIdlRealm<JintAdapter, JsValue> realm, JsValue evalHook)
    {
        if (!realm.Adapter.IsCurrentRealm(realm.EngineRealm))
            return false;

        // The top of the call stack of Jint is the call of the hook, with the expression of its callee
        return GetCallStackTop() is ({ } function, Acornima.Ast.Identifier { Name: "eval" }) && ReferenceEquals(function, evalHook);
    }

    /// <summary>The function of the innermost call of the call stack of Jint, and the expression of its callee (null when it is called by the host).</summary>
    private (object? Function, Acornima.Ast.Expression? Callee) GetCallStackTop()
    {
        var arguments = new object?[1];
        if (!(bool)CallStackTryPeekMethod.Invoke(CallStackField.GetValue(Engine), arguments)! || arguments[0] is not { } element)
            return default;

        var expression = CallStackElementExpressionField.GetValue(element);
        return (CallStackElementFunctionField.GetValue(element), expression is null ? null : GetJintExpressionNode(expression));
    }

    /// <summary>
    /// The callee of the running call of a hook of <c>eval</c> or of a <c>Function</c> constructor (the location of the
    /// violations of their compilation), or null.
    /// </summary>
    private Acornima.Ast.Expression? GetDynamicCodeCallee()
    {
        if (!_dynamicCodeHooksEnabled)
            return null;

        var (function, callee) = GetCallStackTop();
        return function is JintDynamicFunctionConstructor || (function is JsValue value && _evalHooks.TryGetValue(value, out _)) ? callee : null;
    }

    /// <summary>
    /// The hook of a <c>Function</c> constructor (https://tc39.es/ecma262/#sec-createdynamicfunction, with
    /// https://tc39.es/proposal-dynamic-code-brand-checks/): the arguments are converted to strings (the data of a
    /// <see cref="TrustedScript"/>, HostGetCodeForEval, else ToString), HostEnsureCanCompileStrings checks the source text of
    /// the function, then the intrinsic constructor of Jint creates the function from the strings.
    /// </summary>
    internal ObjectInstance ConstructDynamicFunction(JintDynamicFunctionConstructor constructor, JsValue[] arguments, JsValue newTarget)
    {
        var realm = constructor.WebIdlRealm;
        var parameterCount = Math.Max(arguments.Length - 1, 0);
        var parameterStrings = new string[parameterCount];
        var parameterArgs = new TrustedScript?[parameterCount];
        var stringArguments = new JsValue[arguments.Length];
        for (var i = 0; i < parameterCount; i++)
        {
            (parameterStrings[i], parameterArgs[i]) = GetCodeString(realm, arguments[i]);
            stringArguments[i] = JsString.Create(parameterStrings[i]);
        }

        var (bodyString, bodyArg) = arguments.Length == 0 ? ("", null) : GetCodeString(realm, arguments[^1]);
        if (arguments.Length > 0)
        {
            stringArguments[^1] = JsString.Create(bodyString);
        }

        if (!Host.StringCompilationChecked)
        {
            var codeString = constructor.Prefix + " anonymous(" + string.Join(',', parameterStrings) + "\n) {\n" + bodyString + "\n}";
            try
            {
                ContentSecurityPolicyEval.EnsureCanCompileStrings(realm.GlobalScope, parameterStrings, bodyString, codeString, StringCompilationType.Function, parameterArgs, bodyArg);
            }
            catch (EvalErrorException ex)
            {
                throw realm.ToJsException(ex);
            }
        }

        var target = ReferenceEquals(newTarget, constructor) || newTarget.IsUndefined() ? constructor.Intrinsic : newTarget;
        return Host.RunTrustedCompilation(() => constructor.Intrinsic.Construct(stringArguments, target));

        static (string Code, TrustedScript? TrustedScript) GetCodeString(WebIdlRealm<JintAdapter, JsValue> realm, JsValue value)
            => realm.Unwrap(value) is TrustedScript trustedScript ? (trustedScript.Data, trustedScript) : (TypeConverter.ToString(value), null);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "PerformEval")]
    private static safe extern JsValue PerformEval(EvalFunction eval, JsValue x, Realm callerRealm, bool strictCaller, bool direct);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_realm")]
    private static safe extern ref Realm GetFunctionRealm(Function function);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_expression")]
    private static safe extern ref Acornima.Ast.Expression GetJintExpressionNode([UnsafeAccessorType("Jint.Runtime.Interpreter.Expressions.JintExpression, Jint")] object expression);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_AsyncFunction")]
    [return: UnsafeAccessorType("Jint.Native.AsyncFunction.AsyncFunctionConstructor, Jint")]
    private static safe extern object GetAsyncFunctionConstructor(Intrinsics intrinsics);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_GeneratorFunction")]
    [return: UnsafeAccessorType("Jint.Native.Generator.GeneratorFunctionConstructor, Jint")]
    private static safe extern object GetGeneratorFunctionConstructor(Intrinsics intrinsics);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_AsyncGeneratorFunction")]
    [return: UnsafeAccessorType("Jint.Native.AsyncGenerator.AsyncGeneratorFunctionConstructor, Jint")]
    private static safe extern object GetAsyncGeneratorFunctionConstructor(Intrinsics intrinsics);
}

/// <summary>
/// The hook of a constructor of dynamic functions (<c>Function</c>, <c>AsyncFunction</c>, <c>GeneratorFunction</c> or
/// <c>AsyncGeneratorFunction</c>) of a realm: see <see cref="JintScriptEngine.ConstructDynamicFunction"/>.
/// </summary>
internal sealed class JintDynamicFunctionConstructor(JintScriptEngine owner, WebIdlRealm<JintAdapter, JsValue> realm, Constructor intrinsic, string name, string prefix)
    : Constructor(owner.Engine, name)
{
    public WebIdlRealm<JintAdapter, JsValue> WebIdlRealm { get; } = realm;

    /// <summary>The intrinsic constructor of Jint.</summary>
    public Constructor Intrinsic { get; } = intrinsic;

    /// <summary>The prefix of the source text of the functions (e.g. <c>async function</c>).</summary>
    public string Prefix { get; } = prefix;

    /// <summary>Calling the constructor creates a function (https://tc39.es/ecma262/#sec-function-p1-p2-pn-body).</summary>
    protected override JsValue Call(JsValue thisObject, JsValue[] arguments) => Construct(arguments, this);

    public override ObjectInstance Construct(JsValue[] arguments, JsValue newTarget) => owner.ConstructDynamicFunction(this, arguments, newTarget);
}
