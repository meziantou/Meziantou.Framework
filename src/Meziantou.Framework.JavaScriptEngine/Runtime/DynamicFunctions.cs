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
internal readonly record struct DynamicFunctionSource(string Parameters, string Body, string SourceText);

/// <summary>CreateDynamicFunction (https://tc39.es/ecma262/#sec-createdynamicfunction): the constructors <c>Function</c>, <c>GeneratorFunction</c>, <c>AsyncFunction</c> and <c>AsyncGeneratorFunction</c>.</summary>
internal static class DynamicFunctions
{
    /// <summary>CreateDynamicFunction(constructor, newTarget, kind, parameterArgs, bodyArg): the last argument is the body, the others are the parameters.</summary>
    public static JsObject CreateDynamicFunction(JsBuiltinFunction constructor, JsObject? newTarget, DynamicFunctionKind kind, ReadOnlySpan<JsValue> arguments)
    {
        newTarget ??= constructor;
        var source = GetSource(kind, arguments);
        return Interpreter.JsInterpreter.CreateDynamicFunction(constructor, newTarget, kind, source, arguments);
    }

    /// <summary>Converts the arguments to strings (the parameters, then the body) and assembles the source text of the function.</summary>
    public static DynamicFunctionSource GetSource(DynamicFunctionKind kind, ReadOnlySpan<JsValue> arguments)
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
        for (var i = 0; i < parameterCount; i++)
        {
            if (i > 0)
            {
                parameters.Append(',');
            }

            parameters.Append(JsOperations.ToString(arguments[i]));
        }

        var body = arguments.Length == 0 ? "" : JsOperations.ToString(arguments[^1]);
        var parametersString = parameters.ToString();
        var sourceText = prefix + " anonymous(" + parametersString + "\n) {\n" + body + "\n}";
        return new DynamicFunctionSource(parametersString, body, sourceText);
    }
}
