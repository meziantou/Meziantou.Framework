using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Metadata;

/// <summary>A simple property of an XMP packet, such as <c>pdfaid:part</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct XmpProperty(string Prefix, string Namespace, string Name, string Value);
