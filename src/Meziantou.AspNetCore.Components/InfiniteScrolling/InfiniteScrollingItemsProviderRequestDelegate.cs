namespace Meziantou.AspNetCore.Components;

/// <summary>A delegate for providing items in an infinite scrolling component.</summary>
/// <typeparam name="T">The type of items to provide.</typeparam>
/// <param name="context">The request context containing the starting index and cancellation token.</param>
/// <returns>A task that represents the asynchronous operation. The task result contains the items to display.</returns>
public delegate Task<IEnumerable<T>> InfiniteScrollingItemsProviderRequestDelegate<T>(InfiniteScrollingItemsProviderRequest context);
