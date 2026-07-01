namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents a parser or analyzer diagnostic produced while reading XML syntax.</summary>
/// <example>
/// <code>
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item&gt;&lt;/root&gt;");
/// var diagnostic = tree.Diagnostics.First();
/// </code>
/// </example>
public sealed record XmlDiagnostic(string Id, string Message, XmlDiagnosticSeverity Severity, TextSpan Span);
