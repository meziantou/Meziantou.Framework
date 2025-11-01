namespace Meziantou.Framework.HumanReadable.Converters;

/// <summary>Specifies the format to use when serializing HTTP request messages.</summary>
public enum HttpRequestMessageFormat
{
    /// <summary>The request message is not serialized.</summary>
    NotSerialized,

    /// <summary>Only the HTTP method and URI are serialized.</summary>
    MethodAndUri,

    /// <summary>The full request message including headers and content is serialized.</summary>
    Full,
}
