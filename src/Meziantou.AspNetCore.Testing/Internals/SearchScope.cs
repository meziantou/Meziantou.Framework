using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>The nodes that a locator searches: the roots of the page or of a component (searched with themselves), or the elements found by the previous step (searched without themselves).</summary>
internal readonly record struct SearchScope(IReadOnlyList<Node> Nodes, bool IncludeNodes);
