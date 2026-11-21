namespace Meziantou.Framework.Email.Mapi;

/// <summary>The content an RTF body encapsulates ([MS-OXRTFEX]).</summary>
public enum RtfEncapsulationFormat
{
    /// <summary>The RTF body is RTF: it does not encapsulate another format.</summary>
    None,

    /// <summary>The RTF body encapsulates an HTML body (<c>\fromhtml1</c>).</summary>
    Html,

    /// <summary>The RTF body was produced from a plain text body (<c>\fromtext</c>).</summary>
    Text,
}
