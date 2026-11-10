namespace Meziantou.Framework.Diagrams;

/// <summary>The format of the data of an event model (Mermaid <c>`json`{ ... }</c>).</summary>
public enum EventModelingDataFormat
{
    /// <summary>JSON (Mermaid <c>json</c>).</summary>
    Json,

    /// <summary>A JavaScript object (Mermaid <c>jsobj</c>).</summary>
    JavaScriptObject,

    /// <summary>A Figma design (Mermaid <c>figma</c>).</summary>
    Figma,

    /// <summary>A PlantUML Salt wireframe (Mermaid <c>salt</c>).</summary>
    Salt,

    /// <summary>An address (Mermaid <c>uri</c>).</summary>
    Uri,

    /// <summary>Markdown (Mermaid <c>md</c>).</summary>
    Markdown,

    /// <summary>HTML (Mermaid <c>html</c>).</summary>
    Html,

    /// <summary>Plain text (Mermaid <c>text</c>).</summary>
    Text,
}
