namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A message printed by the <c>console</c> namespace of a window (the Printer operation of the Console Standard,
/// https://console.spec.whatwg.org/#printer), given to <see cref="Dom.BrowsingContextOptions.ConsoleMessage"/>.
/// </summary>
public sealed class ConsoleMessage
{
    /// <summary>Creates a console message.</summary>
    /// <param name="method">The name of the console method that printed the message (e.g. <c>log</c>, <c>count</c>, <c>timeEnd</c>).</param>
    /// <param name="level">The severity of the message.</param>
    /// <param name="text">The text of the message.</param>
    public ConsoleMessage(string method, ConsoleMessageLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(text);
        Method = method;
        Level = level;
        Text = text;
    }

    /// <summary>
    /// Gets the name of the console method that printed the message (the log level of the Console Standard): <c>log</c>,
    /// <c>info</c>, <c>warn</c>, <c>error</c>, <c>debug</c>, <c>assert</c>, <c>count</c>, <c>countReset</c>, <c>time</c>,
    /// <c>timeLog</c>, <c>timeEnd</c>, <c>group</c>, <c>groupCollapsed</c>, <c>trace</c>, <c>table</c>, <c>dir</c>,
    /// <c>dirxml</c> or <c>clear</c>.
    /// </summary>
    public string Method { get; }

    /// <summary>Gets the severity of the message.</summary>
    public ConsoleMessageLevel Level { get; }

    /// <summary>
    /// Gets the text of the message: the arguments formatted by the formatter of the Console Standard
    /// (https://console.spec.whatwg.org/#formatter), and the values that are not strings formatted as a browser console
    /// does. It is not indented by the groups (see <see cref="GroupDepth"/>).
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the arguments of the message, before formatting, converted as the IDL type <c>any</c>: the strings are .NET
    /// strings, the platform objects are their .NET objects, <c>undefined</c> is null, and the other values are values of
    /// the script engine.
    /// </summary>
    public IReadOnlyList<object?> Arguments { get; init; } = [];

    /// <summary>Gets the number of groups (<c>console.group()</c>) the message is in.</summary>
    public int GroupDepth { get; init; }

    /// <summary>Gets the call stack of a <c>console.trace()</c> message, or null.</summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// Gets the location of the code that printed the message (the source file, line and column of the innermost running
    /// script), or null when the script engine cannot extract it.
    /// </summary>
    public ScriptLocation? Location { get; init; }

    /// <inheritdoc />
    public override string ToString() => Text;
}
