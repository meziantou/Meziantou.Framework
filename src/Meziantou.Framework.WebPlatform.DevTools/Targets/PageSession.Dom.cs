using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.DevTools.Runtime;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

// The DOM domain: the nodes are identified by their backendNodeId (the nodeId of the DOM agent of Chromium is not supported)
internal sealed partial class PageSession
{
    private JsonObject? InvokeDom(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "DOM.enable":
            case "DOM.disable":
            case "DOM.setInspectedNode":
            case "DOM.markUndoableState":
                return null;

            case "DOM.describeNode":
                return new JsonObject { ["node"] = DescribeNode(GetNode(parameters), parameters.GetInt32("depth") ?? 1) };

            case "DOM.resolveNode":
                return ResolveNode(parameters);

            case "DOM.getContentQuads":
                return GetContentQuads(parameters);

            case "DOM.getBoxModel":
                return GetBoxModel(parameters);

            case "DOM.scrollIntoViewIfNeeded":
                ScrollIntoViewIfNeeded(parameters);
                return null;

            case "DOM.getFrameOwner":
                var frame = Target.GetFrame(parameters.GetRequiredString("frameId"));
                if (frame.BrowsingContext?.Container is not { } container)
                    throw new ProtocolException("Frame with the given id does not belong to the target.");

                return new JsonObject { ["backendNodeId"] = Target.Nodes.GetId(container), ["nodeId"] = 0 };

            case "DOM.setFileInputFiles":
                SetFileInputFiles(parameters);
                return null;

            case "DOM.focus":
                if (GetNode(parameters) is HtmlElement focusable)
                {
                    focusable.Focus();
                }

                return null;

            case "DOM.getDocument":
                Target.EnsureLive();
                return new JsonObject { ["root"] = DescribeNode(Target.MainFrame.Document!, parameters.GetInt32("depth") ?? 1) };

            default:
                throw ProtocolException.NotFound(method);
        }
    }

    /// <summary>The node of the parameters of a command: <c>objectId</c> or <c>backendNodeId</c>.</summary>
    private Node GetNode(JsonElement parameters)
    {
        Target.EnsureLive();
        if (parameters.GetString("objectId") is { } objectId)
        {
            var (context, value) = Objects.Get(objectId);
            return context.FromJsValue(value) as Node ?? throw new ProtocolException("Object id doesn't reference a Node");
        }

        if (parameters.GetInt32("backendNodeId") is { } backendNodeId)
            return Target.Nodes.Find(backendNodeId) ?? throw new ProtocolException("No node found for given backend id");

        if (parameters.GetInt32("nodeId") is not null)
            throw new ProtocolException("Could not find node with given id");

        throw new ProtocolException("Either nodeId, backendNodeId or objectId must be specified");
    }

    private JsonObject DescribeNode(Node node, int depth)
    {
        var result = new JsonObject
        {
            ["nodeId"] = 0,
            ["backendNodeId"] = Target.Nodes.GetId(node),
            ["nodeType"] = (int)node.NodeType,
            ["nodeName"] = node.NodeName,
            ["localName"] = node is Element element ? element.LocalName : "",
            ["nodeValue"] = node.NodeValue ?? "",
        };
        var children = node.ChildNodes;
        result["childNodeCount"] = children.Length;
        if (node is Element attributesElement)
        {
            var attributes = new JsonArray();
            foreach (var attribute in attributesElement.Attributes)
            {
                attributes.Add((JsonNode)attribute.Name);
                attributes.Add((JsonNode)attribute.Value);
            }

            result["attributes"] = attributes;
        }

        // The frame of a document and of its root element, and the frame of the content of a frame owner (an iframe)
        if (node is Document document)
        {
            result["documentURL"] = document.Url;
            result["baseURL"] = document.BaseUri;
            if (FindFrameOfDocument(document) is { } documentFrame)
            {
                result["frameId"] = documentFrame.FrameId;
            }
        }
        else if (node is Element { OwnerDocument: { } ownerDocument } owned && ReferenceEquals(ownerDocument.DocumentElement, owned) && FindFrameOfDocument(ownerDocument) is { } ownerFrame)
        {
            result["frameId"] = ownerFrame.FrameId;
        }
        else if (node is Element frameOwner && Target.GetFrames().FirstOrDefault(f => ReferenceEquals(f.BrowsingContext?.Container, frameOwner)) is { } contentFrame)
        {
            result["frameId"] = contentFrame.FrameId;
        }

        if (depth != 0 && children.Length > 0)
        {
            var childArray = new JsonArray();
            foreach (var child in children)
            {
                childArray.Add((JsonNode)DescribeNode(child, depth < 0 ? depth : depth - 1));
            }

            result["children"] = childArray;
        }

        return result;
    }

    private FrameState? FindFrameOfDocument(Document document) => Target.GetFrames().FirstOrDefault(frame => ReferenceEquals(frame.Document, document));

    private JsonObject ResolveNode(JsonElement parameters)
    {
        var node = GetNode(parameters);
        ScriptContext context;
        if (parameters.GetInt32("executionContextId") is { } contextId)
        {
            context = Target.FindExecutionContext(contextId) ?? throw new ProtocolException("Cannot find context with specified id");
            var nodeDocument = node as Document ?? node.OwnerDocument;
            if (!ReferenceEquals(nodeDocument, context.Document))
                throw new ProtocolException("Node with given id does not belong to the document");
        }
        else
        {
            var nodeDocument = node as Document ?? node.OwnerDocument;
            context = (nodeDocument is null ? null : FindFrameOfDocument(nodeDocument)?.MainWorld) ?? throw new ProtocolException("Cannot find context with specified id");
        }

        return new JsonObject { ["object"] = RemoteObjects.Create(context, context.ToJsValue(node), Objects, parameters.GetString("objectGroup"), returnByValue: false) };
    }

    private JsonObject GetContentQuads(JsonElement parameters)
    {
        var node = GetNode(parameters);
        var quads = new JsonArray();
        foreach (var quad in GetQuads(node))
        {
            quads.Add((JsonNode)ToQuad(quad));
        }

        if (quads.Count == 0)
            throw new ProtocolException("Could not compute content quads.");

        return new JsonObject { ["quads"] = quads };
    }

    private JsonObject GetBoxModel(JsonElement parameters)
    {
        var node = GetNode(parameters);
        var quads = GetQuads(node);
        if (quads.Count == 0)
            throw new ProtocolException("Could not compute box model.");

        var points = quads.SelectMany(quad => quad).ToArray();
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxX = points.Max(p => p.X);
        var maxY = points.Max(p => p.Y);
        (double X, double Y)[] border = [(minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)];
        return new JsonObject
        {
            ["model"] = new JsonObject
            {
                ["content"] = ToQuad(border),
                ["padding"] = ToQuad(border),
                ["border"] = ToQuad(border),
                ["margin"] = ToQuad(border),
                ["width"] = maxX - minX,
                ["height"] = maxY - minY,
            },
        };
    }

    private static JsonArray ToQuad((double X, double Y)[] quad)
    {
        var result = new JsonArray();
        foreach (var (x, y) in quad)
        {
            result.Add((JsonNode)x);
            result.Add((JsonNode)y);
        }

        return result;
    }

    /// <summary>The border boxes of a node, in the coordinates of the viewport of the page.</summary>
    private static List<(double X, double Y)[]> GetQuads(Node node)
    {
        var result = new List<(double X, double Y)[]>();
        if (!node.IsConnected)
            return result;

        if (node is Element element)
        {
            AddRects(result, element.OwnerDocument!, element.GetClientRects());
        }
        else if (node.OwnerDocument is { } document)
        {
            var range = document.CreateRange();
            range.SelectNode(node);
            AddRects(result, document, range.GetClientRects());
        }

        return result;

        static void AddRects(List<(double X, double Y)[]> result, Document document, DomRectList rects)
        {
            foreach (var rect in rects)
            {
                if (rect.Width <= 0 && rect.Height <= 0)
                    continue;

                result.Add(
                [
                    UserInput.MapToTopLevel(document, rect.Left, rect.Top),
                    UserInput.MapToTopLevel(document, rect.Right, rect.Top),
                    UserInput.MapToTopLevel(document, rect.Right, rect.Bottom),
                    UserInput.MapToTopLevel(document, rect.Left, rect.Bottom),
                ]);
            }
        }
    }

    /// <summary>Scrolls an element (or a rectangle of the element) into view when it is not visible in the viewport (<c>DOM.scrollIntoViewIfNeeded</c>).</summary>
    private void ScrollIntoViewIfNeeded(JsonElement parameters)
    {
        var node = GetNode(parameters);
        if (!node.IsConnected)
            throw new ProtocolException("Node is detached from document");

        var element = node as Element ?? node.ParentElement ?? throw new ProtocolException("Node does not have a layout object");
        var rect = element.GetBoundingClientRect();
        if (rect.Width == 0 && rect.Height == 0 && element.GetClientRects().Length == 0)
            throw new ProtocolException("Node does not have a layout object");

        var window = element.OwnerDocument?.DefaultView ?? throw new ProtocolException("Node is detached from document");
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
        if (parameters.TryGet("rect", out var target))
        {
            left = rect.Left + (target.GetDouble("x") ?? 0);
            top = rect.Top + (target.GetDouble("y") ?? 0);
            right = left + (target.GetDouble("width") ?? rect.Width);
            bottom = top + (target.GetDouble("height") ?? rect.Height);
        }

        var inViewport = left >= 0 && top >= 0 && right <= window.InnerWidth && bottom <= window.InnerHeight;
        if (!inViewport)
        {
            _ = element.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Center, Inline = ScrollLogicalPosition.Center, Behavior = ScrollBehavior.Instant });
        }
    }

    /// <summary>Selects files in a file input (<c>DOM.setFileInputFiles</c>): the files are read from the disk of the server.</summary>
    private void SetFileInputFiles(JsonElement parameters)
    {
        if (GetNode(parameters) is not HtmlInputElement { Type: "file" } input)
            throw new ProtocolException("Node is not a file input element");

        var files = new List<Dom.FileApi.File>();
        foreach (var path in parameters.GetArray("files"))
        {
            var fullPath = path.GetString() ?? "";
            byte[] content;
            try
            {
                content = System.IO.File.ReadAllBytes(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new ProtocolException("Cannot read the file " + fullPath + ": " + ex.Message, ex);
            }

            files.Add(new Dom.FileApi.File([new Blob([content])], Path.GetFileName(fullPath), new FilePropertyBag { LastModified = new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(fullPath)).ToUnixTimeMilliseconds() }));
        }

        input.Files = new FileList(files);
        input.DispatchEvent(new Event("input", new EventInit { Bubbles = true, Composed = true }));
        input.DispatchEvent(new Event("change", new EventInit { Bubbles = true }));
    }
}
