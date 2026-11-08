using System.Globalization;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// A debugger (<see cref="JsEngineOptions.Debugger"/>) does not change what a script does: a script gives the same result
/// with a debugger that only observes it, or that runs code and captures the stack in its callbacks, as without debugger.
/// The events are located at the start of a statement of the script (at the <c>debugger</c> keyword for a
/// <c>debugger</c> statement), and the code run by a callback raises no event. A <see cref="JsTerminationException"/>
/// thrown by a callback stops the script, any other exception goes to the host unchanged, and the engine runs code again
/// afterward.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private const string DebuggerScriptUrl = "script.js";

    // Run by the callbacks of the Inspect mode: a loop and a call, which count in the limits of the callback, not the script's
    private const string DebuggerCallbackCode = "(() => { let s = 0; for (let i = 0; i < 100; i++) s += i; return s; })()";

    // The Inspect mode inspects the first events only: inspecting each of the events of a script that reaches the
    // statement limit takes seconds, as the callback runs more statements than the event
    private const int MaxInspectedEvents = 1_000;

    private enum DebuggerMode
    {
        // Records the events
        Observe,

        // Reads the event, captures the stack and runs code in each callback
        Inspect,

        // Throws a JsTerminationException at the event number N
        Terminate,

        // Throws an exception of the host at the event number N
        Throw,
    }

    // The input is the mode, the number of the event at which the Terminate and Throw modes throw, and the script
    private static void Debugger(ReadOnlySpan<byte> data)
    {
        var mode = (DebuggerMode)(FuzzInput.ReadByte(ref data) % 4);
        var stopAt = FuzzInput.ReadByte(ref data);
        var code = FuzzText.Decode(data);

        var expected = RunScript(realm => realm.Execute(code, DebuggerScriptUrl));

        var debugger = new FuzzDebugger(code, mode, stopAt);
        using var engine = CreateEngine(loader: null, FixedTimeProvider.Instance, debugger);
        var realm = CreateDeterministicRealm(engine);
        debugger.Engine = engine;

        ScriptOutcome? actual = null;
        debugger.IsInScript = true;
        try
        {
            var value = realm.Execute(code, DebuggerScriptUrl);
            actual = new ScriptOutcome(ScriptOutcomeKind.Value, value.Kind + ":" + value.ToString(), Location: null);
        }
        catch (JsException exception)
        {
            actual = new ScriptOutcome(ScriptOutcomeKind.Exception, exception.Message, exception.Location);
        }
        catch (JsTerminationException exception)
        {
            actual = new ScriptOutcome(ScriptOutcomeKind.Terminated, exception.Reason.ToString(), Location: null);
            if (debugger.ThrownInScript is not null)
            {
                FuzzAssert.True(ReferenceEquals(debugger.ThrownInScript, exception), "The script was stopped by another exception than the JsTerminationException of the debugger: " + exception.Message);
            }
        }
        catch (FuzzDebuggerException exception)
        {
            FuzzAssert.True(ReferenceEquals(debugger.ThrownInScript, exception), "The script threw an exception of the debugger it did not throw during the script");
        }
        finally
        {
            debugger.IsInScript = false;
        }

        debugger.CheckNoFailure();
        if (debugger.ThrownInScript is null)
        {
            // The events counted, raised and the code run by the callbacks do not change the result of the script
            FuzzAssert.True(actual is not null, "The script did not complete");
            CheckSameOutcome(expected, actual.Value, $"The script runs differently with a debugger ({mode})");
        }
        else if (actual is { } outcome)
        {
            // An exception of the debugger is thrown at the statement, and the scripts cannot catch it
            FuzzAssert.Equal(ScriptOutcomeKind.Terminated, outcome.Kind, $"The script does not stop when the debugger throws a {debugger.ThrownInScript.GetType().Name}: {outcome.Text}");
        }

        Exception? jobsException = null;
        try
        {
            engine.RunJobs();
        }
        catch (JsException exception)
        {
            jobsException = exception;
        }
        catch (Exception exception) when (exception is JsTerminationException or FuzzDebuggerException)
        {
            jobsException = exception;
        }

        debugger.CheckNoFailure();
        if (debugger.ThrownInJobs is { } thrownInJobs)
        {
            FuzzAssert.True(ReferenceEquals(thrownInJobs, jobsException), $"RunJobs does not throw the {thrownInJobs.GetType().Name} of the debugger: {jobsException?.Message ?? "it returns"}");

            // A later RunJobs runs the remaining jobs
            Run(engine.RunJobs);
        }
        else
        {
            FuzzAssert.False(jobsException is FuzzDebuggerException, "RunJobs threw an exception of the debugger it did not throw during the jobs");
        }

        // The engine runs code again after a debugger stopped a script. A new realm: the script may have changed its own
        debugger.IsStopped = true;
        var check = engine.CreateRealm().Evaluate(DebuggerCallbackCode);
        FuzzAssert.Equal("4950", check.ToString(), "The engine does not run code after the debugger stopped a script");
        debugger.CheckNoFailure();
    }

    private static IEnumerable<FuzzSeed> GetDebuggerSeeds()
    {
        var index = 0;
        foreach (var (name, code) in JavaScriptSeeds.Scripts.Concat(JavaScriptSeeds.DebuggerScripts))
        {
            // Each script with each mode, and a stop at an early and a later event
            for (var mode = 0; mode < 4; mode++)
            {
                var stopAt = (byte)((index * 7) % 23);
                yield return FuzzSeed.FromText($"{name}-{(DebuggerMode)mode}".ToLowerInvariant(), [(byte)mode, stopAt], code);
                index++;
            }
        }
    }

    // The column, in UTF-16 code units, of each line of the script (the line terminators of SourceText)
    private static List<int> GetLineStarts(string code)
    {
        var result = new List<int> { 0 };
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '\r')
            {
                if (i + 1 < code.Length && code[i + 1] == '\n')
                {
                    i++;
                }

                result.Add(i + 1);
            }
            else if (c is '\n' or '\u2028' or '\u2029')
            {
                result.Add(i + 1);
            }
        }

        return result;
    }

    private sealed class FuzzDebuggerException : Exception
    {
        public FuzzDebuggerException()
            : base("Thrown by the debugger of the fuzzer")
        {
        }

        public FuzzDebuggerException(string message)
            : base(message)
        {
        }

        public FuzzDebuggerException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed class FuzzDebugger : JsDebugger
    {
        private readonly string _code;
        private readonly DebuggerMode _mode;
        private readonly int _stopAt;
        private readonly List<int> _lineStarts;

        // The script can name its eval code like itself (//# sourceURL=): its positions are then not in the script
        private readonly bool _canCheckScriptPositions;
        private bool _isInCallback;
        private int _eventCount;
        private string? _failure;

        public FuzzDebugger(string code, DebuggerMode mode, int stopAt)
        {
            _code = code;
            _mode = mode;
            _stopAt = stopAt;
            _lineStarts = GetLineStarts(code);
            _canCheckScriptPositions = !code.Contains("sourceURL", StringComparison.Ordinal);
        }

        public JsEngine? Engine { get; set; }

        // Whether Execute runs (else, the jobs or the code run after the script)
        public bool IsInScript { get; set; }

        // Once set, the callbacks only check the events
        public bool IsStopped { get; set; }

        public Exception? ThrownInScript { get; private set; }

        public Exception? ThrownInJobs { get; private set; }

        public void CheckNoFailure()
        {
            if (_failure is not null)
            {
                FuzzAssert.Fail(_failure);
            }
        }

        protected internal override void OnStatement(JsDebugEvent debugEvent) => OnEvent(debugEvent, isDebuggerStatement: false);

        protected internal override void OnDebuggerStatement(JsDebugEvent debugEvent) => OnEvent(debugEvent, isDebuggerStatement: true);

        // A failure is recorded rather than thrown: the exceptions of the callbacks go through the engine
        private void Fail(string message) => _failure ??= message;

        private void OnEvent(JsDebugEvent debugEvent, bool isDebuggerStatement)
        {
            if (_isInCallback)
            {
                Fail("The code run by a callback of the debugger raised an event at " + debugEvent.Location);
                return;
            }

            _isInCallback = true;
            try
            {
                CheckEvent(debugEvent, isDebuggerStatement);
                if (IsStopped)
                    return;

                if (_mode is DebuggerMode.Inspect)
                {
                    if (_eventCount < MaxInspectedEvents)
                    {
                        Inspect(debugEvent);
                    }
                }
                else if (_mode is DebuggerMode.Terminate or DebuggerMode.Throw && _eventCount == _stopAt && ThrownInScript is null && ThrownInJobs is null)
                {
                    Exception exception = _mode is DebuggerMode.Terminate ? new JsTerminationException(JsTerminationReason.Canceled, "Stopped by the debugger of the fuzzer") : new FuzzDebuggerException();
                    if (IsInScript)
                    {
                        ThrownInScript = exception;
                    }
                    else
                    {
                        ThrownInJobs = exception;
                    }

                    throw exception;
                }
            }
            finally
            {
                _eventCount++;
                _isInCallback = false;
            }
        }

        private void CheckEvent(JsDebugEvent debugEvent, bool isDebuggerStatement)
        {
            if (!ReferenceEquals(debugEvent.Engine, Engine))
            {
                Fail("The event is not raised by the engine of the debugger");
            }

            if (debugEvent.CallDepth < 1)
            {
                Fail($"The call depth {debugEvent.CallDepth} of the event at {debugEvent.Location} is not positive");
            }

            if (debugEvent.FunctionName is { Length: 0 })
            {
                Fail("The name of the function of the event is empty instead of null");
            }

            var location = debugEvent.Location;
            if (location != debugEvent.Location)
            {
                Fail($"The location of the event changes: {location} then {debugEvent.Location}");
            }

            if (location.Line < 1 || location.Column < 1)
            {
                Fail($"The location {location} of the event is not 1-based");
                return;
            }

            if (location.Url != DebuggerScriptUrl || !_canCheckScriptPositions)
                return;

            if (location.Line > _lineStarts.Count)
            {
                Fail($"The location {location} of the event is after the last line of the script ({_lineStarts.Count})");
                return;
            }

            var lineStart = _lineStarts[location.Line - 1];
            var lineEnd = location.Line < _lineStarts.Count ? _lineStarts[location.Line] : _code.Length;
            var offset = lineStart + location.Column - 1;
            if (offset >= lineEnd)
            {
                Fail($"The location {location} of the event is after the end of its line (column {lineEnd - lineStart + 1})");
                return;
            }

            // A statement starts at its first token
            var c = _code[offset];
            if (char.IsWhiteSpace(c) || c is '\uFEFF')
            {
                Fail($"The location {location} of the event is a white space (U+{(int)c:X4}), not the start of a statement");
            }

            if (isDebuggerStatement && !_code.AsSpan(offset).StartsWith("debugger", StringComparison.Ordinal))
            {
                Fail($"The location {location} of the debugger statement is not the 'debugger' keyword: '{FuzzText.Escape(_code.Substring(offset, Math.Min(20, _code.Length - offset)))}'");
            }
        }

        private void Inspect(JsDebugEvent debugEvent)
        {
            _ = debugEvent.FunctionName;
            _ = debugEvent.Realm.GlobalObject;

            // The first frame of the stack is the statement
            var stack = debugEvent.CaptureStackTrace();
            var location = debugEvent.Location;
            if (location.Url == DebuggerScriptUrl)
            {
                var firstFrame = stack.Split('\n')[0];
                var position = string.Create(CultureInfo.InvariantCulture, $"{location.Url}:{location.Line}:{location.Column}");
                if (!firstFrame.Contains(position, StringComparison.Ordinal))
                {
                    Fail($"The first frame of the stack captured at {location} is '{FuzzText.Escape(firstFrame)}'");
                }
            }

            // The callbacks run code with their own limits
            try
            {
                var result = debugEvent.Realm.Evaluate(DebuggerCallbackCode);
                if (result.ToString() != "4950")
                {
                    Fail("The code run by a callback returned " + result);
                }
            }
            catch (JsException exception) when (exception.Message.StartsWith("RangeError", StringComparison.Ordinal))
            {
                // The script is at the recursion limit: the frames of the callback are on the same stack
            }
        }
    }
}
