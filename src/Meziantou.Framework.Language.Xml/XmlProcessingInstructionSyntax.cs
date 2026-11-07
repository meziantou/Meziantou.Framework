using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>A processing instruction: a target and the data it carries.</summary>
public sealed class XmlProcessingInstructionSyntax : XmlNodeSyntax
{
    internal XmlProcessingInstructionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StartProcessingInstructionToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxToken DataToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));
    public SyntaxToken EndProcessingInstructionToken => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));

    /// <summary>Gets the target the instruction names.</summary>
    public string Target => NameToken.Text;

    /// <summary>Gets what follows the target and the whitespace after it, or <see langword="null"/> when the instruction carries nothing.</summary>
    public string? Data => DataToken.IsMissing ? null : DataToken.Text;

    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public XmlProcessingInstructionSyntax WithTarget(string target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return string.Equals(target, Target, StringComparison.Ordinal) ? this : WithNameToken(SyntaxFactory.Identifier(target).WithTriviaFrom(NameToken));
    }

    public XmlProcessingInstructionSyntax WithData(string? data)
    {
        if (string.Equals(data, Data, StringComparison.Ordinal))
            return this;

        return WithDataToken(SyntaxFactory.ProcessingInstructionData(data));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlProcessingInstructionSyntax Update(SyntaxToken startProcessingInstructionToken, SyntaxToken nameToken, SyntaxToken dataToken, SyntaxToken endProcessingInstructionToken)
    {
        if (startProcessingInstructionToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && dataToken.Node == Green.GetSlot(2) && endProcessingInstructionToken.Node == Green.GetSlot(3))
            return this;

        return SyntaxFactory.XmlProcessingInstruction(startProcessingInstructionToken, nameToken, dataToken, endProcessingInstructionToken).WithAnnotationsFrom(this);
    }

    public XmlProcessingInstructionSyntax WithStartProcessingInstructionToken(SyntaxToken startProcessingInstructionToken) => Update(startProcessingInstructionToken, NameToken, DataToken, EndProcessingInstructionToken);
    public XmlProcessingInstructionSyntax WithNameToken(SyntaxToken nameToken) => Update(StartProcessingInstructionToken, nameToken, DataToken, EndProcessingInstructionToken);
    public XmlProcessingInstructionSyntax WithDataToken(SyntaxToken dataToken) => Update(StartProcessingInstructionToken, NameToken, dataToken, EndProcessingInstructionToken);
    public XmlProcessingInstructionSyntax WithEndProcessingInstructionToken(SyntaxToken endProcessingInstructionToken) => Update(StartProcessingInstructionToken, NameToken, DataToken, endProcessingInstructionToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitProcessingInstruction(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitProcessingInstruction(this);
    }
}
