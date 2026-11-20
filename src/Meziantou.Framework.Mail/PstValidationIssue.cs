namespace Meziantou.Framework.Mail;

/// <summary>A problem <see cref="PstFile.Validate"/> found in the structures of a file.</summary>
public sealed class PstValidationIssue
{
    internal PstValidationIssue(string message, ulong? offset = null, uint? nodeId = null, ulong? blockId = null)
    {
        Message = message;
        Offset = offset;
        NodeId = nodeId;
        BlockId = blockId;
    }

    /// <summary>Gets the description of the problem.</summary>
    public string Message { get; }

    /// <summary>Gets the offset in the file of the structure, when known.</summary>
    public ulong? Offset { get; }

    /// <summary>Gets the identifier of the node, when the problem concerns a node.</summary>
    public uint? NodeId { get; }

    /// <summary>Gets the identifier of the block or the page, when the problem concerns one.</summary>
    public ulong? BlockId { get; }

    /// <summary>Returns the description of the problem.</summary>
    /// <returns>The description of the problem.</returns>
    public override string ToString() => Message;
}
