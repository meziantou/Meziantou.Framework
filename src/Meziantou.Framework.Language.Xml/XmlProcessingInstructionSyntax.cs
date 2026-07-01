namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents an XML processing instruction node.</summary>
/// <example>
/// <code>
/// var instruction = SyntaxFactory.ProcessingInstruction("xml-stylesheet", "type=\"text/xsl\" href=\"a.xsl\"");
/// var updated = instruction.WithData("type=\"text/xsl\" href=\"b.xsl\"");
/// </code>
/// </example>
public sealed class XmlProcessingInstructionSyntax : XmlSyntaxNode
{
    public XmlProcessingInstructionSyntax(string target, string? data, string fullText)
        : base(XmlSyntaxKind.XmlProcessingInstruction, fullText, [new XmlSyntaxToken(XmlSyntaxKind.ProcessingInstructionToken, fullText)])
    {
        Target = target;
        Data = data;
    }

    public string Target { get; }
    public string? Data { get; }

    public XmlProcessingInstructionSyntax WithTarget(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.Equals(target, Target, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.ProcessingInstruction(target, Data);
    }

    public XmlProcessingInstructionSyntax WithData(string? data)
    {
        if (string.Equals(data, Data, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.ProcessingInstruction(Target, data);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitProcessingInstruction(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitProcessingInstruction(this);
}
