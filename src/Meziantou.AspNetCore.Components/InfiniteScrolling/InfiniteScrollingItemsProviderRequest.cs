namespace Meziantou.AspNetCore.Components;

/// <summary>Represents a request for loading items in an infinite scrolling component.</summary>
public sealed class InfiniteScrollingItemsProviderRequest
{
    /// <summary>Initializes a new instance of the <see cref="InfiniteScrollingItemsProviderRequest"/> class.</summary>
    /// <param name="startIndex">The starting index for the items to load.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the request.</param>
    public InfiniteScrollingItemsProviderRequest(int startIndex, CancellationToken cancellationToken)
    {
        StartIndex = startIndex;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the starting index for the items to load.</summary>
    public int StartIndex { get; }

    /// <summary>Gets the cancellation token that can be used to cancel the request.</summary>
    public CancellationToken CancellationToken { get; }
}
