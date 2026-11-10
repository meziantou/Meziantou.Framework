namespace Meziantou.Framework.JavaScriptEngine.WebApis;

/// <summary>A message written by the <c>console</c> object.</summary>
public sealed class JsConsoleMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="method">The method of the <c>console</c> object that wrote the message (e.g. <c>log</c>).</param>
    /// <param name="level">The severity of the message.</param>
    /// <param name="text">The formatted text of the message.</param>
    /// <param name="groupDepth">The number of groups (<c>console.group</c>) the message is in.</param>
    public JsConsoleMessage(string method, JsConsoleMessageLevel level, string text, int groupDepth)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(groupDepth);
        Method = method;
        Level = level;
        Text = text;
        GroupDepth = groupDepth;
    }

    /// <summary>Gets the method of the <c>console</c> object that wrote the message (e.g. <c>log</c>, <c>warn</c>, <c>timeEnd</c>).</summary>
    public string Method { get; }

    /// <summary>Gets the severity of the message.</summary>
    public JsConsoleMessageLevel Level { get; }

    /// <summary>
    /// Gets the text of the message: the arguments formatted as the Console Standard specifies
    /// (https://console.spec.whatwg.org/#formatter), the objects shown as Node.js shows them.
    /// </summary>
    public string Text { get; }

    /// <summary>Gets the number of groups (<c>console.group</c>) the message is in: a host indents the message by this depth.</summary>
    public int GroupDepth { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
