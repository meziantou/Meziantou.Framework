using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

internal static partial class Accessibility
{
    /// <summary>
    /// Captures the accessibility tree rooted at an element (<see cref="Element.CaptureAccessibilityTree"/>), as Playwright
    /// builds its aria snapshots: the hidden subtrees are excluded, the elements without a role, the presentational elements
    /// and the generic elements are replaced by their children, and the adjacent texts are merged.
    /// </summary>
    public static AccessibilityNode? CaptureTree(Element root)
    {
        var cache = new ComputationCache(root);
        if (cache.IsHiddenInTree(root))
            return null;

        return new TreeBuilder(root, cache).Build();
    }

    private sealed class TreeBuilder(Element root, ComputationCache cache)
    {
        private readonly HashSet<Node> _visited = [];
        private readonly Stack<Frame> _frames = new();
        private NameComputation? _pseudoContent;

        private NameComputation PseudoContent => _pseudoContent ??= new NameComputation(root, cache);

        public AccessibilityNode Build()
        {
            _visited.Add(root);
            var rootNode = new PendingNode(root, GetProperties(root, GetRole(root), cache, isHidden: false));
            Enter(root, rootNode, parent: null, ownsTarget: true);

            // The traversal uses an explicit stack: a deep document does not overflow the call stack
            AccessibilityNode? result = null;
            while (_frames.Count > 0)
            {
                var frame = _frames.Peek();
                if (frame.Children.MoveNext())
                {
                    var child = frame.Children.Current;
                    if (!_visited.Add(child))
                        continue;

                    if (child is Text text)
                    {
                        // The content of a text field is its value
                        if (frame.Target.Properties.Role != "textbox")
                        {
                            frame.Target.AddText(text.Data, text);
                        }
                    }
                    else if (child is Element element)
                    {
                        Visit(element, frame.Target);
                    }

                    continue;
                }

                _frames.Pop();
                frame.Target.AddText(PseudoContent.GetPseudoContent(frame.Element, "after"), frame.Element);
                if (frame.IsBlock)
                {
                    frame.Target.AddText(" ", frame.Element);
                }

                if (frame.OwnsTarget)
                {
                    var node = frame.Target.ToNode();
                    if (frame.Parent is null)
                    {
                        result = node;
                    }
                    else
                    {
                        frame.Parent.AddChild(node);
                    }
                }
            }

            return result!;
        }

        private void Visit(Element element, PendingNode parent)
        {
            // As in Playwright, the subtree of a hidden element is excluded, even its descendants with visibility: visible
            if (cache.IsHiddenInTree(element))
                return;

            var role = GetRole(element);
            if (role is "" or "none" or "generic")
            {
                Enter(element, parent, parent: null, ownsTarget: false);
                return;
            }

            var node = new PendingNode(element, GetProperties(element, role, cache, isHidden: false));
            Enter(element, node, parent, ownsTarget: true);

            // The value of a text field, as Playwright exposes it
            switch (element)
            {
                case HtmlInputElement { InputType: not (InputType.Checkbox or InputType.Radio or InputType.File) } input:
                    node.AddText(input.Value, element);
                    break;
                case HtmlTextAreaElement textarea:
                    node.AddText(textarea.Value, element);
                    break;
            }
        }

        private void Enter(Element element, PendingNode target, PendingNode? parent, bool ownsTarget)
        {
            var isBlock = IsBlock(element);
            if (isBlock)
            {
                target.AddText(" ", element);
            }

            target.AddText(PseudoContent.GetPseudoContent(element, "before"), element);
            _frames.Push(new Frame(element, target, parent, ownsTarget, isBlock, cache.GetChildren(element).GetEnumerator()));
        }

        /// <summary>Whether the content of the element is separated from the adjacent text (a line break or a box that is not inline).</summary>
        private static bool IsBlock(Element element)
        {
            if (element.IsHtml(TagId.Br))
                return true;

            if (!HasStyle(element))
                return !NameComputation.IsInline(element);

            return element.NodeDocument.GetStyleEngine().GetStyle(element).GetComputedValue("display") != "inline";
        }
    }

    /// <summary>An element of the traversal: the node that receives its content, and its remaining children.</summary>
    private sealed class Frame(Element element, PendingNode target, PendingNode? parent, bool ownsTarget, bool isBlock, AccessibleChildren.Enumerator children)
    {
        private AccessibleChildren.Enumerator _children = children;

        public Element Element { get; } = element;

        /// <summary>The node of the element, or of its nearest ancestor that is a node.</summary>
        public PendingNode Target { get; } = target;

        /// <summary>The node that receives <see cref="Target"/> when the element owns it.</summary>
        public PendingNode? Parent { get; } = parent;

        public bool OwnsTarget { get; } = ownsTarget;

        public bool IsBlock { get; } = isBlock;

        public ref AccessibleChildren.Enumerator Children => ref _children;
    }

    /// <summary>A node whose children are being collected.</summary>
    private sealed class PendingNode(Element element, AccessibilityProperties properties)
    {
        private readonly List<AccessibilityNode> _children = [];
        private readonly StringBuilder _text = new();
        private Node? _textSource;

        public AccessibilityProperties Properties { get; } = properties;

        public void AddText(string text, Node source)
        {
            if (text.Length == 0)
                return;

            if (_textSource is null && !string.IsNullOrWhiteSpace(text))
            {
                _textSource = source;
            }

            _text.Append(text);
        }

        public void AddChild(AccessibilityNode node)
        {
            FlushText();
            _children.Add(node);
        }

        public AccessibilityNode ToNode()
        {
            FlushText();

            // A text that repeats the name is not a child (e.g. the name from content of a button)
            if (_children is [{ Properties.Role: "text" } text] && text.Properties.Name == Properties.Name)
            {
                _children.Clear();
            }

            var node = new AccessibilityNode(element, Properties);
            node.SetChildren(_children);
            return node;
        }

        private void FlushText()
        {
            var text = AriaSnapshotWriter.NormalizeWhiteSpace(_text.ToString());
            if (text.Length > 0)
            {
                _children.Add(new AccessibilityNode(_textSource ?? element, AccessibilityProperties.ForText(text)));
            }

            _text.Clear();
            _textSource = null;
        }
    }
}
