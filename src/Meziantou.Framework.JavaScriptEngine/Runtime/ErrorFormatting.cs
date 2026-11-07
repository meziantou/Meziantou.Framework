namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Describes the thrown values without running JavaScript code (for the .NET exception messages).</summary>
internal static class ErrorFormatting
{
    public static string GetDescription(JsValue value)
    {
        if (value.TryGetObject() is { } obj)
        {
            var name = GetDataPropertyString(obj, "name");
            var message = GetDataPropertyString(obj, "message");
            if (name is not null || message is not null)
                return string.IsNullOrEmpty(message) ? name ?? "Error" : (name ?? "Error") + ": " + message;

            return "Uncaught " + value.ToString();
        }

        return "Uncaught " + (value.IsString ? value.AsString() : value.ToString());
    }

    /// <summary>Reads a data property on the prototype chain without calling any getter nor proxy trap.</summary>
    public static string? GetDataPropertyString(JsObject obj, string name)
    {
        var key = new PropertyKey(name);
        for (JsObject? current = obj; current is not null && current.Class != ObjectClass.Proxy; current = current.PrototypeSlot)
        {
            if (!current.IsExotic && current.OrdinaryGetOwnProperty(key, out var descriptor))
                return descriptor.IsDataDescriptor && descriptor.Value.IsString ? descriptor.Value.AsString() : null;
        }

        return null;
    }
}
