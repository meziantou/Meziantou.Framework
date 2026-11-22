using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The names of the diagnostics of the engine, for OpenTelemetry (<c>AddSource</c>, <c>AddMeter</c>) or the listeners of
/// <c>System.Diagnostics</c>. The engine traces the parsing, the compilation and the execution of the scripts and the
/// modules with an <see cref="ActivitySource"/>, and measures their duration with a <see cref="Meter"/>. Nothing is
/// recorded when nothing listens.
/// </summary>
public static class JsEngineTelemetry
{
    /// <summary>The name of the <see cref="ActivitySource"/> of the engine.</summary>
    public const string ActivitySourceName = "Meziantou.Framework.JavaScriptEngine";

    /// <summary>The name of the <see cref="Meter"/> of the engine.</summary>
    public const string MeterName = "Meziantou.Framework.JavaScriptEngine";

    internal const string ParseDurationName = "meziantou.framework.javascript_engine.parse.duration";
    internal const string CompileDurationName = "meziantou.framework.javascript_engine.compile.duration";
    internal const string ExecutionDurationName = "meziantou.framework.javascript_engine.execution.duration";
    internal const string TerminationsName = "meziantou.framework.javascript_engine.terminations";

    internal const string ParseActivityName = "js.parse";
    internal const string CompileActivityName = "js.compile";
    internal const string ScriptExecuteActivityName = "js.script.execute";
    internal const string ModuleLinkActivityName = "js.module.link";
    internal const string ModuleEvaluateActivityName = "js.module.evaluate";

    internal const string CodeKindTagName = "js.code.kind";
    internal const string SourceUrlTagName = "js.source.url";
    internal const string SourceLengthTagName = "js.source.length";
    internal const string TerminationReasonTagName = "js.termination.reason";
    internal const string ErrorTypeTagName = "error.type";

    private static readonly string? Version = typeof(JsEngineTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    internal static readonly Meter Meter = new(MeterName, Version);

    internal static readonly Histogram<double> ParseDuration = Meter.CreateHistogram<double>(ParseDurationName, unit: "s", description: "Duration of the parsing of JavaScript source text.");

    internal static readonly Histogram<double> CompileDuration = Meter.CreateHistogram<double>(CompileDurationName, unit: "s", description: "Duration of the compilation of JavaScript code to bytecode, or of bytecode to .NET code.");

    internal static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>(ExecutionDurationName, unit: "s", description: "Duration of the execution of the scripts and of the evaluation of the modules.");

    internal static readonly Counter<long> Terminations = Meter.CreateCounter<long>(TerminationsName, unit: "{termination}", description: "Number of entries into JavaScript stopped by a limit or a cancellation.");

    /// <summary>Starts the parsing of a source text: a span when <paramref name="traced"/> is true, and a measure of its duration.</summary>
    internal static JsTelemetryOperation StartParse(JsCodeKind kind, bool traced, string? url = null, int sourceLength = -1)
        => JsTelemetryOperation.Start(traced ? ParseActivityName : null, ParseDuration, kind, url, sourceLength);

    /// <summary>Starts the compilation of a source text: a span when <paramref name="traced"/> is true, and a measure of its duration.</summary>
    internal static JsTelemetryOperation StartCompile(JsCodeKind kind, bool traced, string? url = null, int sourceLength = -1)
        => JsTelemetryOperation.Start(traced ? CompileActivityName : null, CompileDuration, kind, url, sourceLength);

    /// <summary>Counts an entry into JavaScript stopped by a limit or a cancellation.</summary>
    internal static void RecordTermination(JsTerminationReason reason)
        => Terminations.Add(1, new KeyValuePair<string, object?>(TerminationReasonTagName, GetTagValue(reason)));

    internal static string GetTagValue(JsCodeKind kind) => kind switch
    {
        JsCodeKind.Script => "script",
        JsCodeKind.Module => "module",
        JsCodeKind.Eval => "eval",
        JsCodeKind.Function => "function",
        JsCodeKind.LazyFunction => "lazy_function",
        JsCodeKind.Jit => "jit",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string GetTagValue(JsTerminationReason reason) => reason switch
    {
        JsTerminationReason.Canceled => "canceled",
        JsTerminationReason.Timeout => "timeout",
        JsTerminationReason.MemoryLimit => "memory_limit",
        JsTerminationReason.StatementLimit => "statement_limit",
        _ => "unknown",
    };
}
