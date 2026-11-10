namespace Meziantou.Framework.Diagrams;

/// <summary>Makes a branch the current branch of a Git graph (Mermaid <c>checkout</c> or <c>switch</c>).</summary>
public sealed class GitGraphCheckout : GitGraphOperation
{
    /// <summary>Initializes a new instance of the <see cref="GitGraphCheckout"/> class.</summary>
    /// <param name="branch">The name of the branch.</param>
    public GitGraphCheckout(string branch)
    {
        ArgumentException.ThrowIfNullOrEmpty(branch);
        Branch = branch;
    }

    /// <summary>Gets the name of the branch.</summary>
    public string Branch { get; }
}
