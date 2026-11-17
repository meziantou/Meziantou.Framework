namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>The options of <see cref="PdfFormScriptSession"/>: the limits of the scripts and the callbacks of the host.</summary>
public sealed class PdfFormScriptOptions
{
    /// <summary>Gets the default options.</summary>
    public static PdfFormScriptOptions Default { get; } = new();

    /// <summary>Gets the time a script may run, each time it runs. The default is one second.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets the number of statements a script may run, each time it runs. The default is 1,000,000.</summary>
    public long MaxStatements { get; init; } = 1_000_000;

    /// <summary>Gets the bytes a script may allocate, each time it runs. The default is 64 MB.</summary>
    public long MemoryLimit { get; init; } = 64L * 1024 * 1024;

    /// <summary>Gets the depth of the calls of a script. The default is 1,000.</summary>
    public int RecursionLimit { get; init; } = 1_000;

    /// <summary>Gets the callback of <c>app.alert</c>, or <see langword="null"/> (the default) to ignore the alerts. The alerts are reported in the diagnostics either way.</summary>
    public Action<string>? Alert { get; init; }

    /// <summary>
    /// Gets a value indicating whether an error of a script throws <see cref="PdfFormScriptException"/>. The default is
    /// <see langword="false"/>: the error is reported in the diagnostics and the other scripts run, as viewers do.
    /// </summary>
    public bool ThrowOnScriptError { get; init; }

    /// <summary>Gets the token that cancels the scripts.</summary>
    public CancellationToken CancellationToken { get; init; }
}
