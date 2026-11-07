namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A JSON text whose JavaScript value is created by the WebIDL bindings (e.g. the response of an <c>XMLHttpRequest</c>
/// whose <c>responseType</c> is <c>json</c>): the value is parsed once, in the realm of the first conversion, and is null
/// when the text is not valid JSON (https://infra.spec.whatwg.org/#parse-json-bytes-to-a-javascript-value).
/// </summary>
internal sealed class JsonText(string text)
{
    public string Text { get; } = text;
}
