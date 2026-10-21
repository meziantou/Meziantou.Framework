using Jint.Native;
using Jint.Runtime;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Jint;
using EngineObject = Meziantou.Framework.JavaScriptEngine.JsObject;
using JsValue = Jint.Native.JsValue;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The JavaScript engine that runs a test: Jint, the engine of the expectations (WPT_ENGINE=jint, the default), or
/// Meziantou.Framework.JavaScriptEngine (WPT_ENGINE=javascript), whose results are only reported (WPT_REPORT).
/// </summary>
internal abstract class WptEngine : IDisposable
{
    /// <summary>Gets whether the tests run with the engine of the expectations (Jint).</summary>
    public static bool IsDefault => Environment.GetEnvironmentVariable("WPT_ENGINE") is null or "" or "jint";

    public abstract ScriptEngine ScriptEngine { get; }

    /// <param name="runner">The runner of the test.</param>
    /// <param name="recursionLimit">The maximum depth of the calls of the scripts (a deeper call aborts the page), or null for no limit.</param>
    public static WptEngine Create(WptTestRunner runner, int? recursionLimit = null)
    {
        if (IsDefault)
            return new JintWptEngine(runner, recursionLimit);

        if (Environment.GetEnvironmentVariable("WPT_ENGINE") == "javascript")
            return new JavaScriptWptEngine(runner, recursionLimit);

        throw new InvalidOperationException("Unknown WPT_ENGINE: " + Environment.GetEnvironmentVariable("WPT_ENGINE"));
    }

    /// <summary>Whether the exception is thrown by the engine when a script exceeds its time or resources.</summary>
    public abstract bool IsTimeout(Exception exception);

    /// <summary>Resets the time limit of the scripts (called before each task).</summary>
    public abstract void ResetConstraints();

    /// <summary>Returns the description of an error value of the engine (its stack), or null.</summary>
    public abstract string? DescribeError(object? error);

    public abstract void Dispose();
}

internal sealed class JintWptEngine : WptEngine
{
    private readonly WptJintScriptEngine _engine;

    public JintWptEngine(WptTestRunner runner, int? recursionLimit)
    {
        _engine = new WptJintScriptEngine(runner, new JintScriptEngineOptions
        {
            Timeout = WptTestRunner.MaxDuration,
            MemoryLimit = null,
            RecursionLimit = recursionLimit,
            RegexTimeout = null,

            ConfigureEngine = options =>
            {
                // Date uses the virtual clock of the runner, as performance.now() and the timers
                options.TimeSystem = new WptTimeSystem(runner.Clock);

                // An unbounded recursion throws a RangeError instead of ending the process (e.g. the harness of the WebAssembly
                // core tests gets the class of the exception of a stack overflow from a recursion)
                options.Constraints.StackOverflowGuard = true;
            },
        });
    }

    public override ScriptEngine ScriptEngine => _engine;

    public override bool IsTimeout(Exception exception) => exception is StatementsCountOverflowException or ExecutionCanceledException or MemoryLimitExceededException or RecursionDepthOverflowException;

    public override void ResetConstraints() => _engine.Engine.Constraints.Reset();

    public override string? DescribeError(object? error)
    {
        if (error is not JsValue value)
            return null;

        try
        {
            // The stack accessor of Error.prototype does not support platform objects (e.g. DOMException)
            if (value is JintPlatformObject { Target: DomException exception })
                return exception.Name + ": " + exception.Message;

            return value.IsObject() && TypeConverter.ToString(value.AsObject().Get("stack")) is { Length: > 0 } stack && stack != "undefined" ? stack : value.ToString();
        }
#pragma warning disable CA1031 // The description is only logged
        catch (Exception)
#pragma warning restore CA1031
        {
            return value.ToString();
        }
    }

    public override void Dispose() => _engine.Dispose();

    /// <summary>The script engine of the tests: the harness functions of the realms, and the document of the test page.</summary>
    private sealed class WptJintScriptEngine : JintScriptEngine
    {
        private readonly WptTestRunner _runner;

        public WptJintScriptEngine(WptTestRunner runner, JintScriptEngineOptions options)
            : base(options)
        {
            _runner = runner;
        }

        private WptJintScriptEngine(WptTestRunner runner, JintScriptEngineOptions options, CancellationToken terminationToken)
            : base(options, terminationToken)
        {
            _runner = runner;
        }

        /// <summary>The engines of the workers parse the scripts of the harness once, as the engine of the test page.</summary>
        protected internal override ScriptEngine? CreateWorkerScriptEngine(CancellationToken terminationToken) => new WptJintScriptEngine(_runner, Options, terminationToken);

        protected internal override void OnDocumentActivated(Document document)
        {
            _runner.OnDocumentActivated(document);
            base.OnDocumentActivated(document);
        }

        internal override void InitializeRealm(WebIdlRealm<JintAdapter, JsValue> realm) => _runner.SetupGlobals(realm);

        /// <summary>
        /// The scripts of the harness are parsed once for all the tests: the external scripts of <c>/resources/</c>, not the
        /// inline scripts nor the <c>javascript:</c> URLs of its documents (e.g. <c>/resources/blank.html</c>), whose URL is
        /// the one of the document.
        /// </summary>
        internal override bool ShouldCacheScript(ClassicScript script) => new Url(script.Url).Pathname is var path && path.StartsWith("/resources/", StringComparison.Ordinal) && path.EndsWith(".js", StringComparison.Ordinal);
    }
}

internal sealed class JavaScriptWptEngine : WptEngine
{
    private readonly WptJavaScriptScriptEngine _engine;

    public JavaScriptWptEngine(WptTestRunner runner, int? recursionLimit)
    {
        _engine = new WptJavaScriptScriptEngine(runner, new JavaScriptScriptEngineOptions
        {
            Timeout = WptTestRunner.MaxDuration,
            MemoryLimit = null,
            RecursionLimit = recursionLimit,
            RegexTimeout = null,

            // Date uses the virtual clock of the runner, as performance.now() and the timers
            TimeProvider = new WptDateTimeProvider(runner.Clock),
        });
    }

    public override ScriptEngine ScriptEngine => _engine;

    public override bool IsTimeout(Exception exception) => exception is JsTerminationException;

    // The limits of the engine apply to each entry into JavaScript
    public override void ResetConstraints()
    {
    }

    public override string? DescribeError(object? error)
    {
        if (error is not EngineObject value)
            return error?.ToString();

        try
        {
            // The stack accessor of Error.prototype does not support platform objects (e.g. DOMException)
            if (value is JsHostObject { Target: DomException exception })
                return exception.Name + ": " + exception.Message;

            using var scope = _engine.Engine.Enter();
            var stack = value.GetErrorStack() ?? (value.GetProperty("stack") is { IsString: true } stackValue ? stackValue.AsString() : null);
            return stack is { Length: > 0 } ? stack : _engine.Engine.ToString(value);
        }
        catch (Exception)
        {
            return value.ToString();
        }
    }

    public override void Dispose() => _engine.Dispose();

    /// <summary>The script engine of the tests: the harness functions of the realms, and the document of the test page.</summary>
    private sealed class WptJavaScriptScriptEngine : JavaScriptScriptEngine
    {
        private readonly WptTestRunner _runner;

        public WptJavaScriptScriptEngine(WptTestRunner runner, JavaScriptScriptEngineOptions options)
            : base(options)
        {
            _runner = runner;
        }

        private WptJavaScriptScriptEngine(WptTestRunner runner, JavaScriptScriptEngineOptions options, CancellationToken terminationToken)
            : base(options, terminationToken)
        {
            _runner = runner;
        }

        /// <summary>The engines of the workers parse the scripts of the harness once, as the engine of the test page.</summary>
        protected internal override ScriptEngine? CreateWorkerScriptEngine(CancellationToken terminationToken) => new WptJavaScriptScriptEngine(_runner, Options, terminationToken);

        protected internal override void OnDocumentActivated(Document document)
        {
            _runner.OnDocumentActivated(document);
            base.OnDocumentActivated(document);
        }

        internal override void InitializeRealm(WebIdlRealm<JavaScriptAdapter, object> realm) => _runner.SetupGlobals(realm);

        /// <summary>
        /// The scripts of the harness are parsed once for all the tests: the external scripts of <c>/resources/</c>, not the
        /// inline scripts nor the <c>javascript:</c> URLs of its documents (e.g. <c>/resources/blank.html</c>), whose URL is
        /// the one of the document.
        /// </summary>
        internal override bool ShouldCacheScript(ClassicScript script) => new Url(script.Url).Pathname is var path && path.StartsWith("/resources/", StringComparison.Ordinal) && path.EndsWith(".js", StringComparison.Ordinal);
    }
}

/// <summary>
/// The clock of Date.now() and new Date(): the virtual clock of the runner, in its time zone (UTC), as document.lastModified.
/// Date.now() has a resolution of 1 ms: each read advances the virtual clock by 1 ms (instead of the 100 µs of a read of
/// performance.now()), so that two reads return different values, as in a browser where time passes between them (e.g.
/// the unique URLs made of Date.now() of module/dynamic-import/microtasks/basic.any.js).
/// </summary>
internal sealed class WptDateTimeProvider(TimeProvider clock) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

    public override long TimestampFrequency => clock.TimestampFrequency;

    public override long GetTimestamp() => clock.GetTimestamp();

    public override DateTimeOffset GetUtcNow()
    {
        var now = clock.GetUtcNow();
        if (clock is Microsoft.Extensions.Time.Testing.FakeTimeProvider fake && fake.AutoAdvanceAmount < TimeSpan.FromMilliseconds(1))
        {
            fake.Advance(TimeSpan.FromMilliseconds(1) - fake.AutoAdvanceAmount);
        }

        return now;
    }
}

/// <summary>The time system of Jint (Date.now(), new Date()): the virtual clock of the runner, in its time zone (UTC), as document.lastModified.</summary>
internal sealed class WptTimeSystem(TimeProvider clock) : global::Jint.Runtime.ITimeSystem
{
    private readonly global::Jint.Runtime.DefaultTimeSystem _default = new(clock.LocalTimeZone, System.Globalization.CultureInfo.CurrentCulture);

    public TimeZoneInfo DefaultTimeZone => _default.DefaultTimeZone;

    // Date.now() has a resolution of 1 ms: each read advances the virtual clock by 1 ms (instead of the 100 µs of a read of
    // performance.now()), so that two reads return different values, as in a browser where time passes between them (e.g.
    // the unique URLs made of Date.now() of module/dynamic-import/microtasks/basic.any.js)
    public DateTimeOffset GetUtcNow()
    {
        var now = clock.GetUtcNow();
        if (clock is Microsoft.Extensions.Time.Testing.FakeTimeProvider fake && fake.AutoAdvanceAmount < TimeSpan.FromMilliseconds(1))
        {
            fake.Advance(TimeSpan.FromMilliseconds(1) - fake.AutoAdvanceAmount);
        }

        return now;
    }

    public TimeSpan GetUtcOffset(long epochMilliseconds) => _default.GetUtcOffset(epochMilliseconds);

    public bool TryParse(string date, out long epochMilliseconds) => _default.TryParse(date, out epochMilliseconds);
}
