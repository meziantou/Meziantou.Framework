namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

/// <summary>An engine with the web APIs installed in a realm, whose console messages are collected.</summary>
internal sealed class WebApisTestContext : IDisposable
{
    public WebApisTestContext(JsWebApisOptions? options = null, TimeProvider? timeProvider = null)
    {
        Engine = new JsEngine(timeProvider is null ? null : new JsEngineOptions { TimeProvider = timeProvider });
        Realm = Engine.CreateRealm();
        options ??= new JsWebApisOptions();
        options.ConsoleMessage ??= Messages.Add;
        Host = JsWebApis.Install(Realm, options);
    }

    public JsEngine Engine { get; }

    public JsRealm Realm { get; }

    public JsWebApiHost Host { get; }

    public List<JsConsoleMessage> Messages { get; } = [];

    public IEnumerable<string> MessageTexts => Messages.Select(message => message.Text);

    /// <summary>
    /// Runs the jobs that can run (e.g. the timers whose delay has elapsed), then the script and the jobs it enqueued, and
    /// returns the completion value of the script converted to a string.
    /// </summary>
    public string Run(string code)
    {
        Engine.RunJobs();
        var value = Realm.Execute(code);
        Engine.RunJobs();
        using var scope = Engine.Enter();
        return Engine.ToString(value);
    }

    /// <summary>Runs the script and returns the name of the error it throws (e.g. <c>TypeError</c>), or null when it does not throw.</summary>
    public string? GetErrorName(string code)
    {
        try
        {
            Realm.Execute(code);
            return null;
        }
        catch (JsException exception)
        {
            using var scope = Engine.Enter();
            var error = exception.Value;
            return error.TryGetObject() is { } obj ? Engine.ToString(obj.GetProperty("name")) : Engine.ToString(error);
        }
    }

    public void Dispose() => Engine.Dispose();
}

/// <summary>A clock that only moves when the test advances it.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan delay) => _timestamp += delay.Ticks;
}
