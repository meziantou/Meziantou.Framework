using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The kind of a function created by CreateDynamicFunction.</summary>
internal enum DynamicFunctionKind
{
    /// <summary><c>Function</c></summary>
    Normal,

    /// <summary><c>GeneratorFunction</c></summary>
    Generator,

    /// <summary><c>AsyncFunction</c></summary>
    Async,

    /// <summary><c>AsyncGeneratorFunction</c></summary>
    AsyncGenerator,
}

/// <summary>The source of a function created by CreateDynamicFunction: the strings given to the constructor, and the source text of the function.</summary>
internal readonly record struct DynamicFunctionSource(string Parameters, string[] ParameterStrings, string Body, string SourceText);

/// <summary>CreateDynamicFunction (https://tc39.es/ecma262/#sec-createdynamicfunction): the constructors <c>Function</c>, <c>GeneratorFunction</c>, <c>AsyncFunction</c> and <c>AsyncGeneratorFunction</c>.</summary>
internal static class DynamicFunctions
{
    /// <summary>CreateDynamicFunction(constructor, newTarget, kind, parameterArgs, bodyArg): the last argument is the body, the others are the parameters.</summary>
    public static JsObject CreateDynamicFunction(JsBuiltinFunction constructor, JsObject? newTarget, DynamicFunctionKind kind, ReadOnlySpan<JsValue> arguments)
    {
        newTarget ??= constructor;
        var source = GetSource(constructor.Realm.Engine, kind, arguments);
        return Interpreter.JsInterpreter.CreateDynamicFunction(constructor, newTarget, kind, source, arguments);
    }

    /// <summary>Converts the arguments to strings (the parameters, then the body) and assembles the source text of the function.</summary>
    public static DynamicFunctionSource GetSource(JsEngine engine, DynamicFunctionKind kind, ReadOnlySpan<JsValue> arguments)
    {
        var prefix = kind switch
        {
            DynamicFunctionKind.Normal => "function",
            DynamicFunctionKind.Generator => "function*",
            DynamicFunctionKind.Async => "async function",
            DynamicFunctionKind.AsyncGenerator => "async function*",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var parameterCount = arguments.Length == 0 ? 0 : arguments.Length - 1;
        var parameters = new StringBuilder();
        var parameterStrings = parameterCount == 0 ? [] : new string[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            if (i > 0)
            {
                parameters.Append(',');
            }

            // Each argument is converted once (the strings are given to HostEnsureCanCompileStrings)
            parameterStrings[i] = GetCode(engine, arguments[i]);
            parameters.Append(parameterStrings[i]);
        }

        var body = arguments.Length == 0 ? "" : GetCode(engine, arguments[^1]);
        var parametersString = parameters.ToString();
        var sourceText = prefix + " anonymous(" + parametersString + "\n) {\n" + body + "\n}";
        return new DynamicFunctionSource(parametersString, parameterStrings, body, sourceText);
    }

    /// <summary>
    /// The code of an argument (https://tc39.es/proposal-dynamic-code-brand-checks/#sec-createdynamicfunction): the code
    /// of the host (HostGetCodeForEval, e.g. the data of a TrustedScript, whose toString is not called), else ToString.
    /// </summary>
    private static string GetCode(JsEngine engine, JsValue argument) => argument.IsString ? argument.AsString() : engine.HostGetCodeForEval(argument) ?? JsOperations.ToString(argument);
}
