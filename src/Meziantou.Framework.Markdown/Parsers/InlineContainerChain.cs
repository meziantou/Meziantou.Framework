using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// Tracks the open containers of the inline tree of the leaf block being processed: the root, then each last child that is
/// an open container. The deepest one receives the next inline.
/// </summary>
/// <remarks>
/// Unresolved delimiters are open containers that hold the inlines following them, so the chain can be as long as the number
/// of delimiters of the leaf block. Walking it from the root for each inline, or walking up the parents of the current inline,
/// takes quadratic time.
/// <para>
/// The chain is only engaged for the leaf blocks where it gets deep, or where a parser would walk many inlines. A container of an
/// engaged chain is flagged, and its depth is found by looking at the levels near the recent changes. A flagged container
/// notifies the chain when its children change or when it is closed. A change of its last child or its closing are the only changes that can alter the
/// chain, so the chain remains valid above the shallowest change and is only walked again below it. A container is unflagged
/// when it leaves the chain, so a container that the chain knows is the container at its depth, and its parents are the
/// containers above it.
/// </para>
/// <para>
/// The chain also keeps, for each depth, the answers that parsers compute by walking up the parents or back through the
/// previous siblings: the link delimiters above, the characters of the emphasis delimiters above, the nearest container that
/// is not a delimiter, and the last anchor HTML tag among the children that precede the chain.
/// </para>
/// </remarks>
internal sealed class InlineContainerChain
{
    private const int MaximumRetainedCapacity = 1024;

    [ThreadStatic]
    private static InlineContainerChain? s_engagedChain;

    private ContainerInline? _recentContainer;
    private int _recentDepth;
    private ContainerInline? _previousRecentContainer;
    private int _previousRecentDepth;
    private InlineContainerChain? _previousEngagedChain;
    private List<Level> _levels = [];
    private List<LinkEntry> _links = [];
    private List<int> _anchorDepths = [];
    private readonly Dictionary<(string Characters, char Character), string> _emphasisCharacters = [];
    private int _validCount;
    private int _maximumChangedDepth = -1;
    private int _linkSumsValidCount;
    private int _removedLinkCount;
    private int _firstLiveLink;
    private int _rootPipeDelimiterCount;

    /// <summary>
    /// Gets or sets the maximum depth reached by the chain, not counting the removed levels.
    /// </summary>
    public int MaximumDepth { get; set; }

    /// <summary>
    /// Gets the root of the chain, or <c>null</c> when the chain is not engaged.
    /// </summary>
    public ContainerInline? Root => _levels.Count > 0 ? _levels[0].Container : null;

    /// <summary>
    /// Starts tracking the chain of <paramref name="root"/>.
    /// </summary>
    public void Engage(ContainerInline root)
    {
        Disengage();
        _previousEngagedChain = s_engagedChain;
        s_engagedChain = this;
        Push(root);
        _validCount = 1;
    }

    /// <summary>
    /// Stops tracking the chain and unflags all its containers.
    /// </summary>
    public void Disengage()
    {
        if (_levels.Count == 0)
        {
            return;
        }

        // Engaged chains are nested when a parser parses another document, so they are normally disengaged in reverse order
        if (ReferenceEquals(s_engagedChain, this))
        {
            s_engagedChain = _previousEngagedChain;
        }
        else
        {
            for (var chain = s_engagedChain; chain is not null; chain = chain._previousEngagedChain)
            {
                if (ReferenceEquals(chain._previousEngagedChain, this))
                {
                    chain._previousEngagedChain = _previousEngagedChain;
                    break;
                }
            }
        }

        _previousEngagedChain = null;
        foreach (var level in CollectionsMarshal.AsSpan(_levels))
        {
            level.Container?.IsInOpenChain = false;
        }

        // Do not keep large buffers alive in pooled instances
        if (_levels.Capacity > MaximumRetainedCapacity || _links.Capacity > MaximumRetainedCapacity || _anchorDepths.Capacity > MaximumRetainedCapacity)
        {
            _levels = [];
            _links = [];
            _anchorDepths = [];
        }
        else
        {
            _levels.Clear();
            _links.Clear();
            _anchorDepths.Clear();
        }

        ForgetRecentContainers();

        _validCount = 0;
        _maximumChangedDepth = -1;
        _linkSumsValidCount = 0;
        _removedLinkCount = 0;
        _firstLiveLink = 0;
        _rootPipeDelimiterCount = 0;
    }

    /// <summary>
    /// Gets the deepest open container.
    /// </summary>
    public ContainerInline GetDeepestContainer()
    {
        Update();
        return _levels[^1].Container!;
    }

    /// <summary>
    /// Finds the nearest <see cref="LinkDelimiterInline"/> among an inline and its parents,
    /// like <see cref="Inline.FirstParentOfType{T}"/>.
    /// </summary>
    /// <returns><c>false</c> when the inline is not attached below the chain.</returns>
    public bool TryFindLinkDelimiter(Inline inline, out LinkDelimiterInline? linkDelimiter)
    {
        if (!TryGetDepth(inline, out var depth, out var isOnChain))
        {
            linkDelimiter = null;
            return false;
        }

        if (!isOnChain && inline is LinkDelimiterInline self)
        {
            linkDelimiter = self;
        }
        else
        {
            var index = FindLiveLinkDelimiter(_levels[depth].LinkCount - 1);
            linkDelimiter = index >= 0 ? _links[index].Delimiter : null;
        }

        return true;
    }

    /// <summary>
    /// Determines whether an inline or one of its parents is an active <see cref="LinkDelimiterInline"/>.
    /// </summary>
    /// <returns><c>false</c> when the inline is not attached below the chain.</returns>
    public bool TryHasActiveLinkDelimiter(Inline inline, out bool hasActiveLinkDelimiter)
    {
        if (!TryGetDepth(inline, out var depth, out var isOnChain))
        {
            hasActiveLinkDelimiter = false;
            return false;
        }

        var linkCount = _levels[depth].LinkCount;
        EnsureLinkSums(linkCount);
        hasActiveLinkDelimiter = (!isOnChain && inline is LinkDelimiterInline { IsActive: true }) || (linkCount > 0 && _links[linkCount - 1].ActiveCount > 0);
        return true;
    }

    /// <summary>
    /// Deactivates an inline and its parents that are link delimiters. Image delimiters are left active.
    /// </summary>
    /// <returns><c>false</c> when the inline is not attached below the chain.</returns>
    public bool TryDeactivateLinkDelimiters(Inline inline)
    {
        if (!TryGetDepth(inline, out var depth, out var isOnChain))
        {
            return false;
        }

        if (!isOnChain && inline is LinkDelimiterInline { IsImage: false } self)
        {
            self.IsActive = false;
        }

        // Delimiters that are already deactivated, and image delimiters, are skipped, so each delimiter is visited once over
        // the whole leaf block
        var index = FindDelimiterToDeactivate(_levels[depth].LinkCount - 1);
        while (index >= 0)
        {
            var linkDelimiter = _links[index].Delimiter;
            if (linkDelimiter is { IsImage: false })
            {
                // The index of the delimiter is known, so it does not need to be notified
                linkDelimiter.IsInOpenChain = false;
                linkDelimiter.IsActive = false;
                linkDelimiter.IsInOpenChain = true;
                _linkSumsValidCount = Math.Min(_linkSumsValidCount, index);
            }

            CollectionsMarshal.AsSpan(_links)[index].Next = index - 1;
            index = FindDelimiterToDeactivate(index - 1);
        }

        return true;
    }

    /// <summary>
    /// Gets what the autolink parser looks for before an inline: the first anchor HTML tag met walking back through the
    /// inline, its previous siblings and its parents, and, among the inline and its parents, the balance of the active link
    /// delimiters and the characters of the emphasis delimiters.
    /// </summary>
    /// <returns><c>false</c> when the inline is not the chain or the last child of a container of the chain.</returns>
    public bool TryGetAutoLinkContext(Inline inline, out HtmlInline? anchor, out int linkDelimiterBalance, out string emphasisCharacters)
    {
        anchor = null;
        linkDelimiterBalance = 0;
        emphasisCharacters = "";
        if (!TryGetDepth(inline, out var depth, out var isOnChain))
        {
            return false;
        }

        var level = _levels[depth];
        int anchorDepth;
        if (isOnChain)
        {
            // The previous siblings of a container of the chain are the other children of the container above it
            anchorDepth = depth - 1;
        }
        else
        {
            // Only the previous siblings of the last child are tracked
            if (!ReferenceEquals(inline, level.Container!.LastChild))
            {
                return false;
            }

            anchorDepth = depth;
        }

        var linkCount = level.LinkCount;
        EnsureLinkSums(linkCount);
        linkDelimiterBalance = linkCount > 0 ? _links[linkCount - 1].LinkDelimiterBalance : 0;
        emphasisCharacters = level.EmphasisCharacters;
        if (!isOnChain)
        {
            if (inline is LinkDelimiterInline { IsActive: true } linkDelimiter)
            {
                linkDelimiterBalance += GetLinkDelimiterBalance(linkDelimiter);
            }
            else if (inline is EmphasisDelimiterInline emphasisDelimiter && !emphasisCharacters.Contains(emphasisDelimiter.DelimiterChar, StringComparison.Ordinal))
            {
                emphasisCharacters += emphasisDelimiter.DelimiterChar;
            }
        }

        anchor = !isOnChain && inline is HtmlInline html && IsAnchorCandidate(html) ? html : FindAnchor(anchorDepth);
        return true;
    }

    /// <summary>
    /// Finds the nearest parent of an inline that is not a <see cref="DelimiterInline"/>.
    /// </summary>
    /// <returns><c>false</c> when the parent of the inline is not attached below the chain.</returns>
    public bool TryFindNonDelimiterParent(Inline inline, out ContainerInline? parent)
    {
        parent = inline.Parent;
        if (parent is null)
        {
            return true;
        }

        if (!TryGetDepth(parent, out var depth, out var isOnChain))
        {
            return false;
        }

        if (isOnChain || parent is DelimiterInline)
        {
            var nonDelimiterDepth = _levels[depth].NonDelimiterDepth;
            parent = nonDelimiterDepth >= 0 ? _levels[nonDelimiterDepth].Container : null;
        }

        return true;
    }

    /// <summary>
    /// Determines whether an inline or one of its parents is a <see cref="PipeTableDelimiterInline"/>, or whether its top
    /// parent has one as a child, like <see cref="Inline.ContainsParentOrSiblingOfType{T}"/>.
    /// </summary>
    /// <returns><c>false</c> when the inline is not attached below the chain.</returns>
    public bool TryContainsPipeTableDelimiter(Inline inline, out bool containsPipeTableDelimiter)
    {
        containsPipeTableDelimiter = false;
        if (!TryGetDepth(inline, out var depth, out var isOnChain) || _levels[0].Container!.Parent is not null)
        {
            return false;
        }

        // The root has no parent, so its children are not checked when the inline is the root
        containsPipeTableDelimiter = inline is PipeTableDelimiterInline ||
            _levels[depth].PipeDelimiterCount > 0 ||
            ((!isOnChain || depth > 0) && _rootPipeDelimiterCount > 0);
        return true;
    }


    internal static void OnClosedChanged(ContainerInline container)
    {
        if (!TryFind(container, out var chain, out var depth))
        {
            return;
        }

        // Whether the root is closed does not matter
        if (depth > 0 && depth < chain._validCount)
        {
            chain._validCount = depth;
        }

        chain._maximumChangedDepth = Math.Max(chain._maximumChangedDepth, depth);
    }

    internal static void OnChildInserted(ContainerInline container, Inline child)
    {
        if (TryFind(container, out var chain, out var depth))
        {
            if (ReferenceEquals(child, container.LastChild))
            {
                chain.LastChildChanged(depth);
            }

            chain.ChildInserted(container, child, depth);
        }
    }

    internal static void OnChildRemoved(ContainerInline container, Inline child)
    {
        if (TryFind(container, out var chain, out var depth))
        {
            // The child is still linked to its next sibling, so it was the last child when it has none
            if (child.NextSibling is null)
            {
                chain.LastChildChanged(depth);
            }

            chain.ChildRemoved(container, child, depth);
        }
    }

    internal static void OnChildrenCleared(ContainerInline container)
    {
        if (TryFind(container, out var chain, out var depth))
        {
            chain.LastChildChanged(depth);
            if (depth == 0)
            {
                chain._rootPipeDelimiterCount = 0;
            }

            chain.SetAnchor(depth, anchor: null, isUnknown: false);
        }
    }

    internal static void OnChildChanged(ContainerInline container, HtmlInline child)
    {
        if (TryFind(container, out var chain, out var depth) && (ReferenceEquals(chain._levels[depth].Anchor, child) || IsAnchorCandidate(child)))
        {
            chain.SetAnchor(depth, anchor: null, isUnknown: true);
        }
    }

    internal static void OnChildrenMoved(ContainerInline container, Inline? previousLastChild, Inline firstChild, Inline lastChild)
    {
        if (!TryFind(container, out var chain, out var depth))
        {
            return;
        }

        if (!ReferenceEquals(container.LastChild, previousLastChild))
        {
            chain.LastChildChanged(depth);
        }

        // Like ChildInserted for each child. The previous last child may now precede the last child.
        var hasAnchorCandidate = previousLastChild is HtmlInline previousHtml && IsAnchorCandidate(previousHtml);
        for (var child = firstChild; ; child = child.NextSibling!)
        {
            if (depth == 0 && child is PipeTableDelimiterInline)
            {
                chain._rootPipeDelimiterCount++;
            }

            hasAnchorCandidate |= child is HtmlInline html && IsAnchorCandidate(html);
            if (ReferenceEquals(child, lastChild))
            {
                break;
            }
        }

        if (hasAnchorCandidate)
        {
            chain.SetAnchor(depth, anchor: null, isUnknown: true);
        }
    }

    internal static void OnContainerDetached(ContainerInline container)
    {
        // Removing the container from its parent already invalidated its level, which is removed or emptied by the next
        // update, so the changes of its children do not matter
        if (TryFind(container, out var chain, out var depth) && depth > 0 && depth >= chain._validCount)
        {
            container.IsInOpenChain = false;
        }
    }

    internal static void OnLinkDelimiterChanged(LinkDelimiterInline linkDelimiter, bool resetDeactivation)
    {
        if (TryFind(linkDelimiter, out var chain, out var depth))
        {
            chain.LinkDelimiterChanged(depth, resetDeactivation);
        }
    }

    private static bool TryFind(ContainerInline container, [NotNullWhen(true)] out InlineContainerChain? chain, out int depth)
    {
        for (chain = s_engagedChain; chain is not null; chain = chain._previousEngagedChain)
        {
            if (chain.TryGetContainerDepth(container, out depth))
            {
                return true;
            }
        }

        depth = -1;
        return false;
    }

    // Finds the depth of a flagged container. Inlines are mostly added to the deepest container, and the other changes are
    // made near the previous change, so the levels around them are looked at first.
    private bool TryGetContainerDepth(ContainerInline container, out int depth)
    {
        var levels = CollectionsMarshal.AsSpan(_levels);
        if (levels.IsEmpty)
        {
            depth = -1;
            return false;
        }

        depth = levels.Length - 1;
        if (ReferenceEquals(levels[depth].Container, container))
        {
            return true;
        }

        if (_recentContainer is not null)
        {
            // Children are often moved from one container to another
            if (ReferenceEquals(_recentContainer, container))
            {
                depth = _recentDepth;
                return true;
            }

            if (ReferenceEquals(_previousRecentContainer, container))
            {
                depth = _previousRecentDepth;
                return true;
            }

            // The parent of the recent container, or its child
            if (_recentDepth > 0 && IsAt(levels, depth = FindLevel(_recentDepth - 1), container))
            {
                return Remember(container, depth);
            }

            if (IsAt(levels, depth = FindNextLevel(_recentDepth + 1), container))
            {
                return Remember(container, depth);
            }
        }

        if (_validCount > 0 && (IsAt(levels, depth = FindLevel(Math.Min(_validCount, levels.Length) - 1), container) || IsAt(levels, depth = FindNextLevel(_validCount), container)))
        {
            return Remember(container, depth);
        }

        for (depth = levels.Length - 2; depth >= 0; depth--)
        {
            if (ReferenceEquals(levels[depth].Container, container))
            {
                return Remember(container, depth);
            }
        }

        return false;

        static bool IsAt(Span<Level> levels, int depth, ContainerInline container)
        {
            return (uint)depth < (uint)levels.Length && ReferenceEquals(levels[depth].Container, container);
        }
    }

    private bool Remember(ContainerInline container, int depth)
    {
        _previousRecentContainer = _recentContainer;
        _previousRecentDepth = _recentDepth;
        _recentContainer = container;
        _recentDepth = depth;
        return true;
    }

    // The depths of the removed levels are not valid anymore
    private void ForgetRecentContainers()
    {
        _recentContainer = null;
        _previousRecentContainer = null;
    }

    // The last child of a container is the only change that can alter the containers below it in the chain
    private void LastChildChanged(int depth)
    {
        // The container keeps its depth, the containers below it may change. Nothing changes when there is none, such as
        // when an inline is appended to the deepest container.
        if (depth + 1 < _levels.Count)
        {
            _validCount = Math.Min(_validCount, depth + 1);
            _maximumChangedDepth = Math.Max(_maximumChangedDepth, depth);
        }
    }

    private void ChildInserted(ContainerInline container, Inline child, int depth)
    {
        if (depth == 0 && child is PipeTableDelimiterInline)
        {
            _rootPipeDelimiterCount++;
        }

        if (ReferenceEquals(child, container.LastChild))
        {
            // The previous last child now precedes the last child
            if (!ReferenceEquals(child, container.FirstChild) && child.PreviousSibling is HtmlInline previous && IsAnchorCandidate(previous))
            {
                SetAnchor(depth, previous, isUnknown: false);
            }
        }
        else if (child is HtmlInline html && IsAnchorCandidate(html))
        {
            SetAnchor(depth, anchor: null, isUnknown: true);
        }
    }

    private void ChildRemoved(ContainerInline container, Inline child, int depth)
    {
        if (depth == 0 && child is PipeTableDelimiterInline)
        {
            _rootPipeDelimiterCount--;
        }

        // Removing the last child makes its previous sibling the last child, which is not before the last child anymore
        var anchor = _levels[depth].Anchor;
        if (anchor is not null && (ReferenceEquals(anchor, child) || ReferenceEquals(anchor, container.LastChild)))
        {
            SetAnchor(depth, anchor: null, isUnknown: true);
        }
    }

    private void LinkDelimiterChanged(int depth, bool resetDeactivation)
    {
        var index = _levels[depth].LinkCount - 1;
        if (index < _linkSumsValidCount)
        {
            _linkSumsValidCount = index;
        }

        // A delimiter that is active again, or whose image flag changed, must not be skipped anymore
        if (resetDeactivation)
        {
            var links = CollectionsMarshal.AsSpan(_links);
            for (var i = 0; i < links.Length; i++)
            {
                links[i].Next = i;
            }
        }
    }

    // The autolink parser stops at the first HTML inline starting with "</a" or "<a" (the second includes tags such as <abbr>).
    // A tag that is null is kept so that the autolink parser fails on it the same way.
    private static bool IsAnchorCandidate(HtmlInline html)
    {
        var tag = html.Tag;
        return tag is null || tag.StartsWith("</a", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetLinkDelimiterBalance(LinkDelimiterInline linkDelimiter)
    {
        return linkDelimiter.Type switch
        {
            DelimiterType.Open => 1,
            DelimiterType.Close => -1,
            _ => 0,
        };
    }

    private bool TryGetDepth(Inline inline, out int depth, out bool isOnChain)
    {
        if (TryGetValidDepth(inline, out depth, out isOnChain))
        {
            return true;
        }

        if (_levels.Count == 0)
        {
            return false;
        }

        Update();
        return TryGetValidDepth(inline, out depth, out isOnChain);
    }

    // When it returns true, the parents of the inline are the containers of the chain up to depth; when isOnChain is true,
    // the inline is the container at depth.
    private bool TryGetValidDepth(Inline inline, out int depth, out bool isOnChain)
    {
        if (inline is ContainerInline { IsInOpenChain: true } container && TryGetContainerDepth(container, out depth) && depth < _validCount)
        {
            isOnChain = true;
            return true;
        }

        if (inline.Parent is { IsInOpenChain: true } parent && TryGetContainerDepth(parent, out depth) && depth < _validCount)
        {
            isOnChain = false;
            return true;
        }

        depth = -1;
        isOnChain = false;
        return false;
    }

    private void Update()
    {
        if (_validCount < _levels.Count)
        {
            // Removed containers do not end the valid part of the chain
            _validCount = FindLevel(_validCount - 1) + 1;

            if (!TryReconnect())
            {
                Truncate(_validCount);
            }
        }

        var container = _levels[^1].Container!;
        while (container.LastChild is ContainerInline { IsClosed: false } child)
        {
            container = child;
            Push(container);
        }

        _validCount = _levels.Count;
        _maximumChangedDepth = -1;
    }

    // When a link delimiter is replaced by a literal, its children are moved to its parent: the containers below it are
    // unchanged and are its children again. Keep them instead of walking them again, which would be quadratic when
    // delimiters are replaced one after the other above many containers. The removed levels are left empty so that the
    // depths of the containers below them do not change.
    private bool TryReconnect()
    {
        var parent = _levels[_validCount - 1].Container!;
        if (parent.LastChild is not ContainerInline { IsClosed: false, IsInOpenChain: true } child)
        {
            return false;
        }

        // The child must be below the removed link delimiters
        var depth = FindNextLevel(_validCount);
        while (depth < _levels.Count && !ReferenceEquals(_levels[depth].Container, child))
        {
            if (_levels[depth].Container is not LinkDelimiterInline)
            {
                return false;
            }

            depth = FindNextLevel(depth + 1);
        }

        // The containers from the child down are unchanged when none of them changed its last child or was closed
        if (depth >= _levels.Count || depth <= _validCount || _maximumChangedDepth >= depth)
        {
            return false;
        }

        for (var i = FindLevel(depth - 1); i >= _validCount; i = FindLevel(i - 1))
        {
            ref var level = ref CollectionsMarshal.AsSpan(_levels)[i];
            level.Container!.IsInOpenChain = false;

            var index = level.LinkCount - 1;
            ref var link = ref CollectionsMarshal.AsSpan(_links)[index];
            link.Delimiter = null;
            link.Live = index - 1;
            link.Next = index - 1;
            _removedLinkCount++;
            while (_firstLiveLink < _links.Count && _links[_firstLiveLink].Delimiter is null)
            {
                _firstLiveLink++;
            }

            if (index < _linkSumsValidCount)
            {
                _linkSumsValidCount = index;
            }

            SetAnchor(i, anchor: null, isUnknown: false);
            level.Container = null;
            level.Previous = i - 1;
            level.Next = i + 1;
        }

        ForgetRecentContainers();

        _validCount = _levels.Count;
        return true;
    }

    private void Push(ContainerInline container)
    {
        var depth = _levels.Count;
        var level = depth == 0 ? new Level { NonDelimiterDepth = -1, EmphasisCharacters = "" } : _levels[depth - 1];
        level.Container = container;
        level.Previous = depth;
        if (container is not DelimiterInline)
        {
            level.NonDelimiterDepth = depth;
        }

        // The removed levels are all above this one
        var liveDepth = depth - _removedLinkCount;
        if (depth > 0)
        {
            // Unresolved emphasis delimiters are resolved into a flat tree, so a long chain of them is fine. The other
            // containers, and all the containers below a link delimiter, are rejected when their chain gets as deep as it was
            // rejected before: removing a link delimiter moves all its children, so a deep chain costs quadratic time.
            // The checks are done before anything changes, so the chain is still valid when the exception is caught.
            if (container is not EmphasisDelimiterInline)
            {
                level.NestingDepth++;
                ThrowHelper.CheckDepthLimit(level.NestingDepth - _removedLinkCount, useLargeLimit: true);
            }

            // The removed link delimiters above the first one that is not removed are the ones before it
            var firstLiveLinkDepth = _firstLiveLink < _links.Count ? _links[_firstLiveLink].Depth - _firstLiveLink : -1;
            if (firstLiveLinkDepth >= 0)
            {
                ThrowHelper.CheckDepthLimit(liveDepth - firstLiveLinkDepth + 1, useLargeLimit: true);

                // Nested link delimiters cost quadratic time too. They are rejected as before when the chain is as deep as it
                // was rejected before, which is where the inlines were rejected anyway.
                if (container is LinkDelimiterInline)
                {
                    ThrowHelper.CheckDepthLimit(liveDepth, useLargeLimit: true);
                }
            }
        }

        switch (container)
        {
            case EmphasisDelimiterInline emphasisDelimiter when !level.EmphasisCharacters.Contains(emphasisDelimiter.DelimiterChar, StringComparison.Ordinal):
                level.EmphasisCharacters = AddEmphasisCharacter(level.EmphasisCharacters, emphasisDelimiter.DelimiterChar);
                break;

            case PipeTableDelimiterInline:
                level.PipeDelimiterCount++;
                break;

            case LinkDelimiterInline linkDelimiter:
                _links.Add(new LinkEntry { Delimiter = linkDelimiter, Depth = depth, Live = _links.Count, Next = _links.Count });
                level.LinkCount = _links.Count;
                break;
        }

        if (depth == 0)
        {
            for (var child = container.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is PipeTableDelimiterInline)
                {
                    _rootPipeDelimiterCount++;
                }
            }
        }

        container.IsInOpenChain = true;
        level.Next = depth;

        // Finding the last anchor among several children is deferred until it is needed
        level.Anchor = null;
        level.IsAnchorUnknown = container.FirstChild is not null && !ReferenceEquals(container.FirstChild, container.LastChild);
        _levels.Add(level);
        if (level.IsAnchorUnknown)
        {
            _anchorDepths.Add(depth);
        }

        MaximumDepth = Math.Max(MaximumDepth, liveDepth);
    }

    // The same few sets of characters are built again and again, so they are only allocated once
    private string AddEmphasisCharacter(string characters, char character)
    {
        if (!_emphasisCharacters.TryGetValue((characters, character), out var result))
        {
            if (_emphasisCharacters.Count >= 256)
            {
                _emphasisCharacters.Clear();
            }

            result = characters + character;
            _emphasisCharacters.Add((characters, character), result);
        }

        return result;
    }

    private void Truncate(int count)
    {
        var levels = CollectionsMarshal.AsSpan(_levels);
        for (var i = count; i < levels.Length; i++)
        {
            levels[i].Container?.IsInOpenChain = false;
        }

        _levels.RemoveRange(count, levels.Length - count);
        ForgetRecentContainers();

        var linkCount = _levels[count - 1].LinkCount;
        foreach (var link in CollectionsMarshal.AsSpan(_links)[linkCount..])
        {
            if (link.Delimiter is null)
            {
                _removedLinkCount--;
            }
        }

        _links.RemoveRange(linkCount, _links.Count - linkCount);
        _firstLiveLink = Math.Min(_firstLiveLink, linkCount);
        if (_linkSumsValidCount > linkCount)
        {
            _linkSumsValidCount = linkCount;
        }

        while (_anchorDepths.Count > 0 && _anchorDepths[^1] >= count)
        {
            _anchorDepths.RemoveAt(_anchorDepths.Count - 1);
        }
    }

    private void EnsureLinkSums(int count)
    {
        var links = CollectionsMarshal.AsSpan(_links);
        for (var i = _linkSumsValidCount; i < count; i++)
        {
            var linkDelimiter = links[i].Delimiter;
            var activeCount = i > 0 ? links[i - 1].ActiveCount : 0;
            var balance = i > 0 ? links[i - 1].LinkDelimiterBalance : 0;
            if (linkDelimiter is { IsActive: true })
            {
                activeCount++;
                balance += GetLinkDelimiterBalance(linkDelimiter);
            }

            links[i].ActiveCount = activeCount;
            links[i].LinkDelimiterBalance = balance;
        }

        if (count > _linkSumsValidCount)
        {
            _linkSumsValidCount = count;
        }
    }

    private int FindLevel(int index)
    {
        var levels = CollectionsMarshal.AsSpan(_levels);
        var result = index;
        while (levels[result].Previous != result)
        {
            result = levels[result].Previous;
        }

        while (index > result)
        {
            var next = levels[index].Previous;
            levels[index].Previous = result;
            index = next;
        }

        return result;
    }

    // Finds the first level at or below this depth whose container is not removed, or the number of levels
    private int FindNextLevel(int index)
    {
        var levels = CollectionsMarshal.AsSpan(_levels);
        var result = index;
        while ((uint)result < (uint)levels.Length && levels[result].Next != result)
        {
            result = levels[result].Next;
        }

        while (index < result && index < levels.Length)
        {
            var next = levels[index].Next;
            levels[index].Next = result;
            index = next;
        }

        return Math.Min(result, levels.Length);
    }

    private int FindLiveLinkDelimiter(int index)
    {
        var links = CollectionsMarshal.AsSpan(_links);
        var result = index;
        while (result >= 0 && links[result].Live != result)
        {
            result = links[result].Live;
        }

        while (index > result)
        {
            var next = links[index].Live;
            links[index].Live = result;
            index = next;
        }

        return result;
    }

    private int FindDelimiterToDeactivate(int index)
    {
        var links = CollectionsMarshal.AsSpan(_links);
        var result = index;
        while (result >= 0 && links[result].Next != result)
        {
            result = links[result].Next;
        }

        // Point the visited delimiters directly to the result
        while (index > result)
        {
            var next = links[index].Next;
            links[index].Next = result;
            index = next;
        }

        return result;
    }

    private HtmlInline? FindAnchor(int maximumDepth)
    {
        var index = _anchorDepths.BinarySearch(maximumDepth);
        index = index >= 0 ? index : ~index - 1;
        while (index >= 0)
        {
            var depth = _anchorDepths[index];
            ref var level = ref CollectionsMarshal.AsSpan(_levels)[depth];
            if (level.IsAnchorUnknown)
            {
                ResolveAnchor(depth);
            }

            if (level.Anchor is { } anchor)
            {
                return anchor;
            }

            index--;
        }

        return null;
    }

    private void ResolveAnchor(int depth)
    {
        var container = _levels[depth].Container!;
        for (var child = container.LastChild?.PreviousSibling; child is not null; child = child.PreviousSibling)
        {
            if (child is HtmlInline html && IsAnchorCandidate(html))
            {
                SetAnchor(depth, html, isUnknown: false);
                return;
            }
        }

        SetAnchor(depth, anchor: null, isUnknown: false);
    }

    private void SetAnchor(int depth, HtmlInline? anchor, bool isUnknown)
    {
        ref var level = ref CollectionsMarshal.AsSpan(_levels)[depth];
        var wasTracked = level.Anchor is not null || level.IsAnchorUnknown;
        var isTracked = anchor is not null || isUnknown;
        level.Anchor = anchor;
        level.IsAnchorUnknown = isUnknown;
        if (wasTracked != isTracked)
        {
            var index = _anchorDepths.BinarySearch(depth);
            if (isTracked)
            {
                _anchorDepths.Insert(~index, depth);
            }
            else
            {
                _anchorDepths.RemoveAt(index);
            }
        }
    }

    private struct Level
    {
        // Null when the container was removed from the middle of the chain
        public ContainerInline? Container;

        // Depth of the nearest level at or above this depth whose container is not removed
        public int Previous;

        // Depth of the nearest level at or below this depth whose container is not removed
        public int Next;

        // Number of containers at or above this depth, the root excluded, that are not emphasis delimiters, including the
        // removed ones
        public int NestingDepth;

        // Depth of the nearest container at or above this depth that is not a delimiter, or -1
        public int NonDelimiterDepth;

        // Characters of the emphasis delimiters at or above this depth
        public string EmphasisCharacters;

        // Number of pipe table delimiters at or above this depth
        public int PipeDelimiterCount;

        // Number of link delimiters at or above this depth, which is the index after the nearest one in _links
        public int LinkCount;

        // Last anchor HTML tag among the children of the container, except its last child
        public HtmlInline? Anchor;
        public bool IsAnchorUnknown;
    }

    private struct LinkEntry
    {
        // Null when the delimiter was removed from the middle of the chain
        public LinkDelimiterInline? Delimiter;

        public int Depth;

        // Index of the nearest delimiter that is not removed: itself, or a lower index
        public int Live;

        // Index of the next delimiter to visit when deactivating from this one: itself, or a lower index when this one and the
        // ones down to that index are deactivated links or images
        public int Next;

        // Number of active delimiters and balance of the active open and close delimiters up to this one
        public int ActiveCount;
        public int LinkDelimiterBalance;
    }
}
